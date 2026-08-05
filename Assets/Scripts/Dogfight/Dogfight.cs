using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

/// <summary>
/// DOGFIGHT — the sky duel, at whatever size the select screen asked for.
///
/// Each side fields <see cref="DogfightTeamSize.PerTeam"/> jets of its picked
/// robot. Squadrons land on a row of pads over the navy void, fold into jets
/// by stop motion in formation, climb out together, and race to a team score
/// that scales with the roster (five wrecks a pilot). One card seats the
/// player in the LEAD cyan jet with AI wingmates; the AI WAR card seats CPU
/// pilots everywhere and hands the couch a broadcast camera. Either way C
/// toggles chase/cockpit, and T walks the transformation triangle.
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
/// the hero's seams, and a driver that ran after its pawn would always be
/// one frame stale.</remarks>
[DefaultExecutionOrder(-50)]
public class Dogfight : MonoBehaviour
{
    // ------------------------------------------------------------------- tuning

    const float JetShield = 100f;

    /// <summary>Wrecks a PILOT is worth chasing — the team target is this
    /// times the team size, capped where a sortie stops being one sitting.</summary>
    const int KillsPerPilot = 5;
    const int KillsCap = 20;

    /// <summary>Seconds between the wreck HITTING THE DECK and the respawn —
    /// the crash is the shot, so the clock starts at impact, not at the kill.
    /// The broadcast lingers on the crater a beat less than this, so the cut
    /// always happens before the respawn yanks the subject across the sky.</summary>
    const float RespawnBeat = 2.5f;

    /// <summary>Failsafe on the fall itself: no funeral runs longer than this
    /// even if a wreck finds somewhere strange to never land.</summary>
    const float MaxFallSeconds = 10f;
    const float GraceSeconds = 2f;

    const float IntroSeconds = 0.9f;
    const float MorphSeconds = 1.8f;
    const float LaunchSeconds = 2.4f;

    /// <summary>Metres between the two pad rows, and along each row between
    /// wingmates. Close enough that a squadron's fold reads as one ceremony;
    /// far enough that the climb-out never braids.</summary>
    const float PadSpacing = 64f;
    const float WingSpacing = 11f;

    enum Stage { Intro, Morph, Launch, Fight, Over }

    /// <summary>One seat: a pawn and its respawn/grace clocks.</summary>
    class Slot
    {
        public JetPawn pawn;
        public float respawnAt = -1f;
        public float graceUntil;
    }

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
    int _teamSize = 1;
    int _killsToWin = KillsPerPilot;

    // Plain fields, not readonly: readonly collections are silently reset by
    // a recompile-during-Play, and a referee with amnesia mid-sortie is the
    // exact bug that rule exists for.
    List<Slot> _cyanTeam = new List<Slot>();
    List<Slot> _magentaTeam = new List<Slot>();
    List<GameObject> _pads = new List<GameObject>();

    /// <summary>The player's seat — lead jet of the cyan row.</summary>
    JetPawn Hero => _cyanTeam.Count > 0 ? _cyanTeam[0].pawn : null;

    Stage _stage = Stage.Intro;
    float _stageStart;
    int _cyanScore;
    int _magentaScore;

    Transform _lockCandidate;
    Vector3 _lockCandidateCenter;
    float _lockProgress;

    /// <summary>The touch player's chosen one: tapped straight onto a target,
    /// held until it dies, leaves, or a tap on empty sky lets it go. While it
    /// stands it IS the lock — no cone, no hold.</summary>
    Transform _tapLock;
    // Generous on purpose: the lock is the missile's tutorial, and a cone a
    // maneuvering AI keeps slipping out of is a tutorial nobody finishes.
    const float PlayerLockSeconds = 0.5f;
    const float PlayerLockCone = 16f;
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
        _teamSize = DogfightTeamSize.PerTeam;
        _killsToWin = Mathf.Min(KillsCap, KillsPerPilot * _teamSize);

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
        // War FX plumes parent under the stage root, so lingering smoke dies
        // with the set instead of drifting over the main menu.
        WarFx.Bin = _stageRoot.transform;
        // The battery goes up with the set but holds fire until the fight —
        // see the WeaponsFree assertion in Update.
        DogfightTurret.BuildRing();

