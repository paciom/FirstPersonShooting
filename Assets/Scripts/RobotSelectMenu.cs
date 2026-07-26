using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Robot selection screen shown after picking AI v AI or Player v AI on the
/// main menu: a row of live rotating 3D previews per team — click a card to
/// pick that team's robot, then START MATCH. Built entirely at runtime like
/// MainMenu. The returned root owns the canvas, the off-screen preview rigs
/// and their render textures, so destroying the root cleans everything up.
/// </summary>
public static class RobotSelectMenu
{
    internal static readonly Color HoloCyan = new Color(0.2f, 0.9f, 1f);
    internal static readonly Color HoloMagenta = new Color(1f, 0.25f, 0.9f);
    static readonly Color CardColor = new Color(0.06f, 0.14f, 0.22f, 0.95f);

    // Preview rigs live far below the arena so the tiny preview cameras
    // (short far plane) see nothing but their own robot.
    internal const float PreviewDepth = -150f;

    // Usable width for a team's card row on the 1920-wide reference canvas,
    // leaving margins for the BACK button and screen edges.
    const float RowWidth = 1800f;

    // Vehicle height as a fraction of the robot's, for the preview cards only —
    // half what the arena uses. The rig cameras are framed on a robot, which is
    // tall and narrow, but a vehicle is long and low: matched on height it runs
    // out over the sides of the card. Gameplay keeps VehicleSkin's own default,
    // where the vehicle has the whole arena to sit in.
    const float PreviewVehicleHeight = 0.31f;

