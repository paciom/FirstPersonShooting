using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// CHINESE QUEST — the learning mode.
///
/// A hero robot stands centre stage. A Chinese character hangs at the bottom
/// of the screen, and four robots ring the hero, one at each corner, all
/// turned in on it, each holding up a meaning and its pinyin.
///
/// Choosing is what earns the pronunciation — the word is spoken the instant
/// an answer is committed to, right or wrong, and never before, because every
/// card carries pinyin and a character read aloud beside them turns reading
/// into matching. Then the ROBOT HOLDING THE RIGHT ANSWER settles it, in one
/// direction or the other: find it and the hero charges it down; miss it and
/// it crosses the stage and floors the hero, which is a lesson where a robot
/// that was wrong punishing you would have been only a penalty.
///
/// It is a quiz wearing the fighting game's clothes: nothing here re-implements
/// combat. The five robots ARE BrawlFighters, driven through the same
/// <see cref="BrawlFighter.Intent"/> struct the player's controller fills in
/// Brawl, so every punch, block, knockdown and hit reaction is the real one
/// with the real frame data. The only thing this file adds is choreography —
/// who attacks whom, and when.
///
/// World lifecycle is Brawl's, exactly: hide the FPS cast, stand down the
/// arena block manager, switch off the Environment's children, build a stage
/// under it, and hand the world back through ArenaRuntime.Load on teardown.
/// </summary>
public class ChineseQuest : MonoBehaviour
{
    enum Phase { Asking, Resolving, Over }

    // ------------------------------------------------------------ the stage
    //
    // Five posts inside BrawlStage's 16x12 deck: the hero on the centre line
    // and four robots pushed out toward the corners of the frame. The far
    // pair sits wider apart than perspective suggests, because the near pair
    // is closer to the camera and spreads on its own.

    static readonly Vector3 HeroPost = new Vector3(0f, 0f, 0.4f);

    /// <summary>
    /// Reading order — the same 1,2,3,4 the number keys answer with, and the
    /// same order top-left, top-right, bottom-left, bottom-right that the
    /// cards end up in on screen.
    ///
    /// The near pair is pushed toward the camera rather than only outward:
    /// spreading on x alone put all four robots in the middle band of the
    /// frame, and it is the depth that drives them into the top and bottom
    /// corners. Every post stays inside BrawlStage's 16x12 deck, which is
    /// also what BrawlFighter clamps to.
    ///
    /// Tight, and tighter than the frame suggests. Because the camera fits
    /// itself to the cast, spreading the posts wider does NOT push the robots
    /// into the corners — it just walks the camera back, and everyone ends up
    /// the same size on screen but further apart in the world, which costs
    /// nothing but the seconds a charge takes to cross. Pulling them in keeps
    /// the corner layout and makes the robots BIGGER, since they do not shrink
    /// with the stage.
    /// </summary>
    static readonly Vector3[] AnswerPosts =
    {
        new Vector3(-5.4f, 0f, 3.8f),    // 1  far left    → top-left
        new Vector3(5.4f, 0f, 3.8f),     // 2  far right   → top-right
        new Vector3(-4.6f, 0f, -2.9f),   // 3  near left   → bottom-left
        new Vector3(4.6f, 0f, -2.9f),    // 4  near right  → bottom-right
    };

    /// <summary>Answers per question — the quiz's number, not this mode's.</summary>
    public const int Options = ChineseQuiz.Count;

    // ------------------------------------------------------------- the rules

    /// <summary>Hits the player can take before the run ends.</summary>
    const int MaxShields = 5;

    const int PointsPerWord = 10;
    /// <summary>Every answer past the first in a streak is worth this much more.</summary>
    const int StreakBonus = 5;

    // Beats, in seconds. Long enough to read, short enough to keep swinging.
    // These are WALL clock and stay that way: they are reading time, and the
    // one thing that must not speed up with the fighting is the moment the
    // player is being taught something.
    const float TurnBeat = 0.25f;
    const float RevealBeat = 1.6f;
    const float BetweenRounds = 0.35f;

