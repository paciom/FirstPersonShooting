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

    /// <summary>
    /// Who is fighting, where, and under whose hands — everything the K.O.
    /// card and the challenge link need that the score alone cannot say.
    /// Set by BrawlController, which is the only thing that knows it.
    /// </summary>
    public struct Bout
    {
        public string cyanRobot, magentaRobot;   // roster display names
        public string stage;                     // BrawlArenas name, "" for RANDOM
        public int difficulty;                   // BrawlDifficulty level
        public bool playerControls;              // false is the AI exhibition
        public Texture cyanFace, magentaFace;    // the live corner portraits
    }

    BrawlFighter _cyan, _magenta;
    BrawlHud _hud;
    Bout _bout;
    string _cyanWinLabel = "PLAYER  WINS";
    string _magentaWinLabel = "CPU  WINS";

    // The move each fighter last STARTED, and the last one that actually
    // connected — the second is the finisher, because a K.O. blow lands one
    // call before OnKnockedOut fires. A round that ends on a mine, on fire or
    // on the clock leaves it empty on purpose: the card would rather say
    // "K.O." than name a kick that was not the cause.
    string _cyanMove = "", _magentaMove = "";
    string _connected = "";
    float _connectedAt = -99f;
    string _deciderFinisher = "";
    bool _deciderWasKo;

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
        string cyanWinLabel, string magentaWinLabel, Bout bout)
    {
        _cyan = cyan;
        _magenta = magenta;
        _hud = hud;
        _bout = bout;
        _cyanWinLabel = cyanWinLabel;
        _magentaWinLabel = magentaWinLabel;
        _cyan.OnKnockedOut += OnKnockedOut;
        _magenta.OnKnockedOut += OnKnockedOut;
        // Any contact resets the referee's patience. The victim is the
        // argument, so the ATTACKER is the other corner — which is whose move
        // name the K.O. card is after.
        System.Action<BrawlFighter, int, bool> landed = (victim, d, k) =>
        {
            _lastContact = Time.time;
            _connected = victim == _cyan ? _magentaMove : _cyanMove;
            _connectedAt = Time.time;
        };
        System.Action<BrawlFighter, BrawlMoveSet.Move> blocked = (v, m) => _lastContact = Time.time;
        _cyan.OnHitLanded += landed;
        _magenta.OnHitLanded += landed;
        _cyan.OnHitBlocked += blocked;
        _magenta.OnHitBlocked += blocked;
        _cyan.OnMoveStarted += name => _cyanMove = name;
        _magenta.OnMoveStarted += name => _magentaMove = name;
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
        _cyanMove = _magentaMove = _connected = "";
        _connectedAt = -99f;
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

    void EndRound(string banner, float bannerSeconds = 1.4f)
    {
        _stage = Stage.RoundEnd;
        _stageTime = 0f;
        SetLocked(true);

        // Remembered per round, and read at FinishMatch: the card is about how
        // the LAST round ended, not the loudest one. Half a second of grace —
        // a hit that landed longer ago than that did not cause this fall, and
        // naming it would put a lie on a picture kids pass around.
        _deciderWasKo = _fallen >= 0;
        _deciderFinisher = _deciderWasKo && Time.time - _connectedAt <= 0.5f ? _connected : "";

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
        _hud.Announce(banner, bannerSeconds, new Color(1f, 0.85f, 0.3f));
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
                // The referee only counts silence while the fighters
                // genuinely CAN'T engage — solid terrain between their
                // chests, or a real distance apart. Trading whiffs and
                // blocks at close range is a fight, not a stall, and must
                // never trigger the break (it teleported live bouts).
                if (CanEngage())
                    _lastContact = Time.time;
                if (Time.time - _lastContact > 8f)
                {
                    _lastContact = Time.time;
                    float atZ = (_cyan.transform.position.z + _magenta.transform.position.z) * 0.5f;
                    atZ = Mathf.Clamp(atZ, -BrawlStage.BoundsHalf.y + 2f, BrawlStage.BoundsHalf.y - 2f);
                    float centre = FindClearSpan(
                        (_cyan.transform.position.x + _magenta.transform.position.x) * 0.5f, atZ);
                    RefereeFlash(_cyan);
                    RefereeFlash(_magenta);
                    _cyan.Reposition(centre - 1.2f, atZ);
                    _magenta.Reposition(centre + 1.2f, atZ);
                    RefereeFlash(_cyan);
                    RefereeFlash(_magenta);
                    _hud.Announce("BREAK!", 0.9f, Color.white);
                    BrawlAudio.PlayFlat(BrawlAudio.Id.RoundDing, 0.7f);
                }
                if (_clock <= 0f)
                {
                    // Spelled out and held longer than a K.O. banner — the
                    // round ending with nobody down needs the explanation.
                    _fallen = -1;
                    EndRound("TIME'S  UP!", 2.2f);
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
    /// True when nothing stops the fight: chests see each other and the
    /// gap is a fighting distance. A wall or a pillar between them (or a
    /// long chase) is what the referee's patience is FOR.
    /// </summary>
    bool CanEngage()
    {
        Vector3 a = _cyan.transform.position + Vector3.up * 1.1f;
        Vector3 b = _magenta.transform.position + Vector3.up * 1.1f;
        if ((b - a).magnitude > 3.5f)
            return false;
        return !Physics.Linecast(a, b,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }

    static void RefereeFlash(BrawlFighter fighter)
    {
        VfxUtil.SpawnBurst(fighter.transform.position + Vector3.up,
            MatchAnnouncer.TeamColor(fighter.TeamId), 10, 3f, 0.12f);
    }

    /// <summary>
    /// The flattest 3.6 m window on the fight line, nearest the FIGHTERS:
    /// where the referee restarts a checkmated bout. Probed from high
    /// above (aboveY) so tall structures read as their true summits — the
    /// blind probe once scored the pyramid's interior as 'flat ground'
    /// and the break teleported both fighters onto its peak.
    /// </summary>
    static float FindClearSpan(float near, float atZ)
    {
        float half = BrawlStage.BoundsHalf.x - 2f;
        float bestCentre = 0f;
        float bestScore = float.MinValue;
        for (float centre = -half; centre <= half; centre += 1f)
        {
            float low = float.MaxValue;
            float high = float.MinValue;
            bool blocked = false;
            for (float offset = -1.8f; offset <= 1.8f; offset += 0.6f)
            {
                float height = BrawlGround.HeightAt(centre + offset, atZ, aboveY: 30f);
                low = Mathf.Min(low, height);
                high = Mathf.Max(high, height);
                // Flat ground is not enough — the ground under a tree trunk
                // reads flat. The AIR has to be clear at body height too.
                if (Physics.CheckSphere(new Vector3(centre + offset, height + 1.1f, atZ),
                        0.35f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    blocked = true;
            }
            float score = -(high - low) * 10f
                          - Mathf.Abs(centre - near) * 0.15f
                          - high * 0.8f
                          - (blocked ? 1000f : 0f);
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

        // A double K.O. has both corners on two pips; cyan takes the card, and
        // the 2-2 on it tells the truth about that.
        bool cyanWon = _cyanPips >= _magentaPips;

        var card = new ShareCard.Result
        {
            winner = cyanWon ? _bout.cyanRobot : _bout.magentaRobot,
            loser = cyanWon ? _bout.magentaRobot : _bout.cyanRobot,
            winnerFace = cyanWon ? _bout.cyanFace : _bout.magentaFace,
            stage = _bout.stage,
            finisher = _deciderFinisher,
            knockout = _deciderWasKo,
            score = cyanWon ? $"{_cyanPips}-{_magentaPips}" : $"{_magentaPips}-{_cyanPips}",
            pilot = PilotName(),
            // P1 is the human corner in Brawl proper; in the exhibition nobody
            // is playing, so nobody won "as" anything.
            playerWon = _bout.playerControls && cyanWon,
            playerFought = _bout.playerControls,
        };

        // The challenge always seats the receiver in the SENDER's corner: the
        // point is "here is my fight, do better", not "here is a fight".
        var challenge = new ChallengeLink.Challenge
        {
            mode = _bout.playerControls ? ChallengeLink.ModeBrawl : ChallengeLink.ModeBrawlWar,
            cyan = _bout.cyanRobot,
            magenta = _bout.magentaRobot,
            stage = _bout.stage,
            difficulty = _bout.difficulty,
            from = PilotName(),
            score = card.score,
            cyanWon = cyanWon,
        };

        Texture2D picture = ShareCard.Render(card);
        string headline = ShareCard.Headline(card);
        string url = ChallengeLink.Url(challenge);

        Metrics.Track("brawl_result",
            ("winner", card.winner ?? ""),
            ("loser", card.loser ?? ""),
            ("score", card.score),
            ("ko", _deciderWasKo ? "1" : "0"),
            ("player", _bout.playerControls ? "1" : "0"));

        _hud.ShowEndPanel(result, picture,
            onShare: () =>
            {
                Metrics.Track("challenge_share",
                    ("mode", challenge.mode),
                    ("won", card.playerWon ? "1" : "0"),
                    ("signed_in", string.IsNullOrEmpty(challenge.from) ? "0" : "1"));
                ShareBridge.OpenSheet(headline, challenge.Boast(), url, picture);
            },
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

    /// <summary>
    /// The signed-in pilot's name, or empty when nobody is signed in — NOT
    /// AccountClient.DisplayName, which answers "GUEST" so that a HUD always
    /// has something to print. On a card and in a link, empty is the useful
    /// answer: it is what turns the by-line into "CAN YOU BEAT IT?" rather
    /// than into "GUEST · CAN YOU BEAT IT?", and it is what the share metric
    /// counts to tell a named challenge from an anonymous one.
    /// </summary>
    static string PilotName()
    {
        var account = AccountClient.Instance;
        return account != null && account.SignedIn ? account.Username : "";
    }

    void SetLocked(bool locked)
    {
        _cyan.ControlsLocked = locked;
        _magenta.ControlsLocked = locked;
    }
}