    public static GameObject Build(GameModeController controller, RobotRoster roster,
        GameMode pendingMode, int cyanIndex, int magentaIndex)
    {
        var root = new GameObject("RobotSelect");
        root.transform.SetParent(controller.transform, false);

        int count = roster.robots.Length;
        var previews = new RenderTexture[count];
        BuildPreviewRigs(root, roster, previews);

        var canvasGo = new GameObject("Canvas");
        canvasGo.transform.SetParent(root.transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 21;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        var backdrop = MakeImage(canvasGo.transform, "Backdrop", new Color(0.01f, 0.03f, 0.06f, 0.9f));
        Stretch(backdrop.rectTransform);

        MakeText(canvasGo.transform, "Title", "CHOOSE  ROBOTS", 64, HoloCyan, FontStyle.Bold,
            new Vector2(0.5f, 1f), new Vector2(0, -85), new Vector2(1200, 80));
        MakeText(canvasGo.transform, "Subtitle",
            pendingMode == GameMode.AIvAI ? "AI  v  AI" : "PLAYER  v  AI", 26,
            new Color(1f, 1f, 1f, 0.55f), FontStyle.Normal,
            new Vector2(0.5f, 1f), new Vector2(0, -145), new Vector2(800, 40));

        var state = root.AddComponent<RobotSelectState>();
        state.cyanIndex = Mathf.Clamp(cyanIndex, 0, count - 1);
        state.magentaIndex = Mathf.Clamp(magentaIndex, 0, count - 1);

        // Created before the rows so their thumbnails can capture it, but its UI
        // is built last so the dialog draws over everything else on the canvas.
        var inspector = RobotInspector.Create(root, roster, state);

        BuildTeamRow(canvasGo.transform, "CYAN TEAM", HoloCyan, 130f, roster, previews, state, true, inspector);
        BuildTeamRow(canvasGo.transform, "MAGENTA TEAM", HoloMagenta, -160f, roster, previews, state, false, inspector);
        state.Refresh();

        MakeButton(canvasGo.transform, "START  MATCH", new Vector2(0, -350), new Vector2(420, 78), 34,
            () => controller.LaunchSelectedMatch(state.cyanIndex, state.magentaIndex));
        MakeButton(canvasGo.transform, "BACK", new Vector2(-560, -350), new Vector2(200, 78), 28,
            controller.CancelRobotSelect);

        inspector.BuildUI(canvasGo.transform);

        return root;
    }

    static void BuildTeamRow(Transform parent, string header, Color teamColor, float rowY,
        RobotRoster roster, RenderTexture[] previews, RobotSelectState state, bool isCyan,
        RobotInspector inspector)
    {
        MakeText(parent, header, header, 30, teamColor, FontStyle.Bold,
            new Vector2(0.5f, 0.5f), new Vector2(0, rowY + 140f), new Vector2(600, 40));

        int n = roster.robots.Length;
        // The roster grows every time a robot is generated, so the row has to
        // shrink to fit rather than run off the 1920-wide reference canvas.
        float spacing = Mathf.Min(210f, RowWidth / Mathf.Max(1, n));
        float cardWidth = spacing - 20f;
        var cards = new Image[n];
        var bars = new Image[n];
        for (int i = 0; i < n; i++)
        {
            float x = (i - (n - 1) * 0.5f) * spacing;
            MakeCard(parent, roster.robots[i].displayName, previews[i], new Vector2(x, rowY),
                cardWidth, teamColor, out cards[i], out bars[i], out var thumbnail);
            int index = i;
            cards[i].GetComponent<Button>().onClick.AddListener(() =>
            {
                if (isCyan) state.cyanIndex = index; else state.magentaIndex = index;
                state.Refresh();
            });

            // The thumbnail sits above the card's own button and swallows the
            // click, so the 3D area opens the inspector while the label and the
            // card border around it still pick the robot outright.
            var inspect = thumbnail.gameObject.AddComponent<Button>();
            inspect.transition = Selectable.Transition.None;
            inspect.onClick.AddListener(() => inspector.Open(index, isCyan));
        }

        if (isCyan) { state.cyanCards = cards; state.cyanBars = bars; state.cyanColor = teamColor; }
        else { state.magentaCards = cards; state.magentaBars = bars; state.magentaColor = teamColor; }
    }

    static void MakeCard(Transform parent, string label, RenderTexture preview, Vector2 position,
        float width, Color teamColor, out Image background, out Image selectionBar,
        out RawImage thumbnail)
    {
        // Cards keep their height and just get narrower as the roster grows; the
        // preview and label scale with the width so nothing spills over the edge.
        float scale = Mathf.Clamp01(width / 190f);

        background = MakeImage(parent, $"Card_{label}", CardColor);
        var rect = background.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(width, 230);

        var button = background.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        var colors = button.colors;
        colors.highlightedColor = new Color(0.10f, 0.30f, 0.42f, 1f);
        colors.pressedColor = teamColor * 0.6f;
        button.colors = colors;

        var previewGo = new GameObject("Preview");
        previewGo.transform.SetParent(background.transform, false);
        var raw = previewGo.AddComponent<RawImage>();
        raw.texture = preview;
        thumbnail = raw;
        var rawRect = raw.rectTransform;
        rawRect.anchorMin = rawRect.anchorMax = new Vector2(0.5f, 0.5f);
        rawRect.anchoredPosition = new Vector2(0, 22);
        rawRect.sizeDelta = new Vector2(width - 16f, 168f * scale);

        MakeText(background.transform, "Label", label, Mathf.RoundToInt(24 * scale), Color.white,
            FontStyle.Bold, new Vector2(0.5f, 0f), new Vector2(0, 32), new Vector2(width - 6f, 32));

        // Team-colored strip along the bottom, shown only on the selected card.
        selectionBar = MakeImage(background.transform, "SelectionBar", teamColor);
        selectionBar.rectTransform.anchorMin = new Vector2(0, 0);
        selectionBar.rectTransform.anchorMax = new Vector2(1, 0);
        selectionBar.rectTransform.offsetMin = new Vector2(6, 4);
        selectionBar.rectTransform.offsetMax = new Vector2(-6, 12);
    }

    static void BuildPreviewRigs(GameObject root, RobotRoster roster, RenderTexture[] previews)
    {
        var rigRoot = new GameObject("PreviewRigs");
        rigRoot.transform.SetParent(root.transform, false);
        rigRoot.transform.position = new Vector3(0f, PreviewDepth, 0f);

        int n = roster.robots.Length;
        var cams = new Camera[n];
        for (int i = 0; i < n; i++)
        {
            var rig = new GameObject($"Rig_{roster.robots[i].displayName}");
            rig.transform.SetParent(rigRoot.transform, false);
            rig.transform.localPosition = new Vector3(i * 25f, 0f, 0f);

            previews[i] = new RenderTexture(256, 256, 16);

            var spin = new GameObject("Spin");
            spin.transform.SetParent(rig.transform, false);
            var spinner = spin.AddComponent<RobotPreviewSpinner>();
            // Spread the fleet evenly around the cycle so the row always has
            // something mid-transformation rather than all nine snapping at once.
            spinner.phaseDegrees = n > 1 ? i * (360f / n) : 0f;
            if (roster.robots[i].modelPrefab != null)
                NormalizeToCenter(Object.Instantiate(roster.robots[i].modelPrefab, spin.transform),
                    spin.transform);

            // Added after the model, so VehicleSkin's Start finds it to measure against.
            var skin = spin.AddComponent<VehicleSkin>();
            skin.holder = spin.transform;
            skin.vehiclePrefab = roster.robots[i].vehiclePrefab;
            skin.heightFraction = PreviewVehicleHeight;

            var camGo = new GameObject("PreviewCam");
            camGo.transform.SetParent(rig.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 0.55f, 2.7f);
            camGo.transform.localRotation = Quaternion.Euler(10f, 180f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.targetTexture = previews[i];
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 12f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.05f, 0.10f, 1f);
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            cams[i] = cam;

            // Rigs are 25 apart and the lights reach 6, so each one lights only
            // its own robot.
            AddThreePointLights(rig.transform);
        }

        var cleanup = root.AddComponent<RobotPreviewCleanup>();
        cleanup.textures = previews;
        cleanup.cameras = cams;
    }

    /// <summary>
    /// Studio rig for a preview camera sitting on +Z looking back at the model.
    ///
    /// Every preview needs one. The arena's key light travels toward +Z, so it
    /// hits the far side of anything a preview camera looks at and leaves the
    /// visible face on ambient alone — which the Meshy robots used to hide by
    /// re-emitting their own albedo, and no longer do.
    ///
    /// POINT lights with a short range on purpose: a directional light has no
    /// position, so it would re-light the whole arena 150 units overhead.
    /// </summary>
    internal static void AddThreePointLights(Transform parent)
    {
        // Key from camera-right and above, cool fill opposite it, rim from
        // behind to lift the silhouette off the background.
        //
        // The intensities look large because point lights fall off with the
        // square of distance and these sit ~3.3 units off a model normalised to
        // 1.6 units tall. An intensity of 3 lands as 3/3.3^2 = 0.27 at the
        // surface, so the robot rendered at about half its albedo and vivid
        // orange came out as dark rust. Roughly d^2 (~11 for the key) puts a
        // fully-facing surface near its actual albedo and lets the falloff read
        // as shading rather than as gloom.
        AddLight(parent, new Vector3(1.7f, 1.9f, 2.1f), new Color(1f, 0.97f, 0.90f), 11.0f);
        AddLight(parent, new Vector3(-1.9f, 0.5f, 1.7f), new Color(0.55f, 0.72f, 1f), 4.5f);
        AddLight(parent, new Vector3(0f, 1.4f, -2.3f), new Color(0.80f, 0.90f, 1f), 6.0f);
    }

    static void AddLight(Transform parent, Vector3 localPosition, Color color, float intensity)
    {
        var go = new GameObject("PreviewLight");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = 6f;
        light.shadows = LightShadows.None;
    }

    /// <summary>Scales a preview model to ~1.6 units tall and centers it on its holder.</summary>
    internal static void NormalizeToCenter(GameObject instance, Transform holder)
    {
        instance.name = "Model";
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        var renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return;
        var bounds = renderers[0].bounds;
        foreach (var r in renderers)
            bounds.Encapsulate(r.bounds);
        float scale = 1.6f / Mathf.Max(0.01f, bounds.size.y);
        instance.transform.localScale *= scale;
        Vector3 localCenter = holder.InverseTransformPoint(bounds.center);
        instance.transform.localPosition = -localCenter * scale;
    }

    // ---------- uGUI helpers (same idiom as MainMenu) ----------

    internal static void MakeButton(Transform parent, string label, Vector2 position, Vector2 size,
        int fontSize, UnityEngine.Events.UnityAction onClick)
    {
        var image = MakeImage(parent, $"Button_{label}", CardColor);
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        var colors = button.colors;
        colors.highlightedColor = new Color(0.10f, 0.30f, 0.42f, 1f);
        colors.pressedColor = HoloCyan * 0.6f;
        button.colors = colors;
        button.onClick.AddListener(onClick);

        var underline = MakeImage(image.transform, "Underline", new Color(HoloCyan.r, HoloCyan.g, HoloCyan.b, 0.8f));
        underline.rectTransform.anchorMin = new Vector2(0, 0);
        underline.rectTransform.anchorMax = new Vector2(1, 0);
        underline.rectTransform.offsetMin = new Vector2(8, 0);
        underline.rectTransform.offsetMax = new Vector2(-8, 3);

        MakeText(image.transform, "Label", label, fontSize, Color.white, FontStyle.Bold,
            new Vector2(0.5f, 0.5f), Vector2.zero, size - new Vector2(20, 12));
    }

    internal static Image MakeImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    internal static Text MakeText(Transform parent, string name, string content, int size, Color color,
        FontStyle style, Vector2 anchor, Vector2 position, Vector2 sizeDelta)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = content;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        var rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = sizeDelta;
        return text;
    }