    /// <summary>
    /// The cast's clock. A bout's pacing is deliberate because two players are
    /// reading each other; a quiz answered forty times in a run is not, and at
    /// Brawl speed the wait between clicking and knowing dominates the mode.
    /// Doubling scales the strike and its animation together — see
    /// <see cref="BrawlFighter.Tempo"/>.
    /// </summary>
    const float FightTempo = 2f;

    /// <summary>
    /// And the charge across the stage is faster still: four times a walk is
    /// 12 m/s, which crosses to an answer in under half a second. A robot that
    /// has to be watched jogging to its target every single question is the
    /// mode's worst beat, so it is the one that gets rocket boots.
    /// </summary>
    const float ChargeTempo = 4f;

    /// <summary>How often the hero picks the ranged answer over running in.</summary>
    const float BlastChance = 0.5f;
    /// <summary>
    /// The right answer's reprisal leans ranged. It is the only attack in a
    /// missed round — the hero never swings — so it can afford to be the fast
    /// kind more often than not.
    /// </summary>
    const float PunishBlastChance = 0.6f;

    /// <summary>
    /// Nothing in a round should take this long. If one does — a robot wedged
    /// on stage geometry, or a coroutine killed by a recompile mid-play — the
    /// watchdog deals the next question rather than leaving a dead screen.
    ///
    /// Set well clear of the honest worst case — a charge that times out, its
    /// strike, and the reveal, with every wait taking its full bound. A
    /// watchdog that can fire on a merely slow round is worse than no
    /// watchdog, because it looks like the bug.
    /// </summary>
    const float RoundWatchdog = 32f;

    // -------------------------------------------------------------- the mode

    GameObject _stageRoot;
    GameObject _cameraRig;
    Camera _camera;
    ChineseQuestCamera _director;
    ChineseQuestHud _hud;
    ArenaBlockManager _blockManager;
    readonly List<GameObject> _hiddenCharacters = new List<GameObject>();

    BrawlFighter _hero;
    BrawlFighter[] _answers = new BrawlFighter[Options];

    /// <summary>Which word, and the three decoys — see ChineseQuiz.</summary>
    ChineseQuiz _quiz;

    Phase _phase = Phase.Over;
    float _phaseTime;

    int _score;
    int _streak;
    int _bestStreak;
    int _asked;
    int _right;
    int _shields = MaxShields;

    // Set by the fighters' own events — the choreography waits on these
    // rather than guessing how long a strike takes to connect.
    bool _heroLanded;
    bool _heroHurt;

    [SerializeField] int _deckIndex;

    public static ChineseQuest Begin(GameModeController owner, RobotRoster roster, int deckIndex)
    {
        var go = new GameObject("ChineseQuest");
        go.transform.SetParent(owner.transform, false);
        var quest = go.AddComponent<ChineseQuest>();
        quest._deckIndex = deckIndex;
        quest.Setup(roster);
        return quest;
    }

    // ---------------------------------------------------------------- set-up

