using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// The MARTIAL ARTS SHOW: one picked robot alone on the Brawl stage,
/// performing every move in its repertoire on a loop, each one captioned —
/// the name in lights, and under it whether the motion is Meshy capture or
/// a forged template. Doubles as the judging bench for the animation
/// pipeline: this screen is where a new Meshy clip earns its place.
///
/// ← / → skip between moves, Escape leaves. World lifecycle is the Brawl
/// one (hide the cast, swap the Environment, hand it back via
/// ArenaRuntime.Load) with a single fighter whose combat brain is switched
/// off — the show drives the Animator directly.
/// </summary>
public class BrawlShow : MonoBehaviour
{
    struct Act
    {
        public string caption;
        public string sourceKey;   // Fight/<robot>-<key>.glb — for the tag
        public float duration;
        /// <summary>True when this act starts where the previous ended (the
        /// rise from the knockdown's floor) — suppresses the between-act
        /// rebind that would snap the robot upright first.</summary>
        public bool continuesPrevious;
        public System.Action<BrawlShow> begin;
        public System.Action<BrawlShow> end;
    }

    static readonly Color HoloCyan = new Color(0.2f, 0.9f, 1f);

    GameObject _stageRoot;
    GameObject _cameraRig;
    ArenaBlockManager _blockManager;
    List<GameObject> _hiddenCharacters = new List<GameObject>();

    BrawlFighter _fighter;
    Animator _animator;
    Text _caption;
    Text _source;
    Act[] _acts;
    int _actIndex = -1;
    float _actTime;

    [SerializeField] int _robot;

    public static BrawlShow Begin(GameModeController owner, RobotRoster roster, int robot)
    {
        var go = new GameObject("BrawlShow");
        go.transform.SetParent(owner.transform, false);
        var show = go.AddComponent<BrawlShow>();
        show._robot = robot;
        show.Setup(roster);
        return show;
    }

    void Setup(RobotRoster roster)
    {
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

        _stageRoot = BrawlStage.Build(environment);

        _fighter = BrawlFighter.Spawn(_stageRoot.transform, roster, _robot, 0);
        // Centre stage, angled toward the house, combat loop off — the show
        // speaks to the Animator directly.
        _fighter.transform.localPosition = Vector3.zero;
        _fighter.transform.rotation = Quaternion.Euler(0f, 205f, 0f);
        _fighter.enabled = false;
        _animator = _fighter.GetComponentInChildren<Animator>(true);

        _cameraRig = BuildCameraRig();
        _cameraRig.GetComponent<BrawlCamera>()
            .SetTargets(_fighter.transform, _fighter.transform);

        BuildCaptions();
        _acts = BuildActs();
        NextAct(+1);
    }

    Act[] BuildActs()
    {
        return new[]
        {
            new Act { caption = "FIGHTING  STANCE", sourceKey = "stance", duration = 3.0f },
            new Act
            {
                caption = "FOOTWORK", sourceKey = "walking", duration = 3.0f,
                begin = show => show._animator.SetFloat(BrawlAnim.SpeedHash, BrawlMoveSet.WalkSpeed),
                end = show => show._animator.SetFloat(BrawlAnim.SpeedHash, 0f),
            },
            Strike("KUNG  FU  PUNCH", "punch", BrawlAnim.Punch, BrawlMoveSet.Move.Punch),
            Strike("ROUNDHOUSE  KICK", "kick", BrawlAnim.Kick, BrawlMoveSet.Move.Kick),
            Strike("FLYING  KICK", "flykick", BrawlAnim.FlyKick, BrawlMoveSet.Move.FlyKick),
            new Act
            {
                caption = "GUARD", sourceKey = "block", duration = 2.2f,
                begin = show => show._animator.SetBool(BrawlAnim.BlockHash, true),
                end = show => show._animator.SetBool(BrawlAnim.BlockHash, false),
            },
            new Act
            {
                caption = "HIT  REACTION", sourceKey = "hit",
                duration = BrawlMoveSet.HitClipTime + 1.2f,
                begin = show => show._animator.SetTrigger(BrawlAnim.Hit),
            },
            new Act
            {
                caption = "KNOCKDOWN", sourceKey = "blownback",
                duration = BrawlMoveSet.KnockdownClipTime + 1.0f,
                begin = show => show._animator.SetTrigger(BrawlAnim.Knockdown),
            },
            new Act
            {
                caption = "RISING", sourceKey = "getup",
                duration = BrawlMoveSet.GetUpTime + 1.2f,
                continuesPrevious = true,
                begin = show => show._animator.SetTrigger(BrawlAnim.GetUp),
            },
            new Act
            {
                caption = "PHOTON  BLAST", sourceKey = "blast",
                duration = BrawlMoveSet.Table[BrawlMoveSet.Move.Blast].Duration + 1.4f,
                begin = show =>
                {
                    show._animator.SetTrigger(BrawlAnim.Blast);
                    BrawlBolt.Fire(show._fighter, null, MatchAnnouncer.TeamColor(0));
                },
            },
            new Act
            {
                caption = "VICTORY", sourceKey = "victory", duration = 2.8f,
                begin = show => show._animator.SetTrigger(BrawlAnim.Victory),
            },
        };
    }

