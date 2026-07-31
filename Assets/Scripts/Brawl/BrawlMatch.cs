using UnityEngine;

/// <summary>
/// The referee: ROUND N → FIGHT! → someone drops or the clock runs out →
/// pips → best-of-3 → the result panel. Plain state enum and float timers,
/// no coroutines — a mid-Play recompile kills coroutines silently, and a
/// referee that forgets the score mid-match is worse than no referee.
/// </summary>
public class BrawlMatch : MonoBehaviour
{
    enum Stage { Intro, Fight, RoundEnd, MatchEnd }

    const int PipsToWin = 2;

    BrawlFighter _cyan, _magenta;
    BrawlHud _hud;
    string _cyanWinLabel = "PLAYER  WINS";
    string _magentaWinLabel = "CPU  WINS";

    Stage _stage = Stage.Intro;
    float _stageTime;
    float _clock = BrawlMoveSet.RoundSeconds;
    int _round = 1;
    int _cyanPips, _magentaPips;
    // Who fell this round: 0 cyan, 1 magenta, -1 timeout/draw pending judge.
    int _fallen = -1;
    // The referee's patience: last moment any hit landed or was blocked.
    float _lastContact;

    public void Bind(BrawlFighter cyan, BrawlFighter magenta, BrawlHud hud,
        string cyanWinLabel, string magentaWinLabel)
    {
        _cyan = cyan;
        _magenta = magenta;
        _hud = hud;
        _cyanWinLabel = cyanWinLabel;
        _magentaWinLabel = magentaWinLabel;
        _cyan.OnKnockedOut += OnKnockedOut;
        _magenta.OnKnockedOut += OnKnockedOut;
        // Any contact resets the referee's patience.
        System.Action<BrawlFighter, int, bool> landed = (v, d, k) => _lastContact = Time.time;
        System.Action<BrawlFighter, BrawlMoveSet.Move> blocked = (v, m) => _lastContact = Time.time;
        _cyan.OnHitLanded += landed;
        _magenta.OnHitLanded += landed;
        _cyan.OnHitBlocked += blocked;
        _magenta.OnHitBlocked += blocked;
        BeginRound();
    }

    void BeginRound()
    {
        // A clean lane every round — leftover cargo despawns.
        BrawlProps.DespawnAll();
        _cyan.ResetForRound();
        _magenta.ResetForRound();
        SetLocked(true);
        _clock = BrawlMoveSet.RoundSeconds;
        _fallen = -1;
        _stage = Stage.Intro;
        _stageTime = 0f;
        _lastContact = Time.time;
        _hud.SetHealth(1f, 1f);
        _hud.SetTimer(BrawlMoveSet.RoundSeconds);
        _hud.SetPips(_cyanPips, _magentaPips);
        _hud.Announce($"ROUND {_round}", 1.0f, Color.white);
    }

    void OnKnockedOut(BrawlFighter fighter)
    {
        if (_stage != Stage.Fight)
            return;
        _fallen = fighter.TeamId;
        EndRound("K.O.!");
    }

