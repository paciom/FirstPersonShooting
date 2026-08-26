using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The referee Gunfight never had. Turns Player v AI and AI v AI from a
/// sandbox that runs forever into a best-of-five match.
///
/// WHY IT WAS ENDLESS. Four things were missing at once, and any one of them
/// alone was enough: nothing watched the arena to decide it was over; every
/// de-rez re-materialized after three seconds, so "last team standing" could
/// never happen; the only score in the game counted practice dummies, not
/// robots; and gold could grow a team but nothing could shrink one. This class
/// supplies the first, drives <see cref="DeRezEffect.Elimination"/> for the
/// second, and keeps the round score itself for the third.
///
/// THE RULES (settled 2026-07-24, built now):
///   · No respawn inside a round. A de-rezzed robot is out until the next one.
///   · Last team standing takes the round; first to three rounds takes the match.
///   · Rounds are timed. A round that runs out goes to whoever has more robots
///     standing, then to whoever has more shield left between them.
///   · De-rezzed robots come back for the next round — nobody dies, they just
///     sit one out, which keeps the no-death fiction the whole game runs on.
///
/// PLAYER v AI: THE PLAYER TAKES OVER A TEAM-MATE. Elimination is right for
/// bots and miserable for a person — a kid who loses a duel twenty seconds in
/// would watch the other three minutes. So when the player falls, they wake up
/// as the healthiest cyan robot still standing. The team still loses exactly
/// one body for the death (the player's, plus the one they take, minus the
/// player standing again), so the round arithmetic is untouched; what changes
/// is that the player keeps playing until their whole team is gone.
///
/// Self-bootstrapping and mode-driven, like MatchAnnouncer next door: it watches
/// GameModeController rather than being wired into it, which keeps the mode
/// controller — the most contended file in the project — out of this entirely.
/// </summary>
public class GunfightMatch : MonoBehaviour
{
    public const int RoundsToWin = 3;

    /// <summary>Rounds inside the clock. Past this, rounds run to a finish.</summary>
    public const int RegulationRounds = 5;

    public const float RoundSeconds = 90f;

    const float IntroSeconds = 2.2f;
    const float RoundEndSeconds = 3.2f;
    const float MatchEndSeconds = 5f;

    /// <summary>
    /// A team must read as wiped for this long before the round is called. A
    /// reinforcement bought with gold can land in the same frame the last
    /// robot falls, and calling the round on that frame would end it against a
    /// team that is, a heartbeat later, standing.
    /// </summary>
    const float WipeConfirmSeconds = 0.5f;

    /// <summary>Beat between the player dissolving and waking up somewhere else.</summary>
    const float TakeoverDelay = 1.1f;

    /// <summary>The census is a scene-wide query — four times a second is plenty.</summary>
    const float CensusInterval = 0.2f;

    enum Stage { Off, Intro, Fight, RoundEnd, MatchEnd }

    static GunfightMatch _instance;

    Stage _stage = Stage.Off;
    int _round;
    readonly int[] _wins = new int[2];
    /// <summary>
    /// Whether this round has a clock at all. A separate flag rather than a
    /// negative <see cref="_clock"/> sentinel: a counting-down float lands on
    /// -0.02 rather than 0, so a sign test would read every regulation round
    /// as a no-clock decider the moment time ran out.
    /// </summary>
    bool _timed;
    float _clock;
    float _stageUntil;
    float _nextCensus;
    float _wipeSince = -1f;
    float _takeoverAt = -1f;
    readonly int[] _alive = new int[2];
    readonly float[] _shield = new float[2];

    GameObject _canvasGo;
    Text _bar;

    static readonly List<EnergyShield> Fighters = new List<EnergyShield>();

