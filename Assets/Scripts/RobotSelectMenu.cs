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
    static readonly Color HoloCyan = new Color(0.2f, 0.9f, 1f);
    static readonly Color HoloMagenta = new Color(1f, 0.25f, 0.9f);
    static readonly Color CardColor = new Color(0.06f, 0.14f, 0.22f, 0.95f);

    // Preview rigs live far below the arena so the tiny preview cameras
    // (short far plane) see nothing but their own robot.
    const float PreviewDepth = -150f;

    // Usable width for a team's card row on the 1920-wide reference canvas,
    // leaving margins for the BACK button and screen edges.
    const float RowWidth = 1800f;

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

        BuildTeamRow(canvasGo.transform, "CYAN TEAM", HoloCyan, 130f, roster, previews, state, true);
        BuildTeamRow(canvasGo.transform, "MAGENTA TEAM", HoloMagenta, -160f, roster, previews, state, false);
        state.Refresh();

        MakeButton(canvasGo.transform, "START  MATCH", new Vector2(0, -350), new Vector2(420, 78), 34,
            () => controller.LaunchSelectedMatch(state.cyanIndex, state.magentaIndex));
        MakeButton(canvasGo.transform, "BACK", new Vector2(-560, -350), new Vector2(200, 78), 28,
            controller.CancelRobotSelect);

        return root;
    }

    static void BuildTeamRow(Transform parent, string header, Color teamColor, float rowY,
        RobotRoster roster, RenderTexture[] previews, RobotSelectState state, bool isCyan)
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
                cardWidth, teamColor, out cards[i], out bars[i]);
            int index = i;
            cards[i].GetComponent<Button>().onClick.AddListener(() =>
            {
                if (isCyan) state.cyanIndex = index; else state.magentaIndex = index;
                state.Refresh();
            });
        }

        if (isCyan) { state.cyanCards = cards; state.cyanBars = bars; state.cyanColor = teamColor; }
        else { state.magentaCards = cards; state.magentaBars = bars; state.magentaColor = teamColor; }
    }

    static void MakeCard(Transform parent, string label, RenderTexture preview, Vector2 position,
        float width, Color teamColor, out Image background, out Image selectionBar)
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
        }

        var cleanup = root.AddComponent<RobotPreviewCleanup>();
        cleanup.textures = previews;
        cleanup.cameras = cams;
    }

    /// <summary>Scales a preview model to ~1.6 units tall and centers it on its holder.</summary>
    static void NormalizeToCenter(GameObject instance, Transform holder)
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

    static void MakeButton(Transform parent, string label, Vector2 position, Vector2 size,
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

    static Image MakeImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    static Text MakeText(Transform parent, string name, string content, int size, Color color,
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

    static void Stretch(RectTransform rect)
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

    Animator _animator;
    bool _canTransform;
    float _turned;
    bool _vehicle;
    Vector3 _restPosition;
    float _lift;

    // Start, not Awake: AddComponent runs Awake immediately, which is before
    // the caller has set phaseDegrees and before the model has been parented
    // under us — an Awake lookup finds no Animator at all.
    void Start()
    {
        _restPosition = transform.localPosition;
        _turned = phaseDegrees;

        _animator = GetComponentInChildren<Animator>();
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