    void Setup(RobotRoster roster)
    {
        _quiz = new ChineseQuiz(_deckIndex);

        var player = FindFirstObjectByType<PlayerBrain>();
        if (player != null)
            Hide(player.gameObject);
        foreach (var bot in FindObjectsByType<AIBrain>(FindObjectsSortMode.None))
            Hide(bot.gameObject);

        _blockManager = FindFirstObjectByType<ArenaBlockManager>();
        if (_blockManager != null)
            _blockManager.enabled = false;

        var surface = FindFirstObjectByType<NavMeshSurface>(FindObjectsInactive.Include);
        Transform environment = surface != null ? surface.transform : null;
        if (environment != null)
            foreach (Transform child in environment)
                child.gameObject.SetActive(false);

        BrawlGround.Clear();
        BrawlProps.Clear();
        // hazards: false, for the same reason the Martial Arts Show turns them
        // off — falling fire and mines are the arena fighting back, and this
        // screen is asking the player to read.
        _stageRoot = BrawlStage.Build(environment, default, roster, hazards: false);

        SpawnCast(roster);

        _cameraRig = BuildCameraRig();
        _camera = _cameraRig.GetComponent<Camera>();
        _director = _cameraRig.GetComponent<ChineseQuestCamera>();
        _director.FrameCast(KeyPoints());

        _hud = ChineseQuestHud.Build(transform, _camera, _quiz.Deck);
        _hud.OnPicked = Answer;
        _hud.OnPlayAgain = Restart;
        for (int i = 0; i < Options; i++)
            _hud.SetAnchor(i, _answers[i].transform);

        _hud.SetScore(_score, _streak);
        _hud.SetShields(_shields, MaxShields);
        BeginRound();
    }

    /// <summary>
    /// Five robots from the roster: the hero in team cyan, the four answers in
    /// magenta so "the thing I am shooting at" needs no explaining. Different
    /// models on purpose — four identical robots holding four different words
    /// are hard to tell apart at a glance, and glancing is the whole input.
    /// </summary>
    void SpawnCast(RobotRoster roster)
    {
        int fleet = (roster != null && roster.HasRobots) ? roster.robots.Length : 1;

        _hero = BrawlFighter.Spawn(_stageRoot.transform, roster, 0, 0);
        _hero.Tempo = FightTempo;
        _hero.ResetAt(HeroPost.x, HeroPost.z);
        _hero.OnHitLanded = (victim, damage, knockdown) => _heroLanded = true;

        for (int i = 0; i < Options; i++)
        {
            // 1, 2, 3, 4 into the roster — never 0, which the hero wears.
            int model = fleet > 1 ? 1 + i % (fleet - 1) : 0;
            var bot = BrawlFighter.Spawn(_stageRoot.transform, roster, model, 1);
            bot.name = $"Answer_{i + 1}";
            bot.Tempo = FightTempo;
            bot.ResetAt(AnswerPosts[i].x, AnswerPosts[i].z);
            bot.OnHitLanded = (victim, damage, knockdown) => _heroHurt = true;
            _answers[i] = bot;
        }
        FaceTheCentre();
    }

    /// <summary>
    /// The ring: the hero square to the camera, presenting itself, and all
    /// four answers turned in on it.
    ///
    /// The answers are aimed by giving them the hero as their Opponent rather
    /// than by setting a rotation, because a BrawlFighter turns to face its
    /// opponent every frame it is standing free — one assignment keeps them
    /// looking inward through a get-up, a shove and a walk home, where a
    /// one-off rotation would be undone by the next frame. The rotation IS
    /// also set here, for the one frame before their own Update catches up.
    ///
    /// The hero is the odd one out: no opponent at all, so nothing overwrites
    /// the half turn that shows it to the player. It is paired only for the
    /// exchange, and unpaired again when the round ends — see
    /// <see cref="RunRound"/>.
    /// </summary>
    void FaceTheCentre()
    {
        // The camera looks up +Z, so facing it is a half turn: robots model
        // +Z as forward.
        _hero.Opponent = null;
        _hero.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

        foreach (var bot in _answers)
        {
            if (bot == null)
                continue;
            bot.Opponent = _hero;
            Vector3 inward = _hero.transform.position - bot.transform.position;
            inward.y = 0f;
            if (inward.sqrMagnitude > 1e-4f)
                bot.transform.rotation = Quaternion.LookRotation(inward.normalized, Vector3.up);
        }
    }

