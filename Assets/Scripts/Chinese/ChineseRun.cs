using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// CHINESE RUN — the learning mode as an endless runner.
///
/// The hero sprints down a road with no end. Every so often four robots are
/// standing across it, one per lane, each holding up a meaning and its pinyin,
/// and a character hangs at the bottom of the screen. The road itself is the
/// clock: the answer has to be chosen before the runner arrives, and running
/// out of road counts as getting it wrong.
///
/// Pick right and the hero accelerates into the robot holding the answer and
/// blows through it — a photon blast on the approach, or a kick delivered at
/// a dead sprint. Pick wrong (or pick nothing) and the robot that WAS right
/// fires down the road instead, and the hero is thrown backward far enough to
/// lose real ground. Either way the word is spoken the moment the choice is
/// made.
///
/// Where <see cref="ChineseQuest"/> is the same question asked standing still,
/// this one asks it under pressure — and the two share everything they can:
/// the lexicon, the decks, the packed font, the baked voice, and the HUD,
/// whose answer cards ride above their robots and so did not care that the
/// robots stopped being arranged in a ring.
/// </summary>
public class ChineseRun : MonoBehaviour
{
    enum Phase { Asking, Resolving, Over }

    /// <summary>
    /// The four lanes, left to right — also the order of the cards and of the
    /// 1-4 keys. Spaced so the runner can only ever reach the robot it steers
    /// at: 3.2 m between neighbours against a 0.9 m body means the other three
    /// are never in the way, which is what makes running THROUGH the gate safe
    /// without any collision special-casing.
    /// </summary>
    public static readonly float[] Lanes = { -4.8f, -1.6f, 1.6f, 4.8f };

    /// <summary>Answers per question — the quiz's number, not this mode's.</summary>
    public const int Options = ChineseQuiz.Count;

    // ------------------------------------------------------------- the rules

    /// <summary>Metres ahead of the runner a new gate is planted.</summary>
    const float GateLead = 58f;

    /// <summary>
    /// Metres short of the gate where the question closes. Not zero: the
    /// answer still has to play out — a charge, a strike, or a blast taken in
    /// the chest — and that needs road to happen on.
    /// </summary>
    const float AnswerDeadline = 12f;

    /// <summary>
    /// Neither side shoots from further off than this. BrawlBolt lives 1.6 s
    /// at 14 m/s, so a shot fired the moment a gate appears would expire in
    /// mid-air and hit nothing — the shooter waits for the road to close.
    /// </summary>
    const float StrikeRange = 18f;

    const int PointsPerWord = 10;
    const int StreakBonus = 5;

    // Clock multipliers — see BrawlFighter.Tempo.
    /// <summary>Cruising: three times a walk is 9 m/s, which reads as a run.</summary>
    const float RunTempo = 3f;
    /// <summary>The lunge at a correct answer. Getting it right should feel like acceleration.</summary>
    const float ChargeTempo = 4.5f;
    /// <summary>
    /// And the fall is SLOWER than the run. The knockdown is 1.7 s of fighter
    /// clock, which at cruising speed would be over in half a second — far too
    /// quick to read as the disaster it is, and shorter than the slide it has
    /// to cover.
    /// </summary>
    const float KnockbackTempo = 1.5f;

    /// <summary>How much road a wrong answer costs.</summary>
    const float KnockbackMetres = 32f;
    const float KnockbackTime = 1.1f;

    const float RevealBeat = 1.2f;
    const float RoundWatchdog = 26f;

    const string BestKey = "PhotonArena.ChineseRunBest";

    // -------------------------------------------------------------- the mode

    GameObject _stageRoot;
    GameObject _cameraRig;
    Camera _camera;
    ChineseRunCamera _director;
    ChineseRunRoad _road;
    ChineseQuestHud _hud;
    ArenaBlockManager _blockManager;
    readonly List<GameObject> _hiddenCharacters = new List<GameObject>();

    BrawlFighter _hero;
    readonly BrawlFighter[] _gate = new BrawlFighter[Options];
    float _gateZ;
    bool _gateLive;

    /// <summary>Which word, and the three decoys — see ChineseQuiz.</summary>
    ChineseQuiz _quiz;

    Phase _phase = Phase.Over;
    float _phaseTime;

    /// <summary>What the hero is running at, world XZ — forward, or at a target.</summary>
    Vector2 _steer = Vector2.up;

