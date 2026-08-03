using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

/// <summary>
/// DOGFIGHT — the sky duel.
///
/// Two robots land on pads over a navy void, fold into jets by stop motion,
/// and race each other to five wrecks. One card seats the player in the cyan
/// cockpit against a CPU pilot; the AI WAR card seats two CPU pilots and turns
/// the camera into a broadcast that cuts between them. Either way the camera
/// answers C (chase / cockpit) — the couch gets the first-person seat too.
///
/// It owns its world the way TANK RAID owns its battlefield: entering
/// deactivates the arena's environment and hides its cast, everything is
/// built under one stage root, and <see cref="Teardown"/> hands the arena
/// back through <c>ArenaRuntime.Load</c>. No NavMesh — nothing in the sky
/// paths.
///
/// THE JETS ARE NOT UNDER THE STAGE ROOT. Bolts resolve their victim through
/// <c>hit.transform.root</c>, so every jet lives at the scene root and is
/// swept by <see cref="JetPawn.DespawnAll"/> instead. The TankPawn rule.
///
/// Match flow is a plain state enum and float timers, no coroutines — a
/// mid-Play recompile kills coroutines silently, and a referee that forgets
/// the score mid-sortie is worse than no referee (BrawlMatch's reasoning).
/// </summary>
/// <remarks>Runs before the pawns, at the drivers' order: this class writes
/// the hero's <c>Steer</c>/<c>Throttle</c>/<c>Firing</c>, and a driver that
/// ran after its pawn would always be one frame stale.</remarks>
[DefaultExecutionOrder(-50)]
public class Dogfight : MonoBehaviour
{
    // ------------------------------------------------------------------- tuning

    const float JetShield = 100f;

    /// <summary>First to this many wrecks takes the sortie.</summary>
    const int KillsToWin = 5;

    /// <summary>The dying jet's screen time, and the winner's breather.</summary>
    const float RespawnBeat = 2.5f;

    /// <summary>Seconds of shielded grace after a respawn, blinked so it reads.</summary>
    const float GraceSeconds = 2f;

    /// <summary>The ceremony: robots stand, fold, then climb out.</summary>
    const float IntroSeconds = 0.9f;
    const float MorphSeconds = 1.8f;
    const float LaunchSeconds = 2.4f;

    /// <summary>Metres between the two pads. Close enough that each robot's
    /// fold is in the other's shot; far enough that the climb-out separates
    /// them before guns are free.</summary>
    const float PadSpacing = 64f;

    enum Stage { Intro, Morph, Launch, Fight, Over }

    // -------------------------------------------------------------------- state

    GameObject _stageRoot;
    GameObject _cameraRig;
    Camera _camera;
    DogfightCamera _director;
    DogfightSky _sky;
    DogfightHud _hud;
    ArenaBlockManager _blockManager;
    readonly List<GameObject> _hiddenCharacters = new List<GameObject>();

    RobotRoster _roster;
    int _cyanRobot;
    int _magentaRobot;
    bool _playerControls;

    JetPawn _cyan;
    JetPawn _magenta;
    GameObject _cyanPad;
    GameObject _magentaPad;

    Stage _stage = Stage.Intro;
    float _stageStart;
    int _cyanScore;
    int _magentaScore;
    float _cyanRespawnAt = -1f;
    float _magentaRespawnAt = -1f;
    float _cyanGraceUntil;
    float _magentaGraceUntil;

    /// <summary>The player's seeker: what the diamond is on, and for how
    /// long the nose has held it. Locked at <see cref="PlayerLockSeconds"/>.</summary>
    Transform _lockCandidate;
    Vector3 _lockCandidateCenter;
    float _lockProgress;
    const float PlayerLockSeconds = 0.7f;
    const float PlayerLockCone = 12f;
    const float PlayerLockRange = 115f;