    internal static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}

/// <summary>Selection state + card highlighting for the robot select screen.
/// Runtime-only (never scene-serialized), so it may live in this file.</summary>
public class RobotSelectState : MonoBehaviour
{
    public int cyanIndex;
    public int magentaIndex;
    public Image[] cyanCards;
    public Image[] cyanBars;
    public Color cyanColor;
    public Image[] magentaCards;
    public Image[] magentaBars;
    public Color magentaColor;

    static readonly Color Unselected = new Color(0.06f, 0.14f, 0.22f, 0.95f);

    public void Refresh()
    {
        RefreshRow(cyanCards, cyanBars, cyanIndex, cyanColor);
        RefreshRow(magentaCards, magentaBars, magentaIndex, magentaColor);
    }

    static void RefreshRow(Image[] cards, Image[] bars, int selected, Color teamColor)
    {
        if (cards == null)
            return;
        for (int i = 0; i < cards.Length; i++)
        {
            bool on = i == selected;
            cards[i].color = on
                ? Color.Lerp(Unselected, teamColor, 0.30f)
                : Unselected;
            if (bars != null && bars[i] != null)
                bars[i].enabled = on;
        }
    }
}

/// <summary>
/// Turntable for the robot-select previews, and the showcase for vehicle form:
/// one full revolution as a robot, fold, another full revolution as a vehicle,
/// unfold, repeat. Tying the transformation to a completed revolution rather
/// than a timer means you always see the robot from every side before it
/// changes, and the same for the vehicle afterwards.
///
/// Cards are given a staggered head start so the row never transforms in
/// unison — with nine of them there is almost always one mid-fold to look at.
///
/// Robots with no forged vehicle clips (the procedural blockbot, the primitive
/// fallback) just spin, exactly as this did before.
/// </summary>
public class RobotPreviewSpinner : MonoBehaviour
{
    public float degreesPerSecond = 40f;

