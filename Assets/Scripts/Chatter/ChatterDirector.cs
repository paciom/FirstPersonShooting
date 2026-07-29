using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Decides which conversation runs, who speaks it, and — mostly — when nobody
/// should say anything.
///
/// The hard problem in ambient chatter is not supply, it is restraint. Nine
/// robots each reacting to every hit they take would produce a wall of text that
/// reads as noise, and noise is what makes chatter boring far faster than
/// repetition does. So this class is largely a set of brakes:
///
///  - ONE CONVERSATION PER TEAM. Each team is a radio channel; two robots on the
///    same channel do not talk over each other. Both channels running at once is
///    what makes a match sound busy without being unreadable.
///  - PRIORITY WITH HYSTERESIS. An ally going down interrupts idle banter, but a
///    slightly-more-urgent cue does not interrupt a merely-urgent one, or the
///    feed would be all first beats and no answers.
///  - PER-ROBOT AND PER-CUE COOLDOWNS. OnDamaged fires on every pellet; without
///    a floor here one shotgun blast is six lines from one robot.
///  - RECENCY. Conversations are suppressed for a long window after use, keyed on
///    id. Players only register the head of a line, so the same *opening* twice
///    in a minute reads as repetition even when the rest differs.
///
/// Cast is by register: a conversation the generator tagged "courtly/rookie" is
/// preferentially spoken by Knight and Scout, so the robots sound like themselves
/// rather than like a shared script.
/// </summary>
public class ChatterDirector : MonoBehaviour
{
    public static ChatterDirector Instance { get; private set; }

    // Cues, highest priority first. A cue absent here never fires.
    static readonly Dictionary<string, int> Priority = new Dictionary<string, int>
    {
        { "VICTORY", 100 }, { "DEFEAT", 100 },
        { "ALLY_DOWN", 90 }, { "KILL_CONFIRM", 85 }, { "SHIELD_CRITICAL", 80 },
        { "INCOMING", 75 }, { "REQUEST_HELP", 70 }, { "OBJECTIVE", 60 },
        { "TAKING_DAMAGE", 55 }, { "CONTACT", 50 }, { "TRANSFORM", 45 },
        { "WEAPON_OUT", 40 }, { "COVERING_FIRE", 38 }, { "REPOSITION", 35 },
        { "RALLY", 33 }, { "BANTER", 10 },
    };

    /// <summary>Shortest gap between two firings of the same cue, per team. The
    /// spammy reactive cues are throttled hardest.</summary>
    static readonly Dictionary<string, float> CueCooldown = new Dictionary<string, float>
    {
        { "TAKING_DAMAGE", 9f }, { "CONTACT", 7f }, { "INCOMING", 6f },
        { "SHIELD_CRITICAL", 10f }, { "KILL_CONFIRM", 4f }, { "ALLY_DOWN", 4f },
        { "TRANSFORM", 8f }, { "WEAPON_OUT", 12f }, { "REPOSITION", 10f },
        { "COVERING_FIRE", 10f }, { "RALLY", 14f }, { "OBJECTIVE", 6f },
        { "BANTER", 16f },
    };

    /// <summary>How much more urgent a cue must be to cut off one mid-flow.</summary>
    const int InterruptMargin = 15;

    const float PerRobotCooldown = 5.5f;
    const float ChannelGapAfterIdle = 3.5f;   // after banter
    const float ChannelGapAfterUrgent = 1.2f; // after a real event
    const int RecencyWindow = 220;

    /// <summary>Registers by robot, matching RobotRoster's nine. The register is
    /// the robot's personality in one word; see Tools/generate_chatter.py, which
    /// authored lines against these same nine.</summary>
    static readonly Dictionary<string, string> RegisterByRobot =
        new Dictionary<string, string>
    {
        { "ranger", "pro" }, { "hawk", "radio" }, { "bolt", "rapid" },
        { "panther", "terse" }, { "knight", "courtly" }, { "titan", "slow" },
        { "samurai", "minimal" }, { "scout", "rookie" }, { "racer", "brash" },
    };