    public static Dogfight Begin(GameModeController owner, RobotRoster roster,
        int cyanRobot, int magentaRobot, bool playerControls)
    {
        var go = new GameObject("Dogfight");
        go.transform.SetParent(owner.transform, false);
        var fight = go.AddComponent<Dogfight>();
        fight._roster = roster;
        fight._cyanRobot = cyanRobot;
        fight._magentaRobot = magentaRobot;
        fight._playerControls = playerControls;
        fight.Setup();
        return fight;
    }

    // -------------------------------------------------------------------- set-up

    void Setup()
    {
        var player = FindFirstObjectByType<PlayerBrain>();
        if (player != null)
            Hide(player.gameObject);
        foreach (var bot in FindObjectsByType<AIBrain>(FindObjectsSortMode.None))
            Hide(bot.gameObject);

        _blockManager = FindFirstObjectByType<ArenaBlockManager>();
        if (_blockManager != null)
            _blockManager.enabled = false;

        // The arena's world goes dark under this mode's own sky. Found through
        // the NavMeshSurface because that is the object every arena hangs its
        // world off, whichever arena is loaded.
        var surface = FindFirstObjectByType<NavMeshSurface>(FindObjectsInactive.Include);
        Transform environment = surface != null ? surface.transform : null;
        if (environment != null)
            foreach (Transform child in environment)
                child.gameObject.SetActive(false);

        _stageRoot = new GameObject("DogfightStage");
        if (environment != null)
            _stageRoot.transform.SetParent(environment, false);

        _sky = DogfightSky.Build(_stageRoot.transform);
        // The battery goes up with the set but holds fire until the fight —
        // see the WeaponsFree assertion in Update.
        DogfightTurret.BuildRing();

        // Robots first: standing on their pads, facing each other across the
        // void, cyan on the west pad. The pawns themselves live at the scene
        // root (see the class note); only the set dressing is staged.
        var cyanAt = new Vector3(-PadSpacing * 0.5f, DogfightSky.PadY, 0f);
        var magentaAt = new Vector3(PadSpacing * 0.5f, DogfightSky.PadY, 0f);
        _cyan = SpawnPawn(_cyanRobot, 0, cyanAt, 90f);
        _magenta = SpawnPawn(_magentaRobot, 1, magentaAt, -90f);
        // The pawn's origin is its flight CENTRE, so the standing robot's feet
        // hang half a fit below it — the pad top meets them there. After the
        // fold the low-slung jet is left hovering a body above the pad, which
        // is exactly the shot the clips end on.
        Vector3 underFeet = Vector3.down * (JetPawn.JetSize * 0.5f);
        _cyanPad = _sky.BuildPad(cyanAt + underFeet, MatchAnnouncer.TeamColor(0));
        _magentaPad = _sky.BuildPad(magentaAt + underFeet, MatchAnnouncer.TeamColor(1));

        _cameraRig = BuildCameraRig();
        _camera = _cameraRig.GetComponent<Camera>();
        _director = _cameraRig.GetComponent<DogfightCamera>();
        _director.Follow(_cyan);

        _hud = DogfightHud.Build(transform);
        _hud.SetScore(0, 0);
        _hud.SetReticleVisible(false);
        _hud.SetPilotRowVisible(false);
        _hud.SetCaption("");

        _stage = Stage.Intro;
        _stageStart = Time.time;
    }

    JetPawn SpawnPawn(int robotIndex, int teamId, Vector3 position, float yaw)
    {
        var entry = _roster != null && _roster.HasRobots
            ? _roster.Get(robotIndex)
            : default;
        var pawn = JetPawn.Spawn(entry, JetStagesFor(entry), teamId, position, yaw, JetShield);
        pawn.OnWrecked += Wrecked;
        return pawn;
    }

    /// <summary>
    /// The stop-motion set this robot flies. Its own where the jet pipeline
    /// has been run for it; the ranger's airframe (in this robot's team
    /// paint) where it has not — one robot has flown so far, and a mode only
    /// ranger could enter would be a menu card that mostly apologises.
    /// </summary>
    GameObject[] JetStagesFor(RobotRoster.Entry entry)
    {
        if (entry.HasJetStages)
            return entry.jetStages;
        if (_roster != null)
            foreach (var candidate in _roster.robots)
                if (candidate.HasJetStages)
                    return candidate.jetStages;
        return null;
    }

