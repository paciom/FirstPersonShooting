using System.Collections;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The online 1v1 match on top of an established NetSession link.
///
/// Wire protocol (all little text messages; floats are InvariantCulture):
///   reliable "ev" channel —
///     cfg|arena|hostRobot     host proposes the match (host's picks)
///     cfgok|guestRobot        guest accepts, sends its robot
///     go|                     host confirms; both sides start
///     f|slot|dx,dy,dz         fire intent (slot into WeaponLoadout.Available)
///     t|                      vehicle transform toggle
///     s|current|max|down      owner's authoritative shield broadcast
///     bye|                    clean leave
///   unreliable "st" channel —
///     P|x,y,z|yaw|pitch       20 Hz local pawn state ("p|"/"q|" are the
///                             NetSession ping; capital P is ours)
///
/// Authority model (v1): each client owns its OWN pawn — position, and its
/// own shield (enemy fire is simulated locally by replaying fire events, so
/// the victim's client detects its own hits). The enemy mirror's shield is a
/// remoteProxy that only moves on their broadcasts. One writer per value.
/// No bots, no airdrops in online v1.
/// </summary>
public class NetMatch : MonoBehaviour
{
    public enum MatchState { Idle, Proposed, Starting, Playing }

    public static NetMatch Instance { get; private set; }

    const float SnapshotInterval = 0.05f;  // 20 Hz pawn state
    const float ShieldKeepalive = 0.25f;   // 4 Hz floor so a missed change self-heals
    const float ShieldChangeGap = 0.05f;   // but a real change goes out within a frame or two
    const float FireInterval = 0.05f;      // held-trigger replication cap
    const float HandshakeTimeout = 8f;     // cfg → cfgok → go must complete inside this

    public MatchState State { get; private set; } = MatchState.Idle;

    /// <summary>One-shot line for the online screen to show — why the last
    /// attempt ended. Reading it clears it.</summary>
    public string ConsumeNotice()
    {
        string notice = _notice;
        _notice = "";
        return notice;
    }

    GameObject _remoteGo;
    RemotePawn _remote;
    int _arenaIndex;
    int _remoteRobot;
    Transform _localPlayer;
    Transform _localHead;
    EnergyShield _localShield;
    float _nextSnapshot;
    float _nextShieldKeepalive;
    float _nextShieldChange;
    float _nextFire;
    float _lastSentShield = -1f;
    float _handshakeDeadline;
    int _snapshotSeq;
    string _notice = "";

    public static NetMatch Ensure()
    {
        if (Instance == null)
            Instance = new GameObject("NetMatch").AddComponent<NetMatch>();
        return Instance;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        var session = NetSession.Ensure();
        session.EventReceived += OnEventMessage;
        session.StateReceived += OnStateMessage;
    }

    void OnDestroy()
    {
        if (Instance != this)
            return;
        // Never leave a mirror pawn standing in the arena with nothing driving
        // it. No-op when the match already ended.
        EndMatch("net match torn down", notifyPeer: false);
        var session = NetSession.Instance;
        if (session != null)
        {
            session.EventReceived -= OnEventMessage;
            session.StateReceived -= OnStateMessage;
        }
        Instance = null;
    }

    // ---------- match flow ----------

    /// <summary>Host's START MATCH button: propose current arena + robot.</summary>
    public void ProposeMatch()
    {
        var gmc = GameModeController.Instance;
        if (gmc == null || State != MatchState.Idle)
            return;
        _arenaIndex = gmc.CurrentArenaIndex;
        NetSession.Instance.SendEvent(
            "cfg|" + _arenaIndex + "|" + gmc.PlayerRobotIndex);
        State = MatchState.Proposed;
        _handshakeDeadline = Time.unscaledTime + HandshakeTimeout;
    }

