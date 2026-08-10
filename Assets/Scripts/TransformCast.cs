using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// The form dial: a round button in the top-right corner showing the PLAYER'S
/// robot in whichever form it is currently in, playing the stop-motion
/// transformation whenever they morph, and doubling as the control that morphs
/// them.
///
/// WHY IT EXISTS. Player v AI is first person, so you never see your own robot:
/// pressing Morph had no picture at all, and nothing on screen said which form
/// you were in afterwards. The dial is the readable copy of an event the camera
/// cannot show.
///
/// NO WORDS ON IT. It used to caption itself ROBOT or TANK. A robot looks like a
/// robot and a tank looks like a tank — the caption named the one thing the
/// picture could never fail to say, while taking a third of the circle to say
/// it. The picture is the whole dial now.
///
/// WHY IT IS ALSO THE BUTTON. It began as a display sitting next to a separate
/// MORPH button — two widgets for one idea, on a phone screen that has no room
/// to spare, and the display was already showing the exact thing the button
/// would change. Tapping the thing you are looking at to change it is both
/// smaller and easier to guess. It is drawn from TouchControls' own disc and
/// ring sprites at their palette so that it reads as pressable the only way
/// that reliably works: by looking like the things that already are. A label
/// under it says so outright until the first morph, then fades for good.
///
/// PLAYER V AI ONLY, and only the player's own robot. Every other mode either
/// shows the robot already (the spectator camera in AI v AI orbits it in full
/// view) or has no player robot to describe, and a panel describing someone
/// else's form is just a distraction sitting over the arena.
///
/// WHY STOP MOTION RATHER THAN THE GENERATED CLIP. This panel used to stream
/// &lt;robot&gt;-transform.mp4 out of StreamingAssets. The clip is prettier but it
/// is not what is happening: pre-rendered, in the factory paint, at its own
/// pace, so it drifted out of step with the fold it was supposed to be showing.
/// The stages are the same meshes the arena folds through, so the corner and the
/// arena now show the same event.
///
/// The rig is the one the select screen's cards use — stage models on a
/// turntable, three point lights, a small camera into a RenderTexture — parked
/// far below the arena. Sizing constants are shared with RobotSelectMenu on
/// purpose: a robot that folds at different proportions on its card and in the
/// HUD reads as two different robots.
///
/// The rig is built on the first frame the panel is wanted and thrown away when
/// it hides, which is when the match ends.
/// </summary>
public class TransformCast : MonoBehaviour
{
    /// <summary>
    /// Diameter of the dial, on the 1920x1080 reference canvas. Bigger than the
    /// action buttons (140-180): it is the only round button carrying a picture
    /// rather than a word, and a robot rendered at 140 across is a smudge.
    /// </summary>
    const float DialSize = 200f;

    /// <summary>Gap from the top-right corner to the edge of the dial.</summary>
    const float DialMargin = 40f;

    /// <summary>How fast the press pulse fades, in units of pulse per second.</summary>
    const float PressFade = 2.6f;

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
    /// How fast the swap flash fades, in units of alpha per second. Consecutive
    /// stages share no topology, so every change is a pop, and the films cut
    /// around a pop with light.
    /// </summary>
    const float FlashFade = 5.5f;

    /// <summary>
    /// Where the rig is parked. Clear of the select screen's own preview rigs,
    /// which sit at RobotSelectMenu.PreviewDepth (-150), 40 above it, and 60
    /// below it for the inspector — nothing of theirs reaches this far down.
    /// </summary>
    const float RigDepth = -300f;

    /// <summary>The player is always cyan, so the panel is too.</summary>
    static readonly Color HoloCyan = new Color(0.2f, 0.9f, 1f);

    public static TransformCast Instance { get; private set; }

    CanvasGroup _group;
    Image _dial;
    RectTransform _dialRect;
    Image _rim;
    Image _flash;
    RawImage _view;
    Text _hint;

    /// <summary>Fades once the player has morphed, and never comes back.</summary>
    bool _hasMorphed;
    float _press;

    FormRig _rig;

    TransformMode _subject;
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
        DestroyRig();
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
                // render texture and the eight stage models.
                DestroyRig();
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