        // Two rows of pads face each other across the void, one robot on
        // each — cyan the west row. The pawns themselves live at the scene
        // root (see the class note); only the set dressing is staged.
        for (int i = 0; i < _teamSize; i++)
        {
            float wing = (i - (_teamSize - 1) * 0.5f) * WingSpacing;
            SpawnSeat(_cyanTeam, _cyanRobot, 0,
                new Vector3(-PadSpacing * 0.5f, DogfightSky.PadY, wing), 90f);
            SpawnSeat(_magentaTeam, _magentaRobot, 1,
                new Vector3(PadSpacing * 0.5f, DogfightSky.PadY, wing), -90f);
        }

        _cameraRig = BuildCameraRig();
        _camera = _cameraRig.GetComponent<Camera>();
        _director = _cameraRig.GetComponent<DogfightCamera>();
        _director.Follow(Hero);

        _hud = DogfightHud.Build(transform);
        _hud.SetScoreTarget(_killsToWin);
        _hud.SetScore(0, 0);
        _hud.SetReticleVisible(false);
        _hud.SetPilotRowVisible(false);
        _hud.SetCaption("");

        // One bar per pilot, camera tag managed per frame in Readout.
        var cyanPawns = new List<JetPawn>();
        foreach (var slot in _cyanTeam)
            cyanPawns.Add(slot.pawn);
        var magentaPawns = new List<JetPawn>();
        foreach (var slot in _magentaTeam)
            magentaPawns.Add(slot.pawn);
        _hud.BuildPawnBars(cyanPawns, magentaPawns, _playerControls ? Hero : null);
        _hud.BuildHelp(_playerControls);

        // The glass hands. They appear on a real touch, or on '=' anywhere —
        // TouchControls' own testing toggle, which this mode follows.
        if (_playerControls)
            DogfightSticks.Build(transform);