    /// <summary>
    /// True whenever nothing is steering the runner, which means it drifts
    /// back to the centre line.
    ///
    /// Not cosmetic. A gate is four robots in four lanes, and the runner only
    /// ever leaves the middle to charge one of them — so a runner that stayed
    /// in the lane it last fought in would meet the NEXT gate's robot head on
    /// in that same lane, having answered nothing. Coming back to the middle
    /// is what keeps 1.6 m of clearance either side and makes running through
    /// an open gate safe without a single collision test.
    /// </summary>
    bool _cruising = true;

    /// <summary>How hard the drift pulls back to centre, per metre off it.</summary>
    const float CentreDrift = 0.35f;

    RobotRoster _roster;

    int _score;
    int _streak;
    /// <summary>Best distance ever reached, kept between sessions.</summary>
    int _best;
    /// <summary>Furthest reached this run — what a wrong answer is measured against.</summary>
    float _furthest;

    bool _heroLanded;
    bool _heroHurt;

    [SerializeField] int _deckIndex;

    public static ChineseRun Begin(GameModeController owner, RobotRoster roster, int deckIndex)
    {
        var go = new GameObject("ChineseRun");
        go.transform.SetParent(owner.transform, false);
        var run = go.AddComponent<ChineseRun>();
        run._deckIndex = deckIndex;
        run.Setup(roster);
        return run;
    }

    // ---------------------------------------------------------------- set-up

    void Setup(RobotRoster roster)
    {
        _quiz = new ChineseQuiz(_deckIndex);
        _best = PlayerPrefs.GetInt(BestKey, 0);

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

        _stageRoot = new GameObject("RunStage");
        if (environment != null)
            _stageRoot.transform.SetParent(environment, false);

        // The fence keeps its sides and loses its end. BrawlFighter clamps
        // itself to this every frame, and the default 6 m of z would stop the
        // runner dead six metres in. Put back in Teardown.
        BrawlStage.SetBounds(new Vector2(ChineseRunRoad.HalfWidth, 1e6f));

        _road = ChineseRunRoad.Build(_stageRoot.transform);
        _roster = roster;

        _hero = BrawlFighter.Spawn(_stageRoot.transform, roster, 0, 0);
        _hero.Tempo = RunTempo;
        _hero.ResetAt(0f, 0f);
        _hero.transform.rotation = Quaternion.identity;
        _hero.OnHitLanded = (victim, damage, knockdown) => _heroLanded = true;

        _cameraRig = BuildCameraRig();
        _camera = _cameraRig.GetComponent<Camera>();
        _director = _cameraRig.GetComponent<ChineseRunCamera>();
        _director.Follow(_hero.transform);

        _hud = ChineseQuestHud.Build(transform, _camera, _quiz.Deck);
        _hud.OnPicked = Answer;
        // Distance, not shields: the road IS the score here, and a wrong
        // answer costs ground rather than a life. And a fixed row of cards,
        // not four floating over four robots — down a road they all converge
        // on the vanishing point and stack into an unreadable pile.
        _hud.UseDistanceMeter();
        _hud.UseCardRow();
        _hud.SetScore(_score, _streak);

        NextGate();
    }

    // ------------------------------------------------------------ the rounds

    /// <summary>
    /// Plant four robots across the road ahead and ask the next word. The old
    /// gate is cleared here rather than the moment it resolves, so its cards
    /// stay readable through the beat where the player is being told what
    /// happened.
    /// </summary>
    void NextGate()
    {
        ClearGate();

        _gateZ = _hero.transform.position.z + GateLead;
        _quiz.Deal();

        int fleet = (_roster != null && _roster.HasRobots) ? _roster.robots.Length : 1;
        for (int i = 0; i < Options; i++)
        {
            int model = fleet > 1 ? 1 + i % (fleet - 1) : 0;
            var bot = BrawlFighter.Spawn(_stageRoot.transform, _roster, model, 1);
            bot.name = $"Gate_{i + 1}";
            bot.Tempo = RunTempo;
            bot.ResetAt(Lanes[i], _gateZ);
            // The hero as opponent is what turns them to face the road they
            // are blocking, and keeps them turned as it closes.
            bot.Opponent = _hero;
            bot.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            bot.OnHitLanded = (victim, damage, knockdown) => _heroHurt = true;
            _gate[i] = bot;
        }

        _gateLive = true;
        _hud.ShowQuestion(_quiz.Word, _quiz.Choices);
        _hud.SetCardsShown(true);
        _hud.SetCardsLive(true);
        _phase = Phase.Asking;
        _phaseTime = 0f;
    }