    [Tooltip("Head start in degrees, so a row of cards doesn't fold in unison.")]
    public float phaseDegrees;

    [Tooltip("How far the folded vehicle rises to stay framed. Purely a preview " +
             "framing aid — the vehicle sits on the floor in the actual game.")]
    public float vehicleLift = 0.35f;

    [Tooltip("Colour of the swap burst; the select screen uses a neutral cyan.")]
    public Color burstColor = new Color(0.2f, 0.9f, 1f);

    Animator _animator;
    VehicleSkin _skin;
    bool _canTransform;
    float _turned;
    bool _vehicle;
    Vector3 _restPosition;
    float _lift;
    float _swapAt = -1f;
    bool _swapTo;

    // Start, not Awake: AddComponent runs Awake immediately, which is before
    // the caller has set phaseDegrees and before the model has been parented
    // under us — an Awake lookup finds no Animator at all.
    void Start()
    {
        _restPosition = transform.localPosition;
        _turned = phaseDegrees;

        _animator = GetComponentInChildren<Animator>();
        _skin = GetComponent<VehicleSkin>();
        _canTransform = HasVehicleParameter(_animator);
    }

    void Update()
    {
        float step = degreesPerSecond * Time.deltaTime;
        transform.Rotate(0f, step, 0f);

        if (!_canTransform)
            return;

        _turned += step;
        if (_turned >= 360f)
        {
            _turned -= 360f;
            _vehicle = !_vehicle;
            _animator.SetBool(TransformMode.VehicleParameter, _vehicle);

            // Same timing the live fold uses, so the preview is showing the
            // real sequence rather than an approximation of it.
            if (_skin != null && _skin.HasVehicle)
            {
                _swapAt = Time.time + TransformMode.FoldSeconds * TransformMode.SwapFraction;
                _swapTo = _vehicle;
            }
        }

        if (_swapAt > 0f && Time.time >= _swapAt)
        {
            _swapAt = -1f;
            VfxUtil.Explosion(transform.position, burstColor, 0.7f);
            _skin.SetVehicle(_swapTo);
        }

        // Ride the lift in over the same window the fold takes, so the model
        // rises with the fold instead of sliding afterwards.
        float target = _vehicle ? vehicleLift : 0f;
        _lift = Mathf.MoveTowards(_lift, target,
            vehicleLift / Mathf.Max(0.01f, TransformMode.FoldSeconds) * Time.deltaTime);
        transform.localPosition = _restPosition + Vector3.up * _lift;
    }