    /// <summary>
    /// A handshake that never completes must not strand the player on a dead
    /// screen. Resets to Idle WITHOUT tearing the link down — nothing was
    /// built yet, so the match code stays valid and START comes back.
    /// </summary>
    void AbandonHandshake()
    {
        State = MatchState.Idle;
        _notice = "your friend didn't answer — try again";
        Debug.Log("[NetMatch] handshake timed out");
    }

    void StartMatch()
    {
        // A duplicate go|/cfgok| must not stack a second mirror pawn and a
        // second set of shield handlers.
        if (State == MatchState.Playing || _remoteGo != null)
            return;
        State = MatchState.Playing;
        _notice = "";
        var gmc = GameModeController.Instance;
        var session = NetSession.Instance;
        gmc.StartOnlinePvP(_arenaIndex, session.IsHost);

        _localPlayer = PlayerBrain.Local != null ? PlayerBrain.Local.transform : null;
        if (_localPlayer != null)
        {
            var motor = _localPlayer.GetComponent<CharacterMotor>();
            _localHead = motor != null ? motor.head : null;
            _localShield = _localPlayer.GetComponent<EnergyShield>();
            if (_localShield != null)
            {
                _localShield.OnDeRezzed += SendShieldNow;
                _localShield.OnRematerialized += SendShieldNow;
            }
        }
        SpawnRemotePawn(session.IsHost);
    }

    /// <summary>
    /// Tear the match down. Reasons flow to the console; the mode controller
    /// brings the menu back. State goes Idle FIRST so the EnterMenu hook
    /// (OnLocalLeftMatch) can't re-enter.
    /// </summary>
    void EndMatch(string reason, bool notifyPeer)
    {
        if (State == MatchState.Idle)
            return;
        State = MatchState.Idle;
        Debug.Log($"[NetMatch] match over: {reason}");
        // Shown on the online screen — a match that just vanishes with no
        // explanation reads as a crash.
        _notice = reason;

        if (notifyPeer && NetSession.Instance != null)
            NetSession.Instance.SendEvent("bye|");

        if (_localShield != null)
        {
            _localShield.OnDeRezzed -= SendShieldNow;
            _localShield.OnRematerialized -= SendShieldNow;
            _localShield = null;
        }
        if (_remoteGo != null)
        {
            Destroy(_remoteGo);
            _remoteGo = null;
            _remote = null;
            // The scope caches one silhouette copy per character. Leaving the
            // mirror's stale entry in there blinds it for the next offline
            // match — silhouettes of a robot that no longer exists.
            var scope = FindFirstObjectByType<XRayScope>(FindObjectsInactive.Include);
            if (scope != null)
                scope.InvalidateSilhouettes();
        }

        var gmc = GameModeController.Instance;
        if (gmc != null && gmc.Mode == GameMode.OnlinePvP)
            gmc.EnterMenu();

        // One match per link in v1 — drop the session so both sides land
        // back on a clean online screen for a rematch. When a goodbye was
        // just queued, give the channel a beat to flush it first.
        if (notifyPeer)
            StartCoroutine(DisconnectSoon());
        else
            NetSession.Instance?.Disconnect();
    }

    IEnumerator DisconnectSoon()
    {
        yield return new WaitForSecondsRealtime(0.3f);
        if (State == MatchState.Idle)
            NetSession.Instance?.Disconnect();
    }

    /// <summary>GameModeController.EnterMenu calls this when leaving OnlinePvP
    /// (Escape). No-op when the match already ended itself.</summary>
    public static void OnLocalLeftMatch()
    {
        if (Instance != null && Instance.State != MatchState.Idle)
            Instance.EndMatch("left via menu", notifyPeer: true);
    }

    // ---------- local replication hooks (called from PlayerBrain) ----------

    public static void NotifyLocalFire(int slot, Vector3 direction)
    {
        var match = Instance;
        if (match == null || match.State != MatchState.Playing)
            return;
        if (Time.unscaledTime < match._nextFire)
            return;
        match._nextFire = Time.unscaledTime + FireInterval;
        NetSession.Instance.SendEvent(
            "f|" + slot + "|" + F(direction.x) + "," + F(direction.y) + "," + F(direction.z));
    }