    // ------------------------------------------------------------- lifecycle

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (_instance == null)
            new GameObject("GunfightMatch").AddComponent<GunfightMatch>();
    }

    void Awake()
    {
        _instance = this;
        BuildUi();
    }

    void OnDestroy()
    {
        if (_instance != this)
            return;
        // Never leave the switch on behind us: elimination bleeding into
        // Brawl or Commander would stop THEIR robots ever coming back.
        DeRezEffect.Elimination = false;
        _instance = null;
    }

    /// <summary>
    /// A refereed match: the two Gunfight modes, and not the training range —
    /// a range where the bots hold fire is a place to practise, not a match to
    /// win, and a round timer on it would just interrupt.
    /// </summary>
    static bool ShouldRun()
    {
        var gmc = GameModeController.Instance;
        if (gmc == null || GameModeController.TrainingMatch)
            return false;
        return gmc.Mode == GameMode.PlayerVsAI || gmc.Mode == GameMode.AIvAI;
    }

    void Update()
    {
        bool run = ShouldRun();
        if (!run)
        {
            if (_stage != Stage.Off)
                Stop();
            return;
        }
        if (_stage == Stage.Off)
        {
            Begin();
            return;
        }

        if (Time.time >= _nextCensus)
        {
            _nextCensus = Time.time + CensusInterval;
            TakeCensus();
        }

        switch (_stage)
        {
            case Stage.Intro: TickIntro(); break;
            case Stage.Fight: TickFight(); break;
            case Stage.RoundEnd: TickRoundEnd(); break;
            case Stage.MatchEnd: TickMatchEnd(); break;
        }

        UpdateBar();
    }

    void Begin()
    {
        _round = 0;
        _wins[0] = _wins[1] = 0;
        DeRezEffect.Elimination = true;
        _canvasGo.SetActive(true);
        BeginRound();
    }

    /// <summary>
    /// Hand the world back. Deliberately does NOT stand the eliminated up: the
    /// mode controller runs RestoreAllDeRez on every mode entry AND on the way
    /// to the menu, which is the same restore done at the right moment. Doing
    /// it again from here lands a frame LATER — after the menu has switched the
    /// bots off — and switches them back on, which is a menu backdrop full of
    /// robots fighting behind the buttons.
    /// </summary>
    void Stop()
    {
        _stage = Stage.Off;
        DeRezEffect.Elimination = false;
        _canvasGo.SetActive(false);
    }

    // ----------------------------------------------------------------- rounds

    void BeginRound()
    {
        _round++;
        _stage = Stage.Intro;
        _stageUntil = Time.time + IntroSeconds;
        _wipeSince = -1f;
        _takeoverAt = -1f;
        // Regulation rounds are timed; a decider is fought to a finish, because
        // a tie-break that has already failed to separate them twice is not
        // going to separate them a third time.
        _timed = _round <= RegulationRounds;
        _clock = RoundSeconds;

        // Everyone standing, whole, back on their spawn line. This is also what
        // brings back whoever was eliminated last round.
        foreach (var effect in FindObjectsByType<DeRezEffect>(FindObjectsSortMode.None))
            if (effect != null && HasBrain(effect.gameObject))
                effect.ReviveAtSpawn();

        // Census BEFORE the freeze: SetBrains walks the cast this found, so
        // freezing first would freeze last round's cast and leave anything
        // bought since running through the countdown.
        TakeCensus();
        SetBrains(false);

        MatchAnnouncer.Say(_round <= RegulationRounds ? $"ROUND {_round}" : "SUDDEN DEATH",
            ScoreLine(), Color.white);
        GameAudio.PlayFlat(GameAudio.Id.WaveStart, 0.7f);
    }

    void TickIntro()
    {
        if (Time.time < _stageUntil)
            return;
        SetBrains(true);
        _stage = Stage.Fight;
        MatchAnnouncer.Say("FIGHT", _timed ? "last team standing takes the round"
            : "no clock - this one runs to a finish", Color.white);
        GameAudio.PlayFlat(GameAudio.Id.MatchStart);
    }

    void TickFight()
    {
        if (_timed)
            _clock = Mathf.Max(0f, _clock - Time.deltaTime);

        MaybeTakeOver();

        bool cyanGone = _alive[0] == 0;
        bool magentaGone = _alive[1] == 0;
        if (cyanGone || magentaGone)
        {
            // Hold the verdict briefly — see WipeConfirmSeconds.
            if (_wipeSince < 0f)
                _wipeSince = Time.time;
            else if (Time.time - _wipeSince >= WipeConfirmSeconds)
                EndRound(cyanGone && magentaGone ? -1 : (cyanGone ? 1 : 0), "WIPED OUT");
            return;
        }
        _wipeSince = -1f;

        if (!_timed || _clock > 0f)
            return;
        // Time. More robots standing wins it; level on bodies, the side that
        // took less damage getting there does.
        int winner = _alive[0] != _alive[1]
            ? (_alive[0] > _alive[1] ? 0 : 1)
            : Mathf.Abs(_shield[0] - _shield[1]) > 0.5f
                ? (_shield[0] > _shield[1] ? 0 : 1)
                : -1;
        EndRound(winner, "TIME");
    }

    void EndRound(int winner, string why)
    {
        _stage = Stage.RoundEnd;
        _stageUntil = Time.time + RoundEndSeconds;
        SetBrains(false);

        if (winner < 0)
        {
            // Plain hyphen: LegacyRuntime.ttf has no em-dash glyph and renders
            // it as a gap, which reads as a missing word rather than a dash.
            MatchAnnouncer.Say("DRAW", $"{why} - nobody takes round {_round}", Color.white);
            return;
        }
        _wins[winner]++;
        MatchAnnouncer.Say($"{MatchAnnouncer.TeamName(winner)} TAKES ROUND {_round}",
            $"{why} · {ScoreLine()}", MatchAnnouncer.TeamColor(winner));
        // The round bell — neutral on purpose, a bell is a bell either way.
        GameAudio.PlayFlat(GameAudio.Id.MatchStart, 0.8f);
    }

    void TickRoundEnd()
    {
        if (Time.time < _stageUntil)
            return;

        int leader = _wins[0] >= _wins[1] ? 0 : 1;
        bool decided = _wins[leader] >= RoundsToWin;
        // Regulation over with nobody at three: the lead takes it, and a level
        // score goes to sudden death rather than being called a draw.
        if (!decided && _round >= RegulationRounds && _wins[0] != _wins[1])
            decided = true;

        if (decided)
        {
            _stage = Stage.MatchEnd;
            _stageUntil = Time.time + MatchEndSeconds;
            SetBrains(false);
            MatchAnnouncer.Say($"{MatchAnnouncer.TeamName(leader)} WINS THE MATCH",
                ScoreLine(), MatchAnnouncer.TeamColor(leader));
            // A spectator (AI v AI) hears the fanfare whoever takes it; only a
            // player who actually LOST gets the losing drum.
            bool playerLost = EnergyShield.PlayerShield != null && leader != 0;
            GameAudio.PlayFlat(playerLost ? GameAudio.Id.Defeat : GameAudio.Id.Victory);
            return;
        }
        BeginRound();
    }

    void TickMatchEnd()
    {
        if (Time.time < _stageUntil)
            return;
        // Stop() before the mode flips, not after: leaving on our own account
        // means the ShouldRun() check never sees the transition, so the round
        // bar would hang over the menu with nothing left to clear it.
        Stop();
        GameModeController.Instance?.EnterMenu();
    }

    // --------------------------------------------------------- taking over

    /// <summary>
    /// The player fell: wake them up as the healthiest team-mate still on their
    /// feet. The team-mate is spent doing it — one body in, one body out — so
    /// the round is still decided by exactly the robots that survived it.
    /// </summary>
    void MaybeTakeOver()
    {
        var gmc = GameModeController.Instance;
        if (gmc == null || gmc.Mode != GameMode.PlayerVsAI)
            return;
        var brain = PlayerBrain.Local;
        if (brain == null)
            return;
        var shield = brain.GetComponent<EnergyShield>();
        var effect = brain.GetComponent<DeRezEffect>();
        if (shield == null || effect == null)
            return;

        if (!shield.IsDown)
        {
            _takeoverAt = -1f;
            return;
        }
        // Let the de-rez read before the hand-off — swapping bodies inside the
        // same frame the old one popped looks like a teleport bug.
        if (_takeoverAt < 0f)
        {
            _takeoverAt = Time.time + TakeoverDelay;
            return;
        }
        if (Time.time < _takeoverAt)
            return;
        _takeoverAt = -1f;

        var host = HealthiestTeammate(shield);
        if (host == null)
            return;   // nobody left to be — the round is already lost

        Vector3 spot = host.transform.position;
        Quaternion facing = host.transform.rotation;
        string name = MatchAnnouncer.CharacterName(host.transform);

        // Spend the host first, so its body is on its way out as ours arrives.
        // Routed through TakeHit rather than a back door: it is the one path
        // that fires OnDeRezzed, and OnDeRezzed is what everything else — the
        // dissolve, the colliders, the brain — actually listens to.
        host.TakeHit(host.maxShield * 10f + 1f, spot);

        effect.Revive(spot, facing);
        MatchAnnouncer.Say("TAKING OVER", $"you are {name} now",
            MatchAnnouncer.TeamColor(shield.teamId));
    }

    /// <summary>The team-mate with the most shield left — the kindest handover.</summary>
    static EnergyShield HealthiestTeammate(EnergyShield player)
    {
        EnergyShield best = null;
        foreach (var shield in Fighters)
        {
            if (shield == null || shield == player || shield.IsDown)
                continue;
            if (shield.teamId != player.teamId)
                continue;
            // An invulnerable host would shrug off the hand-off hit and stay
            // standing, leaving two robots in one spot.
            if (shield.invulnerable)
                continue;
            if (shield.GetComponent<AIBrain>() == null)
                continue;   // only a bot can be taken over
            if (best == null || shield.Current > best.Current)
                best = shield;
        }
        return best;
    }

    // ------------------------------------------------------------- the cast

    /// <summary>
    /// Who is standing, per team. Re-scanned rather than tracked because the
    /// cast is not fixed: gold buys robots mid-round, and a team that bought
    /// one has to count as alive the moment it lands.
    /// </summary>
    void TakeCensus()
    {
        Fighters.Clear();
        _alive[0] = _alive[1] = 0;
        _shield[0] = _shield[1] = 0f;

        foreach (var shield in FindObjectsByType<EnergyShield>(FindObjectsSortMode.None))
        {
            if (shield == null || !shield.gameObject.activeInHierarchy)
                continue;
            if (!HasBrain(shield.gameObject))
                continue;   // dummies, crates and cover are not the match
            Fighters.Add(shield);
            if (shield.IsDown)
                continue;
            int team = Mathf.Clamp(shield.teamId, 0, 1);
            _alive[team]++;
            _shield[team] += shield.Current;
        }
    }

    static bool HasBrain(GameObject go) =>
        go.GetComponent<AIBrain>() != null || go.GetComponent<PlayerBrain>() != null;

    /// <summary>
    /// Freeze or release every fighter for the round intro and the round end.
    /// PlayerBrain is a component toggle; AIBrain has its own switch because
    /// disabling it strands whatever it was mid-way through.
    /// </summary>
    void SetBrains(bool on)
    {
        foreach (var shield in Fighters)
        {
            if (shield == null)
                continue;
            var ai = shield.GetComponent<AIBrain>();
            if (ai != null)
            {
                ai.SetActive(on);
                continue;
            }
            var player = shield.GetComponent<PlayerBrain>();
            // A de-rezzed player has no brain on purpose; leave them down.
            if (player != null && !shield.IsDown)
                player.enabled = on;
        }
    }

    // ------------------------------------------------------------------ ui

    string ScoreLine() =>
        $"CYAN {_wins[0]} - {_wins[1]} MAGENTA   ·   FIRST TO {RoundsToWin}";

    void UpdateBar()
    {
        string clock = !_timed
            ? "NO CLOCK"
            : Mathf.FloorToInt(_clock / 60f) + ":" + Mathf.FloorToInt(_clock % 60f).ToString("00");
        string round = _round <= RegulationRounds ? $"ROUND {_round}" : "SUDDEN DEATH";
        _bar.text = $"{round}   ·   CYAN {_wins[0]} - {_wins[1]} MAGENTA   ·   {clock}"
            + $"   ·   {_alive[0]} v {_alive[1]} STANDING";
    }

    void BuildUi()
    {
        _canvasGo = new GameObject("GunfightMatchCanvas");
        _canvasGo.transform.SetParent(transform, false);
        var canvas = _canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 41;   // just over the announcer's toast
        var scaler = _canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var go = new GameObject("RoundBar");
        go.transform.SetParent(_canvasGo.transform, false);
        _bar = go.AddComponent<Text>();
        _bar.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _bar.fontSize = 26;
        _bar.fontStyle = FontStyle.Bold;
        _bar.alignment = TextAnchor.UpperCenter;
        _bar.color = new Color(0.88f, 0.95f, 1f, 0.9f);
        _bar.horizontalOverflow = HorizontalWrapMode.Overflow;
        var rect = _bar.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        // Above the announcer toast at -102, clear of the de-rez score line.
        rect.anchoredPosition = new Vector2(0, -56);
        rect.sizeDelta = new Vector2(1400, 34);

        _canvasGo.SetActive(false);
    }
}
