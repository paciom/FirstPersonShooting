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

    /// <summary>
    /// What the preview cameras clear to, behind the robot. A neutral grey
    /// rather than the near-black this used to be: several robots are mostly
    /// dark plating, and their silhouettes disappeared into the background.
    ///
    /// Shared by the cards and the inspector so a robot reads the same in both.
    /// Safe to raise — these cameras render with post-processing off, so a
    /// lighter backdrop costs no bloom contrast on the cyan trim.
    /// </summary>
    internal static readonly Color PreviewBackdrop = new Color(0.29f, 0.30f, 0.32f, 1f);

    // Preview rigs live far below the arena so the tiny preview cameras
    // (short far plane) see nothing but their own robot.
    internal const float PreviewDepth = -150f;

    // How far the magenta rig set sits above the cyan one. Comfortably clear of
    // the 6-unit preview lights in both directions, and of the inspector rig
    // 60 below, so no team's lights reach another team's robots.
    const float TeamRigLift = 40f;

    // Usable width for a team's card row on the 1920-wide reference canvas,
    // leaving margins for the BACK button and screen edges.
    const float RowWidth = 1800f;

    // Vehicle height as a fraction of the robot's, for the preview cards only —
    // half what the arena uses. The rig cameras are framed on a robot, which is
    // tall and narrow, but a vehicle is long and low: matched on height it runs
    // out over the sides of the card. Gameplay keeps VehicleSkin's own default,
    // where the vehicle has the whole arena to sit in.
    const float PreviewVehicleHeight = 0.31f;

    // How the stop-motion is sized: the standing robot at one size, everything
    // it turns into at another.
    //
    // Stage one is fitted to RobotHeightFor(robot), so a robot with stages
    // stands the same height as its neighbours in the row — bar the one
    // deliberate outlier that helper documents. EVERY later stage shares
    // StageVehicleDiagonal.
    //
    // Sharing one size across the transforming stages rather than tapering into
    // it, because the mid-fold stages are the bulkiest boxes of the whole set —
    // a lunging pose with limbs spread measures larger than the standing robot
    // (bounding volume 1.59 against 1.47) while plainly not looking bigger. Any
    // taper anchored on the robot therefore leaves the middle looking inflated.
    // Holding them all at the vehicle size lands the entire transformation in a
    // tight band and the only size change is the first swap, where the robot
    // drops into a crouch and a drop in height is what the pose implies anyway.
    //
    // Measured on the DIAGONAL rather than the largest dimension, because Meshy
    // normalises every generated stage into a ~1.9 box: a nearly-cubic mid-fold
    // stage (1.90 x 1.43 x 1.78) and a thin robot (1.40 x 1.80 x 0.83) then
    // agree on one number while differing enormously in bulk, and bulk is what
    // the eye compares.
    // Internal because TransformCast's corner panel builds the same stop motion
    // from the same stages: a robot that folds at one set of proportions on its
    // card and another in the HUD reads as two different robots.
    // Tuned 2026-08-05: robots down 15% from the 1.6 they launched at and
    // vehicles up 15% from 1.45 — at equal prominence the tall robots read
    // fine and the low hulls read like toys.
    internal const float PreviewRobotHeight = 1.36f;
    internal const float StageVehicleDiagonal = 1.67f;

    // Ranger runs the other way, 15% up from the old 1.6: the thinnest
    // silhouette in the fleet reads smaller than every teammate when fitted
    // to the same height.
    const float RangerRobotHeight = 1.84f;

    /// <summary>
    /// Height the standing robot stage is fitted to on a card — and in
    /// TransformCast's corner panel, which must agree with the cards (see
    /// StageVehicleDiagonal's note there).
    /// </summary>
    internal static float RobotHeightFor(string robotName) =>
        robotName != null && robotName.Equals("ranger", System.StringComparison.OrdinalIgnoreCase)
            ? RangerRobotHeight
            : PreviewRobotHeight;

    // Generated stages come out of the image-to-3D pipeline nose-down -Z, so
    // they face away from the camera the robot faces. Stage one is the real rig
    // and is already correct, so only the generated stages are turned — the
    // same offset VehicleSkin.stageYawOffset applies in the arena, and the two
    // must agree or a robot faces one way on its card and the other in a match.
    internal const float StageYawOffset = 180f;

    public static GameObject Build(GameModeController controller, RobotRoster roster,
        GameMode pendingMode, int cyanIndex, int magentaIndex)
    {
        var root = new GameObject("RobotSelect");
        root.transform.SetParent(controller.transform, false);

        int count = roster.robots.Length;
        // One rig set per team rather than one shared set: the two rows have to
        // show the SAME robot in DIFFERENT paint, which a single render texture
        // cannot do.
        var cyanPreviews = new RenderTexture[count];
        var magentaPreviews = new RenderTexture[count];
        BuildPreviewRigs(root, roster, cyanPreviews, magentaPreviews);

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
            pendingMode == GameMode.AIvAI ? "AI  v  AI"
                : pendingMode == GameMode.Brawl ? "BRAWL"
                : pendingMode == GameMode.BrawlWar ? "BRAWL  —  AI  v  AI"
                : pendingMode == GameMode.BrawlShow ? "MARTIAL  ARTS  SHOW  —  CYAN  PERFORMS"
                : pendingMode == GameMode.TankRaid ? "TANK  RAID  —  CYAN  DRIVES"
                : pendingMode == GameMode.Dogfight ? "DOGFIGHT  —  CYAN  FLIES"
                : pendingMode == GameMode.DogfightWar ? "DOGFIGHT  —  AI  v  AI"
                : "PLAYER  v  AI", 26,
            new Color(1f, 1f, 1f, 0.55f), FontStyle.Normal,
            new Vector2(0.5f, 1f), new Vector2(0, -145), new Vector2(800, 40));

        var state = root.AddComponent<RobotSelectState>();
        state.cyanIndex = Mathf.Clamp(cyanIndex, 0, count - 1);
        state.magentaIndex = Mathf.Clamp(magentaIndex, 0, count - 1);

        // Created before the rows so their thumbnails can capture it, but its UI
        // is built last so the dialog draws over everything else on the canvas.
        var inspector = RobotInspector.Create(root, roster, state);

        BuildTeamRow(canvasGo.transform, "CYAN TEAM", HoloCyan, 130f, roster, cyanPreviews, state, true, inspector);
        BuildTeamRow(canvasGo.transform, "MAGENTA TEAM", HoloMagenta, -160f, roster, magentaPreviews, state, false, inspector);
        state.Refresh();

        MakeButton(canvasGo.transform, "START  MATCH", new Vector2(0, -350), new Vector2(420, 78), 34,
            () => controller.LaunchSelectedMatch(state.cyanIndex, state.magentaIndex));
        MakeButton(canvasGo.transform, "BACK", new Vector2(-560, -350), new Vector2(200, 78), 28,
            controller.CancelRobotSelect);

        // The stage gets its own SCREEN after this one; only the CPU level
        // is picked here.
        if (pendingMode == GameMode.Brawl || pendingMode == GameMode.BrawlWar)
            BuildLevelPicker(canvasGo.transform, pendingMode);

        // The two Gunfight modes pick how many robots a side as well as which
        // one — and the two Dogfight cards pick how many jets, off their own
        // store. Nothing else does: every other mode's cast is fixed by its
        // rules (two corners in a Brawl, one driver in Tank Raid).
        if (pendingMode == GameMode.AIvAI || pendingMode == GameMode.PlayerVsAI)
            BuildTeamSizePicker(canvasGo.transform, pendingMode == GameMode.PlayerVsAI);
        if (pendingMode == GameMode.Dogfight || pendingMode == GameMode.DogfightWar)
            BuildDogfightSizePicker(canvasGo.transform, pendingMode == GameMode.Dogfight);

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

    /// <summary>
    /// Builds both teams' rig sets. Each team gets its own copy of every robot,
    /// because a card has to show what the robot will actually look like on the
    /// field — and on the field the same robot on opposite teams is two
    /// different-coloured robots. Only the magenta set is repainted; cyan keeps
    /// the factory colours (see TeamPaint.FactoryTeam), so its rigs cost nothing
    /// beyond the camera that renders them.
    /// </summary>
    static void BuildPreviewRigs(GameObject root, RobotRoster roster,
        RenderTexture[] cyanPreviews, RenderTexture[] magentaPreviews)
    {
        var cameras = new System.Collections.Generic.List<Camera>();
        BuildTeamRigs(root, roster, cyanPreviews, "Cyan", HoloCyan, 0f, cameras);
        BuildTeamRigs(root, roster, magentaPreviews, "Magenta", HoloMagenta, TeamRigLift, cameras);

        var textures = new RenderTexture[cyanPreviews.Length + magentaPreviews.Length];
        cyanPreviews.CopyTo(textures, 0);
        magentaPreviews.CopyTo(textures, cyanPreviews.Length);

        var cleanup = root.AddComponent<RobotPreviewCleanup>();
        cleanup.textures = textures;
        cleanup.cameras = cameras.ToArray();
    }

    static void BuildTeamRigs(GameObject root, RobotRoster roster, RenderTexture[] previews,
        string teamName, Color teamColor, float lift,
        System.Collections.Generic.List<Camera> cameras)
    {
        var rigRoot = new GameObject($"PreviewRigs_{teamName}");
        rigRoot.transform.SetParent(root.transform, false);
        rigRoot.transform.position = new Vector3(0f, PreviewDepth + lift, 0f);

        int n = roster.robots.Length;
        for (int i = 0; i < n; i++)
        {
            var rig = new GameObject($"Rig_{roster.robots[i].displayName}");
            rig.transform.SetParent(rigRoot.transform, false);
            rig.transform.localPosition = new Vector3(i * 25f, 0f, 0f);

            previews[i] = new RenderTexture(256, 256, 16);

            var spin = new GameObject("Spin");
            spin.transform.SetParent(rig.transform, false);

            if (roster.robots[i].HasStages)
            {
                BuildStopMotion(spin.transform, roster.robots[i], i, n, teamColor);
            }
            else
            {
                var spinner = spin.AddComponent<RobotPreviewSpinner>();
                // Spread the fleet evenly around the cycle so the row always has
                // something mid-transformation rather than all nine snapping at once.
                spinner.phaseDegrees = n > 1 ? i * (360f / n) : 0f;
                if (roster.robots[i].modelPrefab != null)
                {
                    var model = Object.Instantiate(roster.robots[i].modelPrefab, spin.transform);
                    NormalizeToCenter(model, spin.transform);
                    TeamPaint.Apply(model, teamColor, TeamPaint.CardSize, false,
                        roster.robots[i].paintAnchorHue);
                }

                // Added after the model, so VehicleSkin's Start finds it to measure against.
                var skin = spin.AddComponent<VehicleSkin>();
                skin.holder = spin.transform;
                skin.vehiclePrefab = roster.robots[i].vehiclePrefab;
                skin.heightFraction = PreviewVehicleHeight;
                skin.tint = teamColor;
                skin.paintSize = TeamPaint.CardSize;
                skin.paintAnchorHue = roster.robots[i].paintAnchorHue;
            }

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
            cam.backgroundColor = PreviewBackdrop;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            cameras.Add(cam);

            // Rigs are 25 apart and the lights reach 6, so each one lights only
            // its own robot.
            AddThreePointLights(rig.transform);
        }
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

    /// <summary>
    /// Instantiates every transformation stage under one holder and drives them
    /// as stop motion — the tank set, then the jet set for robots that have
    /// one, so a card runs robot, tank, robot, jet, robot. See
    /// <see cref="StageVehicleDiagonal"/> for how stages are sized against
    /// each other.
    /// </summary>
    static void BuildStopMotion(Transform holder, RobotRoster.Entry entry, int index, int count,
        Color teamColor)
    {
        float robotHeight = RobotHeightFor(entry.displayName);
        var stages = BuildStageSet(holder, entry, entry.transformStages, teamColor, robotHeight,
            // Generated tank stages come out of the pipeline nose-down -Z, so
            // every stage past the real rig takes the same 180.
            _ => StageYawOffset);

        GameObject[] jets = null;
        if (entry.HasJetStages)
        {
            // The jet set splits differently: its early stages are still
            // robot-framed (yaw 0, like the rig) and only the aircraft-framed
            // tail takes the jet yaw. The split is JetPawn's own table, so the
            // card turns exactly the stages the DOGFIGHT sky turns.
            int robotFrames = JetPawn.JetRobotFramesFor(entry.displayName);
            jets = BuildStageSet(holder, entry, entry.jetStages, teamColor, robotHeight,
                s => s < robotFrames ? 0f : JetPawn.StageYaw);
        }

        var player = holder.gameObject.AddComponent<StopMotionTransformer>();
        player.stages = stages;
        player.jetStages = jets;
        // Spread the fleet across the cycle so the row is never in step.
        player.phaseSeconds = count > 1 ? index * (player.CycleSeconds / count) : 0f;
    }

    /// <summary>One stage set, instantiated, fitted and painted: the standing
    /// robot to <paramref name="robotHeight"/>, everything after it to
    /// <see cref="StageVehicleDiagonal"/>. <paramref name="paintSize"/> is the
    /// repaint budget for the FINISHED form; the in-between frames never take
    /// more than <see cref="TeamPaint.StageSize"/>, VehicleSkin's split, for
    /// VehicleSkin's reason — full-size copies of seven blink-long frames
    /// would cost more texture memory than the fleet.</summary>
    internal static GameObject[] BuildStageSet(Transform holder, RobotRoster.Entry entry,
        GameObject[] sources, Color teamColor, float robotHeight,
        System.Func<int, float> yawFor, int paintSize = TeamPaint.CardSize)
    {
        var stages = new GameObject[sources.Length];
        for (int s = 0; s < stages.Length; s++)
            if (sources[s] != null)
                stages[s] = Object.Instantiate(sources[s], holder);

        // Derive stage one's target from its own proportions rather than a fixed
        // number, so it lands on robotHeight for any robot regardless of how
        // tall or wide that particular rig happens to be.
        float robotDiagonal = StageVehicleDiagonal;
        var first = MeasureBounds(stages.Length > 0 ? stages[0] : null);
        if (first.size.y > 0.01f)
            robotDiagonal = first.size.magnitude * (robotHeight / first.size.y);

        for (int s = 0; s < stages.Length; s++)
        {
            if (stages[s] == null)
                continue;
            NormalizeByDiagonal(stages[s], holder,
                s == 0 ? robotDiagonal : StageVehicleDiagonal,
                s == 0 ? 0f : yawFor(s));
            // Every stage, not just the robot: a fold that starts cyan and ends
            // in the other team's tank would be worse than no paint at all.
            TeamPaint.Apply(stages[s], teamColor,
                s == stages.Length - 1
                    ? paintSize
                    : Mathf.Min(TeamPaint.Resolve(paintSize), TeamPaint.StageSize),
                false, entry.paintAnchorHue);
        }
        return stages;
    }

    /// <summary>Combined renderer bounds of an instance, or an empty box.</summary>
    internal static Bounds MeasureBounds(GameObject instance)
    {
        if (instance == null)
            return new Bounds(Vector3.zero, Vector3.zero);
        var renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(Vector3.zero, Vector3.zero);
        var bounds = renderers[0].bounds;
        foreach (var r in renderers)
            bounds.Encapsulate(r.bounds);
        return bounds;
    }

    /// <summary>Fits a model's bounding-box diagonal to <paramref name="target"/> and centers it.</summary>
    internal static void NormalizeByDiagonal(GameObject instance, Transform holder,
                                             float target, float yaw = 0f)
    {
        instance.name = "Stage";
        instance.transform.localPosition = Vector3.zero;
        // Yaw first: measuring after rotating is what keeps the centring honest,
        // since turning a model afterwards moves it off the offset solved for
        // its old orientation.
        instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        instance.transform.localScale = Vector3.one;

        var bounds = MeasureBounds(instance);
        if (bounds.size.sqrMagnitude < 1e-6f)
            return;

        float scale = target / Mathf.Max(0.01f, bounds.size.magnitude);
        instance.transform.localScale *= scale;
        Vector3 localCenter = holder.InverseTransformPoint(bounds.center);
        instance.transform.localPosition = -localCenter * scale;
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

    /// <summary>
    /// The CPU level chips, to the right of START: 1–5 with the chosen
    /// level's name above them. Remembered per mode (PlayerPrefs) — Player
    /// v AI opens on CADET, the AI war on CONTENDER, so exhibition bouts
    /// stay watchable instead of two perfect guards staring.
    /// </summary>
    static void BuildLevelPicker(Transform parent, GameMode mode)
    {
        var title = MakeText(parent, "LevelTitle", "", 18,
            new Color(1f, 1f, 1f, 0.55f), FontStyle.Bold,
            new Vector2(0.5f, 0.5f), new Vector2(385f, -312f), new Vector2(360f, 24f));

        var chips = new Image[BrawlDifficulty.Levels.Length];
        var labels = new Text[chips.Length];

        void Refresh()
        {
            int selected = BrawlDifficulty.For(mode);
            title.text = (mode == GameMode.BrawlWar ? "AI  LEVEL  —  " : "CPU  LEVEL  —  ")
                         + BrawlDifficulty.NameOf(selected);
            for (int i = 0; i < chips.Length; i++)
            {
                bool on = i + 1 == selected;
                chips[i].color = on
                    ? new Color(HoloCyan.r * 0.35f, HoloCyan.g * 0.35f, HoloCyan.b * 0.35f, 0.95f)
                    : CardColor;
                labels[i].color = on ? HoloCyan : new Color(1f, 1f, 1f, 0.55f);
            }
        }

        for (int i = 0; i < chips.Length; i++)
        {
            int level = i + 1;
            var chip = MakeImage(parent, $"Level_{level}", CardColor);
            var rect = chip.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(385f + (level - 3) * 68f, -352f);
            rect.sizeDelta = new Vector2(60f, 52f);
            chips[i] = chip;

            var button = chip.gameObject.AddComponent<Button>();
            button.targetGraphic = chip;
            button.onClick.AddListener(() =>
            {
                BrawlDifficulty.Set(mode, level);
                Refresh();
            });

            labels[i] = MakeText(chip.transform, "Label", level.ToString(), 26,
                Color.white, FontStyle.Bold,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56f, 48f));
        }
        Refresh();
    }

    /// <summary>
    /// How many robots a side, on the strip between the subtitle and the cyan
    /// row: the four matchups worth one click each, then a box for any other
    /// number.
    ///
    /// The box and the chips are one setting shown twice, so they stay in step
    /// — a preset empties the box back to its placeholder, and a typed number
    /// puts the chips out. Only the chips are redrawn while the box is being
    /// typed into: rewriting a field's text under a live caret moves the caret
    /// to the end, which turns typing "10" into a fight with the cursor.
    ///
    /// The line on the right says what the number actually fields, because in
    /// Player v AI it is not obvious — the player is one of their team's
    /// robots, so 4 v 4 gives them three allies rather than four.
    /// </summary>
    static void BuildTeamSizePicker(Transform parent, bool playerPlays) =>
        BuildSizePicker(parent, "ROBOTS  PER  TEAM", TeamSize.Presets,
            TeamSize.Min, TeamSize.Max,
            () => TeamSize.PerTeam, size => TeamSize.PerTeam = size,
            TeamSize.IsPreset, size => TeamSize.Describe(size, playerPlays));

    /// <summary>DOGFIGHT's row: same control, its own store — presets 1/2/4
    /// and a lower ceiling, because a jet is a heavier thing than a robot.</summary>
    static void BuildDogfightSizePicker(Transform parent, bool playerPlays) =>
        BuildSizePicker(parent, "JETS  PER  TEAM", DogfightTeamSize.Presets,
            DogfightTeamSize.Min, DogfightTeamSize.Max,
            () => DogfightTeamSize.PerTeam, size => DogfightTeamSize.PerTeam = size,
            DogfightTeamSize.IsPreset, size => TeamSize.Describe(size, playerPlays));

    /// <summary>
    /// The size row itself: preset chips, the OTHER box, the range hint and
    /// the spelled-out summary. Which numbers those are — and where the
    /// chosen one lives — belongs to the caller; two mode families share
    /// this without sharing a store.
    /// </summary>
    static void BuildSizePicker(Transform parent, string caption, int[] presets,
        int min, int max, System.Func<int> get, System.Action<int> set,
        System.Func<int, bool> isPreset, System.Func<int, string> describe)
    {
        const float RowY = 350f;
        const float ChipPitch = 100f, FirstChipX = -440f;

        var chips = new Image[presets.Length];
        var labels = new Text[chips.Length];
        InputField box = null;
        Text summary = null;
        bool echoing = false;

        var selectedTint = new Color(HoloCyan.r * 0.35f, HoloCyan.g * 0.35f, HoloCyan.b * 0.35f, 0.95f);

        void RefreshChips()
        {
            int size = get();
            for (int i = 0; i < chips.Length; i++)
            {
                bool on = presets[i] == size;
                chips[i].color = on ? selectedTint : CardColor;
                labels[i].color = on ? HoloCyan : new Color(1f, 1f, 1f, 0.55f);
            }
            summary.text = describe(size);
        }

        void Refresh()
        {
            RefreshChips();
            // The box carries the number only while it is the one in charge; on
            // a preset it drops back to its placeholder.
            echoing = true;
            int size = get();
            box.text = isPreset(size) ? "" : size.ToString();
            echoing = false;
        }

        var captionText = MakeText(parent, "TeamSizeLabel", caption, 18,
            new Color(1f, 1f, 1f, 0.55f), FontStyle.Bold,
            new Vector2(0.5f, 0.5f), new Vector2(-640f, RowY), new Vector2(220f, 26f));
        captionText.alignment = TextAnchor.MiddleRight;

        for (int i = 0; i < chips.Length; i++)
        {
            int size = presets[i];
            var chip = MakeImage(parent, $"TeamSize_{size}", CardColor);
            var rect = chip.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(FirstChipX + i * ChipPitch, RowY);
            rect.sizeDelta = new Vector2(92f, 46f);
            chips[i] = chip;

            var button = chip.gameObject.AddComponent<Button>();
            button.targetGraphic = chip;
            button.onClick.AddListener(() =>
            {
                set(size);
                Refresh();
            });

            labels[i] = MakeText(chip.transform, "Label", TeamSize.Matchup(size), 19,
                Color.white, FontStyle.Bold,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(88f, 42f));
            // "10  v  10" is the widest label and sits within a hair of the
            // chip; wrapped instead of overflowed it would break onto two lines
            // and only that one chip would look wrong.
            labels[i].horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        // The row packs left as the preset count shrinks, so a three-chip
        // deck and a four-chip deck both end flush against the OTHER box.
        float boxX = FirstChipX + presets.Length * ChipPitch + 30f;
        box = BuildAmountBox(parent, new Vector2(boxX, RowY), max);
        MakeText(parent, "TeamSizeRange", $"ANY  {min} – {max}", 15,
            new Color(1f, 1f, 1f, 0.38f), FontStyle.Normal,
            new Vector2(0.5f, 0.5f), new Vector2(boxX + 140f, RowY), new Vector2(160f, 24f));

        summary = MakeText(parent, "TeamSizeSummary", "", 20, HoloCyan, FontStyle.Bold,
            new Vector2(0.5f, 0.5f), new Vector2(boxX + 480f, RowY), new Vector2(500f, 46f));
        summary.alignment = TextAnchor.MiddleLeft;

        box.onValueChanged.AddListener(typed =>
        {
            if (echoing)
                return;
            // Out-of-range and half-typed entries are ignored rather than
            // clamped: clamping mid-keystroke rewrites the field, and "1" on
            // the way to "16" is not a request for a 1 v 1.
            if (int.TryParse(typed, out int size) && size >= min && size <= max)
            {
                set(size);
                RefreshChips();
            }
        });
        // Leaving the box is where anything unusable snaps back to the truth:
        // an emptied field, or a number past the ceiling.
        box.onEndEdit.AddListener(typed =>
        {
            if (int.TryParse(typed, out int size))
                set(size);
            Refresh();
        });

        Refresh();
    }

    /// <summary>The type-a-number box, built the way MainMenu builds its map-code field.</summary>
    static InputField BuildAmountBox(Transform parent, Vector2 position, int max)
    {
        var frame = MakeImage(parent, "TeamSizeBox", new Color(0.04f, 0.09f, 0.15f, 0.95f));
        var rect = frame.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(104f, 46f);

        var underline = MakeImage(frame.transform, "Underline", new Color(HoloCyan.r, HoloCyan.g, HoloCyan.b, 0.55f));
        underline.rectTransform.anchorMin = new Vector2(0f, 0f);
        underline.rectTransform.anchorMax = new Vector2(1f, 0f);
        underline.rectTransform.offsetMin = new Vector2(6f, 0f);
        underline.rectTransform.offsetMax = new Vector2(-6f, 2f);

        var text = MakeText(frame.transform, "Text", "", 22, Color.white, FontStyle.Bold,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(92f, 40f));
        var placeholder = MakeText(frame.transform, "Placeholder", "OTHER", 18,
            new Color(1f, 1f, 1f, 0.30f), FontStyle.Italic,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(92f, 40f));

        var field = frame.gameObject.AddComponent<InputField>();
        field.targetGraphic = frame;
        field.textComponent = text;
        field.placeholder = placeholder;
        field.characterLimit = max.ToString().Length;
        field.contentType = InputField.ContentType.IntegerNumber;
        return field;
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
/// No VehicleSkin and no self-running transformation cycle — this is for
/// studying a form while it holds still. Instead the transformation is YOURS
/// to drive: a scrub bar under the turntable steps through every stage of
/// robot, tank, robot, jet, robot, holding whichever one you stop on. The
/// right pane plays the same journey as film: the generated forward clips and
/// their reversed twins chained end to end.
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
    RawImage _clipView;
    Text _clipMissing;
    RenderTexture _videoTexture;
    UnityEngine.Video.VideoPlayer _video;
    int _index;
    bool _isCyan;
    float _pendingDrag;

    // The scrub bar's world: stage instances under the turntable, and the
    // display list the slider steps through. A state is just "these renderers
    // on, everything else off"; the rig's renderer set opens, punctuates and
    // closes the list, so every robot pause is the live animated rig.
    GameObject[] _tankStages;
    GameObject[] _jetStages;
    System.Collections.Generic.List<Renderer[]> _scrubStates;
    Renderer[] _scrubShown;
    Slider _slider;
    Image _sliderFill;
    Image _sliderHandle;

    // The right pane's playlist: forward clip and reversed twin per form, so
    // the film runs robot, tank, robot, jet, robot and hands back to the top.
    // A null clip means "streamed by name from StreamingAssets", which is
    // where the reversed twins live — matched by name rather than serialized
    // so the roster does not have to be rebuilt into the scene to gain them.
    readonly System.Collections.Generic.List<(UnityEngine.Video.VideoClip clip, string file)>
        _videoChain = new System.Collections.Generic.List<(UnityEngine.Video.VideoClip, string)>();
    int _segment;

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

        // Square, matching the generated clips. Muted: this is a silent
        // illustration sitting next to a turntable, not a cutscene. Not
        // looping — the player runs a PLAYLIST (robot to tank, back, to jet,
        // back), so the end of each clip hands off to the next and the last
        // hands back to the first.
        _videoTexture = new RenderTexture(768, 768, 0);
        var videoGo = new GameObject("TransformVideo");
        videoGo.transform.SetParent(root, false);
        _video = videoGo.AddComponent<UnityEngine.Video.VideoPlayer>();
        _video.playOnAwake = false;
        _video.isLooping = false;
        _video.renderMode = UnityEngine.Video.VideoRenderMode.RenderTexture;
        _video.targetTexture = _videoTexture;
        _video.audioOutputMode = UnityEngine.Video.VideoAudioOutputMode.None;
        _video.waitForFirstFrame = true;
        _video.loopPointReached += _ =>
        {
            if (_videoChain.Count > 0 && _dialog != null && _dialog.activeSelf)
                PlaySegment((_segment + 1) % _videoChain.Count);
        };

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
        _camera.backgroundColor = RobotSelectMenu.PreviewBackdrop;
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
        // Wide enough for two panes: the live 3D on the left, the generated
        // transformation clip on the right. Panel-local y runs -410..+410.
        panel.rectTransform.sizeDelta = new Vector2(1280f, 820f);

        _accent = RobotSelectMenu.MakeImage(panel.transform, "Accent", RobotSelectMenu.HoloCyan);
        _accent.rectTransform.anchorMin = new Vector2(0f, 1f);
        _accent.rectTransform.anchorMax = new Vector2(1f, 1f);
        _accent.rectTransform.offsetMin = new Vector2(0f, -4f);
        _accent.rectTransform.offsetMax = Vector2.zero;

        _title = RobotSelectMenu.MakeText(panel.transform, "Title", "", 44,
            RobotSelectMenu.HoloCyan, FontStyle.Bold,
            new Vector2(0.5f, 1f), new Vector2(0f, -52f), new Vector2(700f, 60f));

        // Left pane: the live turntable you can drag.
        var viewGo = new GameObject("View");
        viewGo.transform.SetParent(panel.transform, false);
        var view = viewGo.AddComponent<RawImage>();
        view.texture = _texture;
        view.rectTransform.anchorMin = view.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        view.rectTransform.anchoredPosition = new Vector2(-305f, 55f);
        view.rectTransform.sizeDelta = new Vector2(580f, 500f);
        viewGo.AddComponent<RobotInspectorDrag>().inspector = this;

        // Right pane: the transformation clip the stages were sampled from. The
        // cards show the stop-motion rebuild; here there is room for the real
        // thing, which is smooth and sells the idea far better.
        var clipGo = new GameObject("Clip");
        clipGo.transform.SetParent(panel.transform, false);
        _clipView = clipGo.AddComponent<RawImage>();
        _clipView.texture = _videoTexture;
        _clipView.rectTransform.anchorMin = _clipView.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        _clipView.rectTransform.anchoredPosition = new Vector2(305f, 55f);
        _clipView.rectTransform.sizeDelta = new Vector2(580f, 500f);

        _clipMissing = RobotSelectMenu.MakeText(panel.transform, "ClipMissing",
            "NO  TRANSFORMATION  CLIP", 22, new Color(1f, 1f, 1f, 0.30f), FontStyle.Normal,
            new Vector2(0.5f, 0.5f), new Vector2(305f, 55f), new Vector2(520f, 40f));

        // The scrub bar, tucked between the turntable and its caption: one
        // notch per stage of robot, tank, robot, jet, robot. Hidden in Open()
        // for robots with no stages.
        BuildScrubSlider(panel.transform, new Vector2(-305f, -212f), new Vector2(540f, 22f));

        RobotSelectMenu.MakeText(panel.transform, "LeftCaption",
            "DRAG  TO  ROTATE   ·   SLIDE  TO  TRANSFORM", 22,
            new Color(1f, 1f, 1f, 0.45f), FontStyle.Normal,
            new Vector2(0.5f, 0.5f), new Vector2(-305f, -248f), new Vector2(560f, 30f));
        RobotSelectMenu.MakeText(panel.transform, "RightCaption", "TRANSFORMATION", 22,
            new Color(1f, 1f, 1f, 0.45f), FontStyle.Normal,
            new Vector2(0.5f, 0.5f), new Vector2(305f, -225f), new Vector2(560f, 30f));

        RobotSelectMenu.MakeButton(panel.transform, "SELECT", new Vector2(-150f, -330f),
            new Vector2(250f, 70f), 30, SelectCurrent);
        RobotSelectMenu.MakeButton(panel.transform, "CLOSE", new Vector2(150f, -330f),
            new Vector2(250f, 70f), 30, Close);

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

        var teamColor = isCyan ? RobotSelectMenu.HoloCyan : RobotSelectMenu.HoloMagenta;

        var entry = _roster.robots[index];
        if (entry.modelPrefab != null)
        {
            _model = Instantiate(entry.modelPrefab, _turntable);
            RobotSelectMenu.NormalizeToCenter(_model, _turntable);
            // Full size, not the card's: this is the pane you open to judge how
            // a robot is painted, so it gets the same repaint the arena does.
            TeamPaint.Apply(_model, teamColor);
        }

        _title.text = entry.displayName;
        _title.color = teamColor;
        _accent.color = teamColor;
        _sliderFill.color = new Color(teamColor.r, teamColor.g, teamColor.b, 0.55f);
        _sliderHandle.color = Color.Lerp(Color.white, teamColor, 0.35f);

        BuildScrubStages(entry, teamColor);

        // The playlist: each form's forward clip and its reversed twin, so the
        // film runs the same journey as the slider — robot, tank, robot, jet,
        // robot. A robot not yet through a pipeline just plays the pairs it
        // has; one through neither gets the placeholder rather than a frozen
        // frame of the previous robot's clip.
        _videoChain.Clear();
        if (entry.transformVideo != null)
        {
            _videoChain.Add((entry.transformVideo, entry.transformVideo.name));
            _videoChain.Add((null, entry.transformVideo.name + "-back"));
        }
        if (entry.jetVideo != null)
        {
            _videoChain.Add((entry.jetVideo, entry.jetVideo.name));
            _videoChain.Add((null, entry.jetVideo.name + "-back"));
        }
        bool hasClip = _videoChain.Count > 0;
        _clipView.enabled = hasClip;
        _clipMissing.enabled = !hasClip;
        if (hasClip)
            PlaySegment(0);
        else
            _video.Stop();

        _dialog.SetActive(true);
        _camera.enabled = true;
    }

    /// <summary>
    /// Starts one playlist entry in whichever way the current player supports.
    ///
    /// WebGL cannot play VideoClip assets at all — the build strips the file to a
    /// stub and the player renders black, with the asset reference still non-null
    /// so nothing here looks wrong. The browser has to stream the same mp4 by URL
    /// instead, which is why a copy lives in StreamingAssets. Everywhere else an
    /// embedded clip is the simpler thing and stays — except the reversed clips,
    /// which have no serialized field ANYWHERE and stream by name on every
    /// platform, so the roster does not have to be rebuilt into the scene to
    /// gain them. Their files sit in StreamingAssets beside the forward copies.
    /// </summary>
    void PlaySegment(int index)
    {
        _segment = index;
        var segment = _videoChain[index];
#if UNITY_WEBGL && !UNITY_EDITOR
        _video.source = UnityEngine.Video.VideoSource.Url;
        _video.url = $"{Application.streamingAssetsPath}/{segment.file}.mp4";
#else
        if (segment.clip != null)
        {
            _video.source = UnityEngine.Video.VideoSource.VideoClip;
            _video.clip = segment.clip;
        }
        else
        {
            _video.source = UnityEngine.Video.VideoSource.Url;
            _video.url = $"{Application.streamingAssetsPath}/{segment.file}.mp4";
        }
#endif
        _video.Play();
    }

    /// <summary>
    /// The stage scrub bar: a uGUI Slider built from plain coloured images the
    /// way everything on this canvas is, one whole number per display state.
    /// The fill and handle take the team's colour in Open(), like the title.
    /// </summary>
    void BuildScrubSlider(Transform parent, Vector2 position, Vector2 size)
    {
        var track = RobotSelectMenu.MakeImage(parent, "ScrubBar",
            new Color(0.03f, 0.07f, 0.12f, 0.95f));
        var rect = track.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        // Fill and handle runs are inset by half the handle so the handle's
        // CENTRE spans the track and its ends never hang past the corners.
        var fillArea = new GameObject("FillArea").AddComponent<RectTransform>();
        fillArea.SetParent(track.transform, false);
        fillArea.anchorMin = Vector2.zero;
        fillArea.anchorMax = Vector2.one;
        fillArea.offsetMin = new Vector2(11f, 5f);
        fillArea.offsetMax = new Vector2(-11f, -5f);

        _sliderFill = RobotSelectMenu.MakeImage(fillArea, "Fill", RobotSelectMenu.HoloCyan);
        var fillRect = _sliderFill.rectTransform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        var handleArea = new GameObject("HandleArea").AddComponent<RectTransform>();
        handleArea.SetParent(track.transform, false);
        handleArea.anchorMin = Vector2.zero;
        handleArea.anchorMax = Vector2.one;
        handleArea.offsetMin = new Vector2(11f, 0f);
        handleArea.offsetMax = new Vector2(-11f, 0f);

        _sliderHandle = RobotSelectMenu.MakeImage(handleArea, "Handle", Color.white);
        _sliderHandle.rectTransform.sizeDelta = new Vector2(22f, 32f);

        _slider = track.gameObject.AddComponent<Slider>();
        _slider.targetGraphic = _sliderHandle;
        _slider.fillRect = fillRect;
        _slider.handleRect = _sliderHandle.rectTransform;
        _slider.minValue = 0f;
        _slider.maxValue = 1f;
        _slider.wholeNumbers = true;
        _slider.onValueChanged.AddListener(value => ApplyScrub(Mathf.RoundToInt(value)));
    }

    /// <summary>
    /// Instantiates one robot's scrub stages and rebuilds the display list the
    /// slider walks: rig, tank fold out and back, rig, jet fold out and back,
    /// rig. Hidden entirely for robots that have no stages.
    /// </summary>
    void BuildScrubStages(RobotRoster.Entry entry, Color teamColor)
    {
        bool hasStages = entry.HasStages && _model != null;
        _slider.gameObject.SetActive(hasStages);
        if (!hasStages)
            return;

        // Fitted against the SAME height the rig is normalised to, so the
        // first notch off the rig changes pose, not size. Full paint budget
        // for the finished forms — this is the pane you open to judge paint.
        _tankStages = RobotSelectMenu.BuildStageSet(_turntable, entry, entry.transformStages,
            teamColor, 1.6f, _ => RobotSelectMenu.StageYawOffset, TeamPaint.DefaultSize);
        if (entry.HasJetStages)
        {
            // The jet set's frame split and craft yaw are JetPawn's own — the
            // same stages turn the same way here as in the DOGFIGHT sky.
            int robotFrames = JetPawn.JetRobotFramesFor(entry.displayName);
            _jetStages = RobotSelectMenu.BuildStageSet(_turntable, entry, entry.jetStages,
                teamColor, 1.6f, s => s < robotFrames ? 0f : JetPawn.StageYaw,
                TeamPaint.DefaultSize);
        }

        var rig = _model.GetComponentsInChildren<Renderer>(true);
        _scrubStates = new System.Collections.Generic.List<Renderer[]> { rig };
        AppendFold(_tankStages, rig);
        if (_jetStages != null)
            AppendFold(_jetStages, rig);

        // Value first, silently, THEN the range: the maxValue setter re-clamps
        // and re-Sets the current value, and a leftover value from the last
        // robot would fire ApplyScrub into a half-built display list.
        _slider.SetValueWithoutNotify(0f);
        _slider.maxValue = _scrubStates.Count - 1;
        _scrubShown = rig;
    }

    /// <summary>
    /// Appends one set's out-and-back walk: its stages forward to the finished
    /// form, back down again, then the rig. The set's own stage one — a static
    /// copy of the robot — is switched off here and never listed; every robot
    /// pause on the bar is the live rig.
    /// </summary>
    void AppendFold(GameObject[] stages, Renderer[] rig)
    {
        var renderers = new Renderer[stages.Length][];
        for (int s = 0; s < stages.Length; s++)
        {
            renderers[s] = stages[s] != null
                ? stages[s].GetComponentsInChildren<Renderer>(true)
                : new Renderer[0];
            foreach (var r in renderers[s])
                r.enabled = false;
        }
        for (int s = 1; s < stages.Length; s++)
            _scrubStates.Add(renderers[s]);
        for (int s = stages.Length - 2; s >= 1; s--)
            _scrubStates.Add(renderers[s]);
        _scrubStates.Add(rig);
    }

    void ApplyScrub(int state)
    {
        if (_scrubStates == null || _scrubStates.Count == 0)
            return;
        var target = _scrubStates[Mathf.Clamp(state, 0, _scrubStates.Count - 1)];
        // Reference compare on purpose: a stage sits in the list twice (out and
        // back) and the rig three times, all as the same array.
        if (target == _scrubShown)
            return;
        if (_scrubShown != null)
            foreach (var r in _scrubShown)
                if (r != null)
                    r.enabled = false;
        foreach (var r in target)
            if (r != null)
                r.enabled = true;
        _scrubShown = target;
    }

    public void Close()
    {
        _dialog.SetActive(false);
        _camera.enabled = false;
        // Decoding a clip nobody can see is pure cost.
        if (_video != null)
            _video.Stop();
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
        DestroySet(_tankStages);
        DestroySet(_jetStages);
        _tankStages = null;
        _jetStages = null;
        _scrubStates = null;
        _scrubShown = null;

        if (_model == null)
            return;
        // Deactivate as well as destroy: Destroy only takes effect at the end of
        // the frame, and a swapped-to robot is instantiated before then.
        _model.SetActive(false);
        Destroy(_model);
        _model = null;
    }

    static void DestroySet(GameObject[] stages)
    {
        if (stages == null)
            return;
        foreach (var stage in stages)
            if (stage != null)
            {
                // Same deactivate-then-destroy the model gets, same reason.
                stage.SetActive(false);
                Destroy(stage);
            }
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
        if (_video != null)
            _video.targetTexture = null;
        if (_videoTexture != null)
        {
            _videoTexture.Release();
            Destroy(_videoTexture);
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

        // Card-sized repaints exist only for this screen — eighteen robots'
        // worth of them, against the two the arena actually needs.
        TeamPaint.Release(TeamPaint.CardSize);
    }
}