    static bool HasVehicleParameter(Animator animator)
    {
        if (animator == null || animator.runtimeAnimatorController == null)
            return false;
        foreach (var parameter in animator.parameters)
            if (parameter.type == AnimatorControllerParameterType.Bool &&
                parameter.name == TransformMode.VehicleParameter)
                return true;
        return false;
    }
}

/// <summary>
/// Full-screen close-up of one robot, opened by clicking a card's 3D thumbnail.
///
/// The cards are ~190px wide and fed by 256px render textures, which is enough
/// to tell the robots apart and not nearly enough to judge how one is shaded.
/// This renders a single robot to a 1024px anti-aliased target instead, on a
/// turntable you can also drag by hand.
///
/// It carries its own copy of the shared three-point rig, placed 60 units below
/// the thumbnail rigs so neither set of lights reaches the other.
///
/// No VehicleSkin and no transformation cycle: this is for studying one form
/// while it holds still, so the fold showcase stays on the cards.
/// </summary>
public class RobotInspector : MonoBehaviour
{
    // Clear of the thumbnail rigs (which run from x=0 outwards at PreviewDepth)
    // by far more than the 6-unit light range, so nothing here spills onto them.
    const float RigDrop = -60f;
    const int TextureSize = 1024;
    const float DegreesPerSecond = 26f;
    const float DegreesPerDragPixel = 0.55f;

    RobotRoster _roster;
    RobotSelectState _state;
    Transform _turntable;
    Camera _camera;
    RenderTexture _texture;
    GameObject _dialog;
    Text _title;
    Image _accent;
    GameObject _model;
    int _index;
    bool _isCyan;
    float _pendingDrag;

    public static RobotInspector Create(GameObject root, RobotRoster roster, RobotSelectState state)
    {
        var inspector = root.AddComponent<RobotInspector>();
        inspector._roster = roster;
        inspector._state = state;
        inspector.BuildRig(root.transform);
        return inspector;
    }