    void ClearGate()
    {
        for (int i = 0; i < Options; i++)
        {
            if (_gate[i] == null)
                continue;
            // Everything leaves the same way it would in any other mode here:
            // a burst of light, not a body left on the road.
            VfxUtil.SpawnBurst(_gate[i].transform.position + Vector3.up,
                MatchAnnouncer.TeamColor(1), 12, 3.5f, 0.12f);
            Destroy(_gate[i].gameObject);
            _gate[i] = null;
        }
        _gateLive = false;
    }

    /// <summary>
    /// A lane was chosen — or the road ran out, which arrives here as -1 and
    /// is scored exactly like a wrong one. Not answering is an answer.
    /// </summary>
    public void Answer(int index)
    {
        if (_phase != Phase.Asking || !_gateLive)
            return;
        _phase = Phase.Resolving;
        _phaseTime = 0f;
        _hud.SetCardsLive(false);
        StartCoroutine(RunGate(index));
    }

    IEnumerator RunGate(int index)
    {
        var answer = _gate[_quiz.Correct];
        bool correct = index == _quiz.Correct;

        if (index >= 0)
            _hud.MarkChosen(index, correct);
        // The syllabus first: on the INFINITE deck this is what advances a
        // word toward being retired, and running out of road counts.
        _quiz.Report(correct);
        // The correct word, said the instant a choice is committed to — right,
        // wrong, or out of road.
        ChineseVoice.Say(_quiz.Word);

        if (correct)
        {
            _streak++;
            _score += PointsPerWord + (_streak - 1) * StreakBonus;
            _hud.SetScore(_score, _streak);
            _hud.Correct(_quiz.Word, _streak);
            yield return SmashThrough(answer);
        }
        else
        {
            _streak = 0;
            _hud.SetScore(_score, _streak);
            // Lit green while it is still standing there charging its shot, so
            // the robot about to fire and the answer that was right are read as
            // the same fact.
            _hud.Wrong(_quiz.Word, _quiz.Correct);
            yield return ThrownBack(answer);
        }

        _hud.SetCardsShown(false);
        yield return new WaitForSeconds(RevealBeat);
        NextGate();
    }

    /// <summary>
    /// Right: steer into that lane, open up, and go through whatever is
    /// standing in it — a blast taken on the approach, or a kick at a dead
    /// sprint. The road never stops moving underneath either one.
    /// </summary>
    IEnumerator SmashThrough(BrawlFighter answer)
    {
        if (answer == null)
            yield break;

        // Opponent, so the fighter aims itself: FaceOpponent turns the runner
        // toward the target every frame, and FacingDir is what the bolt leaves
        // along. It also stops SteerHeroVisually fighting for the rotation.
        _hero.Opponent = answer;
        _heroLanded = false;

        if (Random.value < 0.5f)
        {
            // Shot on the run, from its own lane. Deliberately NOT at charge
            // tempo: a boosted runner covers 13.5 m/s against a 14 m/s bolt,
            // so it arrives with its own shot and lands on top of the target
            // it just destroyed. At cruising speed the bolt gets there first,
            // which is the whole point of taking it.
            yield return Until(() => answer == null
                                     || _gateZ - _hero.transform.position.z <= StrikeRange, 5f);
            _hero.GrantCharge(1f);
            yield return Press(_hero, blast: true);
            // Also released at the gate line. A bolt that finds nothing must
            // not leave the runner sprinting on past the robots it was aimed
            // at while the wait runs its full course.
            yield return Until(() => _heroLanded || PastGate(1f), 2.2f);
        }
        else
        {
            // Straight through it. Getting it right should feel like a gear
            // change, and this is the half of the time it is one.
            _hero.Tempo = ChargeTempo;
            VfxUtil.SpawnBurst(_hero.transform.position + Vector3.up * 0.35f,
                new Color(0.5f, 0.9f, 1f), 14, 5f, 0.12f);
            yield return SteerAt(answer, BrawlMoveSet.MinSeparation + 0.2f, 5f);
            yield return Press(_hero, punch: Random.value < 0.4f, kick: true);
            yield return Until(() => _heroLanded, 1.5f);
        }

        if (answer == null)
        {
            Cruise();
            yield break;
        }

        // Blown apart rather than floored: a body left lying in the lane is
        // something the runner then has to be steered around, and this mode
        // has exactly one control.
        VfxUtil.Explosion(answer.transform.position + Vector3.up, MatchAnnouncer.TeamColor(1), 0.9f);
        BrawlAudio.Play(BrawlAudio.Id.KO, answer.transform.position + Vector3.up);
        BrawlAudio.PlayFlat(BrawlAudio.Id.Victory, 0.45f);
        Destroy(answer.gameObject);
        for (int i = 0; i < Options; i++)
            if (_gate[i] == answer)
                _gate[i] = null;

        Cruise();
    }