    static Act Strike(string caption, string key, string trigger, BrawlMoveSet.Move move)
    {
        return new Act
        {
            caption = caption,
            sourceKey = key,
            duration = BrawlMoveSet.Table[move].Duration + 1.4f,
            begin = show => show._animator.SetTrigger(trigger),
        };
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.RightArrow))
            NextAct(+1);
        else if (Input.GetKeyDown(KeyCode.LeftArrow))
            NextAct(-1);
        else
        {
            _actTime += Time.deltaTime;
            if (_actTime >= _acts[_actIndex].duration)
                NextAct(+1);
        }
    }

    void NextAct(int step)
    {
        if (_animator == null)
            return;
        if (_actIndex >= 0)
            _acts[_actIndex].end?.Invoke(this);

        int previous = _actIndex;
        _actIndex = ((_actIndex + step) % _acts.Length + _acts.Length) % _acts.Length;
        _actTime = 0f;

        var act = _acts[_actIndex];

        // A skipped act may leave a state mid-pose; rebind puts the rig
        // back in the stance so every move starts clean — EXCEPT when the
        // next act deliberately continues the last one's pose (the rise
        // begins on the knockdown's floor, not standing).
        bool continues = act.continuesPrevious && step > 0 && previous == _actIndex - 1;
        if (!continues)
        {
            _animator.Rebind();
            _animator.Update(0f);
        }
        _caption.text = act.caption;
        _source.text = HasMeshyClip(act.sourceKey)
            ? "MESHY  MOTION  CAPTURE"
            : "FORGED  TEMPLATE";
        _caption.transform.localScale = Vector3.one * 1.35f;
        act.begin?.Invoke(this);
    }

    /// <summary>
    /// Whether this act's motion came from a Meshy clip: adopted clips are
    /// cloned as Brawl_&lt;Robot&gt;_&lt;key&gt;_meshy, and the walk keeps
    /// its own name on the meshy track — both detectable off the
    /// controller's clip list, no editor-side bookkeeping needed.
    /// </summary>
    bool HasMeshyClip(string key)
    {
        var controller = _animator.runtimeAnimatorController;
        if (controller == null)
            return false;
        foreach (var clip in controller.animationClips)
        {
            if (clip == null)
                continue;
            if (clip.name.EndsWith($"_{key}_meshy"))
                return true;
            if (key == "walking" && clip.name.EndsWith("_Walk")
                && clip.name.StartsWith("Brawl_"))
                return true;   // Brawl_*_Walk only exists on the meshy track
            if (key == "stance" && clip.name.EndsWith("_stance_meshy"))
                return true;
        }
        return false;
    }

    void BuildCaptions()
    {
        var canvasGo = new GameObject("ShowCaptions");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 15;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        _caption = MakeText(canvasGo.transform, "Caption", 72, HoloCyan);
        var rect = _caption.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 170f);
        rect.sizeDelta = new Vector2(1500f, 90f);

        _source = MakeText(canvasGo.transform, "Source", 24, new Color(1f, 1f, 1f, 0.55f));
        var sourceRect = _source.rectTransform;
        sourceRect.anchorMin = sourceRect.anchorMax = new Vector2(0.5f, 0f);
        sourceRect.anchoredPosition = new Vector2(0f, 118f);
        sourceRect.sizeDelta = new Vector2(800f, 32f);
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

    void LateUpdate()
    {
        if (_caption != null)
            _caption.transform.localScale = Vector3.Lerp(_caption.transform.localScale,
                Vector3.one, 1f - Mathf.Exp(-9f * Time.deltaTime));
    }

    public void Teardown()
    {
        if (_cameraRig != null)
            Destroy(_cameraRig);

        if (_stageRoot != null)
        {
            DestroyImmediate(_stageRoot);
            _stageRoot = null;
        }

        if (_hiddenCharacters.Count == 0)
        {
            var player = FindFirstObjectByType<PlayerBrain>(FindObjectsInactive.Include);
            if (player != null && !player.gameObject.activeSelf)
                _hiddenCharacters.Add(player.gameObject);
            foreach (var bot in FindObjectsByType<AIBrain>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!bot.gameObject.activeSelf)
                    _hiddenCharacters.Add(bot.gameObject);
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
        var rig = new GameObject("ShowCamera");
        rig.tag = "MainCamera";
        var cam = rig.AddComponent<Camera>();
        cam.fieldOfView = BrawlCamera.Fov;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = BrawlStage.VoidColor;
        rig.AddComponent<AudioListener>();
        var data = rig.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        rig.AddComponent<BrawlCamera>();
        return rig;
    }
}