    // --------------------------------------------------------------- every frame

    void Update()
    {
        float elapsed = Time.time - _stageStart;

        switch (_stage)
        {
            case Stage.Intro:
                if (elapsed >= IntroSeconds)
                {
                    Advance(Stage.Morph);
                    _hud.Flash("TRANSFORM", new Color(0.7f, 0.9f, 1f), MorphSeconds);
                }
                break;

            case Stage.Morph:
                float progress = Mathf.Clamp01(elapsed / MorphSeconds);
                if (_cyan.HasMorph)
                    _cyan.ShowMorph(progress);
                if (_magenta.HasMorph)
                    _magenta.ShowMorph(progress);
                if (elapsed >= MorphSeconds)
                {
                    Advance(Stage.Launch);
                    Launch();
                }
                break;

            case Stage.Launch:
                // The mode itself is the pilot for the climb-out: both jets
                // get the same gentle nose-up and full throttle, written
                // through the same seam the player and the brains use.
                var climb = new Vector2(0f, Mathf.Lerp(0.55f, 0.1f, elapsed / LaunchSeconds));
                _cyan.Steer = climb;
                _cyan.Throttle = 1f;
                _magenta.Steer = climb;
                _magenta.Throttle = 1f;
                if (elapsed >= LaunchSeconds)
                {
                    Advance(Stage.Fight);
                    OpenFight();
                }
                break;

            case Stage.Fight:
                RunFight();
                break;

            case Stage.Over:
                // The sky keeps flying behind the panel; only the score is
                // settled. ESC or MENU is the way out, as in TANK RAID.
                RunRespawns();
                break;
        }

        // Asserted every frame rather than set once: statics are wiped by a
        // mid-Play recompile, and a battery that came back from the reload
        // pacifist would be a quiet bug nobody files.
        DogfightTurret.WeaponsFree = _stage == Stage.Fight || _stage == Stage.Over;

        ReadCameraKeys();
        RunGraceBlink(_cyan, _cyanGraceUntil);
        RunGraceBlink(_magenta, _magentaGraceUntil);
        Readout();
    }

    void Advance(Stage stage)
    {
        _stage = stage;
        _stageStart = Time.time;
    }

    void Launch()
    {
        _cyan.FlightOn = true;
        _magenta.FlightOn = true;
        foreach (var pad in new[] { _cyanPad, _magentaPad })
        {
            if (pad == null)
                continue;
            VfxUtil.SpawnBurst(pad.transform.position + Vector3.up * 0.6f,
                new Color(0.6f, 0.85f, 1f), 18, 5f, 0.14f);
            pad.SetActive(false);
        }
    }

    void OpenFight()
    {
        _hud.Flash("FIGHT", new Color(1f, 0.75f, 0.2f));
        _hud.SetReticleVisible(_playerControls);
        _hud.SetPilotRowVisible(_playerControls);
        if (_playerControls)
        {
            AddBrain(_magenta, _cyan, -1f);
        }
        else
        {
            // Mirrored break sides, so the opening merge becomes a circle
            // rather than a queue.
            AddBrain(_cyan, _magenta, 1f);
            AddBrain(_magenta, _cyan, -1f);
            _director.Broadcast = true;
            _hud.SetCaption("C — VIEW   ·   SPACE — NEXT JET");
        }
    }

    void AddBrain(JetPawn pawn, JetPawn quarry, float breakSign)
    {
        var brain = pawn.gameObject.AddComponent<JetBrain>();
        brain.pawn = pawn;
        brain.quarry = quarry;
        brain.breakSign = breakSign;
    }

    void RunFight()
    {
        if (_playerControls)
            DrivePlayer();
        RunRespawns();
    }