    /// <summary>Feet, head and shoulders of all five robots — what the camera must fit.</summary>
    Vector3[] KeyPoints()
    {
        var points = new List<Vector3>();
        void Add(Vector3 post)
        {
            points.Add(post);
            points.Add(post + Vector3.up * BrawlMoveSet.BodyHeight);
            points.Add(post + Vector3.up * BrawlMoveSet.BodyHeight + Vector3.right * 0.9f);
            points.Add(post + Vector3.up * BrawlMoveSet.BodyHeight - Vector3.right * 0.9f);
        }
        Add(HeroPost);
        foreach (var post in AnswerPosts)
            Add(post);
        return points.ToArray();
    }

    // ------------------------------------------------------------ the rounds

    /// <summary>
    /// Deal the next question. Deliberately NOT a coroutine stopper: the round
    /// coroutine's last act is to call this, and tearing down the caller from
    /// inside the caller is the kind of thing that works until it doesn't.
    /// Everything that interrupts a round mid-flight — the watchdog, PLAY
    /// AGAIN, leaving the mode — calls StopRound itself first.
    /// </summary>
    void BeginRound()
    {
        WarpHome(_hero, HeroPost);
        for (int i = 0; i < Options; i++)
        {
            _answers[i].Driven = default;
            WarpHome(_answers[i], AnswerPosts[i]);
        }
        FaceTheCentre();

        _quiz.Deal();
        _hud.ShowQuestion(_quiz.Word, _quiz.Choices);
        _hud.SetCardsLive(true);
        _director.Relax();
        // Pointedly NOT spoken here. Every card carries pinyin, so a character
        // read aloud beside them turns the question from "what does this mean"
        // into "which of these four sounds did I just hear" — the player never
        // has to look at the character at all. It is said the instant an answer
        // is committed to instead; see RunRound.

        _phase = Phase.Asking;
        _phaseTime = 0f;
    }

    /// <summary>
    /// Back to your post. A robot that charged across the stage (or was blown
    /// off it) is set down where it started, and the move is dressed as a warp
    /// rather than hidden: a burst at each end reads as teleporting back to
    /// your mark, where a silent snap reads as a bug. Robots that never left
    /// get neither.
    /// </summary>
    static void WarpHome(BrawlFighter fighter, Vector3 post)
    {
        Vector3 from = fighter.transform.position;
        Vector3 gap = from - post;
        gap.y = 0f;
        bool travelled = gap.magnitude > 0.6f;
        fighter.ResetAt(post.x, post.z);
        // Also where rocket boots are taken off: a charge abandoned by the
        // watchdog or by PLAY AGAIN must not leave a robot stuck at 4x.
        fighter.Tempo = FightTempo;
        if (!travelled)
            return;
        var spark = new Color(0.45f, 0.85f, 1f);
        VfxUtil.SpawnBurst(from + Vector3.up, spark, 10, 3.2f, 0.11f);
        VfxUtil.SpawnBurst(fighter.transform.position + Vector3.up, spark, 10, 3.2f, 0.11f);
    }

    /// <summary>A card, a robot or a number key was picked. One answer per round.</summary>
    public void Answer(int index)
    {
        if (_phase != Phase.Asking || index < 0 || index >= Options)
            return;
        _phase = Phase.Resolving;
        _phaseTime = 0f;
        _hud.SetCardsLive(false);
        StartCoroutine(RunRound(index));
    }

