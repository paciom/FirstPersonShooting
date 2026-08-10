using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The unmissable "you are dead" overlay for the gunfight modes.
///
/// Before this, a player who got de-rezzed just watched their gun vanish and
/// the world sit there for three seconds — nothing said WHY the controls had
/// stopped working, which reads as a hang, not a death. Now the screen itself
/// says it: a dark red wash, DE-REZZED in letters nobody can miss, and a
/// countdown to the rebuild so the wait is a promise instead of a mystery.
///
/// Two clients, one overlay:
///  * <b>Player death</b> (Player v AI, Online) — held up for the whole
///    de-rez cycle, with the respawn countdown, and drops the moment the
///    robot re-materializes.
///  * <b>Spectated death</b> (AI v AI) — the camera's current subject going
///    down triggers a short version naming the fallen robot, so a cut away
///    from a fight reads as "they died", not "the director got bored".
///    SpectatorCamera calls in just before it cuts.
///
/// Self-bootstraps like MatchAnnouncer, and like it carries no
/// GraphicRaycaster — the overlay must never eat a touch that TouchControls
/// (respawn steering, the MENU button) is waiting for.
/// </summary>
public class DeathScreen : MonoBehaviour
{
    public static DeathScreen Instance { get; private set; }

    /// <summary>Seconds the spectator variant stays up — long enough to read
    /// a name, short enough not to blind the next shot.</summary>
    const float SpectatorSeconds = 1.8f;

    /// <summary>Body shrink time DeRezEffect runs before its respawn wait —
    /// the countdown target is death + this + respawnDelay.</summary>
    const float ShrinkSeconds = 0.3f;

    GameObject _canvasGo;
    CanvasGroup _group;
    Text _headline;
    Text _subline;
    Text _countdown;

    // Player tracking — polled, not event-subscribed: the player object is
    // rebuilt on robot swaps and hidden between modes, and a poll cannot dangle.
    EnergyShield _playerShield;
    DeRezEffect _playerDeRez;
    float _nextPlayerScan;
    bool _playerWasDead;
    float _playerDiedAt;

    float _spectatorHideAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance == null)
            new GameObject("DeathScreen").AddComponent<DeathScreen>();
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

    // ------------------------------------------------------------- static API

    /// <summary>
    /// The spectator camera's subject just went down. Shows the short variant
    /// with the fallen robot's name in its team colour.
    /// </summary>
    public static void SpectatorSubjectDied(Transform subject, EnergyShield shield)
    {
        if (Instance == null)
            return;
        string name = MatchAnnouncer.CharacterName(subject);
        Color color = shield != null ? MatchAnnouncer.TeamColor(shield.teamId)
                                     : new Color(1f, 0.3f, 0.2f);
        Instance.ShowSpectator(name, color);
    }

    // ------------------------------------------------------------- behaviour

    void Update()
    {
        var controller = GameModeController.Instance;
        GameMode mode = controller != null ? controller.Mode : GameMode.Menu;

        bool playerDead = false;
        if (mode == GameMode.PlayerVsAI || mode == GameMode.OnlinePvP)
            playerDead = PollPlayerDeath();
        else
            _playerWasDead = false;

        bool spectating = mode == GameMode.AIvAI && Time.time < _spectatorHideAt;

        bool show = playerDead || spectating;
        if (playerDead)
            ShowPlayerFrame();

        // Snap in (a death is an instant), fade out (a respawn is a relief).
        float target = show ? 1f : 0f;
        _group.alpha = target > _group.alpha
            ? target
            : Mathf.MoveTowards(_group.alpha, target, Time.deltaTime * 3.5f);
        if (_canvasGo.activeSelf != _group.alpha > 0.01f)
            _canvasGo.SetActive(_group.alpha > 0.01f);
    }

    bool PollPlayerDeath()
    {
        if (_playerShield == null && Time.time >= _nextPlayerScan)
        {
            _nextPlayerScan = Time.time + 1f;
            var brain = FindFirstObjectByType<PlayerBrain>(FindObjectsInactive.Include);
            _playerShield = brain != null ? brain.GetComponent<EnergyShield>() : null;
            _playerDeRez = brain != null ? brain.GetComponent<DeRezEffect>() : null;
        }

        bool dead = _playerShield != null && _playerShield.IsDown
                    && _playerShield.gameObject.activeInHierarchy;
        if (dead && !_playerWasDead)
            _playerDiedAt = Time.time;
        _playerWasDead = dead;
        return dead;
    }

    void ShowPlayerFrame()
    {
        _headline.text = "DE-REZZED!";
        _headline.color = new Color(1f, 0.30f, 0.18f);
        _subline.text = "Your robot took too many hits";

        float respawn = _playerDeRez != null ? _playerDeRez.respawnDelay : 3f;
        float remaining = _playerDiedAt + ShrinkSeconds + respawn - Time.time;
        _countdown.text = remaining > 0f
            ? $"REBUILDING IN {Mathf.CeilToInt(remaining)}"
            : "REBUILDING…";
    }

    void ShowSpectator(string name, Color color)
    {
        _spectatorHideAt = Time.time + SpectatorSeconds;
        _headline.text = $"{name} DE-REZZED";
        _headline.color = color;
        _subline.text = "Finding the next fight…";
        _countdown.text = "";
    }

    // ------------------------------------------------------------- UI build

    void BuildUi()
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        _canvasGo = new GameObject("DeathCanvas");
        _canvasGo.transform.SetParent(transform, false);
        var canvas = _canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 45;   // above HUD and announcer, below the debug console
        var scaler = _canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        _group = _canvasGo.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable = false;

        // Full-screen red-black wash. Deliberately translucent: the fight (or
        // your own wreckage) stays visible behind the verdict.
        var wash = MakeImage(_canvasGo.transform, "Wash", new Color(0.10f, 0.00f, 0.02f, 0.55f));
        Stretch(wash.rectTransform, Vector2.zero, Vector2.one);

        // A darker band across the middle so the text never fights the scene.
        var band = MakeImage(_canvasGo.transform, "Band", new Color(0f, 0f, 0f, 0.5f));
        Stretch(band.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f));
        band.rectTransform.sizeDelta = new Vector2(0f, 230f);
        band.rectTransform.anchoredPosition = new Vector2(0f, 40f);

        _headline = MakeText("Headline", font, 88, FontStyle.Bold, new Vector2(0f, 110f));
        _subline = MakeText("Subline", font, 30, FontStyle.Normal, new Vector2(0f, 30f));
        _subline.color = new Color(0.92f, 0.94f, 1f, 0.9f);
        _countdown = MakeText("Countdown", font, 44, FontStyle.Bold, new Vector2(0f, -40f));
        _countdown.color = new Color(1f, 0.82f, 0.25f);

        _canvasGo.SetActive(false);
    }

    Image MakeImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
    }

    Text MakeText(string name, Font font, int size, FontStyle style, Vector2 position)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_canvasGo.transform, false);
        var text = go.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        var rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(1400f, 100f);
        return text;
    }
}
