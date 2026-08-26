using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The referee: keeps the Core's energy score, books every leak, and stages
/// the two endings — the Core folding into light under a red banner, or the
/// last wave swept and the canyon lit up in fireworks. Then back to the menu,
/// the same exit CommanderMatch takes.
/// </summary>
public class TDMatch : MonoBehaviour
{
    public static TDMatch Instance { get; private set; }

    public const int StartingCoreEnergy = 10;

    const float MenuReturnDelay = 8f;

    [SerializeField] int _coreEnergy = StartingCoreEnergy;
    [SerializeField] bool _over;

    /// <summary>What the HUD's CORE readout shows.</summary>
    public int CoreEnergy => _coreEnergy;
    public bool IsOver => _over;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// A raider reached the Core. Static like the creep's other notify hooks
    /// so callers never juggle the instance; a leak after the whistle is
    /// simply not booked.
    /// </summary>
    public static void NotifyLeak(int amount)
    {
        if (Instance == null || Instance._over)
            return;
        Instance.BookLeak(amount);
    }

    void BookLeak(int amount)
    {
        _coreEnergy = Mathf.Max(0, _coreEnergy - amount);

        // The wound made visible: a red flare at the Core, scaled to the bite.
        VfxUtil.Explosion(TDMap.CoreSite + Vector3.up * 3f,
            new Color(1f, 0.25f, 0.2f), amount > 1 ? 2.2f : 1.3f);
        GameAudio.PlayFlat(GameAudio.Id.Warning);

        if (_coreEnergy <= 0)
            Defeat();
    }

    /// <summary>All ten waves repelled with energy to spare.</summary>
    public void Victory()
    {
        if (_over)
            return;
        _over = true;
        ShowBanner("THE  CORE  STANDS", new Color(0.2f, 0.9f, 1f),
            "every wave repelled   ·   back to the menu in a moment");
        GameAudio.PlayFlat(GameAudio.Id.Victory);
        StartCoroutine(CelebrateThenLeave());
    }

    void Defeat()
    {
        _over = true;
        TDWaves.Instance?.Halt();

        // The Core takes the standard collapse — the biggest de-rez on the
        // field, which is exactly what losing it should look like. Asked of
        // the controller, whose serialized reference survives a recompile;
        // Definition-scanning here wouldn't (TD defs aren't re-derivable).
        var core = TDController.Instance != null ? TDController.Instance.Core : null;
        if (core != null && core.IsAlive)
        {
            var shield = core.GetComponent<EnergyShield>();
            if (shield != null && !shield.IsDown)
                shield.TakeHit(999999f, core.transform.position + Vector3.up * 2f);
        }

        ShowBanner("THE  CORE  IS  LOST", new Color(1f, 0.35f, 0.3f),
            "the raiders drained it dry   ·   back to the menu in a moment");
        GameAudio.PlayFlat(GameAudio.Id.Defeat);
        StartCoroutine(LeaveAfterDelay());
    }

    /// <summary>
    /// Fireworks over the canyon — cyan bursts walking the lane from gate to
    /// Core — then home. Cosmetic coroutine: a reload that kills it costs
    /// confetti, never state.
    /// </summary>
    IEnumerator CelebrateThenLeave()
    {
        var cyan = new Color(0.2f, 0.9f, 1f);
        for (int i = 0; i < 10; i++)
        {
            Vector3 at = Vector3.Lerp(TDMap.PortalSite, TDMap.CoreSite, i / 9f)
                + new Vector3(Random.Range(-4f, 4f), 4f + Random.Range(0f, 3f),
                    Random.Range(-4f, 4f));
            VfxUtil.EnergyBurst(at, i % 3 == 0 ? Color.white : cyan, 1.5f);
            yield return new WaitForSeconds(0.45f);
        }
        yield return new WaitForSeconds(MenuReturnDelay - 4.5f);
        GameModeController.Instance?.EnterMenu();
    }

    IEnumerator LeaveAfterDelay()
    {
        yield return new WaitForSeconds(MenuReturnDelay);
        GameModeController.Instance?.EnterMenu();
    }

    /// <summary>The verdict, full-screen — same staging as Commander's banner.</summary>
    void ShowBanner(string headline, Color tint, string subline)
    {
        var canvasGo = new GameObject("TDBanner");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 25;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var textGo = new GameObject("Verdict");
        textGo.transform.SetParent(canvasGo.transform, false);
        var text = textGo.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 92;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = tint;
        text.text = headline;
        text.raycastTarget = false;
        var rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, 120f);
        rect.sizeDelta = new Vector2(1600f, 120f);

        var subGo = new GameObject("Sub");
        subGo.transform.SetParent(canvasGo.transform, false);
        var sub = subGo.AddComponent<Text>();
        sub.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        sub.fontSize = 26;
        sub.alignment = TextAnchor.MiddleCenter;
        sub.color = new Color(1f, 1f, 1f, 0.6f);
        sub.text = subline;
        sub.raycastTarget = false;
        var subRect = sub.rectTransform;
        subRect.anchorMin = subRect.anchorMax = new Vector2(0.5f, 0.5f);
        subRect.anchoredPosition = new Vector2(0f, 40f);
        subRect.sizeDelta = new Vector2(1200f, 40f);
    }
}
