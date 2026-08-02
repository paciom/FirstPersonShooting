using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Top-right corner panel that always says which form the robot on screen is
/// in — ROBOT or TANK — and plays the stop-motion transformation whenever that
/// robot morphs.
///
/// WHY IT EXISTS. In Player v AI first person you never see your own robot, so
/// pressing Morph used to have no picture at all; in AI v AI the fold is 0.55
/// seconds somewhere in a firefight and is easy to miss entirely. The panel is
/// the readable copy of an event the camera is bad at showing.
///
/// WHY STOP MOTION RATHER THAN THE GENERATED CLIP. This panel used to stream
/// &lt;robot&gt;-transform.mp4 out of StreamingAssets. The clip is prettier but it
/// is not what is happening: it is one canned robot in one canned paint,
/// pre-rendered, so it could not follow a magenta bot, could not follow the
/// spectator camera cutting to a different robot, and drifted out of step with
/// the real fold. The stages are the same meshes the arena folds through, so
/// the corner and the arena now show the same event, in the team's colours, for
/// whichever robot is actually on camera.
///
/// The rig is the one the select screen's cards use — stage models on a
/// turntable, three point lights, a small camera into a RenderTexture — parked
/// far below the arena. Sizing constants are shared with RobotSelectMenu on
/// purpose: a robot that folds at different proportions on its card and in the
/// HUD reads as two different robots.
///
/// One rig per team, built on demand and kept: AI v AI cuts between cyan and
/// magenta every few seconds, and rebuilding eight GLB stages on every cut
/// would hitch the frame the camera cuts on. Both go when the panel hides,
/// which is when the match ends.
/// </summary>
public class TransformCast : MonoBehaviour
{
    /// <summary>Side of the rendered square, on the 1920x1080 reference canvas.</summary>
    const float PanelSize = 200f;

    const float FadeInSeconds = 0.2f;
    const float FadeOutSeconds = 0.35f;

    /// <summary>
    /// Floor on how long one stage may be held. The arena's fold is 0.55s, which
    /// across eight stages is 69ms each — under the point where a still
    /// registers, so the panel would flicker rather than read as a sequence.
    /// The cast is a replay, not the event, so it is allowed to run slightly
    /// past the fold it started with; the arena keeps its own timing.
    /// </summary>
    const float MinStageSeconds = 0.11f;

    const float IdleSpinDegrees = 20f;
    const float FoldSpinDegrees = 85f;

    /// <summary>
    /// How fast the swap flash fades, in units of alpha per second. The same
    /// trick StopMotionTransformer's light burst plays: consecutive stages share
    /// no topology, so every change is a pop, and the films cut around a pop
    /// with light.
    /// </summary>
    const float FlashFade = 5.5f;

    /// <summary>
    /// Where the rigs are parked. Clear of the select screen's own preview rigs,
    /// which sit at RobotSelectMenu.PreviewDepth (-150), 40 above it, and 60
    /// below it for the inspector — nothing of theirs reaches this far down.
    /// </summary>
    const float RigDepth = -300f;

    /// <summary>Gap between the two team rigs. Wider than the 6-unit preview lights.</summary>
    const float RigSpacing = 30f;

    static readonly Color HoloCyan = new Color(0.2f, 0.9f, 1f);

    public static TransformCast Instance { get; private set; }

    CanvasGroup _group;
    Image _frame;
    Image _flash;
    RawImage _view;
    Text _caption;
    Text _title;

    readonly FormRig[] _rigs = new FormRig[2];

    TransformMode _subject;
    int _subjectTeam;
    bool _shown;

    bool _folding;
    bool _foldToVehicle;
    float _foldClock;
    float _foldSeconds;
    float _flashAmount;