    static readonly string[] AllRegisters =
    {
        "pro", "radio", "rapid", "terse", "courtly", "slow", "minimal", "rookie", "brash",
    };

    class Channel
    {
        public ConversationBank.Conversation convo;
        public ChatterContext context;
        public Transform[] cast = new Transform[3];   // A, B, C
        public int beatIndex;
        public float nextBeatAt;
        public int priority;
        public bool Active => convo != null;
    }

    readonly Channel[] _channels = { new Channel(), new Channel() };
    readonly Dictionary<string, float> _cueReadyAt = new Dictionary<string, float>();
    // Keyed on the Transform rather than an instance id: GetInstanceID is
    // deprecated in Unity 6, and reference identity is exactly what is wanted
    // here. Cleared between matches so destroyed robots are not held forever.
    readonly Dictionary<Transform, float> _robotReadyAt = new Dictionary<Transform, float>();
    readonly Queue<string> _recentOrder = new Queue<string>();
    readonly HashSet<string> _recent = new HashSet<string>();
    readonly List<EnergyShield> _roster = new List<EnergyShield>();
    readonly List<Transform> _scratch = new List<Transform>();
    float _nextRosterScan;

    // ------------------------------------------------------------- static API

    /// <summary>
    /// Offers an event to the director, which usually declines. Returns true if a
    /// conversation actually started, for callers that want to know.
    /// </summary>
    public static bool Request(string cue, Transform speaker, Transform enemy,
                               int teamId, float shieldNormalized = 1f)
    {
        return Instance != null
            && Instance.TryStart(cue, speaker, enemy, teamId, shieldNormalized);
    }

    /// <summary>The register a given robot speaks in, by name; stable per robot so
    /// a robot never changes voice between matches.</summary>
    public static string RegisterOf(Transform root)
    {
        if (root == null)
            return "pro";
        string name = ChatterContext.ShortName(root, "ranger").ToLowerInvariant();
        foreach (var pair in RegisterByRobot)
            if (name.Contains(pair.Key))
                return pair.Value;
        // Unknown robot (a reinforcement, a decoy): derive one from the name so
        // it is at least consistent for that robot rather than random per line.
        int hash = 0;
        foreach (char ch in name)
            hash = hash * 31 + ch;
        return AllRegisters[Mathf.Abs(hash) % AllRegisters.Length];
    }

