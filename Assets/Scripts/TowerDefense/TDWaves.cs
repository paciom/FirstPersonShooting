using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The invasion's timetable: ten waves through the warp gate, a build phase
/// between them, and the ledger of who came, who fell and who got through.
///
/// Timer-driven, no coroutines — a recompile during Play kills coroutines
/// silently, and a wave director that forgets mid-wave soft-locks the whole
/// mode. Everything that matters is a serialized field plus Time.time
/// arithmetic, which both survive the reload.
///
/// Wave themes cycle the roster: line waves wear whatever robot the wave
/// number lands on, every third wave is a SWIFT rush of scouts, every
/// fourth an ARMORED crawl of titans, and the last wave walks a boss in
/// behind its escort.
/// </summary>
public class TDWaves : MonoBehaviour
{
    public static TDWaves Instance { get; private set; }

    public const int TotalWaves = 10;

    /// <summary>Thinking room before the first wave; less once the map is known.</summary>
    const float FirstBuildSeconds = 20f;
    const float BuildSeconds = 14f;

    /// <summary>Early-call reward per banked second — patience priced against tempo.</summary>
    const int RushBonusPerSecond = 2;

    /// <summary>Everything one wave is made of. Rolled fresh from the wave number.</summary>
    struct Recipe
    {
        public int count;
        public float interval;
        public float hp;
        public float speed;
        public float damage;
        public int bounty;
        public int leak;
        public string robotName;
    }

    [SerializeField] int _wave;              // 0 = before the first wave
    [SerializeField] bool _waveActive;
    [SerializeField] float _phaseEndsAt;     // build-phase deadline while !_waveActive
    [SerializeField] int _toSpawn;
    [SerializeField] float _nextSpawnAt;
    [SerializeField] float _spawnInterval;
    [SerializeField] bool _halted;

    /// <summary>Raiders on the field. Component-ref lists survive a reload.</summary>
    [SerializeField] List<TDCreep> _alive = new List<TDCreep>();

    RobotRoster _roster;
    float _nextSweep;

    // ------------------------------------------------------------- HUD reads

    public int Wave => _wave;
    public bool IsBuildPhase => !_waveActive;
    public float BuildSecondsLeft => Mathf.Max(0f, _phaseEndsAt - Time.time);
    public int AliveCount => _alive.Count;
    public int RemainingToSpawn => _toSpawn;