        UpdateDial(dt);
    }

    /// <summary>
    /// The button half: the press pulse, and the hint that says what pressing it
    /// does until the player has found out.
    /// </summary>
    void UpdateDial(float dt)
    {
        _press = Mathf.MoveTowards(_press, 0f, PressFade * dt);

        // Lit rather than merely tinted while it is being used, which is how
        // every other round button answers a thumb.
        Color idle = TouchControls.ButtonIdle;
        _dial.color = Color.Lerp(idle, new Color(HoloCyan.r, HoloCyan.g, HoloCyan.b, 0.75f), _press);
        _rim.color = new Color(HoloCyan.r, HoloCyan.g, HoloCyan.b, Mathf.Lerp(0.7f, 1f, _press));

        // Worded for whichever input is actually driving. The on-screen controls
        // can come and go mid-match — picking up a tablet is all it takes — so
        // this is read every frame rather than set once.
        _hint.text = TouchControls.Active ? "TAP  TO  MORPH" : "T  —  MORPH";
        float wanted = _hasMorphed ? 0f : 0.55f;
        var color = _hint.color;
        color.a = Mathf.MoveTowards(color.a, wanted, 1.2f * dt);
        _hint.color = color;
        _hint.gameObject.SetActive(color.a > 0.01f);
    }

    // ------------------------------------------------------------------ subject

    /// <summary>
    /// Point the panel at the player's own robot, in Player v AI and nowhere
    /// else.
    ///
    /// Re-resolved every frame rather than once at match start: the player's
    /// robot is built after the mode starts, and rebuilt whenever they pick a
    /// different one.
    /// </summary>
    void TrackSubject()
    {
        var mode = GameModeController.Instance != null
            ? GameModeController.Instance.Mode : GameMode.Menu;

        TransformMode subject = null;
        if (GameModeController.IsFirstPersonMatch(mode) && PlayerBrain.Local != null)
            subject = PlayerBrain.Local.GetComponent<TransformMode>();

        // A robot with no vehicle clips can never morph, so a panel describing
        // its form would never change — say nothing instead.
        if (subject != null && !subject.CanTransform)
            subject = null;

        if (subject != _subject)
        {
            Unsubscribe();
            _subject = subject;
            _folding = false;
            if (_subject != null)
                _subject.OnFoldStarted += HandleFold;
        }

        bool want = _subject != null;
        if (want && !_group.gameObject.activeSelf)
            _group.gameObject.SetActive(true);
        _shown = want;
    }

    void Unsubscribe()
    {
        if (_subject != null)
            _subject.OnFoldStarted -= HandleFold;
        _subject = null;
    }

    void HandleFold(bool toVehicle)
    {
        var rig = ActiveRig();
        // Fires for a fold that was actually ACCEPTED, which makes it the honest
        // press feedback: a tap while frozen, or on a robot that cannot fold, is
        // refused upstream and correctly lights nothing.
        _press = 1f;
        _hasMorphed = true;
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

        // No caption. The dial used to spell out ROBOT or TANK under the
        // picture, which was a word for something the picture had already said —
        // a robot looks like a robot and a tank looks like a tank, and the fold
        // between them is the one thing on this dial that MOVES. Dropping it
        // also gives the render the whole circle instead of two thirds of it.
    }

    // --------------------------------------------------------------------- rigs

    /// <summary>
    /// The player's stage set, built on first sight and rebuilt when they pick a
    /// different robot.
    /// </summary>
    FormRig ActiveRig()
    {
        if (_subject == null || GameModeController.Instance == null)
            return null;

        int robot = GameModeController.Instance.PlayerRobotIndex;

        if (_rig != null && _rig.robotIndex != robot)
        {
            _rig.Dispose();
            _rig = null;
        }
        if (_rig == null)
        {
            _rig = FormRig.Build(transform, GameModeController.Instance.PlayerRobot, robot, HoloCyan);
            if (_rig == null)
                return null;
        }

        if (_view.texture != _rig.texture)
            _view.texture = _rig.texture;

        return _rig;
    }

    void DestroyRig()
    {
        if (_rig != null)
            _rig.Dispose();
        _rig = null;
        if (_view != null)
            _view.texture = null;
    }

    /// <summary>
    /// The stop-motion set: the stage models on a turntable, a camera looking at
    /// them, and the texture it renders into.
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
                                    Color tint)
        {
            // Stages are the whole point; a robot that has none still gets a
            // panel, built from the two models it does have.
            GameObject[] sources = entry.HasStages
                ? entry.transformStages
                : new[] { entry.modelPrefab, entry.vehiclePrefab };
            if (sources == null || sources.Length == 0 || sources[0] == null)
                return null;

            var rig = new FormRig { robotIndex = robotIndex };

            rig.root = new GameObject("FormRig");
            rig.root.transform.SetParent(parent, false);
            rig.root.transform.position = new Vector3(0f, RigDepth, 0f);

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
                              * (RobotSelectMenu.RobotHeightFor(entry.displayName) / first.size.y);

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
                // Every stage, not just the robot: a fold that starts in the
                // player's colours and ends in the factory paint would be worse
                // than no paint at all.
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

    /// <summary>
    /// The dial: a round button the size of the on-screen action buttons, with
    /// the live form inside it instead of a word.
    ///
    /// WHY THE DISPLAY IS THE BUTTON. There used to be a MORPH button as well as
    /// this readout — two widgets for one idea, and the readout was already
    /// showing the exact thing the button would change. Merging them costs a
    /// corner of the screen less and puts the control where the player is
    /// already looking to find out what form they are in.
    ///
    /// It is built from TouchControls' own disc and ring sprites, at their
    /// palette, on purpose: "you can press this" is carried entirely by looking
    /// like the things that are already pressable. A differently-drawn circle
    /// would read as a picture frame.
    /// </summary>
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
        var panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(1f, 1f);
        panelRect.pivot = new Vector2(1f, 1f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = Vector2.zero;
        _group = panel.AddComponent<CanvasGroup>();

        // The disc IS the hit target, so everything that should be tappable is
        // inside it and nothing that should not be — the hint below sits outside.
        var dialGo = new GameObject("Dial");
        dialGo.transform.SetParent(panel.transform, false);
        _dial = dialGo.AddComponent<Image>();
        _dial.sprite = TouchControls.DiscSprite();
        _dial.color = TouchControls.ButtonIdle;
        _dial.raycastTarget = false;
        _dialRect = _dial.rectTransform;
        _dialRect.anchorMin = _dialRect.anchorMax = new Vector2(1f, 1f);
        _dialRect.pivot = new Vector2(1f, 1f);
        _dialRect.anchoredPosition = new Vector2(-DialMargin, -DialMargin);
        _dialRect.sizeDelta = Vector2.one * DialSize;

        // Clips the square render into the circle. Without it the picture is a
        // box sitting inside a ring, which is exactly the picture-frame reading
        // the round shape is there to avoid.
        var mask = dialGo.AddComponent<Mask>();
        mask.showMaskGraphic = true;

        var viewGo = new GameObject("View");
        viewGo.transform.SetParent(_dialRect, false);
        _view = viewGo.AddComponent<RawImage>();
        _view.raycastTarget = false;
        var viewRect = _view.rectTransform;
        viewRect.anchorMin = Vector2.zero;
        viewRect.anchorMax = Vector2.one;
        viewRect.offsetMin = Vector2.zero;
        viewRect.offsetMax = Vector2.zero;

        // Over the render, under the caption: this is the light burst that
        // covers a stage swap, and a caption that strobes with it would just
        // look broken.
        var flashGo = new GameObject("Flash");
        flashGo.transform.SetParent(_dialRect, false);
        _flash = flashGo.AddComponent<Image>();
        _flash.sprite = TouchControls.DiscSprite();
        _flash.color = new Color(1f, 1f, 1f, 0f);
        _flash.raycastTarget = false;
        var flashRect = _flash.rectTransform;
        flashRect.anchorMin = Vector2.zero;
        flashRect.anchorMax = Vector2.one;
        flashRect.offsetMin = Vector2.zero;
        flashRect.offsetMax = Vector2.zero;

        // A sibling of the disc rather than a child: the mask would eat the
        // outer edge of a ring drawn exactly at the boundary.
        var rimGo = new GameObject("Rim");
        rimGo.transform.SetParent(panel.transform, false);
        _rim = rimGo.AddComponent<Image>();
        _rim.sprite = TouchControls.RingSprite();
        _rim.color = new Color(HoloCyan.r, HoloCyan.g, HoloCyan.b, 0.7f);
        _rim.raycastTarget = false;
        var rimRect = _rim.rectTransform;
        rimRect.anchorMin = rimRect.anchorMax = new Vector2(1f, 1f);
        rimRect.pivot = new Vector2(1f, 1f);
        rimRect.anchoredPosition = _dialRect.anchoredPosition;
        rimRect.sizeDelta = _dialRect.sizeDelta;

        // Says what pressing it does, until the player has pressed it. The same
        // bargain the turn hint strikes: a label that stays forever is clutter,
        // and one that was never there is a control nobody finds.
        _hint = MakeLabel(panel.transform, "Hint", "", 20, FontStyle.Bold,
            new Color(1f, 1f, 1f, 0.55f));
        var hintRect = _hint.rectTransform;
        hintRect.anchorMin = hintRect.anchorMax = new Vector2(1f, 1f);
        hintRect.pivot = new Vector2(1f, 1f);
        // Exactly the dial's width and column, so the centred text lands under
        // the middle of the circle rather than off to one side of it.
        hintRect.anchoredPosition = new Vector2(-DialMargin, -DialMargin - DialSize - 6f);
        hintRect.sizeDelta = new Vector2(DialSize, 30f);

        // The taps come through TouchControls so they are swallowed the way a
        // tap on any other button is — see SetFormDial.
        TouchControls.SetFormDial(_dialRect);

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