    /// <summary>
    /// The stick: the jet chases the cursor. Offset from the screen's centre
    /// is the steer, with a deadzone so a parked mouse flies straight; the
    /// arrow keys and A/D add on top (sticks add, they do not replace — the
    /// TANK RAID rule, one control up); W boosts and S brakes; the left
    /// button is the trigger.
    /// </summary>
    void DrivePlayer()
    {
        if (_cyan == null || _cyan.IsDown)
            return;

        Vector2 mouse = new Vector2(
            (Input.mousePosition.x - Screen.width * 0.5f) / (Screen.height * 0.38f),
            (Input.mousePosition.y - Screen.height * 0.5f) / (Screen.height * 0.38f));
        if (mouse.magnitude < 0.08f)
            mouse = Vector2.zero;
        var steer = Vector2.ClampMagnitude(mouse, 1f);

        float keyYaw = (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)
                       - (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f)
                       + (Input.GetKey(KeyCode.D) ? 1f : 0f)
                       - (Input.GetKey(KeyCode.A) ? 1f : 0f);
        float keyPitch = (Input.GetKey(KeyCode.UpArrow) ? 1f : 0f)
                         - (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
        steer += new Vector2(keyYaw, keyPitch);

        _cyan.Steer = steer;
        _cyan.Throttle = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.LeftShift) ? 1f : 0f)
                         - (Input.GetKey(KeyCode.S) ? 1f : 0f);
        _cyan.Firing = Input.GetMouseButton(0) || Input.GetKey(KeyCode.Space);