    void Awake()
    {
        Instance = this;
        _roster = GetComponentInParent<RobotRoster>();
        _phaseEndsAt = Time.time + FirstBuildSeconds;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Defeat stops the clock — no eleventh-hour spawns over the credits.</summary>
    public void Halt()
    {
        _halted = true;
        _toSpawn = 0;
    }

    /// <summary>
    /// The NEXT WAVE button: skip the rest of the build phase and bank the
    /// unspent seconds as credits. No-op mid-wave.
    /// </summary>
    public void CallNextWave()
    {
        if (_halted || _waveActive || _wave >= TotalWaves)
            return;
        int bonus = Mathf.FloorToInt(BuildSecondsLeft) * RushBonusPerSecond;
        if (bonus > 0)
            TDEconomy.Grant(bonus);
        StartWave();
    }

    /// <summary>
    /// A raider that reached the Core, reporting synchronously BEFORE its
    /// death effect fires — struck from the ledger here so the de-rez that
    /// follows can never read as a kill and pay a bounty on it.
    /// </summary>
    public static void NotifyLeaked(TDCreep creep)
    {
        Instance?._alive.Remove(creep);
    }

    void Update()
    {
        if (_halted)
            return;

        if (!_waveActive)
        {
            if (_wave < TotalWaves && Time.time >= _phaseEndsAt)
                StartWave();
            return;
        }

        // Spawning: one raider per interval until the wave is all aboard.
        if (_toSpawn > 0 && Time.time >= _nextSpawnAt)
        {
            _nextSpawnAt = Time.time + _spawnInterval;
            _toSpawn--;
            SpawnOne(isBoss: _wave == TotalWaves && _toSpawn == 0);
        }

        // The ledger sweep: drop the destroyed (a reload can orphan a death
        // notification, so membership is polled, never assumed), then judge
        // the wave.
        if (Time.time >= _nextSweep)
        {
            _nextSweep = Time.time + 0.25f;
            _alive.RemoveAll(creep => creep == null || !creep.IsAlive);
            if (_toSpawn == 0 && _alive.Count == 0)
                FinishWave();
        }
    }

    void StartWave()
    {
        _wave++;
        _waveActive = true;
        var recipe = ThemeFor(_wave);
        _toSpawn = recipe.count;
        _spawnInterval = recipe.interval;
        _nextSpawnAt = Time.time;   // first raider steps through immediately
        VfxUtil.Explosion(TDMap.PortalSite + Vector3.up * 2.5f, new Color(1f, 0.3f, 0.9f), 1.4f);
    }

    void FinishWave()
    {
        _waveActive = false;
        // The clear bonus: the wave's worth, paid on a swept field.
        TDEconomy.Grant(60 + 10 * _wave);
        if (_wave >= TotalWaves)
        {
            TDMatch.Instance?.Victory();
            _halted = true;
            return;
        }
        _phaseEndsAt = Time.time + BuildSeconds;
    }

    void SpawnOne(bool isBoss)
    {
        var recipe = ThemeFor(_wave);

        float scale = 1f;
        if (isBoss)
        {
            // The finale walks in at half speed and half a head taller,
            // with a whole wave's shield on its own back and a gun to match.
            recipe.hp *= 12f;
            recipe.speed = 2.3f;
            recipe.damage *= 2f;
            recipe.bounty = 150;
            recipe.leak = 5;
            recipe.robotName = "titan";
            scale = 1.5f;
        }

        var entry = UnitCatalog.EntryOf(_roster, recipe.robotName);
        var pos = TDMap.PortalSite + new Vector3(Random.Range(-1.5f, 1.5f), 0f, 0f);
        var creep = TDCreep.Spawn(entry, pos, recipe.hp, recipe.speed, recipe.damage,
            recipe.bounty, recipe.leak, scale);
        _alive.Add(creep);

        // Bounty on the kill, event-driven so it pays at the moment of the
        // de-rez. A reload severs this closure; the sweep above still
        // retires the raider, so only the dev-editor bounty is lost.
        var shield = creep.GetComponent<EnergyShield>();
        int worth = creep.Bounty;
        shield.OnDeRezzed += () =>
        {
            if (Instance != null && Instance._alive.Remove(creep))
                TDEconomy.Grant(worth);
        };
    }

    /// <summary>
    /// The wave recipe — every number the invasion runs on, in one place.
    /// Shield growth is the difficulty curve; gun growth keeps the defender
    /// corps mortal; everything else is flavour spread across the roster.
    /// </summary>
    static Recipe ThemeFor(int wave)
    {
        float baseHp = 55f * Mathf.Pow(1.22f, wave - 1);
        var recipe = new Recipe
        {
            count = 8 + 2 * wave,
            interval = 0.9f,
            hp = baseHp,
            speed = 3.5f,
            damage = 5f + 0.7f * wave,
            bounty = 10 + 2 * wave,
            leak = 1,
        };

        if (wave == TotalWaves)
        {
            // The escort ahead of the boss: a thin line wave (the boss
            // itself is rolled in SpawnOne, as the last one through).
            recipe.count = 9;
            recipe.robotName = "ranger";
            return recipe;
        }
        if (wave % 4 == 0)
        {
            // ARMORED: fewer, slower, nearly double the shield, heavier gun.
            recipe.count = Mathf.Max(5, Mathf.RoundToInt(recipe.count * 0.6f));
            recipe.hp = baseHp * 1.9f;
            recipe.speed = 2.7f;
            recipe.damage *= 1.3f;
            recipe.bounty = Mathf.RoundToInt(recipe.bounty * 1.6f);
            recipe.interval = 1.4f;
            recipe.robotName = "titan";
            return recipe;
        }
        if (wave % 3 == 0)
        {
            // SWIFT: a scout rush — thin shields and light guns at pace.
            recipe.count = Mathf.RoundToInt(recipe.count * 1.2f);
            recipe.hp = baseHp * 0.6f;
            recipe.speed = 5.2f;
            recipe.damage *= 0.8f;
            recipe.interval = 0.65f;
            recipe.robotName = "scout";
            return recipe;
        }
        // The line: cycle the roster so every wave wears a different robot.
        string[] line = { "ranger", "panther", "hawk", "knight", "samurai", "racer", "bolt" };
        recipe.robotName = line[wave % line.Length];
        return recipe;
    }
}