    void BuildRig(Transform root)
    {
        var rig = new GameObject("InspectRig");
        rig.transform.SetParent(root, false);
        rig.transform.position = new Vector3(0f, RobotSelectMenu.PreviewDepth + RigDrop, 0f);

        var turntable = new GameObject("Turntable");
        turntable.transform.SetParent(rig.transform, false);
        _turntable = turntable.transform;

        _texture = new RenderTexture(TextureSize, TextureSize, 24) { antiAliasing = 4 };

        var camGo = new GameObject("InspectCam");
        camGo.transform.SetParent(rig.transform, false);
        camGo.transform.localPosition = new Vector3(0f, 0.45f, 3.1f);
        camGo.transform.localRotation = Quaternion.Euler(6f, 180f, 0f);
        _camera = camGo.AddComponent<Camera>();
        _camera.targetTexture = _texture;
        _camera.fieldOfView = 34f;
        _camera.nearClipPlane = 0.05f;
        _camera.farClipPlane = 12f;
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = new Color(0.02f, 0.05f, 0.10f, 1f);
        camGo.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
        // Only renders while the dialog is up; nine thumbnails are enough work.
        _camera.enabled = false;

        // Same rig the thumbnails use, at the same offsets — both normalize the
        // model to ~1.6 units centered on the rig origin, so a robot shades the
        // same here as on its card, only bigger.
        RobotSelectMenu.AddThreePointLights(rig.transform);
    }

    /// <summary>Builds the dialog hidden. Call last, so it draws over the rows.</summary>
    public void BuildUI(Transform canvas)
    {
        _dialog = new GameObject("InspectDialog");
        _dialog.transform.SetParent(canvas, false);
        RobotSelectMenu.Stretch(_dialog.AddComponent<RectTransform>());

        // Scrim and panel are siblings rather than parent and child: nested, a
        // click anywhere on the panel would bubble up to the scrim's button and
        // close the dialog out from under whatever was just pressed.
        var scrim = RobotSelectMenu.MakeImage(_dialog.transform, "Scrim",
            new Color(0.01f, 0.02f, 0.05f, 0.88f));
        RobotSelectMenu.Stretch(scrim.rectTransform);
        var scrimButton = scrim.gameObject.AddComponent<Button>();
        scrimButton.transition = Selectable.Transition.None;
        scrimButton.onClick.AddListener(Close);

        var panel = RobotSelectMenu.MakeImage(_dialog.transform, "Panel",
            new Color(0.05f, 0.11f, 0.18f, 0.98f));
        panel.rectTransform.anchorMin = panel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        panel.rectTransform.anchoredPosition = new Vector2(0f, 20f);
        panel.rectTransform.sizeDelta = new Vector2(760f, 800f);

        _accent = RobotSelectMenu.MakeImage(panel.transform, "Accent", RobotSelectMenu.HoloCyan);
        _accent.rectTransform.anchorMin = new Vector2(0f, 1f);
        _accent.rectTransform.anchorMax = new Vector2(1f, 1f);
        _accent.rectTransform.offsetMin = new Vector2(0f, -4f);
        _accent.rectTransform.offsetMax = Vector2.zero;

        _title = RobotSelectMenu.MakeText(panel.transform, "Title", "", 44,
            RobotSelectMenu.HoloCyan, FontStyle.Bold,
            new Vector2(0.5f, 1f), new Vector2(0f, -52f), new Vector2(700f, 60f));

        var viewGo = new GameObject("View");
        viewGo.transform.SetParent(panel.transform, false);
        var view = viewGo.AddComponent<RawImage>();
        view.texture = _texture;
        // Panel-local y runs -400..+400. Title sits at +318..+378, so the view
        // is centred just under it and the hint and buttons stack below.
        view.rectTransform.anchorMin = view.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        view.rectTransform.anchoredPosition = new Vector2(0f, 35f);
        view.rectTransform.sizeDelta = new Vector2(620f, 540f);
        viewGo.AddComponent<RobotInspectorDrag>().inspector = this;

        RobotSelectMenu.MakeText(panel.transform, "Hint", "DRAG  TO  ROTATE", 20,
            new Color(1f, 1f, 1f, 0.45f), FontStyle.Normal,
            new Vector2(0.5f, 0f), new Vector2(0f, 130f), new Vector2(500f, 30f));

        RobotSelectMenu.MakeButton(panel.transform, "SELECT", new Vector2(-130f, -340f),
            new Vector2(230f, 70f), 30, SelectCurrent);
        RobotSelectMenu.MakeButton(panel.transform, "CLOSE", new Vector2(130f, -340f),
            new Vector2(230f, 70f), 30, Close);

        _dialog.SetActive(false);
    }