    /// <summary>Sends the resulting FORM, not a toggle — see
    /// RemotePawn.RemoteSetVehicle for why.</summary>
    public static void NotifyLocalTransform(bool nowVehicle)
    {
        var match = Instance;
        if (match == null || match.State != MatchState.Playing)
            return;
        NetSession.Instance.SendEvent("t|" + (nowVehicle ? 1 : 0));
    }

    // ---------- pumps ----------

    void Update()
    {
        var session = NetSession.Instance;
        if (session == null)
            return;

        // The link dying is the match ending, whatever state we were in.
        if (State != MatchState.Idle && session.Status != NetStatus.Connected)
        {
            EndMatch("connection lost", notifyPeer: false);
            return;
        }

        // A backgrounded browser tab throttles to ~1 Hz; that stall is not the
        // peer failing to answer. Push the deadline out by the lost time.
        if (Time.unscaledDeltaTime > 2f)
            _handshakeDeadline += Time.unscaledDeltaTime;

        if ((State == MatchState.Proposed || State == MatchState.Starting)
            && Time.unscaledTime > _handshakeDeadline)
        {
            AbandonHandshake();
            return;
        }

        if (State != MatchState.Playing || _localPlayer == null)
            return;

        if (Time.unscaledTime >= _nextSnapshot)
        {
            _nextSnapshot = Time.unscaledTime + SnapshotInterval;
            Vector3 p = _localPlayer.position;
            float yaw = _localPlayer.eulerAngles.y;
            float pitch = _localHead != null ? NormalizePitch(_localHead.localEulerAngles.x) : 0f;
            // The sequence number lets the receiver discard reordered
            // snapshots — the state channel is explicitly unordered.
            session.SendState("P|" + (++_snapshotSeq)
                + "|" + F(p.x) + "," + F(p.y) + "," + F(p.z)
                + "|" + F(yaw) + "|" + F(pitch));
        }

        if (_localShield != null)
        {
            // Two independent rates: a real change goes out almost at once
            // (taking a hit should show on their screen now), and a keepalive
            // underneath it so a dropped update always self-heals.
            bool changed = Mathf.Abs(_localShield.Current - _lastSentShield) > 0.5f;
            if (changed && Time.unscaledTime >= _nextShieldChange)
                SendShieldNow();
            else if (Time.unscaledTime >= _nextShieldKeepalive)
                SendShieldNow();
        }
    }

    void SendShieldNow()
    {
        if (_localShield == null || State != MatchState.Playing)
            return;
        _nextShieldChange = Time.unscaledTime + ShieldChangeGap;
        _nextShieldKeepalive = Time.unscaledTime + ShieldKeepalive;
        _lastSentShield = _localShield.Current;
        NetSession.Instance.SendEvent("s|" + F(_localShield.Current)
            + "|" + F(_localShield.maxShield)
            + "|" + (_localShield.IsDown ? 1 : 0));
    }

    // ---------- message handling ----------