    /// <summary>
    /// Wrong: the hero does not get to swing. The robot that WAS the answer
    /// fires down the road, and the hero loses ground — properly, tens of
    /// metres of it, which is the only currency this mode has.
    /// </summary>
    IEnumerator ThrownBack(BrawlFighter answer)
    {
        if (answer == null)
            yield break;

        answer.Opponent = _hero;
        _heroHurt = false;

        // The hero keeps running INTO it. Waiting for the range to close is
        // what makes the shot connect at all, and running toward the robot
        // that is aiming at you is a better picture than stopping dead.
        yield return Until(() => _gateZ - _hero.transform.position.z <= StrikeRange, 6f);

        answer.GrantCharge(1f);
        yield return Press(answer, blast: true);
        // Same guard, and it matters more here: a missed shot with a plain
        // timeout would run the hero straight through the gate before the
        // throw-back that was supposed to stop it ever started.
        yield return Until(() => _heroHurt || PastGate(2f), 2.4f);

        _director.Shake(0.8f);
        yield return Slide();
    }

    /// <summary>
    /// The scripted throw-back. BrawlFighter's own knockback is tuned for a
    /// ring two robots share — a couple of metres — and this needs to cost
    /// real road, so the slide is driven from here and eased out over the
    /// fall. The runner is drawn back toward the middle lane on the way, so
    /// it gets up somewhere it can be steered from.
    /// </summary>
    IEnumerator Slide()
    {
        _hero.Tempo = KnockbackTempo;
        _cruising = false;
        _steer = Vector2.zero;

        float startX = _hero.transform.position.x;
        float travelled = 0f;
        float t = 0f;
        while (t < KnockbackTime)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / KnockbackTime);
            float eased = 1f - (1f - p) * (1f - p);
            float want = KnockbackMetres * eased;