    public void Open(int index, bool isCyan)
    {
        if (_roster == null || _roster.robots == null || index < 0 || index >= _roster.robots.Length)
            return;

        _index = index;
        _isCyan = isCyan;

        ClearModel();
        // Square on to the camera every time, rather than wherever the last
        // robot happened to have spun to.
        _turntable.localRotation = Quaternion.identity;
        _pendingDrag = 0f;

        var entry = _roster.robots[index];
        if (entry.modelPrefab != null)
        {
            _model = Instantiate(entry.modelPrefab, _turntable);
            RobotSelectMenu.NormalizeToCenter(_model, _turntable);
        }

        var teamColor = isCyan ? RobotSelectMenu.HoloCyan : RobotSelectMenu.HoloMagenta;
        _title.text = entry.displayName;
        _title.color = teamColor;
        _accent.color = teamColor;

        _dialog.SetActive(true);
        _camera.enabled = true;
    }

    public void Close()
    {
        _dialog.SetActive(false);
        _camera.enabled = false;
        ClearModel();
    }

    /// <summary>Horizontal pointer travel over the view, in pixels.</summary>
    public void Drag(float pixels)
    {
        _pendingDrag += pixels;
    }

    void SelectCurrent()
    {
        if (_isCyan) _state.cyanIndex = _index; else _state.magentaIndex = _index;
        _state.Refresh();
        Close();
    }

    void ClearModel()
    {
        if (_model == null)
            return;
        // Deactivate as well as destroy: Destroy only takes effect at the end of
        // the frame, and a swapped-to robot is instantiated before then.
        _model.SetActive(false);
        Destroy(_model);
        _model = null;
    }

    void Update()
    {
        if (_dialog == null || !_dialog.activeSelf)
            return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
            return;
        }

        if (Mathf.Abs(_pendingDrag) > 0.001f)
        {
            _turntable.Rotate(0f, -_pendingDrag * DegreesPerDragPixel, 0f);
            _pendingDrag = 0f;
        }
        else
        {
            _turntable.Rotate(0f, DegreesPerSecond * Time.deltaTime, 0f);
        }
    }

    void OnDestroy()
    {
        if (_camera != null)
            _camera.targetTexture = null;
        if (_texture != null)
        {
            _texture.Release();
            Destroy(_texture);
        }
    }
}

/// <summary>Relays drags on the inspector's view to the turntable.</summary>
public class RobotInspectorDrag : MonoBehaviour, UnityEngine.EventSystems.IDragHandler
{
    public RobotInspector inspector;

    public void OnDrag(UnityEngine.EventSystems.PointerEventData eventData)
    {
        if (inspector != null)
            inspector.Drag(eventData.delta.x);
    }
}

/// <summary>Releases preview render textures when the select screen closes.</summary>
public class RobotPreviewCleanup : MonoBehaviour
{
    public RenderTexture[] textures;
    public Camera[] cameras;

    void OnDestroy()
    {
        if (cameras != null)
            foreach (var cam in cameras)
                if (cam != null)
                    cam.targetTexture = null;
        if (textures != null)
            foreach (var rt in textures)
                if (rt != null)
                {
                    rt.Release();
                    Destroy(rt);
                }
    }
}