    /// <summary>
    /// The whole beat, from the click to the next question. Written as one
    /// coroutine rather than a state machine because that is what it is: a
    /// sequence with waits in it, and every wait is on something the fighters
    /// report — a hit landing — rather than on a guessed duration.
    /// </summary>
    IEnumerator RunRound(int index)
    {
        // The right robot either way. It is the one the hero attacks when the
        // player finds it, and the one that comes for the hero when they do
        // not — so the character always ends the round standing next to the
        // robot that was holding its meaning, whichever direction the punch
        // travelled.
        var answer = _answers[_quiz.Correct];
        bool correct = index == _quiz.Correct;
        _asked++;

        _hud.MarkChosen(index, correct);
        // The syllabus hears about it before anything else does: on the
        // INFINITE deck this is what advances a word toward being retired.
        _quiz.Report(correct);
        // The correct word, said the moment a choice is committed to — right
        // or wrong, always, exactly once. The pronunciation is what answering
        // buys, so it must never arrive before an answer.
        ChineseVoice.Say(_quiz.Word);

        // The pairing: from here until the round ends these two look at each
        // other, which is also what aims every strike between them. The hero
        // needs it explicitly because it alone stands unpaired between rounds.
        _hero.Opponent = answer;

        if (correct)
        {
            _director.WatchFight(_hero.transform, answer.transform);
            _heroLanded = false;
            yield return new WaitForSeconds(TurnBeat);
            yield return Attack(_hero, answer, Random.value < BlastChance);
            yield return Until(() => _heroLanded, 2.2f);
            yield return Reward(answer);
        }
        else
        {
            // The hero holds its mark and takes it. The robot that was right
            // is the one that crosses the stage.
            yield return Punish(answer);
        }

        // Unpaired before the reset so the hero does not spin to follow a
        // robot walking away. The answers keep theirs — facing the centre is
        // their resting pose, not a fight.
        _hero.Opponent = null;
        answer.Driven = default;

        if (_shields <= 0)
        {
            GameOver();
            yield break;
        }

        yield return new WaitForSeconds(BetweenRounds);
        BeginRound();
    }

    /// <summary>Right: the target goes down, the hero poses, the word says itself.</summary>
    IEnumerator Reward(BrawlFighter target)
    {
        _right++;
        _streak++;
        _bestStreak = Mathf.Max(_bestStreak, _streak);
        _score += PointsPerWord + (_streak - 1) * StreakBonus;

        // Floored for good, whatever the strike's own frame data decided —
        // a defeated answer that stands back up muddies the reading.
        target.KnockOut();
        _hero.Celebrate();

        Vector3 crown = _hero.transform.position + Vector3.up * 2.1f;
        VfxUtil.SpawnBurst(crown, new Color(1f, 0.86f, 0.3f), 26, 4.5f, 0.16f);
        VfxUtil.Explosion(target.transform.position + Vector3.up, MatchAnnouncer.TeamColor(1), 0.6f);
        // Pulled back from 0.7: the sting and the spoken word land together,
        // and the word is the half worth hearing.
        BrawlAudio.PlayFlat(BrawlAudio.Id.Victory, 0.45f);

        _hud.SetScore(_score, _streak);
        _hud.Correct(_quiz.Word, _streak);
        _director.Celebrate(_hero.transform);

        yield return new WaitForSeconds(RevealBeat);
    }

    /// <summary>
    /// Wrong: the hero stands its ground — no swing, no charge — and the robot
    /// that WAS the answer crosses the stage and hits it.
    ///
    /// The attacker is the correct answer rather than the one the player
    /// picked, which makes the round say the right sentence. The red card is
    /// what you chose; the green one is what was true, and it is the one that
    /// walks over. A robot that was wrong punishing you for agreeing with it
    /// taught nothing.
    ///
    /// The hero is paired to its attacker, so it turns to face what is coming.
    /// Turning is not moving; the feet stay on the mark.
    /// </summary>
    IEnumerator Punish(BrawlFighter answer)
    {
        _streak = 0;

        _heroHurt = false;
        _director.WatchFight(answer.transform, _hero.transform);
        yield return new WaitForSeconds(TurnBeat);
        yield return Attack(answer, _hero, Random.value < PunishBlastChance);
        yield return Until(() => _heroHurt, 2.4f);

        _shields = Mathf.Max(0, _shields - 1);
        _hud.SetShields(_shields, MaxShields);
        _hud.SetScore(_score, _streak);
        _director.Shake(0.55f);

        // The teaching beat: the robot that was right takes its pose over the
        // hero it just floored, and its card lights green beside the red one.
        answer.Celebrate();
        VfxUtil.SpawnBurst(answer.transform.position + Vector3.up * 2.1f,
            new Color(0.35f, 1f, 0.55f), 20, 4f, 0.14f);

        _hud.Wrong(_quiz.Word, _quiz.Correct);
        yield return new WaitForSeconds(RevealBeat + 0.4f);
    }

