using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The STORY mode: an episode file performed by the robot cast on the Brawl
/// stage — walked marks, spoken lines, fight-library verbs, and a camera that
/// cuts like a filmed conversation. The screenplay is data
/// (Resources/Episodes/*.json); this class is only the stagehand.
///
/// World lifecycle is the Brawl one (hide the cast, swap the Environment,
/// hand it back via ArenaRuntime.Load), fighters spawned with their combat
/// brain off — the same arrangement BrawlShow proved. An interpreter run by
/// coroutine, deliberately not a Timeline: the beats' targets only exist at
/// runtime, which is exactly where Timeline authoring stops being worth it.
/// </summary>
public class StoryDirector : MonoBehaviour
{
    /// <summary>The running director, for the render harness.</summary>
    public static StoryDirector Active { get; private set; }

    /// <summary>Flips true after THE END — the editor-side renderer watches
    /// this to stop the recorder and close the editor.</summary>
    public static bool Completed;

    class Actor
    {
        public string name;
        public int team;
        public BrawlFighter fighter;
        public Animator animator;
        public Transform head;
        public bool walking;
        public Coroutine face;
    }

    static readonly Color HoloCyan = new Color(0.2f, 0.9f, 1f);

    GameObject _stageRoot;
    StoryCamera _camera;
    ArenaBlockManager _blockManager;
    readonly List<GameObject> _hiddenCharacters = new List<GameObject>();
    readonly List<Actor> _cast = new List<Actor>();

    StoryEpisode _episode;
    string _episodeId;
    bool _autoExit;

    Text _subtitleName;
    Text _subtitleText;
    Text _card;
    Image _fade;

    public static StoryDirector Begin(GameModeController owner, RobotRoster roster,
        string episodeId, bool autoExit)
    {
        Completed = false;
        var go = new GameObject("StoryDirector");
        go.transform.SetParent(owner.transform, false);
        var director = go.AddComponent<StoryDirector>();
        director._episodeId = episodeId;
        director._autoExit = autoExit;
        director.Setup(roster);
        return director;
    }

    void Setup(RobotRoster roster)
    {
        Active = this;
        _episode = StoryEpisode.Load(_episodeId);

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
        // No hazards, no toys: nothing on stage the script didn't write.
        _stageRoot = BrawlStage.Build(environment, default, roster, hazards: false);

        if (_episode != null)
            foreach (var member in _episode.cast)
                SpawnActor(roster, member);

        _camera = StoryCamera.Build(transform);
        BuildUi();

        // The opening frame exists before the fade reveals it.
        ApplyShot("wide");
        StartCoroutine(Run());
    }

