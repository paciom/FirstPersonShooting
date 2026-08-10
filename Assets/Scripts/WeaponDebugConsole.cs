using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Weapon debug console: during play, hold CTRL and type a two-digit number
/// (01–54) and EVERY robot locks to that weapon (the player's active weapon is
/// set to it too, so you can inspect it first-person). A top-left overlay
/// shows the weapon's number, name, and description. Ctrl-00 releases the
/// lock and returns the AI to normal weapon switching.
///
/// CTRL is what keeps this out of the player's way — bare number keys are the
/// shortcut bar now. See the note in Update for what it cost before.
///
/// Self-bootstraps on play — no scene wiring or arena rebuild required.
/// </summary>
public class WeaponDebugConsole : MonoBehaviour
{
    public static WeaponDebugConsole Instance { get; private set; }

    /// <summary>True between the first and second digit of an entry (PlayerBrain pauses digit switching).</summary>
    public bool AwaitingSecondDigit => _pendingDigit >= 0;

    const float SecondDigitWindow = 2f;

    int _pendingDigit = -1;
    float _pendingDeadline;
    float _hideMessageAt = float.PositiveInfinity;

    GameObject _panelGo;
    Text _titleText;
    Text _bodyText;
    Text _fpsText;
    float _smoothedDelta = 1f / 60f;
    float _nextFpsRefresh;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (UnattendedRender.Active)
            return;
        if (Instance == null)
            new GameObject("WeaponDebugConsole").AddComponent<WeaponDebugConsole>();
    }

    void Awake()
    {
        Instance = this;
        BuildUi();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        UpdateFps();

        // Capture timeout.
        if (_pendingDigit >= 0 && Time.unscaledTime > _pendingDeadline)
        {
            _pendingDigit = -1;
            _hideMessageAt = Time.unscaledTime;   // drop the "type second digit" hint
        }

        if (Time.unscaledTime >= _hideMessageAt)
        {
            _panelGo.SetActive(false);
            _hideMessageAt = float.PositiveInfinity;
        }

        int digit = ReadDigit();
        if (digit < 0)
            return;

        if (_pendingDigit < 0)
        {
            // CTRL to OPEN an entry. Bare number keys belong to the player.
            //
            // This console used to take any digit, any time, which was fine when
            // the number keys did almost nothing for the player — two basics and
            // an airdrop. They are now the primary weapon control (the four-gun
            // shortcut bar), and the collision was silently eating them:
            //
            //   * the first press only reached PlayerBrain if PlayerBrain's
            //     Update happened to run before this one, and NEITHER declares
            //     an execution order — so whether a key worked at all came down
            //     to arbitrary component ordering, which is exactly the shape of
            //     "sometimes I can't change weapons";
            //   * once a digit was captured, PlayerBrain stood down for the full
            //     two-second window, so any second press inside it was eaten
            //     too — and typing 1 then 2 quickly did not select two weapons,
            //     it locked EVERY robot in the match onto weapon 12.
            //
            // Only the opening digit is gated: after that the player has plainly
            // asked for the console, and the prompt says to type the second.
            if (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl))
                return;

            _pendingDigit = digit;
            _pendingDeadline = Time.unscaledTime + SecondDigitWindow;
            ShowMessage($"WEAPON {digit}_", "type the second digit…", sticky: true);
        }
        else
        {
            int number = _pendingDigit * 10 + digit;
            _pendingDigit = -1;
            ApplyNumber(number);
        }
    }

    void UpdateFps()
    {
        // Exponential smoothing so the number is readable, refreshed 4x/s.
        _smoothedDelta = Mathf.Lerp(_smoothedDelta, Time.unscaledDeltaTime, 0.08f);
        if (Time.unscaledTime < _nextFpsRefresh || _fpsText == null)
            return;
        _nextFpsRefresh = Time.unscaledTime + 0.25f;

        float fps = 1f / Mathf.Max(_smoothedDelta, 0.0001f);
        _fpsText.text = $"{fps:0} FPS";
        _fpsText.color = fps >= 50f ? new Color(0.3f, 1f, 0.5f)
            : fps >= 30f ? new Color(1f, 0.85f, 0.3f)
            : new Color(1f, 0.35f, 0.3f);
    }

    static int ReadDigit()
    {
        for (int i = 0; i <= 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha0 + i) || Input.GetKeyDown(KeyCode.Keypad0 + i))
                return i;
        }
        return -1;
    }

    void ApplyNumber(int number)
    {
        // Characters normally only carry two basics and whatever they've won off
        // an airdrop, so the console grants the requested weapon through each
        // WeaponLoadout (indefinitely) before locking everyone onto it. That
        // keeps all 54 numbers testable without giving robots the full arsenal.
        var loadouts = FindObjectsByType<WeaponLoadout>(FindObjectsSortMode.None);

        if (number == 0)
        {
            foreach (var loadout in loadouts)
                loadout.ClearSpecial();
            foreach (var bot in FindObjectsByType<AIBrain>(FindObjectsSortMode.None))
                bot.SetForcedWeapon(-1);
            PlayerBrain.Local?.ForceWeapon(0);
            ShowMessage("AI WEAPON LOCK RELEASED",
                "robots are back to their two basics and whatever they can grab", sticky: false);
            return;
        }

        // Weapon numbers are 1-based over the shared arsenal order (same on every character).
        int index = number - 1;
        int count = 0;
        foreach (var loadout in loadouts)
            count = Mathf.Max(count, loadout.TotalWeapons);

        if (index >= count)
        {
            ShowMessage($"NO WEAPON #{number:00}", $"valid numbers are 01–{count:00} (00 releases the lock)", sticky: false);
            return;
        }

        Weapon sample = null;
        foreach (var loadout in loadouts)
        {
            loadout.ClearSpecial();
            var granted = loadout.Grant(index, float.PositiveInfinity);
            if (granted == null)
                continue;
            if (sample == null)
                sample = granted;

            int slot = loadout.SlotOf(granted);
            loadout.GetComponent<AIBrain>()?.SetForcedWeapon(slot);
            // The player gets it too, so it can be inspected first-person.
            loadout.GetComponent<PlayerBrain>()?.ForceWeapon(slot);
        }

        string weaponName = sample != null ? sample.weaponName : $"weapon {number:00}";
        string description = sample != null && Descriptions.TryGetValue(sample.GetType().Name, out var d)
            ? d
            : "(no description on file)";
        ShowMessage($"#{number:00}  {weaponName.ToUpperInvariant()}",
            $"{description}\nall robots locked to this weapon · type 00 to release", sticky: true);
    }

    void ShowMessage(string title, string body, bool sticky)
    {
        _panelGo.SetActive(true);
        _titleText.text = title;
        _bodyText.text = body;
        _hideMessageAt = sticky ? float.PositiveInfinity : Time.unscaledTime + 3.5f;
    }

    void BuildUi()
    {
        var canvasGo = new GameObject("WeaponDebugCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;   // above HUD and menu
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        _panelGo = new GameObject("Panel");
        _panelGo.transform.SetParent(canvasGo.transform, false);
        var bg = _panelGo.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.6f);
        var rect = bg.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(18, -18);
        rect.sizeDelta = new Vector2(640, 110);

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var titleGo = new GameObject("Title");
        titleGo.transform.SetParent(_panelGo.transform, false);
        _titleText = titleGo.AddComponent<Text>();
        _titleText.font = font;
        _titleText.fontSize = 28;
        _titleText.fontStyle = FontStyle.Bold;
        _titleText.alignment = TextAnchor.UpperLeft;
        _titleText.color = new Color(0.2f, 0.9f, 1f);
        var titleRect = _titleText.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0f, 1f);
        titleRect.anchoredPosition = new Vector2(14, -10);
        titleRect.sizeDelta = new Vector2(-28, 34);

        var bodyGo = new GameObject("Body");
        bodyGo.transform.SetParent(_panelGo.transform, false);
        _bodyText = bodyGo.AddComponent<Text>();
        _bodyText.font = font;
        _bodyText.fontSize = 18;
        _bodyText.alignment = TextAnchor.UpperLeft;
        _bodyText.color = new Color(0.9f, 0.95f, 1f, 0.92f);
        var bodyRect = _bodyText.rectTransform;
        bodyRect.anchorMin = new Vector2(0f, 1f);
        bodyRect.anchorMax = new Vector2(1f, 1f);
        bodyRect.pivot = new Vector2(0f, 1f);
        bodyRect.anchoredPosition = new Vector2(14, -46);
        bodyRect.sizeDelta = new Vector2(-28, 58);

        _panelGo.SetActive(false);

        // FPS readout, top-right — always visible, independent of the weapon panel.
        var fpsGo = new GameObject("Fps");
        fpsGo.transform.SetParent(canvasGo.transform, false);
        _fpsText = fpsGo.AddComponent<Text>();
        _fpsText.font = font;
        _fpsText.fontSize = 26;
        _fpsText.fontStyle = FontStyle.Bold;
        _fpsText.alignment = TextAnchor.UpperRight;
        _fpsText.color = new Color(0.3f, 1f, 0.5f);
        var fpsRect = _fpsText.rectTransform;
        fpsRect.anchorMin = fpsRect.anchorMax = new Vector2(1f, 1f);
        fpsRect.pivot = new Vector2(1f, 1f);
        fpsRect.anchoredPosition = new Vector2(-18, -14);
        fpsRect.sizeDelta = new Vector2(160, 32);
    }

    /// <summary>Power type + behaviour, keyed by weapon class name.</summary>
    static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        // Core four
        ["LaserBlaster"] = "Photon bolts — the all-rounder: steady glowing laser bolts with travel time.",
        ["PhotonBeam"] = "Continuous light — short-range beam that melts shields while it stays connected.",
        ["PlasmaLobber"] = "Plasma — slow arcing orbs that burst for area damage; flushes enemies from cover.",
        ["RailZapper"] = "Rail energy — hold to charge, then an instant long-range neon rail. One big hit.",
        // Light & photon
        ["PrismSplitter"] = "Refracted light — a white bolt that splits into three rainbow bolts on impact.",
        ["StrobeBurst"] = "Pulsed light — three-round bursts of dazzling white-cyan strobes.",
        ["SunflareCannon"] = "Solar energy — charge up and release a mini sun with a golden corona explosion.",
        ["GlowwormLauncher"] = "Bioluminescence — lobs sticky blobs that cling and breathe green light.",
        ["MirrorRicochet"] = "Coherent light — instant silver laser that bounces up to 5 times off walls.",
        ["HaloRingGun"] = "Light rings — golden tori that pierce straight through multiple enemies.",
        ["BlacklightMarker"] = "Ultraviolet — tags victims with a neon outline visible through walls.",
        // Electric & magnetic
        ["ArcWhip"] = "Chained lightning — close-range whip that arcs on to two extra targets.",
        ["TeslaTurretThrower"] = "Static electricity — plants a coil that zaps anything hostile nearby.",
        ["MagnetRam"] = "Magnetism — plants a magnet field on a surface that drags enemies in and pins them.",
        ["StaticShotgun"] = "Charge dispersal — a wide fan of crackling short-range pellets.",
        ["VoltBoomerang"] = "Kinetic + electric — a spinning blade that returns, zapping on both passes.",
        ["IonRain"] = "Ionized particles — marks a zone, then glittering charged rain hammers it.",
        // Plasma & heat
        ["CometSling"] = "Superheated plasma — a huge slow fireball with a long tail and massive splash.",
        ["EmberGatling"] = "Micro-plasma — spins up from a putter to a roaring stream of tracer sparks.",
        ["MagmaMortar"] = "Molten energy — an arcing glob that splits into four smaller globs on landing.",
        ["PhoenixDart"] = "Rebirth flame — a kill re-launches the dart at the next nearest enemy.",
        ["HeatHazeProjector"] = "Infrared — an invisible beam; only shimmer motes and a red-hot contact glow.",
        // Cryo & ice
        ["FrostbiteBeam"] = "Cryogenic — slows harder the longer it holds; full exposure freezes solid.",
        ["IcicleFlechette"] = "Crystallized ice — five glinting needles; misses embed as melting icicles.",
        ["SnowglobeGrenade"] = "Flash-freeze — a dome of falling snow that slows everyone inside.",
        ["GlacierWall"] = "Ice construction — a crystal wall erupts as temporary cover, then melts.",
        // Sound & waves
        ["BassDropper"] = "Subsonic — charge, then a pressure ring knocks everyone nearby airborne.",
        ["SonicScreech"] = "High frequency — a warping cone that scrambles enemy aim (dizzy stars!).",
        ["EchoLocator"] = "Sonar — a ping that reveals and stings every enemy through walls.",
        ["DrumlineCannon"] = "Rhythmic pressure — fires on the beat; every fourth beat is a double-damage accent.",
        ["WaveRider"] = "Standing waves — a teal ribbon that snakes sideways, weaving around cover.",
        // Gravity & force
        ["BlackHoleYoyo"] = "Micro singularity — anchors mid-air, inhales enemies, then snaps back to you.",
        ["RepulsorPalm"] = "Kinetic force — shoves enemies back and pops incoming bolts out of the air.",
        ["MoonbootsBeam"] = "Anti-gravity — victims float up helplessly in zero-g sparkles.",
        ["MeteorCaller"] = "Gravity well — mark the ground; a meteor drops from the sky moments later.",
        ["OrbitLauncher"] = "Centripetal force — up to 3 guard-moons orbit you, then slingshot on command.",
        // Goo, bubbles & slime
        ["BubbleBlower"] = "Surfactant field — soap bubbles trap victims floating inside until they pop.",
        ["GooGusher"] = "Polymer slime — a purple goo hose; victims gum up, floors stay slippery.",
        ["BouncyBallCannon"] = "Elastic energy — a rubber ball that gets FASTER with every ricochet.",
        ["GlueGrenade"] = "Adhesive — glues nearby enemies' feet down with stretchy golden strands.",
        ["PaintBomber"] = "Chromatic splatter — splash damage that paints the arena in your team color.",
        // Nature & elemental
        ["TornadoTube"] = "Wind vortex — a wandering funnel that lifts and flings whoever it touches.",
        ["VineSnare"] = "Rapid-growth flora — neon vines whip out of the ground and root the target.",
        ["ThundercloudPet"] = "Weather — a personal storm cloud follows the victim, raining and zapping.",
        ["SandstormSprayer"] = "Abrasive wind — a golden grit cone that scours shields and blinds aim.",
        ["GeyserRod"] = "Hydro pressure — a water column erupts underfoot and launches the target.",
        // Gadgets & exotic
        ["PortalPistol"] = "Spatial fold — first shot plants a portal; later shots fire OUT of it.",
        ["CloneDecoyCaster"] = "Hard light — projects a jogging hologram decoy that soaks enemy fire.",
        ["TimeBubbleBomb"] = "Chrono field — an amber dome where characters AND projectiles run slow.",
        ["SwarmHive"] = "Nanobots — a pod bursts into homing firefly-bots that nibble the target.",
        ["RicochetDisc"] = "Hard-light frisbee — a neon disc that hops from enemy to enemy.",
        ["ShrinkRay"] = "Mass compression — squishes the victim to half size for a few seconds.",
        ["MimicCube"] = "Adaptive energy — copies the flavour of the last shot that hit its owner.",
        ["FireworksFinale"] = "Celebratory pyrotechnics — a rocket detonating in three damaging waves.",
    };
}