    void OnEventMessage(string msg)
    {
        if (msg.StartsWith("cfg|", System.StringComparison.Ordinal))
        {
            // Guest side: host proposed. Accept with our robot and wait for go.
            var parts = msg.Split('|');
            if (parts.Length < 3 || State != MatchState.Idle)
                return;
            int.TryParse(parts[1], out _arenaIndex);
            int.TryParse(parts[2], out _remoteRobot);
            var gmc = GameModeController.Instance;
            NetSession.Instance.SendEvent("cfgok|" + (gmc != null ? gmc.PlayerRobotIndex : 0));
            State = MatchState.Starting;
            _handshakeDeadline = Time.unscaledTime + HandshakeTimeout;
        }
        else if (msg.StartsWith("cfgok|", System.StringComparison.Ordinal))
        {
            // Host side: guest accepted — lock it in and start.
            var parts = msg.Split('|');
            if (parts.Length < 2 || State != MatchState.Proposed)
                return;
            int.TryParse(parts[1], out _remoteRobot);
            NetSession.Instance.SendEvent("go|");
            StartMatch();
        }
        else if (msg.StartsWith("go|", System.StringComparison.Ordinal))
        {
            if (State == MatchState.Starting)
                StartMatch();
        }
        else if (msg.StartsWith("f|", System.StringComparison.Ordinal))
        {
            if (_remote == null)
                return;
            var parts = msg.Split('|');
            if (parts.Length < 3 || !int.TryParse(parts[1], out int slot))
                return;
            if (TryParseVector(parts[2], out Vector3 dir))
                _remote.RemoteFire(slot, dir);
        }
        else if (msg.StartsWith("t|", System.StringComparison.Ordinal))
        {
            var parts = msg.Split('|');
            if (parts.Length >= 2)
                _remote?.RemoteSetVehicle(parts[1] == "1");
        }
        else if (msg.StartsWith("s|", System.StringComparison.Ordinal))
        {
            if (_remote == null)
                return;
            var parts = msg.Split('|');
            if (parts.Length < 4)
                return;
            if (TryF(parts[1], out float current) && TryF(parts[2], out float max))
                _remote.ApplyShield(current, max, parts[3] == "1");
        }
        else if (msg.StartsWith("bye|", System.StringComparison.Ordinal))
        {
            EndMatch("your friend left the match", notifyPeer: false);
        }
    }

    void OnStateMessage(string msg)
    {
        if (_remote == null || !msg.StartsWith("P|", System.StringComparison.Ordinal))
            return;
        var parts = msg.Split('|');
        if (parts.Length < 5)
            return;
        if (int.TryParse(parts[1], out int seq)
            && TryParseVector(parts[2], out Vector3 pos)
            && TryF(parts[3], out float yaw)
            && TryF(parts[4], out float pitch))
            _remote.PushSnapshot(seq, pos, yaw, pitch);
    }

    // ---------- remote pawn construction ----------