    void SpawnActor(RobotRoster roster, StoryActor member)
    {
        int index = 0;
        for (int i = 0; i < roster.robots.Length; i++)
        {
            var display = roster.robots[i].displayName;
            if (!string.IsNullOrEmpty(display)
                && display.Replace(" ", "").Equals(member.name,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        var fighter = BrawlFighter.Spawn(_stageRoot.transform, roster, index, member.team);
        // The story stage manager moves everyone itself: combat brain off,
        // exactly the Show's arrangement.
        fighter.enabled = false;

        if (!StoryMarks.TryGet(member.at, out var start))
            start = Vector3.zero;
        fighter.transform.position = start;
        FaceInstant(fighter.transform, Vector3.zero);

        var actor = new Actor
        {
            name = member.name,
            team = member.team,
            fighter = fighter,
            animator = fighter.GetComponentInChildren<Animator>(true),
            head = FindDeep(fighter.transform, "Head"),
        };
        _cast.Add(actor);
    }

    // ------------------------------------------------------------------ run

    IEnumerator Run()
    {
        yield return Fade(1f, 0f, 1.1f);

        if (_episode == null)
        {
            yield return Card("NO  EPISODE  LOADED", 3f);
        }
        else
        {
            foreach (var scene in _episode.scenes)
                foreach (var beat in scene.beats)
                    yield return RunBeat(beat);
        }

        HideSubtitle();
        yield return new WaitForSeconds(0.6f);
        StartCoroutine(Fade(0f, 1f, 1.4f));
        yield return Card("THE   END", 3.2f);
        Completed = true;
        // Interactive viewers leave with Escape (the controller owns it);
        // a render run is closed down by the editor-side harness instead.
    }

    IEnumerator RunBeat(StoryBeat beat)
    {
        bool performs = !string.IsNullOrEmpty(beat.text) || !string.IsNullOrEmpty(beat.verb);

        // A shot that frames settled actors (anything but a shot riding its
        // own move) waits for walks still in flight — framing someone
        // mid-stride where they'll no longer be is the classic staging bug.
        bool framesSettled = performs
            || (!string.IsNullOrEmpty(beat.shot) && string.IsNullOrEmpty(beat.move));
        if (framesSettled)
            while (AnyWalking())
                yield return null;

        if (!string.IsNullOrEmpty(beat.shot))
            ApplyShot(beat.shot);

        if (!string.IsNullOrEmpty(beat.caption))
        {
            yield return Card(beat.caption, beat.wait > 0f ? beat.wait : 2.4f);
            yield break;
        }

        var actor = string.IsNullOrEmpty(beat.who) ? null : Find(beat.who);

        if (!string.IsNullOrEmpty(beat.move) && actor != null
            && StoryMarks.TryGet(beat.move, out var target))
            StartCoroutine(Walk(actor, target));

        // Anyone about to speak or perform waits for every walk in flight —
        // dialogue happens settled; move-only beats overlap freely.
        if (performs)
            while (AnyWalking())
                yield return null;

        if (!string.IsNullOrEmpty(beat.face) && actor != null)
            FaceToward(actor, ResolvePoint(beat.face, actor));
        else if (!string.IsNullOrEmpty(beat.text) && actor != null)
            AutoFace(actor);

        float hold = 0f;
        if (!string.IsNullOrEmpty(beat.verb) && actor != null)
            hold = FireVerb(actor, beat.verb);

        if (!string.IsNullOrEmpty(beat.text) && actor != null)
        {
            var clip = StoryVoice.Say(_episodeId, beat.voice);
            float line = clip != null
                ? clip.length + 0.35f
                : Mathf.Clamp(1.1f + 0.055f * beat.text.Length, 1.6f, 5.5f);
            ShowSubtitle(actor, beat.text);
            hold = Mathf.Max(hold, line);
        }

        if (hold > 0f)
        {
            yield return new WaitForSeconds(hold);
            HideSubtitle();
        }
        if (beat.wait > 0f)
            yield return new WaitForSeconds(beat.wait);
        yield return new WaitForSeconds(0.12f);
    }

    // ---------------------------------------------------------------- verbs

    /// <summary>Trigger name per verb — the core contract plus every strike
    /// variant, keyed the way episodes (and the Meshy pipeline) name them.</summary>
    static Dictionary<string, string> _triggers;

    static Dictionary<string, string> Triggers()
    {
        if (_triggers != null)
            return _triggers;
        _triggers = new Dictionary<string, string>
        {
            { "hit", BrawlAnim.Hit }, { "knockdown", BrawlAnim.Knockdown },
            { "getup", BrawlAnim.GetUp }, { "victory", BrawlAnim.Victory },
            { "blast", BrawlAnim.Blast }, { "flykick", BrawlAnim.FlyKick },
        };
        foreach (var variant in BrawlMoveSet.PunchVariants)
            _triggers[variant.meshyKey] = variant.trigger;
        foreach (var variant in BrawlMoveSet.KickVariants)
            _triggers[variant.meshyKey] = variant.trigger;
        return _triggers;
    }

    float FireVerb(Actor actor, string verb)
    {
        verb = verb.ToLowerInvariant();
        if (verb == "block")
        {
            StartCoroutine(HoldBlock(actor, 1.5f));
            return 1.8f;
        }
        if (verb == "stance")
            return 0.6f;

        if (!Triggers().TryGetValue(verb, out var trigger))
        {
            Debug.LogWarning($"[Story] Unknown verb '{verb}' — holding a beat instead.");
            return 1.0f;
        }
        actor.animator.SetTrigger(trigger);

        if (verb == "blast")
            BrawlBolt.Fire(actor.fighter, null, MatchAnnouncer.TeamColor(actor.team));
        if (verb == "knockdown")
            StartCoroutine(KnockbackSlide(actor));

        if (verb == "hit")
            return BrawlMoveSet.HitClipTime + 0.5f;
        if (verb == "knockdown")
            return BrawlMoveSet.KnockdownClipTime + 0.9f;
        if (verb == "getup")
            return BrawlMoveSet.GetUpTime + 0.7f;
        if (verb == "victory")
            return 2.6f;
        if (verb == "blast")
            return BrawlMoveSet.Table[BrawlMoveSet.Move.Blast].Duration + 0.9f;
        if (verb == "flykick")
            return BrawlMoveSet.Table[BrawlMoveSet.Move.FlyKick].startup + 0.8f;
        if (trigger.StartsWith("Punch"))
            return BrawlMoveSet.Table[BrawlMoveSet.Move.Punch].Duration + 0.5f;
        return BrawlMoveSet.Table[BrawlMoveSet.Move.Kick].Duration + 0.5f;
    }

    IEnumerator HoldBlock(Actor actor, float seconds)
    {
        actor.animator.SetBool(BrawlAnim.BlockHash, true);
        yield return new WaitForSeconds(seconds);
        if (actor.animator != null)
            actor.animator.SetBool(BrawlAnim.BlockHash, false);
    }

    /// <summary>The clip plays in place; the fall's travel is scripted, the
    /// Show's trick — eased backward, staying where it lands for the rise.</summary>
    IEnumerator KnockbackSlide(Actor actor)
    {
        var t = actor.fighter.transform;
        Vector3 from = t.position;
        Vector3 direction = -t.forward;
        direction.y = 0f;
        direction.Normalize();
        float elapsed = 0f;
        while (elapsed < BrawlMoveSet.KnockdownClipTime)
        {
            elapsed += Time.deltaTime;
            float p = Mathf.Clamp01(elapsed / BrawlMoveSet.KnockdownClipTime);
            float ease = 1f - (1f - p) * (1f - p);
            t.position = from + direction * (1.5f * ease);
            yield return null;
        }
    }

    // -------------------------------------------------------------- staging

    IEnumerator Walk(Actor actor, Vector3 target)
    {
        actor.walking = true;
        var t = actor.fighter.transform;
        target.y = t.position.y;
        actor.animator.SetFloat(BrawlAnim.SpeedHash, BrawlMoveSet.WalkSpeed);
        while (true)
        {
            Vector3 to = target - t.position;
            to.y = 0f;
            float distance = to.magnitude;
            if (distance < 0.08f)
                break;
            Vector3 dir = to / distance;
            t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(dir),
                1f - Mathf.Exp(-9f * Time.deltaTime));
            float step = Mathf.Min(BrawlMoveSet.WalkSpeed * Time.deltaTime, distance);
            t.position += dir * step;
            yield return null;
        }
        actor.animator.SetFloat(BrawlAnim.SpeedHash, 0f);
        actor.walking = false;
    }

    bool AnyWalking()
    {
        foreach (var actor in _cast)
            if (actor.walking)
                return true;
        return false;
    }

    /// <summary>The conversation's default blocking: the speaker turns to the
    /// nearest other actor, everyone else turns to the speaker.</summary>
    void AutoFace(Actor speaker)
    {
        Actor nearest = null;
        float best = float.MaxValue;
        foreach (var other in _cast)
        {
            if (other == speaker)
                continue;
            float d = (other.fighter.transform.position
                - speaker.fighter.transform.position).sqrMagnitude;
            if (d < best)
            {
                best = d;
                nearest = other;
            }
        }
        if (nearest == null)
            return;
        FaceToward(speaker, nearest.fighter.transform.position);
        foreach (var other in _cast)
            if (other != speaker)
                FaceToward(other, speaker.fighter.transform.position);
    }

    void FaceToward(Actor actor, Vector3 point)
    {
        if (actor.face != null)
            StopCoroutine(actor.face);
        actor.face = StartCoroutine(FaceRoutine(actor, point));
    }

    IEnumerator FaceRoutine(Actor actor, Vector3 point)
    {
        var t = actor.fighter.transform;
        Vector3 dir = point - t.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
            yield break;
        var goal = Quaternion.LookRotation(dir.normalized);
        float elapsed = 0f;
        while (elapsed < 0.4f && Quaternion.Angle(t.rotation, goal) > 1f)
        {
            elapsed += Time.deltaTime;
            t.rotation = Quaternion.Slerp(t.rotation, goal,
                1f - Mathf.Exp(-10f * Time.deltaTime));
            yield return null;
        }
        actor.face = null;
    }

    static void FaceInstant(Transform t, Vector3 point)
    {
        Vector3 dir = point - t.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
            t.rotation = Quaternion.LookRotation(dir.normalized);
    }

    Vector3 ResolvePoint(string name, Actor self)
    {
        var other = Find(name);
        if (other != null && other != self)
            return other.fighter.transform.position;
        if (StoryMarks.TryGet(name, out var mark))
            return mark;
        return Vector3.zero;
    }

    Actor Find(string name)
    {
        foreach (var actor in _cast)
            if (actor.name.Equals(name, System.StringComparison.OrdinalIgnoreCase))
                return actor;
        if (!string.IsNullOrEmpty(name))
            Debug.LogWarning($"[Story] No actor named '{name}' in the cast.");
        return null;
    }

    // --------------------------------------------------------------- camera

    void ApplyShot(string spec)
    {
        var parts = spec.Split(':');
        string kind = parts[0].ToLowerInvariant();
        string arg = parts.Length > 1 ? parts[1] : "";

        switch (kind)
        {
            case "wide":
                _camera.SetShot(new Vector3(0f, 3.6f, -11.5f), null,
                    Centroid() + Vector3.up * 1.1f, 48f);
                return;
            case "front":
                _camera.SetShot(new Vector3(0f, 1.3f, -8.2f), null,
                    Centroid() + Vector3.up * 1.25f, 44f);
                return;
            case "twoshot":
                TwoShot();
                return;
        }

        var actor = Find(arg.Split('>')[0]);
        if (actor == null)
        {
            _camera.SetShot(new Vector3(0f, 3.6f, -11.5f), null,
                Centroid() + Vector3.up * 1.1f, 48f);
            return;
        }
        var t = actor.fighter.transform;
        var head = actor.head;
        Vector3 headPos = head != null ? head.position : t.position + Vector3.up * 1.55f;

        switch (kind)
        {
            case "closeup":
                _camera.SetShot(headPos + t.forward * 2.0f + t.right * 0.35f,
                    head, headPos, 33f);
                return;
            case "medium":
                _camera.SetShot(t.position + t.forward * 3.4f + t.right * 0.5f
                    + Vector3.up * 1.55f, head, headPos, 41f);
                return;
            case "ots":
            {
                var subject = Find(arg.Contains(">") ? arg.Split('>')[1] : "");
                if (subject == null)
                    goto case "medium";
                var st = subject.fighter.transform;
                Vector3 subjectHead = subject.head != null
                    ? subject.head.position : st.position + Vector3.up * 1.55f;
                Vector3 back = (t.position - st.position).normalized;
                Vector3 side = Vector3.Cross(Vector3.up, back).normalized;
                _camera.SetShot(t.position + back * 1.35f + side * 0.8f
                    + Vector3.up * 1.85f, subject.head, subjectHead, 37f);
                return;
            }
            case "track":
            {
                Vector3 offset = -t.forward * 3.6f + t.right * 1.2f + Vector3.up * 2.0f;
                _camera.SetShot(t.position + offset, head, headPos, 45f,
                    blendSeconds: 0.9f, follow: t, followOffset: offset);
                return;
            }
            default:
                Debug.LogWarning($"[Story] Unknown shot '{spec}' — wide instead.");
                _camera.SetShot(new Vector3(0f, 3.6f, -11.5f), null,
                    Centroid() + Vector3.up * 1.1f, 48f);
                return;
        }
    }

    void TwoShot()
    {
        if (_cast.Count < 2)
        {
            _camera.SetShot(new Vector3(0f, 3.6f, -11.5f), null,
                Centroid() + Vector3.up * 1.1f, 48f);
            return;
        }
        Vector3 a = _cast[0].fighter.transform.position;
        Vector3 b = _cast[1].fighter.transform.position;
        Vector3 mid = (a + b) * 0.5f;
        Vector3 axis = b - a;
        Vector3 side = Vector3.Cross(Vector3.up, axis.normalized).normalized;
        if (side.z > 0f)
            side = -side;   // the house sits on -z; never film from the backdrop
        float distance = Mathf.Clamp(axis.magnitude * 1.5f + 2.4f, 3.6f, 7.5f);
        _camera.SetShot(mid + side * distance + Vector3.up * 1.5f, null,
            mid + Vector3.up * 1.25f, 46f);
    }

    Vector3 Centroid()
    {
        if (_cast.Count == 0)
            return Vector3.zero;
        Vector3 sum = Vector3.zero;
        foreach (var actor in _cast)
            sum += actor.fighter.transform.position;
        return sum / _cast.Count;
    }

    // ------------------------------------------------------------------- ui

    void BuildUi()
    {
        var canvasGo = new GameObject("StoryUi");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 15;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // The frame: letterbox bars, the cheapest honest signal that this is
        // a film and not a match.
        Bar(canvasGo.transform, top: true);
        Bar(canvasGo.transform, top: false);

        _subtitleName = MakeText(canvasGo.transform, "Speaker", 27, HoloCyan);
        var nameRect = _subtitleName.rectTransform;
        nameRect.anchorMin = nameRect.anchorMax = new Vector2(0.5f, 0f);
        nameRect.anchoredPosition = new Vector2(0f, 178f);
        nameRect.sizeDelta = new Vector2(1500f, 34f);

        _subtitleText = MakeText(canvasGo.transform, "Line", 36, Color.white);
        var lineRect = _subtitleText.rectTransform;
        lineRect.anchorMin = lineRect.anchorMax = new Vector2(0.5f, 0f);
        lineRect.anchoredPosition = new Vector2(0f, 118f);
        lineRect.sizeDelta = new Vector2(1500f, 90f);
        _subtitleText.horizontalOverflow = HorizontalWrapMode.Wrap;

        var fadeGo = new GameObject("Fade");
        fadeGo.transform.SetParent(canvasGo.transform, false);
        _fade = fadeGo.AddComponent<Image>();
        _fade.color = Color.black;
        var fadeRect = _fade.rectTransform;
        fadeRect.anchorMin = Vector2.zero;
        fadeRect.anchorMax = Vector2.one;
        fadeRect.sizeDelta = Vector2.zero;
        _fade.raycastTarget = false;

        // The card outranks the fade in the hierarchy so THE END reads
        // through the final fade-out.
        _card = MakeText(canvasGo.transform, "Card", 76, HoloCyan);
        var cardRect = _card.rectTransform;
        cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.anchoredPosition = new Vector2(0f, 40f);
        cardRect.sizeDelta = new Vector2(1700f, 120f);
        SetAlpha(_card, 0f);

        HideSubtitle();
    }

    static void Bar(Transform parent, bool top)
    {
        var go = new GameObject(top ? "LetterboxTop" : "LetterboxBottom");
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = false;
        var rect = image.rectTransform;
        rect.anchorMin = top ? new Vector2(0f, 1f) : new Vector2(0f, 0f);
        rect.anchorMax = top ? new Vector2(1f, 1f) : new Vector2(1f, 0f);
        rect.pivot = top ? new Vector2(0.5f, 1f) : new Vector2(0.5f, 0f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(0f, 96f);
    }

    void ShowSubtitle(Actor actor, string text)
    {
        _subtitleName.text = actor.name.ToUpperInvariant();
        _subtitleName.color = MatchAnnouncer.TeamColor(actor.team);
        _subtitleText.text = text;
        _subtitleName.enabled = true;
        _subtitleText.enabled = true;
    }

    void HideSubtitle()
    {
        if (_subtitleName != null)
            _subtitleName.enabled = false;
        if (_subtitleText != null)
            _subtitleText.enabled = false;
    }

    IEnumerator Card(string text, float seconds)
    {
        _card.text = text;
        yield return FadeText(_card, 0f, 1f, 0.3f);
        yield return new WaitForSeconds(seconds);
        yield return FadeText(_card, 1f, 0f, 0.35f);
    }

    IEnumerator Fade(float from, float to, float seconds)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += Time.deltaTime;
            SetAlpha(_fade, Mathf.Lerp(from, to, elapsed / seconds));
            yield return null;
        }
        SetAlpha(_fade, to);
    }

    IEnumerator FadeText(Text text, float from, float to, float seconds)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += Time.deltaTime;
            SetAlpha(text, Mathf.Lerp(from, to, elapsed / seconds));
            yield return null;
        }
        SetAlpha(text, to);
    }

    static void SetAlpha(Graphic graphic, float alpha)
    {
        var color = graphic.color;
        color.a = alpha;
        graphic.color = color;
    }

    static Text MakeText(Transform parent, string name, int size, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        return text;
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name)
            return root;
        foreach (Transform child in root)
        {
            var found = FindDeep(child, name);
            if (found != null)
                return found;
        }
        return null;
    }

    // ------------------------------------------------------------- teardown

    public void Teardown()
    {
        StopAllCoroutines();
        StoryVoice.Stop();
        StoryVoice.Release();

        if (_camera != null)
            Destroy(_camera.gameObject);

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
        Active = null;
        Destroy(gameObject);
    }

    void Hide(GameObject character)
    {
        if (character == null || !character.activeSelf)
            return;
        character.SetActive(false);
        _hiddenCharacters.Add(character);
    }
}