    // -------------------------------------------------------- the swing itself

    /// <summary>
    /// One robot attacks another, either down the diagonal or in its face.
    /// Both routes go through <see cref="BrawlFighter.Intent"/> — the same
    /// struct a human's controller fills — so this is the real move with the
    /// real frame data, not a special-cased animation.
    /// </summary>
    IEnumerator Attack(BrawlFighter attacker, BrawlFighter target, bool ranged)
    {
        if (ranged)
        {
            // The blast meter is earned in a bout and granted here: reading a
            // character right is this mode's version of landing a combo.
            attacker.GrantCharge(1f);
            yield return new WaitForSeconds(0.2f);
            yield return Press(attacker, blast: true);
            yield break;
        }

        var move = BrawlMoveSet.Table[BrawlMoveSet.Move.Kick];
        // Rocket boots on for the crossing, off for the strike — the charge is
        // dead time, the strike is the thing being watched.
        attacker.Tempo = ChargeTempo;
        VfxUtil.SpawnBurst(attacker.transform.position + Vector3.up * 0.35f,
            new Color(0.5f, 0.9f, 1f), 14, 5f, 0.12f);
        // Stop just inside reach: BrawlFighter's own Separate() holds the
        // pair 0.9 m apart, so anything tighter is a wall the walk cannot pass.
        yield return CloseIn(attacker, target, Mathf.Max(BrawlMoveSet.MinSeparation + 0.15f,
            move.range * 0.8f), 3.2f);
        attacker.Tempo = FightTempo;
        yield return Press(attacker, punch: Random.value < 0.45f, kick: true);
    }

    /// <summary>Walk toward the target until it is in reach, or until time runs out.</summary>
    IEnumerator CloseIn(BrawlFighter walker, BrawlFighter target, float stopAt, float timeout)
    {
        float spent = 0f;
        while (spent < timeout)
        {
            Vector3 gap = target.transform.position - walker.transform.position;
            gap.y = 0f;
            if (gap.magnitude <= stopAt)
                break;
            gap.Normalize();
            walker.Driven.move = new Vector2(gap.x, gap.z);
            spent += Time.deltaTime;
            yield return null;
        }
        walker.Driven.move = Vector2.zero;
    }

    /// <summary>
    /// Hold a button down long enough for the fighter's own Update to read it,
    /// then let go and let the move play out.
    ///
    /// The hold is not one frame: coroutines resume AFTER every Update has
    /// run, so an intent set and cleared inside a single resume would be
    /// written and erased without BrawlFighter ever seeing it. Waiting for
    /// the phase to actually change is what makes this exact rather than
    /// lucky.
    /// </summary>
    IEnumerator Press(BrawlFighter fighter, bool punch = false, bool kick = false,
        bool blast = false)
    {
        fighter.Driven.punch = punch;
        fighter.Driven.kick = kick;
        fighter.Driven.blast = blast;

        yield return Until(() => fighter.Phase == BrawlFighter.State.Attacking
                                 || fighter.Phase == BrawlFighter.State.AirAttack, 0.6f);
        fighter.Driven = default;

        yield return Until(() => fighter.Phase != BrawlFighter.State.Attacking
                                 && fighter.Phase != BrawlFighter.State.AirAttack, 2.0f);
    }

    /// <summary>Wait for a condition, but never forever.</summary>
    static IEnumerator Until(System.Func<bool> done, float timeout)
    {
        float spent = 0f;
        while (spent < timeout && !done())
        {
            spent += Time.deltaTime;
            yield return null;
        }
    }

    // ------------------------------------------------------------------ input