    /// <summary>Create the panel if it isn't there yet. Safe to call repeatedly.</summary>
    public static TransformCast Ensure()
    {
        if (Instance == null)
        {
            var go = new GameObject("TransformCast");
            go.AddComponent<TransformCast>();
        }
        return Instance;
    }

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        BuildUi();
    }

    void OnDestroy()
    {
        Unsubscribe();
        DestroyRigs();
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (_group == null)
            return;

        TrackSubject();

        float dt = Time.unscaledDeltaTime;
        _group.alpha = Mathf.MoveTowards(_group.alpha, _shown ? 1f : 0f,
            dt / (_shown ? FadeInSeconds : FadeOutSeconds));

        if (!_shown)
        {
            if (_group.alpha <= 0.001f && _group.gameObject.activeSelf)
            {
                _group.gameObject.SetActive(false);
                // Nothing to render and nothing to render it for: give back the
                // render textures and the eight stage models per team.
                DestroyRigs();
            }
            return;
        }

        var rig = ActiveRig();
        if (rig == null)
            return;

        AdvanceFold(rig);
        rig.Spin((_folding ? FoldSpinDegrees : IdleSpinDegrees) * Time.deltaTime);

        _flashAmount = Mathf.MoveTowards(_flashAmount, 0f, FlashFade * dt);
        _flash.color = new Color(1f, 1f, 1f, 0.5f * _flashAmount);
    }

    // ------------------------------------------------------------------ subject

    /// <summary>
    /// Point the panel at whoever the audience is watching: the local player in
    /// Player v AI, and whichever bot the spectator camera is on in AI v AI.
    ///
    /// Re-resolved every frame rather than once at match start, because both can
    /// change underneath us — the player's robot is built after the mode starts,
    /// and the spectator cuts to a new bot every few seconds.
    /// </summary>
    void TrackSubject()
    {
        var mode = GameModeController.Instance != null
            ? GameModeController.Instance.Mode : GameMode.Menu;

        TransformMode subject = null;
        int team = 0;

        if (mode == GameMode.PlayerVsAI)
        {
            if (PlayerBrain.Local != null)
                subject = PlayerBrain.Local.GetComponent<TransformMode>();
            team = 0;
        }
        else if (mode == GameMode.AIvAI)
        {
            var director = SpectatorCamera.Active;
            var watched = director != null ? director.Subject : null;
            if (watched != null)
            {
                subject = watched.GetComponent<TransformMode>();
                var shield = watched.GetComponent<EnergyShield>();
                team = shield != null ? shield.teamId : 0;
            }
        }

        // A robot with no vehicle clips can never morph, so a panel describing
        // its form would never change — say nothing instead.
        if (subject != null && !subject.CanTransform)
            subject = null;

        if (subject != _subject)
        {
            Unsubscribe();
            _subject = subject;
            _subjectTeam = team;
            _folding = false;
            if (_subject != null)
                _subject.OnFoldStarted += HandleFold;
        }
        else
        {
            _subjectTeam = team;
        }

        bool want = _subject != null;
        if (want && !_group.gameObject.activeSelf)
            _group.gameObject.SetActive(true);
        _shown = want;

        if (want)
            _frame.color = new Color(TeamTint().r, TeamTint().g, TeamTint().b, 0.85f);
    }

    void Unsubscribe()
    {
        if (_subject != null)
            _subject.OnFoldStarted -= HandleFold;
        _subject = null;
    }

    Color TeamTint() => MatchAnnouncer.TeamColor(_subjectTeam);

    void HandleFold(bool toVehicle)
    {
        var rig = ActiveRig();
        _folding = true;
        _foldToVehicle = toVehicle;
        _foldClock = 0f;
        _foldSeconds = Mathf.Max(TransformMode.FoldSeconds,
            (rig != null ? rig.StageCount : 1) * MinStageSeconds);
    }

    /// <summary>
    /// Step the stop motion, then land the panel on whatever form the subject is
    /// actually in.
    ///
    /// The end state is read off the subject rather than assumed from the fold
    /// that started, because a fold does not always finish the way it began: a
    /// de-rez calls ForceRobotForm mid-fold, and a request that arrived while
    /// busy turns straight around into the opposite one.
    /// </summary>
    void AdvanceFold(FormRig rig)
    {
        int last = Mathf.Max(0, rig.StageCount - 1);
        int stage;

        if (_folding)
        {
            _foldClock += Time.deltaTime;
            float progress = Mathf.Clamp01(_foldClock / Mathf.Max(0.01f, _foldSeconds));
            // Unfolding runs the same stages backwards — one sequence serves both
            // directions, exactly as the arena's fold does.
            float along = _foldToVehicle ? progress : 1f - progress;
            stage = Mathf.Clamp(Mathf.FloorToInt(along * (last + 1)), 0, last);
            if (_foldClock >= _foldSeconds)
                _folding = false;
        }
        else
        {
            stage = _subject != null && _subject.IsVehicle ? last : 0;
        }

        if (rig.Show(stage))
            _flashAmount = 1f;

        _caption.text = _folding
            ? (_foldToVehicle ? "TRANSFORMING" : "BACK  TO  ROBOT")
            : (_subject != null && _subject.IsVehicle ? "TANK" : "ROBOT");
        _caption.color = _folding ? new Color(0.02f, 0.06f, 0.10f, 0.75f)
                                  : new Color(0.02f, 0.06f, 0.10f);
    }

    // --------------------------------------------------------------------- rigs

    /// <summary>
    /// The rig for the subject's team, built if this is the first sight of it and
    /// rebuilt if that team has since changed robot. Only the rig being shown
    /// renders — the other team's camera is switched off rather than drawing a
    /// texture nothing samples.
    /// </summary>
    FormRig ActiveRig()
    {
        if (_subject == null || GameModeController.Instance == null)
            return null;

        int team = Mathf.Clamp(_subjectTeam, 0, _rigs.Length - 1);
        int robot = GameModeController.Instance.RobotIndexFor(team);
        if (robot < 0)
            return null;

        var rig = _rigs[team];
        if (rig != null && rig.robotIndex != robot)
        {
            rig.Dispose();
            rig = _rigs[team] = null;
        }
        if (rig == null)
        {
            rig = _rigs[team] = FormRig.Build(transform, GameModeController.Instance.RobotFor(team),
                                              robot, MatchAnnouncer.TeamColor(team), team);
            if (rig == null)
                return null;
            _view.texture = rig.texture;
        }

        if (_view.texture != rig.texture)
            _view.texture = rig.texture;

        for (int i = 0; i < _rigs.Length; i++)
            if (_rigs[i] != null && _rigs[i].camera != null)
                _rigs[i].camera.enabled = _rigs[i] == rig;

        return rig;
    }

    void DestroyRigs()
    {
        for (int i = 0; i < _rigs.Length; i++)
        {
            if (_rigs[i] != null)
                _rigs[i].Dispose();
            _rigs[i] = null;
        }
        if (_view != null)
            _view.texture = null;
    }

    /// <summary>
    /// One team's stop-motion set: the stage models on a turntable, a camera
    /// looking at them, and the texture it renders into.
    /// </summary>
    class FormRig
    {
        public GameObject root;
        public Transform turntable;
        public Camera camera;
        public RenderTexture texture;
        public GameObject[] stages;
        public int robotIndex = -1;

        int _shown = -1;

        public int StageCount => stages != null ? stages.Length : 0;

        public static FormRig Build(Transform parent, RobotRoster.Entry entry, int robotIndex,
                                    Color tint, int team)
        {
            // Stages are the whole point; a robot that has none still gets a
            // panel, built from the two models it does have.
            GameObject[] sources = entry.HasStages
                ? entry.transformStages
                : new[] { entry.modelPrefab, entry.vehiclePrefab };
            if (sources == null || sources.Length == 0 || sources[0] == null)
                return null;

            var rig = new FormRig { robotIndex = robotIndex };

            rig.root = new GameObject($"FormRig_{team}");
            rig.root.transform.SetParent(parent, false);
            rig.root.transform.position = new Vector3(team * RigSpacing, RigDepth, 0f);

            var spin = new GameObject("Turntable");
            spin.transform.SetParent(rig.root.transform, false);
            rig.turntable = spin.transform;

            var built = new System.Collections.Generic.List<GameObject>(sources.Length);
            foreach (var source in sources)
                built.Add(source != null ? Object.Instantiate(source, rig.turntable) : null);

            // Stage one's target comes from its own proportions rather than a
            // fixed number, so every robot stands the same height in the panel
            // however tall or wide its rig happens to be. Measured before
            // anything is normalised, exactly as the cards do it.
            float robotDiagonal = RobotSelectMenu.StageVehicleDiagonal;
            var first = RobotSelectMenu.MeasureBounds(built[0]);
            if (first.size.y > 0.01f)
                robotDiagonal = first.size.magnitude
                              * (RobotSelectMenu.PreviewRobotHeight / first.size.y);

            // Generated stages come out of the image-to-3D pipeline nose-down -Z.
            // The separately generated vehicle models do not — the same split
            // VehicleSkin.stageYawOffset draws in the arena.
            float laterYaw = entry.HasStages ? RobotSelectMenu.StageYawOffset : 0f;

            for (int i = 0; i < built.Count; i++)
            {
                if (built[i] == null)
                    continue;
                RobotSelectMenu.NormalizeByDiagonal(built[i], rig.turntable,
                    i == 0 ? robotDiagonal : RobotSelectMenu.StageVehicleDiagonal,
                    i == 0 ? 0f : laterYaw);
                // Every stage, not just the robot: a fold that starts cyan and
                // ends in the other team's tank would be worse than no paint.
                TeamPaint.Apply(built[i], tint, TeamPaint.CardSize, false, entry.paintAnchorHue);
                built[i].SetActive(false);
            }
            rig.stages = built.ToArray();

            rig.texture = new RenderTexture(256, 256, 16);

            var camGo = new GameObject("FormCam");
            camGo.transform.SetParent(rig.root.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 0.55f, 2.7f);
            camGo.transform.localRotation = Quaternion.Euler(10f, 180f, 0f);
            rig.camera = camGo.AddComponent<Camera>();
            rig.camera.targetTexture = rig.texture;
            rig.camera.fieldOfView = 40f;
            rig.camera.nearClipPlane = 0.05f;
            rig.camera.farClipPlane = 12f;
            rig.camera.clearFlags = CameraClearFlags.SolidColor;
            rig.camera.backgroundColor = RobotSelectMenu.PreviewBackdrop;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;

            // The arena's key light travels toward +Z, so it hits the far side of
            // anything this camera looks at. Every preview needs its own.
            RobotSelectMenu.AddThreePointLights(rig.root.transform);

            rig.Show(0);
            return rig;
        }

        public void Spin(float degrees)
        {
            if (turntable != null)
                turntable.Rotate(0f, degrees, 0f);
        }

        /// <summary>Show one stage. Returns true when this was a change.</summary>
        public bool Show(int index)
        {
            if (stages == null || stages.Length == 0)
                return false;
            index = Mathf.Clamp(index, 0, stages.Length - 1);
            if (index == _shown)
                return false;

            for (int i = 0; i < stages.Length; i++)
                if (stages[i] != null)
                    stages[i].SetActive(i == index);

            bool changed = _shown >= 0;   // the first show is an appearance, not a swap
            _shown = index;
            return changed;
        }

        public void Dispose()
        {
            if (camera != null)
                camera.targetTexture = null;
            if (texture != null)
                texture.Release();
            texture = null;
            if (root != null)
                Object.Destroy(root);
            root = null;
            stages = null;
        }
    }

    // ----------------------------------------------------------------------- ui

    void BuildUi()
    {
        var canvasGo = new GameObject("TransformCastCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Over the HUD (0) and the mode overlay (10), under the on-screen
        // controls (15) and the menu (20).
        canvas.sortingOrder = 12;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // Top-right corner, which the touch MENU button vacated for it.
        var panel = new GameObject("Panel");
        panel.transform.SetParent(canvasGo.transform, false);
        _group = panel.AddComponent<CanvasGroup>();
        _frame = panel.AddComponent<Image>();
        _frame.color = new Color(HoloCyan.r, HoloCyan.g, HoloCyan.b, 0.85f);
        _frame.raycastTarget = false;
        var frameRect = _frame.rectTransform;
        frameRect.anchorMin = frameRect.anchorMax = new Vector2(1f, 1f);
        frameRect.pivot = new Vector2(1f, 1f);
        frameRect.anchoredPosition = new Vector2(-40f, -40f);
        frameRect.sizeDelta = new Vector2(PanelSize + 8f, PanelSize + 76f);

        _title = MakeLabel(panel.transform, "Title", "FORM", 16, FontStyle.Bold,
            new Color(0.02f, 0.06f, 0.10f, 0.6f));
        var titleRect = _title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.offsetMin = new Vector2(0f, -22f);
        titleRect.offsetMax = new Vector2(0f, -2f);

        var viewGo = new GameObject("View");
        viewGo.transform.SetParent(panel.transform, false);
        _view = viewGo.AddComponent<RawImage>();
        _view.raycastTarget = false;
        var viewRect = _view.rectTransform;
        viewRect.anchorMin = viewRect.anchorMax = new Vector2(0.5f, 1f);
        viewRect.pivot = new Vector2(0.5f, 1f);
        viewRect.anchoredPosition = new Vector2(0f, -24f);
        viewRect.sizeDelta = new Vector2(PanelSize, PanelSize);

        // Sits over the render, not over the caption: this is the light burst
        // that covers a stage swap, and a caption that strobes with it would
        // just look broken.
        var flashGo = new GameObject("Flash");
        flashGo.transform.SetParent(panel.transform, false);
        _flash = flashGo.AddComponent<Image>();
        _flash.color = new Color(1f, 1f, 1f, 0f);
        _flash.raycastTarget = false;
        var flashRect = _flash.rectTransform;
        flashRect.anchorMin = flashRect.anchorMax = new Vector2(0.5f, 1f);
        flashRect.pivot = new Vector2(0.5f, 1f);
        flashRect.anchoredPosition = viewRect.anchoredPosition;
        flashRect.sizeDelta = viewRect.sizeDelta;

        _caption = MakeLabel(panel.transform, "Caption", "ROBOT", 24, FontStyle.Bold,
            new Color(0.02f, 0.06f, 0.10f));
        var captionRect = _caption.rectTransform;
        captionRect.anchorMin = new Vector2(0f, 0f);
        captionRect.anchorMax = new Vector2(1f, 0f);
        captionRect.pivot = new Vector2(0.5f, 0f);
        captionRect.offsetMin = new Vector2(0f, 6f);
        captionRect.offsetMax = new Vector2(0f, 44f);

        _group.alpha = 0f;
        panel.SetActive(false);
    }

    static Text MakeLabel(Transform parent, string name, string content, int fontSize,
                          FontStyle style, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = content;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }
}
