using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

/// <summary>
/// TANK RAID — the vertical scroller.
///
/// The hero drives up a battlefield with no end while tanks and robots come down
/// it. The left stick is a heading the hull has to turn onto; the right stick is
/// where the gun points, and the two are independent — that separation is the
/// whole mode. The trigger is automatic.
///
/// It owns its world the way <see cref="CommanderController"/> and
/// <see cref="ChineseRun"/> own theirs: entering deactivates the arena's
/// environment and hides its cast, everything is built under one stage root, and
/// <see cref="Teardown"/> hands the arena back through
/// <c>ArenaRuntime.Load</c>. The one thing it does NOT do is bake a NavMesh —
/// nothing in this mode paths, because the ground under the fight is recycled
/// every few seconds and re-baking that would cost more than the raiders are
/// worth.
///
/// THE PAWNS ARE NOT UNDER THE STAGE ROOT. Bolts resolve their victim through
/// <c>hit.transform.root</c>, so every tank lives at the scene root and is swept
/// by <see cref="TankPawn.DespawnAll"/> instead. The same rule Commander runs on,
/// and the same reason.
/// </summary>
/// <remarks>
/// Runs BEFORE the pawns (and alongside <see cref="TankBrain"/>, at the same
/// order): every driver writes <see cref="TankPawn.Drive"/> and the pawn reads
/// and clears it, so a driver that ran after its pawn would always be one frame
/// stale. Stated rather than left to Unity's undefined component order, which
/// would otherwise make the hero's response to the stick differ from build to
/// build.
/// </remarks>
[DefaultExecutionOrder(-50)]
public class TankRaid : MonoBehaviour
{
    // ------------------------------------------------------------------- tuning

    // ONE TANK AGAINST AN ARMY. The hero is not a raider with a bigger number
    // on it: it out-shields the heaviest raider six to one and out-guns it by
    // more than ten, so a raider that catches the hero alone loses, and it takes
    // a crossfire of them to be a threat at all. See TankArsenal for the guns —
    // the cannon one-shots a walker and two-shots a raider tank.
    //
    // Regen matters as much as the pool: it is slow enough that a bad stretch
    // still costs a continue, and quick enough that a player who breaks contact
    // and clears a flank is rewarded for it.
    /// <summary>Public because <see cref="TankBoons"/> recomputes from these
    /// rather than multiplying live values — see its class note.</summary>
    public const float HeroShield = 600f;
    public const float HeroRegenPerSecond = 22f;

    const float HeroRegenDelay = 3.5f;
    const float HeroSpeed = 12f;
    const float HeroTurn = 190f;

    const float RaiderTankShield = 110f;
    const float RaiderTankSpeed = 7f;
    const float RaiderWalkerShield = 50f;
    const float RaiderWalkerSpeed = 9.5f;

    /// <summary>Continues. Three is the arcade number and it is the right one.</summary>
    const int Lives = 3;

    /// <summary>Seconds the hero is off the field after a de-rez.</summary>
    const float ContinueBeat = 1.5f;

    /// <summary>Seconds of grace after continuing, so a hero does not come back
    /// into the shell that was already in the air when it went down.</summary>
    const float GraceSeconds = 2.5f;

    /// <summary>
    /// Where raiders arrive: past the top of the screen, so nothing ever appears
    /// out of thin air in view. Expressed against the camera's own reach rather
    /// than as a number, because the day someone changes the pitch is the day a
    /// hand-picked number quietly starts spawning tanks on camera.
    /// </summary>
    const float SpawnLead = TankRaidCamera.VisibleAhead + 14f;

    /// <summary>
    /// How many raiders may be on the field at once, at the start and at full
    /// difficulty. Raised alongside the hero: a cannon that two-shots a raider
    /// tank clears the old cap faster than it refilled, and an army that is
    /// mostly not there is not an army. Numbers, not toughness, is the honest
    /// way to pressure a hero that is meant to be stronger than any one of them.
    /// </summary>
    const int StartingPressure = 5;
    const int MaxPressure = 14;