    void Update()
    {
        _phaseTime += Time.deltaTime;

        if (_phase == Phase.Asking)
        {
            ReadPick();
            return;
        }

        // The watchdog. A robot wedged against stage geometry, or a coroutine
        // lost to a recompile mid-play, must not end up as a screen that
        // never asks again.
        if (_phase == Phase.Resolving && _phaseTime > RoundWatchdog)
        {
            Debug.LogWarning("[ChineseQuest] Round stalled — dealing the next question.");
            StopRound();
            BeginRound();
        }
    }

    /// <summary>
    /// Three ways in, all equal: the number keys, the answer cards, and the
    /// robots themselves. Tapping the robot is the one an eight-year-old
    /// reaches for first, so it cannot be the one that does not work.
    /// </summary>
    void ReadPick()
    {
        for (int i = 0; i < Options; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
            {
                Answer(i);
                return;
            }
        }

        if (!Input.GetMouseButtonDown(0))
            return;
        // A click that the HUD already handled is not also a click on the
        // world behind it.
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        // Layer 2 (Ignore Raycast) is where BrawlFighter parks its body
        // capsule so terrain probes never read a robot as scenery — which
        // makes it the one layer holding exactly the five robots and nothing
        // else. Triggers ignored: the per-bone hurtboxes are not targets.
        if (!Physics.Raycast(_camera.ScreenPointToRay(Input.mousePosition), out var hit,
                200f, 1 << 2, QueryTriggerInteraction.Ignore))
            return;
        var picked = hit.collider.GetComponentInParent<BrawlFighter>();
        for (int i = 0; i < Options; i++)
            if (_answers[i] == picked)
            {
                Answer(i);
                return;
            }
    }

    // ------------------------------------------------------------- end of run

    void GameOver()
    {
        StopRound();
        _phase = Phase.Over;
        FaceTheCentre();
        _hud.ShowResults(_score, _right, _asked, _bestStreak);
        BrawlAudio.PlayFlat(BrawlAudio.Id.Gong, 0.8f);
        LeaderboardClient.Submit(GameMode.ChineseQuest, _quiz.Deck.title, _score);
    }

    /// <summary>The results panel's PLAY AGAIN: same deck, clean slate.</summary>
    public void Restart()
    {
        StopRound();
        _score = 0;
        _streak = 0;
        _bestStreak = 0;
        _asked = 0;
        _right = 0;
        _shields = MaxShields;
        _hud.HideResults();
        _hud.SetScore(_score, _streak);
        _hud.SetShields(_shields, MaxShields);
        BeginRound();
    }

    /// <summary>
    /// Abandon a round in flight, and shut the voice up with it — leaving the
    /// mode should not leave a word finishing itself over the main menu.
    /// StopAllCoroutines rather than a stored handle because this object runs
    /// exactly one coroutine chain and never two, so a handle would only be a
    /// thing to forget to clear.
    /// </summary>
    void StopRound()
    {
        StopAllCoroutines();
        ChineseVoice.Stop();
    }

    // ------------------------------------------------------------- lifecycle

    public void Teardown()
    {
        StopRound();
        _quiz.Flush();
        ChineseVoice.Release();

        if (_cameraRig != null)
            Destroy(_cameraRig);
        if (_stageRoot != null)
        {
            // Immediate, like Brawl's: ArenaRuntime.Load below rebuilds the
            // world this frame, and a stage still standing when it does ends
            // up nested inside the arena it was supposed to replace.
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
        var rig = new GameObject("ChineseQuestCamera");
        // Tagged MainCamera so Camera.main resolves while the player's own
        // camera is inactive — the billboarding VFX read it.
        rig.tag = "MainCamera";
        var cam = rig.AddComponent<Camera>();
        cam.fieldOfView = 52f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = BrawlStage.VoidColor;
        rig.AddComponent<AudioListener>();
        var data = rig.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        rig.AddComponent<ChineseQuestCamera>();
        return rig;
    }
}