        _stage = Stage.Intro;
        _stageStart = Time.time;
    }

    void SpawnSeat(List<Slot> team, int robotIndex, int teamId, Vector3 position, float yaw)
    {
        var entry = _roster != null && _roster.HasRobots
            ? _roster.Get(robotIndex)
            : default;
        var pawn = JetPawn.Spawn(entry, JetStagesFor(entry), teamId, position, yaw, JetShield);
        pawn.OnWrecked += Wrecked;
        pawn.OnWreckLanded += WreckLanded;
        team.Add(new Slot { pawn = pawn });

        // The pawn's origin is its flight CENTRE, so the standing robot's
        // feet hang half a fit below it — the pad top meets them there.
        _pads.Add(_sky.BuildPad(position + Vector3.down * (JetPawn.JetSize * 0.5f),
            MatchAnnouncer.TeamColor(teamId)));
    }

    /// <summary>
    /// The stop-motion set this robot flies. Its own where the jet pipeline
    /// has been run for it; the ranger's airframe (in this robot's team
    /// paint) where it has not.
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
                ForEachPawn(pawn =>
                {
                    if (pawn.HasMorph)
                        pawn.ShowMorph(progress);
                });
                if (elapsed >= MorphSeconds)
                {
                    Advance(Stage.Launch);
                    Launch();
                }
                break;

            case Stage.Launch:
                // The mode itself is the pilot for the climb-out, written
                // through the same seams everyone else uses. But the pad rows
                // FACE each other, and identical climbs met nose-to-nose
                // ~1.4 s after wheels-up (0.4 m at the pass — Separate()
                // shoved the formations through each other and it read as a
                // mid-air pile-up). So the climb is split by THROTTLE: cyan
                // takes the burner, magenta the brake, and the squadrons
                // cross with ~23 m of sky between them at ANY team size,
                // because the offset is vertical and the wing rows only
                // overlap laterally. A pitch split cannot do this — the
                // floor assist pitches every low jet toward 1 and erases it.
                var climb = new Vector2(0f, Mathf.Lerp(0.55f, 0.1f, elapsed / LaunchSeconds));
                foreach (var slot in _cyanTeam)
                    if (slot.pawn != null)
                    {
                        slot.pawn.Steer = climb;
                        slot.pawn.Throttle = 1f;
                    }
                foreach (var slot in _magentaTeam)
                    if (slot.pawn != null)
                    {
                        slot.pawn.Steer = climb;
                        slot.pawn.Throttle = -1f;
                    }
                if (elapsed >= LaunchSeconds)
                {
                    Advance(Stage.Fight);
                    OpenFight();
                }
                break;

            case Stage.Fight:
                if (_playerControls)
                    DrivePlayer();
                RunRespawns();
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

        // Same discipline for the glass: the gunfight ground layout exists
        // exactly while the player's hero stands on the deck as a robot or
        // tank, and folds back to the flight layout the moment that stops
        // being true — death, take-off, or the end of the sortie included.
        var heroNow = Hero;
        bool deck = _playerControls && _stage == Stage.Fight
            && heroNow != null && !heroNow.IsDown && !heroNow.Morphing
            && heroNow.Grounded && heroNow.CurrentForm != JetPawn.Form.Jet;
        DogfightSticks.SetGroundLayout(deck,
            heroNow != null && heroNow.CurrentForm == JetPawn.Form.Robot);

        // '?' is Shift+Slash on most boards; the bare key counts too.
        if (Input.GetKeyDown(KeyCode.Slash))
            _hud.ToggleHelp();

        ReadCameraKeys();
        foreach (var slot in _cyanTeam)
            RunGraceBlink(slot);
        foreach (var slot in _magentaTeam)
            RunGraceBlink(slot);
        Readout();
    }

    void ForEachPawn(System.Action<JetPawn> act)
    {
        foreach (var slot in _cyanTeam)
            if (slot.pawn != null)
                act(slot.pawn);
        foreach (var slot in _magentaTeam)
            if (slot.pawn != null)
                act(slot.pawn);
    }

    void Advance(Stage stage)
    {
        _stage = stage;
        _stageStart = Time.time;
    }

    void Launch()
    {
        ForEachPawn(pawn => pawn.FlightOn = true);
        foreach (var pad in _pads)
        {
            if (pad == null)
                continue;
            VfxUtil.SpawnBurst(pad.transform.position + Vector3.up * 0.6f,
                new Color(0.6f, 0.85f, 1f), 18, 5f, 0.14f);
            pad.SetActive(false);
        }
    }

    /// <summary>
    /// Guns free. Brains go on every CPU seat with mirrored break sides down
    /// the rows, and quarries dealt round-robin across the aisle — a squadron
    /// that all picked the same enemy would fly as one blob and die as one.
    /// </summary>
    void OpenFight()
    {
        _hud.Flash("FIGHT", new Color(1f, 0.75f, 0.2f));
        _hud.SetReticleVisible(_playerControls);
        _hud.SetPilotRowVisible(_playerControls);

        for (int i = 0; i < _cyanTeam.Count; i++)
        {
            bool playerSeat = _playerControls && i == 0;
            if (!playerSeat)
                AddBrain(_cyanTeam[i].pawn,
                    _magentaTeam[i % _magentaTeam.Count].pawn,
                    i % 2 == 0 ? 1f : -1f);
        }
        for (int i = 0; i < _magentaTeam.Count; i++)
            AddBrain(_magentaTeam[i].pawn,
                _cyanTeam[i % _cyanTeam.Count].pawn,
                i % 2 == 0 ? -1f : 1f);

        if (!_playerControls)
        {
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

    /// <summary>
    /// The stick, per form. As a JET the airframe chases the cursor (offset
    /// from the screen's centre is the steer, deadzoned so a parked mouse
    /// flies straight; arrows and A/D add on top — the TANK RAID rule); W
    /// boosts, S brakes. As a ROBOT or TANK the cursor becomes a free AIM
    /// (the ray under it, out to gun range) and WASD moves — drift, walk or
    /// drive, the pawn knows which. Everywhere: hold click for guns, right
    /// click for a locked missile, F for flares, T for the next fold.
    /// </summary>
    void DrivePlayer()
    {
        var hero = Hero;
        if (hero == null || hero.IsDown)
            return;

        bool glass = DogfightSticks.Showing;

        // On the deck as a robot or tank the controls ARE the gunfight
        // game's: FIRE becomes a held button, SHIFT or the stick's rim runs,
        // and SPACE or JUMP hops (robots only). The glass layout itself is
        // asserted from Update, which outlives this method's early returns.
        bool landed = hero.Grounded && hero.CurrentForm != JetPawn.Form.Jet;

        // The tap verbs land before the morph gate so CAM answers mid-fold;
        // the pawn refuses what it must (a fold during a fold, an unlocked
        // missile) on its own.
        if (Input.GetKeyDown(KeyCode.T) || DogfightSticks.TransformTapped)
            hero.RequestNextForm();
        if (DogfightSticks.CameraTapped)
            _director.ToggleView();
        if (Input.GetKeyDown(KeyCode.F) || DogfightSticks.FlaresTapped)
            hero.TryPopFlares();
        if (DogfightSticks.WorldTapped)
            PickTapTarget(DogfightSticks.WorldTapPoint);
        if (hero.Morphing)
            return;

        if (hero.CurrentForm == JetPawn.Form.Jet)
        {
            Vector2 mouse = new Vector2(
                (Input.mousePosition.x - Screen.width * 0.5f) / (Screen.height * 0.38f),
                (Input.mousePosition.y - Screen.height * 0.5f) / (Screen.height * 0.38f));
            if (mouse.magnitude < 0.08f || glass)
                mouse = Vector2.zero;
            var steer = Vector2.ClampMagnitude(mouse, 1f);

            float keyYaw = (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)
                           - (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f)
                           + (Input.GetKey(KeyCode.D) ? 1f : 0f)
                           - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            float keyPitch = (Input.GetKey(KeyCode.UpArrow) ? 1f : 0f)
                             - (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
            // The sticks ADD, they do not replace — the TANK RAID rule.
            steer += new Vector2(keyYaw, keyPitch) + DogfightSticks.Steer;

            hero.Steer = steer;
            float keyThrottle = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.LeftShift) ? 1f : 0f)
                                - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            // Right thumb up is the burner, down is the brake.
            hero.Throttle = glass && Mathf.Abs(DogfightSticks.AimStick.y) > 0.05f
                ? DogfightSticks.AimStick.y
                : keyThrottle;
        }
        else
        {
            hero.Steer = new Vector2(Input.GetAxisRaw("Horizontal"),
                Input.GetAxisRaw("Vertical")) + DogfightSticks.Steer;
            // The gunfight gait on the deck: SHIFT runs on keys; on glass the
            // rim of the stick runs, TouchControls' own sprint rule.
            hero.Sprint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                || DogfightSticks.Steer.sqrMagnitude > 0.85f;
            if (landed && (Input.GetKeyDown(KeyCode.Space) || DogfightSticks.JumpTapped))
                hero.RequestJump();
            if (glass)
            {
                AimByThumb(hero);
            }
            else
            {
                var ray = _camera.ScreenPointToRay(Input.mousePosition);
                hero.AimAt(ray.origin + ray.direction * 140f);
            }
        }

        // In the AIR the glass guns fire themselves — a thumb that must hold
        // FIRE is a thumb that cannot steer. On the deck FIRE is a held
        // button and SPACE belongs to the jump, both the gunfight game's
        // rules; keys keep the mouse trigger everywhere.
        hero.Firing = glass
            ? (landed ? DogfightSticks.GunHeld : _stage == Stage.Fight)
            : Input.GetMouseButton(0) || (!landed && Input.GetKey(KeyCode.Space));

        UpdatePlayerLock();
        if ((Input.GetMouseButtonDown(1) || DogfightSticks.MissileTapped)
            && _lockProgress >= PlayerLockSeconds)
            hero.TryFireMissile(_lockCandidate);
    }

    /// <summary>The right thumb's aim for the ground forms: a camera-relative
    /// direction while deflected, the nearest threat while at rest — nobody
    /// aims a tank with a stick they are also driving with.</summary>
    void AimByThumb(JetPawn hero)
    {
        Vector2 thumb = DogfightSticks.AimStick;
        if (thumb.sqrMagnitude > 0.01f)
        {
            Vector3 forward = _camera.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 right = _camera.transform.right;
            right.y = 0f;
            right.Normalize();
            Vector3 direction = right * thumb.x + forward * thumb.y;
            hero.AimAt(hero.Center + direction * 75f + Vector3.up * 6f);
            return;
        }

        // At rest the gun watches the CHOSEN one, then whatever is nearest.
        if (_tapLock != null)
        {
            var pawn = _tapLock.GetComponent<JetPawn>();
            var battery = _tapLock.GetComponent<DogfightTurret>();
            if (pawn != null && !pawn.IsDown)
            {
                hero.AimAt(pawn.Center);
                return;
            }
            if (battery != null && !battery.IsDead)
            {
                hero.AimAt(battery.Center);
                return;
            }
        }

        var enemy = JetPawn.NearestEnemy(hero.transform.position, 0, 120f);
        if (enemy != null)
        {
            hero.AimAt(enemy.Center);
            return;
        }
        var nearest = DogfightTurret.Nearest(hero.transform.position, 120f);
        if (nearest != null)
            hero.AimAt(nearest.Center);
    }

    /// <summary>
    /// A tap on the world: whichever enemy — jet, robot, tank or battery —
    /// sits under the thumb, within a forgiving radius, becomes the lock on
    /// the spot. A tap on empty sky lets the lock go. Screen distance picks
    /// the winner, because the thumb pointed at PIXELS, not at metres.
    /// </summary>
    void PickTapTarget(Vector2 screenPoint)
    {
        float reach = 90f * (Screen.height / 1080f);
        float bestSqr = reach * reach;
        Transform best = null;

        foreach (var pawn in JetPawn.All)
        {
            if (pawn == null || pawn.Team == 0 || pawn.IsDown)
                continue;
            if (TapDistance(pawn.Center, screenPoint, out float sqr) && sqr < bestSqr)
            {
                bestSqr = sqr;
                best = pawn.transform;
            }
        }
        foreach (var turret in DogfightTurret.All)
        {
            if (turret == null || turret.IsDead)
                continue;
            if (TapDistance(turret.Center, screenPoint, out float sqr) && sqr < bestSqr)
            {
                bestSqr = sqr;
                best = turret.transform;
            }
        }

        _tapLock = best;
        if (best != null)
            _hud.Flash("LOCKED", new Color(1f, 0.75f, 0.2f), 0.6f);
    }

    bool TapDistance(Vector3 world, Vector2 screenPoint, out float sqrPixels)
    {
        Vector3 screen = _camera.WorldToScreenPoint(world);
        sqrPixels = ((Vector2)screen - screenPoint).sqrMagnitude;
        return screen.z > 0f;
    }

    /// <summary>
    /// The seeker's eye. A tapped target IS the lock while it lives;
    /// otherwise the nearest enemy jet or turret the hero's AIM is actually
    /// on — hold it inside the cone and the diamond solidifies; look away
    /// and the lock starts over. A missile costs commitment, paid either in
    /// a tap or in a held nose.
    /// </summary>
    void UpdatePlayerLock()
    {
        var hero = Hero;

        if (_tapLock != null)
        {
            var shield = _tapLock.GetComponent<EnergyShield>();
            if (shield == null || shield.IsDown)
            {
                _tapLock = null;
            }
            else
            {
                var pawn = _tapLock.GetComponent<JetPawn>();
                var battery = _tapLock.GetComponent<DogfightTurret>();
                _lockCandidate = _tapLock;
                _lockCandidateCenter = pawn != null ? pawn.Center
                    : battery != null ? battery.Center
                    : WeaponUtil.Center(shield);
                _lockProgress = PlayerLockSeconds;
                hero.FocusTarget = _tapLock;
                return;
            }
        }
        hero.FocusTarget = null;

        Transform best = null;
        Vector3 bestCenter = Vector3.zero;
        float bestAngle = PlayerLockCone;

        Vector3 seeker = hero.AimDirection;
        var enemy = JetPawn.NearestEnemy(hero.transform.position, 0, PlayerLockRange);
        if (enemy != null)
        {
            float angle = Vector3.Angle(seeker, enemy.Center - hero.transform.position);
            if (angle < bestAngle)
            {
                bestAngle = angle;
                best = enemy.transform;
                bestCenter = enemy.Center;
            }
        }

        var turret = DogfightTurret.Nearest(hero.transform.position, PlayerLockRange);
        if (turret != null)
        {
            float angle = Vector3.Angle(seeker, turret.Center - hero.transform.position);
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
        var slot = FindSlot(pawn, out bool cyanSide);
        if (slot == null)
            return;

        if (_stage == Stage.Fight)
        {
            if (cyanSide)
                _magentaScore++;
            else
                _cyanScore++;
            _hud.SetScore(_cyanScore, _magentaScore);

            int scorer = cyanSide ? 1 : 0;
            _hud.Flash($"{MatchAnnouncer.TeamName(scorer)} SCORES",
                MatchAnnouncer.TeamColor(scorer));
        }

        if (_director.Subject == pawn)
            _director.Shake(1.1f);
        // A kill is THE shot: the broadcast rides the wreck down and lingers
        // on the crater before it cuts away (the camera holds its own clock).
        if (!_playerControls)
            _director.CoverWreck(pawn);

        // The real respawn clock starts at IMPACT (see WreckLanded); this is
        // only the failsafe for a fall that never finds the floor.
        slot.respawnAt = Time.time + MaxFallSeconds;

        if (_stage == Stage.Fight
            && (_cyanScore >= _killsToWin || _magentaScore >= _killsToWin))
        {
            // The match point gets the mushroom — but at the IMPACT, where
            // mushrooms grow, not at the kill shot in mid-air.
            _finalWreck = pawn;
            EndSortie();
        }
    }

    JetPawn _finalWreck;

    /// <summary>The wreck met the deck: now the respawn clock is honest, and
    /// a match-point crater gets its nuke.</summary>
    void WreckLanded(JetPawn pawn)
    {
        var slot = FindSlot(pawn, out _);
        if (slot != null)
            slot.respawnAt = Mathf.Min(slot.respawnAt, Time.time + RespawnBeat);
        if (pawn == _finalWreck)
        {
            _finalWreck = null;
            WarFx.Spawn(WarFx.Kind.Nuke,
                new Vector3(pawn.Center.x, 0f, pawn.Center.z));
        }
    }

    Slot FindSlot(JetPawn pawn, out bool cyanSide)
    {
        foreach (var slot in _cyanTeam)
            if (slot.pawn == pawn)
            {
                cyanSide = true;
                return slot;
            }
        foreach (var slot in _magentaTeam)
            if (slot.pawn == pawn)
            {
                cyanSide = false;
                return slot;
            }
        cyanSide = false;
        return null;
    }

    void RunRespawns()
    {
        for (int i = 0; i < _cyanTeam.Count; i++)
            RunRespawn(_cyanTeam[i], i);
        for (int i = 0; i < _magentaTeam.Count; i++)
            RunRespawn(_magentaTeam[i], i);
    }

    void RunRespawn(Slot slot, int index)
    {
        if (slot.pawn == null)
            return;
        if (slot.respawnAt > 0f && Time.time >= slot.respawnAt)
        {
            slot.respawnAt = -1f;
            RespawnPawn(slot.pawn, index);
            slot.graceUntil = Time.time + GraceSeconds;
        }
        if (slot.pawn.Shield != null)
            slot.pawn.Shield.invulnerable = Time.time < slot.graceUntil;
    }

    /// <summary>Back onto the spawn ring on the far side from the enemy's
    /// centre of mass, wingmates fanned along the ring so a squadron never
    /// respawns as a single stacked target — always facing the fight.</summary>
    void RespawnPawn(JetPawn pawn, int index)
    {
        Vector3 centroid = Vector3.zero;
        int enemies = 0;
        foreach (var other in JetPawn.All)
        {
            if (other == null || other.Team == pawn.Team || other.IsDown)
                continue;
            centroid += other.transform.position;
            enemies++;
        }

        Vector3 away = Vector3.right * (pawn.Team == 0 ? -1f : 1f);
        if (enemies > 0)
        {
            Vector3 flat = centroid / enemies;
            flat.y = 0f;
            if (flat.sqrMagnitude > 1f)
                away = -flat.normalized;
        }

        // Fan the seats: 0 dead ahead, then ±14, ±28... so wingmates arrive
        // abreast rather than nose-to-tail.
        float fan = (index % 2 == 0 ? 1f : -1f) * Mathf.Ceil(index / 2f) * 14f;
        away = Quaternion.Euler(0f, fan, 0f) * away;

        Vector3 at = away * DogfightSky.SpawnRing + Vector3.up * DogfightSky.SpawnAltitude;
        float yaw = Quaternion.LookRotation(-away, Vector3.up).eulerAngles.y;
        pawn.Respawn(at, yaw);
    }

    /// <summary>The grace made visible: a blink nothing else in the sky has.
    /// Skipped for the cockpit subject — the camera owns that jet's visibility.</summary>
    void RunGraceBlink(Slot slot)
    {
        var pawn = slot.pawn;
        if (pawn == null || pawn.IsDown)
            return;
        if (_director.Cockpit && _director.Subject == pawn)
            return;
        if (Time.time < slot.graceUntil)
            pawn.SetVisible(Mathf.FloorToInt((slot.graceUntil - Time.time) * 9f) % 2 == 0);
        else if (Time.time < slot.graceUntil + 0.5f)
            pawn.SetVisible(true);
    }

    void EndSortie()
    {
        Advance(Stage.Over);
        int winner = _cyanScore >= _killsToWin ? 0 : 1;
        Color accent = MatchAnnouncer.TeamColor(winner);
        _hud.Flash($"{MatchAnnouncer.TeamName(winner)} TAKES THE SKY", accent, 3f);
        _hud.ShowOver($"{MatchAnnouncer.TeamName(winner)} WINS",
            $"{_cyanScore}  —  {_magentaScore}\nESC or MENU to fly again", accent);
        _hud.SetCaption("");
    }

    // ------------------------------------------------------------------ readout

    void Readout()
    {
        if (_hud == null)
            return;

        // Every pilot's own bar, and the camera tag on whoever is on screen.
        _hud.UpdatePawnBars(_director.Subject);
        _hud.SetHelpButtonVisible(!DogfightSticks.Showing);

        var subject = _director.Subject;
        if (subject != null)
        {
            _hud.SetSpeed(subject.ReadoutSpeed,
                subject.CurrentForm == JetPawn.Form.Jet
                && subject.Speed > JetPawn.SpeedCruise + 4f);
            _hud.SetForm(subject.Morphing ? "FOLDING" : FormName(subject.CurrentForm),
                MatchAnnouncer.TeamColor(subject.Team));
        }

        var hero = Hero;
        if (_playerControls && _stage == Stage.Fight && hero != null)
        {
            _hud.SetReticleHot(hero.AssistEngaged);
            UpdateTargetArrow();
            _hud.SetOrdnance(hero.MissileReadyFraction, hero.MissileReady, hero.FlareCharges);
            _hud.SetLockDiamond(
                _lockCandidate != null
                    ? _camera.WorldToViewportPoint(_lockCandidateCenter)
                    : Vector3.back,
                _lockCandidate == null ? 0 : _lockProgress >= PlayerLockSeconds ? 2 : 1);
            WarnOfMissiles();
        }
    }

    static string FormName(JetPawn.Form form) =>
        form == JetPawn.Form.Jet ? "JET" : form == JetPawn.Form.Robot ? "ROBOT" : "TANK";

    /// <summary>The cockpit's threat receiver: any missile with the player's
    /// name on it inside warning range keeps the INCOMING line lit (the HUD
    /// throttles the shouting).</summary>
    void WarnOfMissiles()
    {
        var hero = Hero;
        if (hero == null || hero.IsDown)
            return;
        foreach (var missile in DogfightMissile.All)
        {
            if (missile == null || missile.Quarry != hero.transform)
                continue;
            if ((missile.transform.position - hero.transform.position).sqrMagnitude < 55f * 55f)
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
        var hero = Hero;
        var enemy = JetPawn.NearestEnemy(hero.transform.position, 0);
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
        // Plumes still burning under the stage root go down with it.
        WarFx.Bin = null;

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