    /// <summary>Metres of progress it takes to add one to the pressure.</summary>
    const float MetresPerStep = 260f;

    const float SpawnInterval = 0.9f;

    /// <summary>Odds a wreck leaves anything at all.</summary>
    const float DropChance = 0.45f;

    /// <summary>Recruits the hero may keep at once, before any factory is taken.</summary>
    const int BaseAllyCap = 3;

    /// <summary>Shield and speed a recruit drives with.</summary>
    const float AllyShield = 190f;
    const float AllySpeed = 10.5f;

    /// <summary>Metres to the first outpost, and between them after that.</summary>
    const float FirstOutpostAt = 420f;
    const float OutpostSpacing = 560f;

    /// <summary>How far ahead of the frontier an outpost is planted. Well past the
    /// top of the screen, so it comes over the horizon rather than appearing.</summary>
    const float OutpostLead = TankRaidCamera.VisibleAhead + 34f;

    const string BestKey = "PhotonArena.TankRaidBest";

    // -------------------------------------------------------------------- state

    GameObject _stageRoot;
    GameObject _cameraRig;
    Camera _camera;
    TankRaidCamera _director;
    TankField _field;
    TankRaidHud _hud;
    TankSticks _sticks;
    ArenaBlockManager _blockManager;
    readonly List<GameObject> _hiddenCharacters = new List<GameObject>();

    RobotRoster _roster;
    int _heroRobot;
    TankPawn _hero;

    /// <summary>False in AI v AI, where <see cref="TankPilot"/> has the controls.</summary>
    bool _playerDrives = true;

    /// <summary>Everything captured so far, and what it is worth. See TankBoons.</summary>
    readonly TankBoons _boons = new TankBoons();

    int _lives = Lives;
    int _wrecks;
    int _outpostsTaken;
    int _best;
    float _furthest;
    float _nextSpawn;
    float _nextOutpostAt = FirstOutpostAt;
    float _graceUntil;
    bool _running;
    bool _continuing;

    public static TankRaid Begin(GameModeController owner, RobotRoster roster, int heroRobot,
        bool playerDrives)
    {
        var go = new GameObject("TankRaid");
        go.transform.SetParent(owner.transform, false);
        var raid = go.AddComponent<TankRaid>();
        raid._roster = roster;
        raid._heroRobot = heroRobot;
        raid._playerDrives = playerDrives;
        raid.Setup();
        return raid;
    }

    // -------------------------------------------------------------------- set-up

    void Setup()
    {
        _best = PlayerPrefs.GetInt(BestKey, 0);
        TankField.ResetFrontier();

        var player = FindFirstObjectByType<PlayerBrain>();
        if (player != null)
            Hide(player.gameObject);
        foreach (var bot in FindObjectsByType<AIBrain>(FindObjectsSortMode.None))
            Hide(bot.gameObject);

        _blockManager = FindFirstObjectByType<ArenaBlockManager>();
        if (_blockManager != null)
            _blockManager.enabled = false;

        // The arena's environment goes dark under this mode's own battlefield.
        // Found through the NavMeshSurface because that is the object every
        // arena hangs its world off, whichever arena is loaded.
        var surface = FindFirstObjectByType<NavMeshSurface>(FindObjectsInactive.Include);
        Transform environment = surface != null ? surface.transform : null;
        if (environment != null)
            foreach (Transform child in environment)
                child.gameObject.SetActive(false);

        _stageRoot = new GameObject("RaidStage");
        if (environment != null)
            _stageRoot.transform.SetParent(environment, false);

        _field = TankField.Build(_stageRoot.transform);

        var entry = Entry(_heroRobot);
        _hero = TankPawn.Spawn(entry, TankPawn.Chassis.Tank, 0, Vector3.zero, 0f, HeroShield,
            TankArsenal.Role.Hero);
        _hero.speed = HeroSpeed;
        _hero.turnSpeed = HeroTurn;
        _hero.HeldAtTrail = true;
        _hero.Shield.regenDelay = HeroRegenDelay;
        _hero.Shield.regenPerSecond = HeroRegenPerSecond;
        _hero.OnWrecked += HeroWrecked;
        if (!_playerDrives)
            _hero.gameObject.AddComponent<TankPilot>();

        _cameraRig = BuildCameraRig();
        _camera = _cameraRig.GetComponent<Camera>();
        _director = _cameraRig.GetComponent<TankRaidCamera>();
        _director.Follow(_hero.transform);

        _hud = TankRaidHud.Build(transform);
        _hud.OnRestart = Restart;
        _hud.SetLives(_lives);
        _hud.SetProgress(0, 0);
        // No thumb sticks in AI v AI — there is nothing for a thumb to do, and a
        // stick that appears under a finger would drive nothing.
        if (_playerDrives)
            _sticks = TankSticks.Build(transform);

        _running = true;
        _nextSpawn = 0.6f;
    }

