using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// CHINESE QUEST — the learning mode.
///
/// A hero robot stands centre stage. A Chinese character hangs at the bottom
/// of the screen, and four robots ring the hero, one at each corner, each
/// holding up a meaning and its pinyin. Pick one and the hero attacks it:
/// a photon blast down the diagonal, or a run-in and a kung-fu strike. Get it
/// right and that robot goes down, the hero celebrates and the word is read
/// aloud. Get it wrong and the robot blocks, charges back and lands one on
/// the hero — then the real answer stands up and says itself.
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

    public const int Options = 4;

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
    /// <summary>The counter-punch leans ranged — a charge across the stage twice a round drags.</summary>
    const float CounterBlastChance = 0.6f;

    /// <summary>
    /// Nothing in a round should take this long. If one does — a robot wedged
    /// on stage geometry, or a coroutine killed by a recompile mid-play — the
    /// watchdog deals the next question rather than leaving a dead screen.
    ///
    /// Set well clear of the honest worst case, which is a wrong answer where
    /// both the strike and the counter-strike are charges that time out:
    /// roughly 20 s of legitimate waiting. A watchdog that can fire on a slow
    /// round is worse than no watchdog, because it looks like the bug.
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

    ChineseLexicon.Deck _deck;
    ChineseLexicon.Word _word;
    readonly ChineseLexicon.Word[] _options = new ChineseLexicon.Word[Options];
    int _correct;

    /// <summary>
    /// Words asked recently, newest last — a deck of twelve that re-asks the
    /// same character three times in a row does not feel random, it feels
    /// broken. Half the deck is held back, so short decks still rotate.
    /// </summary>
    readonly List<string> _recent = new List<string>();

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
    bool _heroBlocked;
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
        _deck = ChineseLexicon.DeckAt(_deckIndex);

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

        _hud = ChineseQuestHud.Build(transform, _camera, _deck);
        _hud.OnPicked = Answer;
        _hud.OnPlayAgain = Restart;
        _hud.OnHear = () => ChineseVoice.Say(_word);
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
        _hero.OnHitBlocked = (victim, move) => _heroBlocked = true;

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
        FaceTheHouse();
    }

    /// <summary>
    /// Everyone square to the camera, presenting themselves — the line-up
    /// pose between questions.
    ///
    /// A BrawlFighter turns to face its Opponent every frame it is standing
    /// free, so posing one means having no opponent at all. The pairing is
    /// made at the moment an answer is picked and unmade when the round ends;
    /// see <see cref="RunRound"/>.
    /// </summary>
    void FaceTheHouse()
    {
        // The camera looks up +Z, so facing it is a half turn: robots model
        // +Z as forward.
        var toCamera = Quaternion.Euler(0f, 180f, 0f);
        _hero.Opponent = null;
        _hero.transform.rotation = toCamera;
        foreach (var bot in _answers)
        {
            if (bot == null)
                continue;
            bot.Opponent = null;
            bot.transform.rotation = toCamera;
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
        FaceTheHouse();

        DealQuestion();
        _hud.ShowQuestion(_word, _options);
        _hud.SetCardsLive(true);
        _director.Relax();
        // Said the moment it appears, not only once it is answered. A learner
        // who never hears the character until after they have guessed is being
        // tested, not taught — and the sound is half of what a character IS.
        ChineseVoice.Say(_word);

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

    /// <summary>
    /// Roll the question: one word the player has not seen lately, and three
    /// decoys from the SAME deck. Same-deck decoys are the point — four words
    /// from four themes can be answered off the theme alone, without ever
    /// reading the character.
    /// </summary>
    void DealQuestion()
    {
        var words = _deck.words;
        int hold = Mathf.Min(_recent.Count, Mathf.Max(0, words.Length / 2 - 1));

        int pick = Random.Range(0, words.Length);
        for (int attempt = 0; attempt < 40; attempt++)
        {
            int candidate = Random.Range(0, words.Length);
            if (!RecentlyAsked(words[candidate].hanzi, hold))
            {
                pick = candidate;
                break;
            }
        }
        _word = words[pick];
        _recent.Add(_word.hanzi);
        if (_recent.Count > words.Length)
            _recent.RemoveAt(0);

        // Three distinct decoys. Distinct by MEANING, not by index: a deck
        // holding two words that both mean "old" would otherwise offer the
        // player two correct-looking answers and mark one of them wrong.
        var chosen = new List<ChineseLexicon.Word> { _word };
        for (int attempt = 0; attempt < 200 && chosen.Count < Options; attempt++)
        {
            var candidate = words[Random.Range(0, words.Length)];
            bool clash = false;
            foreach (var taken in chosen)
                if (taken.english == candidate.english || taken.hanzi == candidate.hanzi)
                    clash = true;
            if (!clash)
                chosen.Add(candidate);
        }
        // A deck too small or too repetitive to fill four slots pads with
        // whatever is left rather than dealing a broken question.
        for (int i = 0; chosen.Count < Options && i < words.Length; i++)
            if (!chosen.Contains(words[i]))
                chosen.Add(words[i]);

        for (int i = 0; i < Options; i++)
            _options[i] = chosen[i % chosen.Count];

        // Shuffle, then find where the answer landed.
        for (int i = Options - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (_options[i], _options[j]) = (_options[j], _options[i]);
        }
        _correct = 0;
        for (int i = 0; i < Options; i++)
            if (_options[i].hanzi == _word.hanzi)
                _correct = i;
    }

    bool RecentlyAsked(string hanzi, int hold)
    {
        for (int i = _recent.Count - hold; i < _recent.Count; i++)
            if (i >= 0 && _recent[i] == hanzi)
                return true;
        return false;
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
    /// report (a hit landed, a guard held) rather than on a guessed duration.
    /// </summary>
    IEnumerator RunRound(int index)
    {
        var target = _answers[index];
        bool correct = index == _correct;
        _asked++;

        _hud.MarkChosen(index, correct);
        _director.WatchFight(_hero.transform, target.transform);

        // The pairing: from here until the round ends these two look at each
        // other, which is also what aims every strike between them.
        _hero.Opponent = target;
        target.Opponent = _hero;
        // A wrong answer raises its guard — the hero's strike will be eaten,
        // which is what sells "that was not it" before any text appears.
        target.Driven.block = !correct;

        _heroLanded = _heroBlocked = false;
        yield return new WaitForSeconds(TurnBeat);
        yield return Attack(_hero, target, Random.value < BlastChance);
        yield return Until(() => _heroLanded || _heroBlocked, 2.2f);

        if (correct)
            yield return Reward(target);
        else
            yield return Punish(target);

        // Unpair before the reset so nobody spins to face a departing enemy.
        _hero.Opponent = null;
        target.Opponent = null;
        target.Driven = default;

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
        _hud.Correct(_word, _streak);
        _director.Celebrate(_hero.transform);

        ChineseVoice.Say(_word);
        yield return new WaitForSeconds(Mathf.Max(RevealBeat, ChineseVoice.LengthOf(_word) + 0.7f));
    }

    /// <summary>
    /// Wrong: the guard held, and now the robot the player picked comes back
    /// at them. Then the real answer stands up and says itself — the miss is
    /// the moment the word is worth teaching.
    /// </summary>
    IEnumerator Punish(BrawlFighter wrong)
    {
        _streak = 0;

        // Guard down, gloves up.
        wrong.Driven.block = false;
        yield return new WaitForSeconds(0.25f);

        _heroHurt = false;
        _director.WatchFight(wrong.transform, _hero.transform);
        yield return Attack(wrong, _hero, Random.value < CounterBlastChance);
        yield return Until(() => _heroHurt, 2.4f);

        _shields = Mathf.Max(0, _shields - 1);
        _hud.SetShields(_shields, MaxShields);
        _hud.SetScore(_score, _streak);
        _director.Shake(0.55f);

        // The teaching beat: the right robot rises, its card lights green,
        // and the word is spoken.
        var answer = _answers[_correct];
        answer.Opponent = null;
        answer.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        answer.Celebrate();
        VfxUtil.SpawnBurst(answer.transform.position + Vector3.up * 2.1f,
            new Color(0.35f, 1f, 0.55f), 20, 4f, 0.14f);

        _hud.Wrong(_word, _correct);
        ChineseVoice.Say(_word);
        yield return new WaitForSeconds(Mathf.Max(RevealBeat + 0.4f, ChineseVoice.LengthOf(_word) + 1.0f));
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
        // Say it again. Space because it is the biggest key on the board and
        // this is the one thing a player will want to repeat.
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.H))
        {
            ChineseVoice.Say(_word);
            return;
        }

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
        FaceTheHouse();
        _hud.ShowResults(_score, _right, _asked, _bestStreak);
        BrawlAudio.PlayFlat(BrawlAudio.Id.Gong, 0.8f);
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
        _recent.Clear();
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