    void EndRound(string banner)
    {
        _stage = Stage.RoundEnd;
        _stageTime = 0f;
        SetLocked(true);

        BrawlFighter winner = null;
        if (_fallen == 0) { _magentaPips++; winner = _magenta; }
        else if (_fallen == 1) { _cyanPips++; winner = _cyan; }
        else
        {
            // Timeout: the healthier robot takes the round; a dead heat
            // gives the pip to both (sudden-death final rounds stay possible).
            if (_cyan.Health > _magenta.Health) { _cyanPips++; winner = _cyan; }
            else if (_magenta.Health > _cyan.Health) { _magentaPips++; winner = _magenta; }
            else { _cyanPips++; _magentaPips++; }
        }

        winner?.Celebrate();
        _hud.SetPips(_cyanPips, _magentaPips);
        _hud.Announce(banner, 1.4f, new Color(1f, 0.85f, 0.3f));
        BrawlAudio.PlayFlat(BrawlAudio.Id.Gong, 0.9f);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        _stageTime += dt;

        switch (_stage)
        {
            case Stage.Intro:
                if (_stageTime >= 1.0f)
                {
                    _stage = Stage.Fight;
                    _stageTime = 0f;
                    SetLocked(false);
                    _hud.Announce("FIGHT!", 0.6f, new Color(0.3f, 1f, 0.5f));
                    BrawlAudio.PlayFlat(BrawlAudio.Id.RoundDing);
                }
                break;

            case Stage.Fight:
                _clock -= dt;
                _hud.SetTimer(_clock);
                _hud.SetHealth(_cyan.Health / BrawlMoveSet.MaxHealth,
                               _magenta.Health / BrawlMoveSet.MaxHealth);
                _hud.SetCharge(_cyan.Charge, _magenta.Charge);
                // Six silent seconds means the terrain checkmated them (a
                // lane wall has no 'around') — the referee moves BOTH
                // fighters to one clear span, everything else kept.
                // Corners alone would re-split them across the same pillar.
                if (Time.time - _lastContact > 6f)
                {
                    _lastContact = Time.time;
                    float centre = FindClearSpan();
                    _cyan.Reposition(centre - 1.2f);
                    _magenta.Reposition(centre + 1.2f);
                    _hud.Announce("BREAK!", 0.9f, Color.white);
                    BrawlAudio.PlayFlat(BrawlAudio.Id.RoundDing, 0.7f);
                }
                if (_clock <= 0f)
                {
                    _fallen = -1;
                    EndRound("TIME!");
                }
                break;

            case Stage.RoundEnd:
                if (_stageTime < 2.4f)
                    break;
                if (_cyanPips >= PipsToWin && _magentaPips >= PipsToWin)
                    FinishMatch("DOUBLE  K.O.");
                else if (_cyanPips >= PipsToWin)
                    FinishMatch(_cyanWinLabel);
                else if (_magentaPips >= PipsToWin)
                    FinishMatch(_magentaWinLabel);
                else
                {
                    _round++;
                    BeginRound();
                }
                break;

            case Stage.MatchEnd:
                break;
        }
    }

    /// <summary>
    /// The flattest 3.6 m window on the fight line, nearest the centre:
    /// where the referee restarts a checkmated bout. Heights come from the
    /// same terrain authority the fighters walk on.
    /// </summary>
    static float FindClearSpan()
    {
        float half = BrawlStage.CurrentLaneHalf - 2f;
        float bestCentre = 0f;
        float bestScore = float.MinValue;
        for (float centre = -half; centre <= half; centre += 1f)
        {
            float low = float.MaxValue;
            float high = float.MinValue;
            for (float offset = -1.8f; offset <= 1.8f; offset += 0.6f)
            {
                float height = BrawlGround.HeightAt(centre + offset);
                low = Mathf.Min(low, height);
                high = Mathf.Max(high, height);
            }
            float score = -(high - low) * 10f - Mathf.Abs(centre) * 0.1f - high * 0.5f;
            if (score > bestScore)
            {
                bestScore = score;
                bestCentre = centre;
            }
        }
        return bestCentre;
    }

    void FinishMatch(string result)
    {
        _stage = Stage.MatchEnd;
        BrawlAudio.PlayFlat(BrawlAudio.Id.Victory);
        _hud.ShowEndPanel(result,
            onRematch: () =>
            {
                _cyanPips = _magentaPips = 0;
                _round = 1;
                _hud.HideEndPanel();
                BeginRound();
            },
            onRobots: () => GameModeController.Instance.RestartBrawlSelect(),
            onMenu: () => GameModeController.Instance.EnterMenu());
    }

    void SetLocked(bool locked)
    {
        _cyan.ControlsLocked = locked;
        _magenta.ControlsLocked = locked;
    }
}
