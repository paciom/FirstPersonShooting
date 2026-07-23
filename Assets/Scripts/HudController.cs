using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds a holographic-cyan HUD entirely at runtime (no scene wiring needed
/// during greybox): crosshair, shield bar, and de-rez score counter.
/// Attach to the player rig next to its EnergyShield.
/// </summary>
public class HudController : MonoBehaviour
{
    static readonly Color HoloCyan = new Color(0.2f, 0.9f, 1f, 0.9f);

    EnergyShield _shield;
    Image _shieldFill;
    Text _scoreText;

    void Awake()
    {
        _shield = GetComponent<EnergyShield>();
    }

    void Start()
    {
        BuildCanvas();
        ScoreKeeper.OnScoreChanged += HandleScore;
        HandleScore(ScoreKeeper.Score);
    }

    void OnDestroy()
    {
        ScoreKeeper.OnScoreChanged -= HandleScore;
    }

    void Update()
    {
        if (_shield != null && _shieldFill != null)
        {
            _shieldFill.fillAmount = _shield.Normalized;
            // Shield bar cools from cyan to warning orange as it drains.
            _shieldFill.color = Color.Lerp(new Color(1f, 0.5f, 0.1f, 0.9f), HoloCyan, _shield.Normalized);
        }
    }

    void HandleScore(int score)
    {
        if (_scoreText != null)
            _scoreText.text = $"DE-REZ  {score}";
    }

    void BuildCanvas()
    {
        var canvasGo = new GameObject("HUD");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // Crosshair: four small bars around center.
        BuildCrosshairBar(canvas.transform, new Vector2(0, 14), new Vector2(2, 10));
        BuildCrosshairBar(canvas.transform, new Vector2(0, -14), new Vector2(2, 10));
        BuildCrosshairBar(canvas.transform, new Vector2(14, 0), new Vector2(10, 2));
        BuildCrosshairBar(canvas.transform, new Vector2(-14, 0), new Vector2(10, 2));

        // Shield bar (bottom-left).
        var barBg = MakeImage(canvas.transform, "ShieldBarBg", new Color(0f, 0f, 0f, 0.45f));
        SetRect(barBg.rectTransform, new Vector2(0, 0), new Vector2(60, 50), new Vector2(360, 26), new Vector2(0.5f, 0.5f));
        barBg.rectTransform.anchorMin = barBg.rectTransform.anchorMax = new Vector2(0f, 0f);
        barBg.rectTransform.anchoredPosition = new Vector2(60 + 180, 50);

        _shieldFill = MakeImage(barBg.transform, "ShieldFill", HoloCyan);
        _shieldFill.rectTransform.anchorMin = Vector2.zero;
        _shieldFill.rectTransform.anchorMax = Vector2.one;
        _shieldFill.rectTransform.offsetMin = new Vector2(3, 3);
        _shieldFill.rectTransform.offsetMax = new Vector2(-3, -3);
        _shieldFill.type = Image.Type.Filled;
        _shieldFill.fillMethod = Image.FillMethod.Horizontal;

        // Score (top-center).
        var scoreGo = new GameObject("Score");
        scoreGo.transform.SetParent(canvas.transform, false);
        _scoreText = scoreGo.AddComponent<Text>();
        _scoreText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _scoreText.fontSize = 34;
        _scoreText.fontStyle = FontStyle.Bold;
        _scoreText.alignment = TextAnchor.MiddleCenter;
        _scoreText.color = HoloCyan;
        var scoreRect = _scoreText.rectTransform;
        scoreRect.anchorMin = scoreRect.anchorMax = new Vector2(0.5f, 1f);
        scoreRect.anchoredPosition = new Vector2(0, -50);
        scoreRect.sizeDelta = new Vector2(400, 60);
    }

    void BuildCrosshairBar(Transform parent, Vector2 offset, Vector2 size)
    {
        var img = MakeImage(parent, "Crosshair", HoloCyan);
        img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        img.rectTransform.anchoredPosition = offset;
        img.rectTransform.sizeDelta = size;
    }

    static Image MakeImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    static void SetRect(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size, Vector2 pivot)
    {
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.pivot = pivot;
    }
}