    /// <summary>
    /// Build the other player's mirror by cloning the local rig under an
    /// inactive holder (so Awakes are deferred), stripping the local-only
    /// parts, flipping the team, and only then letting it wake up — the
    /// weapons capture their TeamId from the shield during Awake, and
    /// PlayerBrain.Awake would clobber PlayerBrain.Local if it ever ran.
    /// </summary>
    void SpawnRemotePawn(bool localIsHost)
    {
        if (_localPlayer == null)
            return;
        var def = ArenaContext.Current;
        // Host stands at the player spawn; guest across the arena. The remote
        // pawn takes whichever spot the local player didn't.
        Vector3 spot = localIsHost ? def.TeamSpawns(1)[0] : def.PlayerSpawn;
        float yaw = localIsHost ? 180f : 0f;

        var holder = new GameObject("RemoteHolder");
        holder.SetActive(false);

        var clone = Instantiate(_localPlayer.gameObject, holder.transform);
        clone.name = "RemotePlayer";

        // Strip before activation: components whose Awake/state is local-only.
        // DestroyImmediate, not Destroy — Destroy defers past the reparent
        // below, which is what activates the clone, so OnEnable would fire on
        // components that are supposed to be gone.
        DestroyImmediate(clone.GetComponent<PlayerBrain>());
        DestroyImmediate(clone.GetComponent<HudController>());
        var sniper = clone.GetComponent<SniperScope>();
        if (sniper != null) DestroyImmediate(sniper);
        var xray = clone.GetComponent<XRayScope>();
        if (xray != null) DestroyImmediate(xray);
        // Hides the LOCAL robot from its own camera by forcing every renderer
        // under Body to shadows-only. Left on, it does that to the opponent —
        // a match against a moving shadow.
        var firstPerson = clone.GetComponent<FirstPersonBody>();
        if (firstPerson != null) DestroyImmediate(firstPerson);

        // COMPONENTS, not their GameObject: the camera lives on "Head", and so
        // does the blaster carrying all 54 weapons. Destroying the Head object
        // leaves the mirror unarmed and unable to pitch.
        var camData = clone.GetComponentInChildren<UniversalAdditionalCameraData>(true);
        if (camData != null) DestroyImmediate(camData);   // RequireComponent(Camera): must go first
        var camera = clone.GetComponentInChildren<Camera>(true);
        if (camera != null) DestroyImmediate(camera);
        var listener = clone.GetComponentInChildren<AudioListener>(true);
        if (listener != null) DestroyImmediate(listener);
        // The stripped HUD/scope components already built their canvases in the
        // source rig; those children clone across on their own.
        foreach (var canvas in clone.GetComponentsInChildren<Canvas>(true))
            if (canvas != null) DestroyImmediate(canvas.gameObject);

        var motor = clone.GetComponent<CharacterMotor>();
        if (motor != null)
            motor.enabled = false; // snapshots drive the transform directly

        var shield = clone.GetComponent<EnergyShield>();
        if (shield != null)
        {
            shield.teamId = 1;
            shield.remoteProxy = true;
        }

        clone.transform.SetPositionAndRotation(spot, Quaternion.Euler(0f, yaw, 0f));
        _remote = clone.AddComponent<RemotePawn>();

        // Wake it up with the right team already in place.
        clone.transform.SetParent(null, true);
        Destroy(holder);

        // Wear the opponent's chosen robot in enemy colors.
        var gmc = GameModeController.Instance;
        var roster = gmc != null ? gmc.Roster : null;
        var body = clone.transform.Find("Body");
        if (roster != null && roster.HasRobots && body != null)
        {
            var entry = roster.Get(_remoteRobot);
            if (entry.modelPrefab != null)
            {
                Color tint = MatchAnnouncer.TeamColor(1);
                // paintAnchorHue matters as much as the tint: without it a
                // warm-dominant robot barely turns magenta, and the two teams
                // read the same. Mirrors ApplyRobotSelection exactly.
                RobotFactory.Reskin(body, entry.modelPrefab, tint, entry.paintAnchorHue);
                var skin = body.GetComponent<VehicleSkin>();
                if (skin != null)
                {
                    skin.paintAnchorHue = entry.paintAnchorHue;
                    if (entry.HasStages)
                        skin.SetStages(entry.transformStages, tint);
                    else
                        skin.SetVehiclePrefab(entry.vehiclePrefab, tint);
                }
            }
        }

        // Undo the shadows-only hiding the clone inherited from the local
        // rig's FirstPersonBody (its renderers were already hidden when they
        // were copied — stripping the component doesn't put them back).
        // ShadowsOnly → On only: XRayScope and StatusEffects deliberately set
        // Off on silhouettes and effect meshes, and those must stay hidden.
        foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
            if (renderer != null
                && renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

        var loadout = clone.GetComponent<WeaponLoadout>();
        if (loadout == null || loadout.Available == null || loadout.Available.Length == 0)
            Debug.LogError("[NetMatch] the mirror pawn has no weapons — it can never "
                + "show the opponent shooting. Did the strip remove the Blaster?");

        var scope = FindFirstObjectByType<XRayScope>(FindObjectsInactive.Include);
        if (scope != null)
            scope.InvalidateSilhouettes();

        _remoteGo = clone;
    }

    // ---------- small helpers ----------

    static string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);

    static bool TryF(string s, out float v) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    static bool TryParseVector(string s, out Vector3 v)
    {
        v = Vector3.zero;
        var parts = s.Split(',');
        if (parts.Length != 3)
            return false;
        if (!TryF(parts[0], out v.x) || !TryF(parts[1], out v.y) || !TryF(parts[2], out v.z))
            return false;
        return true;
    }

    /// <summary>Euler X reads 0..360; pitch wants -180..180.</summary>
    static float NormalizePitch(float eulerX) => eulerX > 180f ? eulerX - 360f : eulerX;
}