    // ------------------------------------------------------------- lifecycle

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance == null)
            new GameObject("ChatterDirector").AddComponent<ChatterDirector>();
    }

    void Awake()
    {
        Instance = this;
        ConversationBank.Load();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        bool inMatch = GameModeController.Instance != null
            && (GameModeController.Instance.Mode == GameMode.PlayerVsAI
                || GameModeController.Instance.Mode == GameMode.AIvAI);
        if (!inMatch)
        {
            for (int i = 0; i < _channels.Length; i++)
                Reset(_channels[i], 0f);
            // Between matches the robots are gone; keeping their cooldowns would
            // pin destroyed Transforms and silence whoever reuses the slot.
            if (_robotReadyAt.Count > 0)
            {
                _robotReadyAt.Clear();
                _cueReadyAt.Clear();
                _roster.Clear();
                _nextRosterScan = 0f;
            }
            return;
        }

        for (int i = 0; i < _channels.Length; i++)
            Advance(_channels[i]);
    }

    // -------------------------------------------------------------- selection

    bool TryStart(string cue, Transform speaker, Transform enemy, int teamId,
                  float shieldNormalized)
    {
        // Same gate Update applies: chatter is an FPS-match feature. Without
        // it here, reactive cues from other modes' shield events (Commander
        // fights, most of all) start-and-discard conversations fast enough to
        // flush the recency brakes that keep the radio from repeating itself.
        if (GameModeController.Instance == null
            || (GameModeController.Instance.Mode != GameMode.PlayerVsAI
                && GameModeController.Instance.Mode != GameMode.AIvAI))
            return false;

        if (speaker == null || ConversationBank.IsEmpty)
            return false;
        if (!Priority.TryGetValue(cue, out int priority))
            return false;

        teamId = Mathf.Clamp(teamId, 0, 1);
        var channel = _channels[teamId];
        float now = Time.unscaledTime;

        // Brakes, cheapest first.
        if (channel.Active && priority < channel.priority + InterruptMargin)
            return false;
        if (!channel.Active && now < channel.nextBeatAt)
            return false;   // channel still cooling down from the last exchange
        if (_cueReadyAt.TryGetValue(CueKey(cue, teamId), out float cueReady) && now < cueReady)
            return false;
        if (_robotReadyAt.TryGetValue(speaker, out float robotReady) && now < robotReady)
            return false;

        var candidates = ConversationBank.ForCue(cue);
        if (candidates == null || candidates.Count == 0)
            return false;

        Teammates(speaker, teamId, _scratch);
        var convo = Pick(candidates, speaker, _scratch);
        if (convo == null)
            return false;

        Reset(channel, 0f);
        channel.convo = convo;
        channel.priority = priority;
        channel.beatIndex = 0;
        channel.nextBeatAt = now;   // first beat lands this frame
        channel.cast[0] = speaker;
        channel.cast[1] = CastFor(convo, "B", speaker, _scratch);
        channel.cast[2] = CastFor(convo, "C", speaker, _scratch);
        channel.context = new ChatterContext
        {
            speaker = speaker,
            enemy = enemy,
            teamId = teamId,
            shieldNormalized = shieldNormalized,
        };
        for (int i = 0; i < channel.cast.Length; i++)
            channel.context.cast[i] = channel.cast[i];

        Remember(convo.id);
        _cueReadyAt[CueKey(cue, teamId)] = now + Cooldown(cue);
        _robotReadyAt[speaker] = now + PerRobotCooldown;
        return true;
    }

    /// <summary>
    /// Picks an unused conversation this team can actually cast. Samples at random
    /// rather than scanning: the bank has hundreds per cue, so a handful of draws
    /// almost always finds a fresh one, and it costs the same every time — no
    /// frame spike when the recency set is nearly full.
    /// </summary>
    ConversationBank.Conversation Pick(List<ConversationBank.Conversation> candidates,
                                       Transform speaker, List<Transform> teammates)
    {
        int available = 1 + teammates.Count;
        ConversationBank.Conversation fallback = null;

        for (int attempt = 0; attempt < 12; attempt++)
        {
            var convo = candidates[Random.Range(0, candidates.Count)];
            if (convo.SpeakerCount > available)
                continue;
            if (fallback == null)
                fallback = convo;      // castable, but recently heard
            if (_recent.Contains(convo.id))
                continue;

            // Prefer one whose second voice matches a robot actually present.
            string wanted = convo.RegisterFor("B");
            if (wanted != null && teammates.Count > 0 && !HasRegister(teammates, wanted)
                && attempt < 8)
                continue;
            return convo;
        }
        return fallback;
    }

    static bool HasRegister(List<Transform> pool, string register)
    {
        for (int i = 0; i < pool.Count; i++)
            if (RegisterOf(pool[i]) == register)
                return true;
        return false;
    }

    /// <summary>Casts a speaker slot, preferring a register match and otherwise
    /// the nearest teammate — the robot a call would plausibly be answered by.</summary>
    Transform CastFor(ConversationBank.Conversation convo, string slot,
                      Transform speaker, List<Transform> teammates)
    {
        bool needed = false;
        for (int i = 0; i < convo.beats.Length; i++)
            if (convo.beats[i].s == slot) { needed = true; break; }
        if (!needed || teammates.Count == 0)
            return null;

        string wanted = convo.RegisterFor(slot);
        Transform best = null;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < teammates.Count; i++)
        {
            var candidate = teammates[i];
            if (candidate == speaker)
                continue;
            bool matches = wanted == null || RegisterOf(candidate) == wanted;
            float distance = Vector3.SqrMagnitude(candidate.position - speaker.position);
            if (!matches)
                distance += 10000f;   // usable, but only if nothing matches
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }
        return best;
    }

    // ---------------------------------------------------------------- playback

    void Advance(Channel channel)
    {
        if (!channel.Active || Time.unscaledTime < channel.nextBeatAt)
            return;

        if (channel.beatIndex >= channel.convo.beats.Length)
        {
            bool wasIdle = channel.priority <= Priority["BANTER"];
            Reset(channel, wasIdle ? ChannelGapAfterIdle : ChannelGapAfterUrgent);
            return;
        }

        var beat = channel.convo.beats[channel.beatIndex];
        int slot = SlotIndex(beat.s);
        var who = channel.cast[slot];
        channel.context.speakingSlot = slot;

        // A speaker can be de-rezzed mid-exchange. Skipping the beat rather than
        // aborting keeps the reply that made it a conversation, and a call that
        // goes unanswered because its answerer just exploded is honest.
        if (who != null)
        {
            RadioLog.Push(ChatterContext.ShortName(who, "unit"),
                          channel.context.Resolve(beat.t),
                          channel.context.teamId);
        }

        channel.beatIndex++;
        channel.nextBeatAt = Time.unscaledTime + BeatGap(beat.r);
    }

    static int SlotIndex(string slot)
    {
        switch (slot)
        {
            case "B": return 1;
            case "C": return 2;
            default:  return 0;
        }
    }

    /// <summary>Delay before the next line. Register drives it, because how long
    /// a robot takes to answer is part of how it talks.</summary>
    static float BeatGap(string register)
    {
        float baseGap;
        switch (register)
        {
            case "slow":    baseGap = 2.0f; break;
            case "courtly": baseGap = 1.7f; break;
            case "rapid":   baseGap = 0.95f; break;
            case "terse":
            case "minimal": baseGap = 1.1f; break;
            default:        baseGap = 1.35f; break;
        }
        return baseGap + Random.Range(-0.15f, 0.35f);
    }

    void Reset(Channel channel, float cooldown)
    {
        channel.convo = null;
        channel.context = null;
        channel.cast[0] = channel.cast[1] = channel.cast[2] = null;
        channel.beatIndex = 0;
        channel.priority = 0;
        channel.nextBeatAt = Time.unscaledTime + cooldown;
    }

    // ----------------------------------------------------------------- helpers

    static string CueKey(string cue, int teamId) => teamId == 1 ? cue + "#1" : cue;

    static float Cooldown(string cue) =>
        CueCooldown.TryGetValue(cue, out float seconds) ? seconds : 6f;

    void Remember(string id)
    {
        if (string.IsNullOrEmpty(id) || !_recent.Add(id))
            return;
        _recentOrder.Enqueue(id);
        while (_recentOrder.Count > RecencyWindow)
            _recent.Remove(_recentOrder.Dequeue());
    }

    /// <summary>Living teammates of the speaker. The roster is rescanned on a
    /// timer rather than per request; reinforcements and decoys arrive mid-match,
    /// but not so often that every call should pay for a FindObjectsByType.</summary>
    void Teammates(Transform speaker, int teamId, List<Transform> into)
    {
        into.Clear();
        if (Time.unscaledTime >= _nextRosterScan)
        {
            _nextRosterScan = Time.unscaledTime + 2f;
            _roster.Clear();
            _roster.AddRange(FindObjectsByType<EnergyShield>(FindObjectsSortMode.None));
        }
        for (int i = 0; i < _roster.Count; i++)
        {
            var shield = _roster[i];
            if (shield == null || shield.IsDown || shield.teamId != teamId)
                continue;
            if (shield.transform == speaker)
                continue;
            into.Add(shield.transform);
        }
    }
}