    RobotRoster.Entry Entry(int index) =>
        _roster != null && _roster.HasRobots ? _roster.Get(index) : default;

    /// <summary>
    /// A raider's model. Never the hero's own robot where the roster has more
    /// than one: an enemy wearing your chassis in your enemy's colours is a
    /// second of "wait, is that me?" every time one drives on screen.
    /// </summary>
    RobotRoster.Entry RaiderEntry()
    {
        if (_roster == null || !_roster.HasRobots)
            return default;
        int count = _roster.robots.Length;
        if (count <= 1)
            return _roster.Get(0);
        int pick = Random.Range(0, count - 1);
        return _roster.Get(pick >= _heroRobot ? pick + 1 : pick);
    }

    // --------------------------------------------------------------- every frame

    void Update()
    {
        if (!_running)
        {
            // The over panel is up (the only way _running goes false while this
            // object still exists). ENTER or R restarts without the mouse.
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.R))
                Restart();
            return;
        }
        if (_hero == null)
            return;

        float dt = Time.deltaTime;

        // The field only advances while the hero is actually on it. A frontier
        // that crept on through the continue beat would push the wreck site off
        // the bottom of the screen and drop the hero back in behind its own loot.
        if (!_continuing)
            _field.Advance(_hero.transform.position.z, dt);

        if (_playerDrives)
            DriveHero();
        RunRaiders();
        RunPickups(dt);
        Spawn(dt);
        PlantOutpost();
        Readout();
    }

    /// <summary>
    /// Both thumbs, or the keyboard and mouse, or a mixture.
    ///
    /// THE STICKS ADD, THEY DO NOT REPLACE. '=' puts the on-screen controls up on
    /// any machine (that is TouchControls' own testing toggle, and this mode
    /// follows it), and a player who does that on a desktop has not stopped
    /// having a keyboard — a stick at rest must not read as "hold still" and
    /// take WASD away. Whichever input is actually being pushed wins; the stick
    /// gets first refusal because a thumb on it is unambiguous.
    ///
    /// The drive stick is a WORLD heading rather than a hull-relative one: the
    /// camera never rotates in this mode, so screen-up is always up the field,
    /// and steering that stayed relative to a hull the player just spun would
    /// reverse the controls at the worst possible moment.
    /// </summary>
    void DriveHero()
    {
        if (_continuing || _hero.IsDown)
            return;

        Vector2 drive = TankSticks.Drive;
        if (drive.sqrMagnitude < 1e-4f)
            drive = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        _hero.Drive = drive;

        // The gun: the right stick while a thumb is on it, the mouse otherwise.
        // The mouse aims at a POINT on the ground rather than along a direction,
        // which is what lets the player put the crosshair on a raider instead of
        // pushing the turret at it.
        Vector2 aimStick = TankSticks.Aim;
        if (aimStick.sqrMagnitude > 1e-4f)
        {
            _hero.AimAlong(new Vector3(aimStick.x, 0f, aimStick.y));
        }
        else if (!TankSticks.Showing &&
                 TankRaidCamera.GroundUnder(_camera, Input.mousePosition, out var ground))
        {
            _hero.AimAt(ground);
        }
        else
        {
            // Nothing steering the gun: hold it on whatever is nearest, so a
            // player using one thumb still fights back.
            var nearest = TankPawn.NearestEnemy(_hero.transform.position, 0, 55f);
            if (nearest != null)
                _hero.AimAt(nearest.Center);
        }

        // Constant fire IS the mode: there is no trigger to hold, and the only
        // gate on a shot leaving is whether the barrel has arrived (TankPawn).
        _hero.Firing = true;
    }

    /// <summary>
    /// Sweep what the field has left behind, and keep the army pointed at the
    /// hero.
    ///
    /// The favourite is only ever stamped on RAIDERS. A recruit's brain must
    /// keep choosing its own targets — pointing it at the hero would have the
    /// escort attack the thing it is escorting — and an outpost's is set once,
    /// when it is planted.
    /// </summary>
    void RunRaiders()
    {
        var pawns = TankPawn.All;
        for (int i = pawns.Count - 1; i >= 0; i--)
        {
            var pawn = pawns[i];
            if (pawn == null || pawn == _hero)
                continue;
            // Swept once it drops off the bottom of the screen: anything left
            // trailing the frontier forever is a pawn burning a slot in the
            // pressure budget for a fight that can no longer happen. Recruits go
            // the same way — one that cannot keep up has been lost, and saying so
            // beats an invisible tank fighting somewhere off camera.
            if (TankField.FallenBehind(pawn.transform.position, 26f))
            {
                Destroy(pawn.gameObject);
                continue;
            }
            if (pawn.Team == 0 || pawn.Kind == TankPawn.Chassis.Structure)
                continue;
            var brain = pawn.GetComponent<TankBrain>();
            if (brain != null && brain.favourite == null && !_continuing)
                brain.favourite = _hero;
        }
    }

    /// <summary>Recruits currently driving with the hero.</summary>
    int AllyCount()
    {
        int count = 0;
        foreach (var pawn in TankPawn.All)
            if (pawn != null && pawn != _hero && pawn.Team == 0 && !pawn.IsDown)
                count++;
        return count;
    }

    int AllyCap => BaseAllyCap + _boons.allyCapBonus;

    /// <summary>
    /// One tank changes sides and falls in beside the hero.
    ///
    /// Leashed to the hero rather than free: a recruit that chased a retreating
    /// raider would be led off the bottom of the screen and swept, which reads to
    /// the player as the reward evaporating for no reason.
    /// </summary>
    TankPawn Recruit(Vector3 where)
    {
        var ally = TankPawn.Spawn(RaiderEntry(), TankPawn.Chassis.Tank, 0, where, 0f,
            AllyShield, TankArsenal.Role.Ally);
        ally.speed = AllySpeed;
        ally.turnSpeed = 165f;
        ally.OnWrecked += AllyWrecked;

        var brain = ally.gameObject.AddComponent<TankBrain>();
        brain.anchor = _hero.transform;
        brain.leash = 20f;
        brain.standoff = 16f;
        brain.idleDrift = 0f;               // it holds station, it does not retreat
        brain.orbit = Random.value < 0.5f ? 1f : -1f;
        return ally;
    }

    void AllyWrecked(TankPawn pawn)
    {
        _director.Shake(0.35f);
        pawn.Wreck();
        _hud.Flash("RECRUIT DOWN", new Color(0.35f, 0.7f, 1f), 1.1f);
    }

    void RunPickups(float dt)
    {
        var pickups = TankPickup.All;
        for (int i = pickups.Count - 1; i >= 0; i--)
            if (pickups[i] != null)
                pickups[i].Tick(_continuing ? null : _hero, dt);
    }

    /// <summary>
    /// Feed the field. Pressure — how many raiders may be alive at once — is the
    /// difficulty curve, and it is driven by DISTANCE rather than by time: a
    /// player who is fighting carefully is going slowly, and should not be
    /// punished for it by a clock.
    /// </summary>
    void Spawn(float dt)
    {
        if (_continuing)
            return;

        _nextSpawn -= dt;
        if (_nextSpawn > 0f)
            return;
        _nextSpawn = SpawnInterval;

        int pressure = Mathf.Min(MaxPressure,
            StartingPressure + Mathf.FloorToInt(_furthest / MetresPerStep));
        // Raiders only. Counting the hero's recruits here would have the army
        // thin out exactly as the player got stronger, and counting outposts
        // would stop the field feeding entirely while one stood.
        int alive = 0;
        foreach (var pawn in TankPawn.All)
            if (pawn != null && pawn.Team == 1 && pawn.Kind != TankPawn.Chassis.Structure)
                alive++;
        if (alive >= pressure)
            return;

        // Walkers early, tanks increasingly: the first minute should teach the
        // controls against something forgiving.
        float tankOdds = Mathf.Lerp(0.3f, 0.7f, Mathf.Clamp01(_furthest / 900f));
        bool tank = Random.value < tankOdds;

        float x = Random.Range(-TankField.HalfWidth + 4f, TankField.HalfWidth - 4f);
        var where = new Vector3(x, 0f, TankField.Frontier + SpawnLead);

        var raider = TankPawn.Spawn(RaiderEntry(),
            tank ? TankPawn.Chassis.Tank : TankPawn.Chassis.Walker, 1, where, 180f,
            tank ? RaiderTankShield : RaiderWalkerShield, TankArsenal.Role.Raider);
        raider.speed = tank ? RaiderTankSpeed : RaiderWalkerSpeed;
        raider.turnSpeed = tank ? 110f : 260f;
        raider.OnWrecked += RaiderWrecked;

        var brain = raider.gameObject.AddComponent<TankBrain>();
        brain.favourite = _hero;
        brain.standoff = tank ? 19f : 13f;
        brain.orbit = Random.value < 0.5f ? 1f : -1f;
    }

    // -------------------------------------------------------------------- outposts

    /// <summary>
    /// Put a structure on the field, every few hundred metres.
    ///
    /// It is planted well past the top of the screen and off the centre line, so
    /// it comes over the horizon as a thing the player can see coming and decide
    /// about — which is the entire reason it exists. Everything else in the mode
    /// arrives wanting a fight; this is the one thing that waits to be picked.
    ///
    /// One at a time. Two outposts in shot at once turns a decision into a
    /// shopping list, and the field is only thirty metres wide.
    /// </summary>
    void PlantOutpost()
    {
        if (_continuing || _furthest < _nextOutpostAt)
            return;
        foreach (var pawn in TankPawn.All)
            if (pawn != null && pawn.Kind == TankPawn.Chassis.Structure)
                return;                                  // one is still standing

        _nextOutpostAt = _furthest + OutpostSpacing;

        string key = TankOutpost.RollBuilding(_furthest);
        var reward = TankOutpost.RewardFor(key);
        // Off to one side, and far enough in that it can be driven past: a
        // structure across the middle would be a wall, not a choice.
        float side = Random.value < 0.5f ? -1f : 1f;
        var where = new Vector3(side * Random.Range(5f, TankField.HalfWidth - 5.5f), 0f,
            TankField.Frontier + OutpostLead);

        var structure = TankPawn.Spawn(default, TankPawn.Chassis.Structure, 1, where,
            Random.Range(0f, 360f), TankOutpost.ShieldFor(key), TankArsenal.Role.Outpost, key);
        var outpost = structure.gameObject.AddComponent<TankOutpost>();
        outpost.buildingKey = key;
        outpost.reward = reward;
        structure.OnWrecked += OutpostWrecked;

        var brain = structure.gameObject.AddComponent<TankBrain>();
        brain.favourite = _hero;
        // It cannot chase, so it must not try to shoot what it cannot reach.
        brain.engageRange = 34f;

        _hud.Flash($"{reward.title} AHEAD", new Color(1f, 0.55f, 0.25f), 2.2f);
    }

    /// <summary>
    /// An outpost is down, and the run is permanently better for it. This is the
    /// mode's only lasting progression — see <see cref="TankBoons"/>.
    /// </summary>
    void OutpostWrecked(TankPawn pawn)
    {
        _outpostsTaken++;
        Vector3 where = pawn.transform.position;
        var outpost = pawn.GetComponent<TankOutpost>();
        pawn.Wreck();
        _director.Shake(1.4f);

        if (outpost == null)
            return;

        _boons.Add(outpost.reward);
        _boons.Apply(_hero);
        _hud.SetBoons(_boons.Summary);
        _hud.Flash($"{outpost.reward.title}   ·   {outpost.reward.blurb}",
            new Color(1f, 0.85f, 0.35f), 2.6f);

        // Crews walking out of the wreckage. Placed in a fan in FRONT of it, so
        // they arrive between the hero and whatever comes next rather than
        // materialising behind the fight.
        for (int i = 0; i < outpost.reward.recruits && AllyCount() < AllyCap; i++)
            Recruit(where + new Vector3((i - 0.5f) * 5f, 0f, -6f));

        // And the rest of it as loot on the ground, so a capture pays out
        // immediately as well as permanently.
        for (int i = 0; i < 2; i++)
        {
            var drop = TankPickup.Drop(_stageRoot.transform,
                where + new Vector3(Random.Range(-4f, 4f), 0f, Random.Range(-6f, -2f)),
                i == 0 ? TankPickup.Kind.Repair : TankPickup.Kind.Weapon);
            drop.OnCollected += Collect;
        }
    }

    void Readout()
    {
        float z = _hero.transform.position.z;
        _furthest = Mathf.Max(_furthest, z);
        _hud.SetProgress(Mathf.RoundToInt(Mathf.Max(0f, _furthest)), _wrecks);
        _hud.SetShield(_hero.Shield.Normalized, _hero.Shield.Current);
        _hud.SetEscort(AllyCount(), AllyCap);

        var gun = _hero.CurrentGun;
        var loadout = _hero.Loadout;
        bool special = loadout != null && loadout.Special != null;
        _hud.SetWeapon(gun != null ? gun.weaponName : "",
            special ? loadout.SpecialSecondsLeft : -1f, TankArsenal.PodSeconds);
    }

    // ----------------------------------------------------------------- casualties

    /// <summary>
    /// A raider is down. It leaves the field in a burst and, just under half the
    /// time, leaves something behind.
    ///
    /// WHAT it leaves is chosen against the state of the run, not rolled flat.
    /// The repair odds rise as the hero's shield falls, so a run going badly is
    /// handed a way back and one going well is handed guns instead of medicine it
    /// does not need. Recruit beacons stop dropping once the escort is full,
    /// because a pickup that does nothing is worse than no pickup — the player
    /// drove across the field for it.
    /// </summary>
    void RaiderWrecked(TankPawn pawn)
    {
        _wrecks++;
        Vector3 where = pawn.transform.position;
        bool tank = pawn.Kind == TankPawn.Chassis.Tank;
        pawn.Wreck();

        // Tanks are worth more than walkers, so they are likelier to pay out.
        if (Random.value > DropChance * (tank ? 1.25f : 0.8f))
            return;

        var pickup = TankPickup.Drop(_stageRoot.transform, where, RollDrop());
        pickup.OnCollected += Collect;
    }

    TankPickup.Kind RollDrop()
    {
        float hurt = _hero != null && _hero.Shield != null ? 1f - _hero.Shield.Normalized : 0f;
        if (Random.value < Mathf.Lerp(0.25f, 0.8f, hurt))
            return TankPickup.Kind.Repair;
        // A beacon is the rarer of the two remaining, and only while there is
        // room in the escort for it to mean anything.
        if (AllyCount() < AllyCap && Random.value < 0.35f)
            return TankPickup.Kind.Recruit;
        return TankPickup.Kind.Weapon;
    }

    void Collect(TankPickup pickup)
    {
        if (_hero == null || _hero.IsDown)
            return;

        switch (pickup.Sort)
        {
            case TankPickup.Kind.Repair:
                _hero.Shield.Restore(_hero.Shield.maxShield * 0.4f);
                _hud.Flash("REPAIRED", new Color(0.35f, 1f, 0.6f));
                return;

            case TankPickup.Kind.Recruit:
                if (AllyCount() >= AllyCap)
                {
                    // The escort filled up between the drop and the pickup. Pay
                    // out in shield rather than nothing at all.
                    _hero.Shield.Restore(_hero.Shield.maxShield * 0.25f);
                    _hud.Flash("ESCORT FULL", new Color(0.35f, 0.7f, 1f));
                    return;
                }
                // Beside the hero, not on top of it: a tank materialising inside
                // another tank is two pawns shoving each other apart on frame one.
                Recruit(_hero.transform.position + new Vector3(
                    Random.value < 0.5f ? -4.5f : 4.5f, 0f, -2f));
                _hud.Flash("RECRUIT JOINED", new Color(0.35f, 0.7f, 1f));
                return;

            default:
                var granted = _hero.Loadout != null ? _hero.Loadout.GrantRandom() : null;
                // Re-stamped: a pod's gun is built once at spawn and knows nothing
                // about the outposts captured since.
                _boons.Apply(_hero);
                _hud.Flash(granted != null ? granted.weaponName.ToUpperInvariant() : "WEAPON POD",
                    new Color(1f, 0.75f, 0.2f));
                return;
        }
    }

    void HeroWrecked(TankPawn pawn)
    {
        if (_continuing || !_running)
            return;
        _lives--;
        _hud.SetLives(Mathf.Max(0, _lives));
        _director.Shake(1.2f);
        VfxUtil.Explosion(_hero.Center, MatchAnnouncer.TeamColor(0), 1.8f);
        StartCoroutine(_lives > 0 ? Continue() : EndRun());
    }

    /// <summary>
    /// A continue: off the field for a beat, then back on it in front of the
    /// frontier with the field cleared far enough ahead to see what is coming.
    /// The raiders are NOT wiped — a continue that emptied the screen would make
    /// dying the cheapest way to clear a bad wave.
    /// </summary>
    IEnumerator Continue()
    {
        _continuing = true;
        _hero.Firing = false;
        _hero.SetVisible(false);
        // Drop the army's fixation on a hero that is no longer on the field.
        // Only theirs: a recruit never had one, and clearing an outpost's would
        // just be re-stamped by RunRaiders the moment the hero is back.
        foreach (var pawn in TankPawn.All)
        {
            if (pawn == null || pawn.Team == 0)
                continue;
            var brain = pawn.GetComponent<TankBrain>();
            if (brain != null)
                brain.favourite = null;
        }

        _hud.Flash($"{_lives} LEFT", new Color(1f, 0.35f, 0.3f), ContinueBeat);
        yield return new WaitForSeconds(ContinueBeat);

        // Back at the bottom of the strip, on the centre line, pointing up the
        // field — the same place and posture the run started in.
        _hero.transform.position = new Vector3(0f, 0f, TankField.Frontier - TankField.Trail + 6f);
        _hero.transform.rotation = Quaternion.identity;
        _hero.Revive();
        _hero.SetVisible(true);
        VfxUtil.SpawnBurst(_hero.Center, MatchAnnouncer.TeamColor(0), 24, 7f, 0.16f);

        // Re-stamped after the shield was refilled: Rematerialize fills to
        // maxShield, and the boons are what decide what maxShield is. From base
        // every time, so three continues do not hand out three reactors.
        _boons.Apply(_hero);

        _graceUntil = Time.time + GraceSeconds;
        _hero.Shield.invulnerable = true;
        _continuing = false;

        yield return new WaitForSeconds(GraceSeconds);
        if (_hero != null && _hero.Shield != null)
            _hero.Shield.invulnerable = false;
    }

    /// <summary>
    /// Out of continues. The raiders go with the hero — not out of mercy, but
    /// because Update stops running here, and with it the sweep that keeps
    /// raiders from trailing off the bottom of the world. A quiet field under
    /// the panel is the honest end state.
    /// </summary>
    IEnumerator EndRun()
    {
        _running = false;
        _hero.Firing = false;
        _hero.SetVisible(false);

        foreach (var pawn in TankPawn.All)
            if (pawn != null && pawn != _hero)
                VfxUtil.Explosion(pawn.Center, MatchAnnouncer.TeamColor(1), 0.8f);
        yield return null;                      // let the bursts spawn before the bodies go
        for (int i = TankPawn.All.Count - 1; i >= 0; i--)
        {
            var pawn = TankPawn.All[i];
            if (pawn != null && pawn != _hero)
                Destroy(pawn.gameObject);
        }
        TankPickup.DespawnAll();

        BankBest();
        _hud.ShowOver(Mathf.RoundToInt(Mathf.Max(0f, _furthest)), _wrecks, _outpostsTaken, _best);
    }

    void BankBest()
    {
        int reached = Mathf.RoundToInt(Mathf.Max(0f, _furthest));
        if (reached <= _best)
            return;
        _best = reached;
        PlayerPrefs.SetInt(BestKey, _best);
        PlayerPrefs.Save();
    }

    // --------------------------------------------------------------- lifecycle

    /// <summary>
    /// DRIVE AGAIN, from the over panel. Routed through the controller's full
    /// teardown-and-begin rather than resetting fields in place: the run's
    /// state is spread across pawns, pickups, boons, the field's frontier and
    /// the camera, and the one code path guaranteed to reset all of it is the
    /// one that already builds a fresh run from nothing.
    /// </summary>
    void Restart()
    {
        var controller = GetComponentInParent<GameModeController>();
        if (controller != null)
            controller.RestartTankRaid();
    }

    public void Teardown()
    {
        StopAllCoroutines();
        BankBest();

        // Pawns and pickups first: both live outside the stage root (pawns at
        // the scene root by the shootable rule, pickups under the stage but
        // holding callbacks into this object) and neither is swept by destroying
        // anything else.
        TankPawn.DespawnAll();
        TankPickup.DespawnAll();
        TankField.ResetFrontier();

        if (_cameraRig != null)
            Destroy(_cameraRig);
        if (_stageRoot != null)
        {
            DestroyImmediate(_stageRoot);
            _stageRoot = null;
        }

        foreach (var go in _hiddenCharacters)
            if (go != null)
                go.SetActive(true);
        _hiddenCharacters.Clear();

        if (_blockManager != null)
            _blockManager.enabled = true;

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
        var rig = new GameObject("TankRaidCamera");
        // Tagged so Camera.main works while the player's own camera is inactive;
        // FlashQuad's billboarding depends on it.
        rig.tag = "MainCamera";
        var cam = rig.AddComponent<Camera>();
        cam.fieldOfView = 52f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = BrawlStage.VoidColor;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 260f;
        rig.AddComponent<AudioListener>();
        var data = rig.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        rig.AddComponent<TankRaidCamera>();
        return rig;
    }
}