        UpdatePlayerLock();
        if (Input.GetMouseButtonDown(1) && _lockProgress >= PlayerLockSeconds)
            _cyan.TryFireMissile(_lockCandidate);
        if (Input.GetKeyDown(KeyCode.F))
            _cyan.TryPopFlares();
    }

    /// <summary>
    /// The seeker's eye: the enemy jet or the nearest live turret, whichever
    /// the nose is actually on. Hold it inside the cone and the diamond
    /// solidifies; look away and the lock starts over — a missile costs
    /// commitment, which is the whole difference from the guns.
    /// </summary>
    void UpdatePlayerLock()
    {
        Transform best = null;
        Vector3 bestCenter = Vector3.zero;
        float bestAngle = PlayerLockCone;

        var enemy = JetPawn.NearestEnemy(_cyan.transform.position, 0, PlayerLockRange);
        if (enemy != null)
        {
            float angle = Vector3.Angle(_cyan.transform.forward,
                enemy.Center - _cyan.transform.position);
            if (angle < bestAngle)
            {
                bestAngle = angle;
                best = enemy.transform;
                bestCenter = enemy.Center;
            }
        }

        var turret = DogfightTurret.Nearest(_cyan.transform.position, PlayerLockRange);
        if (turret != null)
        {
            float angle = Vector3.Angle(_cyan.transform.forward,
                turret.Center - _cyan.transform.position);
            if (angle < bestAngle)
            {
                best = turret.transform;
                bestCenter = turret.Center;
            }
        }

        if (best != _lockCandidate)
        {
            _lockCandidate = best;
            _lockProgress = 0f;
        }
        else if (best != null)
        {
            _lockProgress += Time.deltaTime;
        }
        _lockCandidateCenter = bestCenter;
    }

    /// <summary>C and SPACE work in every seat — the couch watching an AI WAR
    /// gets the cockpit too, which is half the point of the card.</summary>
    void ReadCameraKeys()
    {
        if (Input.GetKeyDown(KeyCode.C))
            _director.ToggleView();
        // SPACE is the trigger in the player's seat; the broadcast alone
        // spends it on cuts.
        if (!_playerControls && Input.GetKeyDown(KeyCode.Space))
            _director.Next();
    }

    // ----------------------------------------------------------------- the score

    void Wrecked(JetPawn pawn)
    {
        bool cyanDown = pawn == _cyan;
        if (_stage == Stage.Fight)
        {
            if (cyanDown)
                _magentaScore++;
            else
                _cyanScore++;
            _hud.SetScore(_cyanScore, _magentaScore);

            int scorer = cyanDown ? 1 : 0;
            _hud.Flash($"{MatchAnnouncer.TeamName(scorer)} SCORES",
                MatchAnnouncer.TeamColor(scorer));
        }

        if (_director.Subject == pawn)
            _director.Shake(1.1f);

        if (cyanDown)
            _cyanRespawnAt = Time.time + RespawnBeat;
        else
            _magentaRespawnAt = Time.time + RespawnBeat;

        if (_stage == Stage.Fight
            && (_cyanScore >= KillsToWin || _magentaScore >= KillsToWin))
            EndSortie();
    }

    void RunRespawns()
    {
        if (_cyanRespawnAt > 0f && Time.time >= _cyanRespawnAt)
        {
            _cyanRespawnAt = -1f;
            RespawnPawn(_cyan, _magenta);
            _cyanGraceUntil = Time.time + GraceSeconds;
        }
        if (_magentaRespawnAt > 0f && Time.time >= _magentaRespawnAt)
        {
            _magentaRespawnAt = -1f;
            RespawnPawn(_magenta, _cyan);
            _magentaGraceUntil = Time.time + GraceSeconds;
        }

        if (_cyan != null && _cyan.Shield != null)
            _cyan.Shield.invulnerable = Time.time < _cyanGraceUntil;
        if (_magenta != null && _magenta.Shield != null)
            _magenta.Shield.invulnerable = Time.time < _magentaGraceUntil;
    }

    /// <summary>Back onto the spawn ring on the far side from the enemy,
    /// facing the fight — never into the fence, never into a waiting gun.</summary>
    void RespawnPawn(JetPawn pawn, JetPawn enemy)
    {
        Vector3 away = Vector3.right * (pawn.Team == 0 ? -1f : 1f);
        if (enemy != null)
        {
            Vector3 flat = new Vector3(enemy.transform.position.x, 0f,
                enemy.transform.position.z);
            if (flat.sqrMagnitude > 1f)
                away = -flat.normalized;
        }
        Vector3 at = away * DogfightSky.SpawnRing + Vector3.up * DogfightSky.SpawnAltitude;
        float yaw = Quaternion.LookRotation(-away, Vector3.up).eulerAngles.y;
        pawn.Respawn(at, yaw);
    }

    /// <summary>The grace made visible: a blink nothing else in the sky has,
    /// so "you cannot be hurt yet" needs no caption. Skipped for the cockpit
    /// subject — the camera owns that jet's visibility.</summary>
    void RunGraceBlink(JetPawn pawn, float graceUntil)
    {
        if (pawn == null || pawn.IsDown)
            return;
        if (_director.Cockpit && _director.Subject == pawn)
            return;
        if (Time.time < graceUntil)
            pawn.SetVisible(Mathf.FloorToInt((graceUntil - Time.time) * 9f) % 2 == 0);
        else if (Time.time < graceUntil + 0.5f)
            pawn.SetVisible(true);
    }

    void EndSortie()
    {
        Advance(Stage.Over);
        int winner = _cyanScore >= KillsToWin ? 0 : 1;
        Color accent = MatchAnnouncer.TeamColor(winner);
        _hud.Flash($"{MatchAnnouncer.TeamName(winner)} TAKES THE SKY", accent, 3f);
        _hud.ShowOver($"{MatchAnnouncer.TeamName(winner)} WINS",
            $"{_cyanScore}  —  {_magentaScore}\nESC or MENU to fly again", accent);
        _hud.SetCaption("");
    }

    // ------------------------------------------------------------------ readout

    void Readout()
    {
        if (_hud == null || _cyan == null || _magenta == null)
            return;

        _hud.SetShields(_cyan.Shield != null ? _cyan.Shield.Normalized : 0f,
            _magenta.Shield != null ? _magenta.Shield.Normalized : 0f);

        var subject = _director.Subject;
        if (subject != null)
            _hud.SetSpeed(subject.Speed, subject.Speed > JetPawn.SpeedCruise + 4f);

        if (_playerControls && _stage == Stage.Fight)
        {
            _hud.SetReticleHot(_cyan.AssistTarget != null);
            UpdateTargetArrow();
            _hud.SetOrdnance(_cyan.MissileReadyFraction, _cyan.MissileReady, _cyan.FlareCharges);
            _hud.SetLockDiamond(
                _lockCandidate != null
                    ? _camera.WorldToViewportPoint(_lockCandidateCenter)
                    : Vector3.back,
                _lockCandidate == null ? 0 : _lockProgress >= PlayerLockSeconds ? 2 : 1);
            WarnOfMissiles();
        }
    }

    /// <summary>The cockpit's threat receiver: any missile with the player's
    /// name on it inside warning range keeps the INCOMING line lit (the HUD
    /// throttles the shouting).</summary>
    void WarnOfMissiles()
    {
        if (_cyan == null || _cyan.IsDown)
            return;
        foreach (var missile in DogfightMissile.All)
        {
            if (missile == null || missile.Quarry != _cyan.transform)
                continue;
            if ((missile.transform.position - _cyan.transform.position).sqrMagnitude < 55f * 55f)
            {
                _hud.WarnIncoming();
                return;
            }
        }
    }

    /// <summary>The chevron to an enemy the canopy cannot see. Projected
    /// through the live camera; an enemy behind the lens projects mirrored,
    /// so the sign flip keeps the arrow honest.</summary>
    void UpdateTargetArrow()
    {
        var enemy = JetPawn.NearestEnemy(_cyan.transform.position, 0);
        if (enemy == null)
        {
            _hud.SetTargetArrow(Vector2.zero, false);
            return;
        }

        Vector3 view = _camera.WorldToViewportPoint(enemy.Center);
        bool onScreen = view.z > 0f
                        && view.x > 0.05f && view.x < 0.95f
                        && view.y > 0.08f && view.y < 0.92f;
        if (onScreen)
        {
            _hud.SetTargetArrow(Vector2.zero, false);
            return;
        }

        var direction = new Vector2(view.x - 0.5f, view.y - 0.5f);
        if (view.z < 0f)
            direction = -direction;
        _hud.SetTargetArrow(direction, true);
    }

    // ---------------------------------------------------------------- lifecycle

    public void Teardown()
    {
        // The ordnance first — a live homing missile outliving the mode would
        // happily chase the menu's robots — then every other scene-root actor,
        // none of which is swept by destroying anything else.
        DogfightMissile.DespawnAll();
        DogfightFlare.DespawnAll();
        DogfightTurret.DespawnAll();
        JetPawn.DespawnAll();

        if (_cameraRig != null)
            Destroy(_cameraRig);
        if (_stageRoot != null)
        {
            // Immediate, not deferred: ArenaRuntime.Load re-bakes this same
            // frame, and a deferred destroy bakes the sky into the arena's
            // first-load capture forever (BrawlController's discovery).
            DestroyImmediate(_stageRoot);
            _stageRoot = null;
        }

        foreach (var go in _hiddenCharacters)
            if (go != null)
                go.SetActive(true);
        _hiddenCharacters.Clear();

        if (_blockManager != null)
            _blockManager.enabled = true;

        // One call restores geometry, NavMesh, atmosphere (our fog and
        // ambient overrides included) and character placement.
        ArenaRuntime.Load(ArenaRuntime.CurrentIndex);
        Destroy(gameObject);
    }

    void Hide(GameObject character)
    {
        if (character == null || !character.activeSelf)
            return;
        character.SetActive(false);
        _hiddenCharacters.Add(character);
    }

    GameObject BuildCameraRig()
    {
        var rig = new GameObject("DogfightCamera");
        // Tagged so Camera.main works while the player's own camera is
        // inactive; FlashQuad's billboarding and the sky's clouds depend on it.
        rig.tag = "MainCamera";
        var cam = rig.AddComponent<Camera>();
        cam.fieldOfView = 62f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = DogfightSky.SkyTint;
        cam.nearClipPlane = 0.3f;
        // Far enough to hold the ground's corners from the ceiling.
        cam.farClipPlane = 900f;
        rig.AddComponent<AudioListener>();
        var data = rig.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        rig.AddComponent<DogfightCamera>();
        return rig;
    }
}