            var position = _hero.transform.position;
            position.z -= want - travelled;
            position.x = Mathf.Lerp(startX, 0f, eased);
            _hero.transform.position = position;
            travelled = want;
            yield return null;
        }

        Cruise();
    }

    /// <summary>
    /// Hand the runner back to the road: no target, cruising speed, and the
    /// drift to centre back in charge of where it goes.
    /// </summary>
    void Cruise()
    {
        _hero.Opponent = null;
        _hero.Tempo = RunTempo;
        _cruising = true;
    }

    // ------------------------------------------------------ driving a fighter

    /// <summary>
    /// Run at a target until it is this close. Steering and running are the
    /// same act here — the intent vector points at the robot, so closing the
    /// gap never means giving up ground down the road.
    /// </summary>
    IEnumerator SteerAt(BrawlFighter target, float stopAt, float timeout)
    {
        _cruising = false;
        float spent = 0f;
        while (spent < timeout && target != null)
        {
            Vector3 gap = target.transform.position - _hero.transform.position;
            gap.y = 0f;
            if (gap.magnitude <= stopAt)
                break;
            gap.Normalize();
            _steer = new Vector2(gap.x, gap.z);
            spent += Time.deltaTime;
            yield return null;
        }
    }

    /// <summary>
    /// Hold a button long enough for the fighter's own Update to read it. The
    /// hold cannot be a single resume: coroutines run after every Update, so
    /// an intent set and cleared inside one would be written and erased
    /// without BrawlFighter ever seeing it.
    /// </summary>
    IEnumerator Press(BrawlFighter fighter, bool punch = false, bool kick = false,
        bool blast = false)
    {
        fighter.Driven.punch = punch;
        fighter.Driven.kick = kick;
        fighter.Driven.blast = blast;

        yield return Until(() => fighter == null
                                 || fighter.Phase == BrawlFighter.State.Attacking
                                 || fighter.Phase == BrawlFighter.State.AirAttack, 0.6f);
        if (fighter == null)
            yield break;
        fighter.Driven.punch = fighter.Driven.kick = fighter.Driven.blast = false;

        yield return Until(() => fighter == null
                                 || (fighter.Phase != BrawlFighter.State.Attacking
                                     && fighter.Phase != BrawlFighter.State.AirAttack), 2.0f);
    }

    /// <summary>Has the runner come within this many metres of the gate line?</summary>
    bool PastGate(float margin) => _gateZ - _hero.transform.position.z <= margin;

    static IEnumerator Until(System.Func<bool> done, float timeout)
    {
        float spent = 0f;
        while (spent < timeout && !done())
        {
            spent += Time.deltaTime;
            yield return null;
        }
    }

    // ------------------------------------------------------------- every frame

    void Update()
    {
        if (_hero == null)
            return;

        float z = _hero.transform.position.z;
        _road.FollowRunner(z);
        _furthest = Mathf.Max(_furthest, z);
        _hud.SetDistance(Mathf.RoundToInt(Mathf.Max(0f, z)), _best);

        if (_cruising)
        {
            float drift = Mathf.Clamp(-_hero.transform.position.x * CentreDrift, -0.8f, 0.8f);
            _steer = new Vector2(drift, 1f).normalized;
        }

        // The one continuous instruction in the mode: keep running. Ignored by
        // BrawlFighter in every state but Neutral, so a fall or a strike plays
        // out untouched and the run resumes on its own afterwards.
        _hero.Driven.move = _steer;
        SteerHeroVisually();

        _phaseTime += Time.deltaTime;

        if (_phase == Phase.Asking)
        {
            ReadPick();
            // Out of road. Not answering is an answer, and it is a wrong one.
            if (_gateLive && z >= _gateZ - AnswerDeadline)
                Answer(-1);
            return;
        }

        if (_phase == Phase.Resolving && _phaseTime > RoundWatchdog)
        {
            Debug.LogWarning("[ChineseRun] Gate stalled — planting the next one.");
            StopRound();
            Cruise();
            NextGate();
        }
    }

    /// <summary>
    /// Point the runner where it is running. BrawlFighter only turns a fighter
    /// that has an Opponent, and the hero deliberately has none while it
    /// cruises — so the lean into a lane change belongs here.
    /// </summary>
    void SteerHeroVisually()
    {
        if (_hero.Opponent != null || _steer.sqrMagnitude < 1e-4f)
            return;
        // Forward is floored well above zero: a purely sideways steer would
        // otherwise turn the runner broadside to the road it is sprinting down.
        var look = new Vector3(_steer.x, 0f, Mathf.Max(0.4f, _steer.y));
        _hero.transform.rotation = Quaternion.Slerp(_hero.transform.rotation,
            Quaternion.LookRotation(look.normalized, Vector3.up),
            1f - Mathf.Exp(-8f * Time.deltaTime));
    }

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
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        // Layer 2 is where BrawlFighter parks its body capsule, which makes it
        // the one layer holding the robots and nothing else.
        if (!Physics.Raycast(_camera.ScreenPointToRay(Input.mousePosition), out var hit,
                300f, 1 << 2, QueryTriggerInteraction.Ignore))
            return;
        var picked = hit.collider.GetComponentInParent<BrawlFighter>();
        for (int i = 0; i < Options; i++)
            if (_gate[i] == picked)
            {
                Answer(i);
                return;
            }
    }

    // -------------------------------------------------------------- lifecycle

    void StopRound()
    {
        StopAllCoroutines();
        ChineseVoice.Stop();
    }

    /// <summary>Bank the distance. Called on the way out — there is no other end.</summary>
    void BankBest()
    {
        int reached = Mathf.RoundToInt(Mathf.Max(0f, _furthest));
        if (reached <= _best)
            return;
        _best = reached;
        PlayerPrefs.SetInt(BestKey, _best);
        PlayerPrefs.Save();
    }

    public void Teardown()
    {
        StopRound();
        BankBest();
        _quiz.Flush();
        ChineseVoice.Release();

        // Handed back before anything else builds on it — a stage that keeps
        // the runner's fence would give the next Brawl a ring with no ends.
        BrawlStage.SetBounds(BrawlStage.DefaultBounds);

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
        var rig = new GameObject("ChineseRunCamera");
        rig.tag = "MainCamera";
        var cam = rig.AddComponent<Camera>();
        cam.fieldOfView = 58f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = BrawlStage.VoidColor;
        // The road runs a long way ahead; the default 1000 is plenty but the
        // near plane wants to be tight for a camera this close behind.
        cam.nearClipPlane = 0.1f;
        rig.AddComponent<AudioListener>();
        var data = rig.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        rig.AddComponent<ChineseRunCamera>();
        return rig;
    }
}
