using UnityEngine;

/// <summary>
/// The invisible hand on the difficulty dial. Watches how the defense is
/// actually going and keeps the player on the edge of it: cruising gets
/// punished with bolder waves, drowning gets a quiet reprieve — the
/// Left 4 Dead trick, sized for a canyon.
///
/// One number rules everything: PRESSURE, a multiplier the wave recipes
/// read (count fully, shield/gun strength gently). It moves BETWEEN waves
/// on a performance rubric — leaks, clear speed, banked credits, towers
/// lost, core health — and never by more than a nudge, so difficulty
/// breathes instead of lurching. DURING a wave the director acts live
/// instead: a wave being tabled early with zero leaks gets reinforcements
/// through the gate ("THE GATE SURGES"), and a wave that has already torn
/// three core points away stops coming ("THE GATE SPUTTERS").
///
/// Timer-driven serialized state throughout, like every TD system — a
/// recompile mid-match keeps the dial where the player earned it.
/// </summary>
public class TDDirector : MonoBehaviour
{
    public static TDDirector Instance { get; private set; }

    /// <summary>The dial's travel: never gentler than 60%, never past 170%.</summary>
    const float MinPressure = 0.6f;
    const float MaxPressure = 1.7f;

    /// <summary>Rubric points move the dial this much each — max swing ±0.25 a wave.</summary>
    const float NudgePerPoint = 0.05f;

    /// <summary>A comfortable bank at wave's end reads as "too easy".</summary>
    const int RichThreshold = 350;

    /// <summary>Core lost in ONE wave that triggers the mid-wave mercy cut.</summary>
    const int SputterCoreLoss = 3;

    [SerializeField] float _pressure = 1f;
    [SerializeField] int _towersLostThisWave;
    [SerializeField] int _coreAtWaveStart;
    [SerializeField] int _surgesThisWave;
    [SerializeField] bool _sputteredThisWave;
    [SerializeField] string _notice = "";
    [SerializeField] float _noticeUntil;
    float _nextPoll;

    public float Pressure => _pressure;

    /// <summary>What raiders a wave gets: the dial, straight.</summary>
    public float CountScale => _pressure;

    /// <summary>
    /// What each raider is made of: the dial, softened — doubling the horde
    /// AND doubling every shield in it would be two difficulty dials moving
    /// as one cliff.
    /// </summary>
    public float PowerScale => Mathf.Sqrt(_pressure);

    /// <summary>The current stage direction, or null once its moment has passed.</summary>
    public string Notice => Time.time < _noticeUntil ? _notice : null;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Structure losses feed the rubric. Subscribed the hot-reload-safe way;
    /// the Core is excluded by reference (its loss is the match ending, not
    /// a difficulty signal).
    /// </summary>
    void OnEnable()
    {
        Building.OnBuildingLost -= HandleBuildingLost;
        Building.OnBuildingLost += HandleBuildingLost;
    }

    void OnDisable()
    {
        Building.OnBuildingLost -= HandleBuildingLost;
    }

    void HandleBuildingLost(Building lost)
    {
        if (lost == null || lost.TeamId != 0)
            return;
        if (TDController.Instance != null && lost == TDController.Instance.Core)
            return;
        _towersLostThisWave++;
    }

    // ------------------------------------------------------------- wave hooks

    public void OnWaveStarted()
    {
        _towersLostThisWave = 0;
        _surgesThisWave = 0;
        _sputteredThisWave = false;
        _coreAtWaveStart = TDMatch.Instance != null
            ? TDMatch.Instance.CoreEnergy : TDMatch.StartingCoreEnergy;
    }

    /// <summary>
    /// The between-waves verdict: score the wave, nudge the dial. Positive
    /// points say the player coasted; negative say they bled. The rubric is
    /// deliberately redundant — several small signals beat one clever one.
    /// </summary>
    public void OnWaveFinished(int baseCount, float clearSeconds, int leaks)
    {
        int score = 0;
        if (leaks == 0) score += 2;
        else if (leaks == 1) score += 1;
        if (clearSeconds < Par(baseCount) * 0.7f) score += 1;
        if (TDEconomy.Credits >= RichThreshold) score += 1;
        if (_towersLostThisWave == 0) score += 1;

        if (leaks >= 3) score -= 3;
        if (_towersLostThisWave >= 2) score -= 2;
        int core = TDMatch.Instance != null ? TDMatch.Instance.CoreEnergy : 0;
        if (core <= 4) score -= 3;

        _pressure = Mathf.Clamp(_pressure + NudgePerPoint * score, MinPressure, MaxPressure);
    }

    // ------------------------------------------------------------- live hand

    void Update()
    {
        if (Time.time < _nextPoll)
            return;
        _nextPoll = Time.time + 0.5f;

        var waves = TDWaves.Instance;
        var match = TDMatch.Instance;
        if (waves == null || match == null || match.IsOver || waves.IsBuildPhase)
            return;

        int coreLost = _coreAtWaveStart - match.CoreEnergy;

        // Mercy first: a wave that has already taken three core points has
        // made its point. The rest of it stays home.
        if (!_sputteredThisWave && coreLost >= SputterCoreLoss)
        {
            _sputteredThisWave = true;
            waves.CutSpawnsTo(2);
            Say("THE GATE SPUTTERS — the raiders thin out");
            return;
        }

        // Boldness second: the wave is nearly swept, early, without a single
        // leak or core scratch — the gate answers with reinforcements.
        // Twice at most; a third surge would turn a good defense into a
        // treadmill.
        if (_surgesThisWave < 2 && waves.LeaksThisWave == 0 && coreLost == 0
            && waves.RemainingToSpawn == 0
            && waves.AliveCount <= Mathf.Max(2, waves.WaveBaseCount / 5)
            && waves.WaveElapsed < Par(waves.WaveBaseCount) * 0.55f)
        {
            _surgesThisWave++;
            waves.AddSpawns(Mathf.Max(2, waves.WaveBaseCount / 6));
            Say("THE GATE SURGES — more raiders!");
        }
    }

    /// <summary>Rough seconds a wave of this size deserves: march time plus spawn tail.</summary>
    static float Par(int count) => 55f + count * 1f;

    void Say(string notice)
    {
        _notice = notice;
        _noticeUntil = Time.time + 6f;
    }
}
