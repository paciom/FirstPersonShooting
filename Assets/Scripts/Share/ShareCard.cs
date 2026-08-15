using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The K.O. card: the picture a kid actually shares.
///
/// "TITAN WINS" on the end panel is not shareable. A card that says TITAN
/// BEAT BOLT 2-1 WITH A FLYING DRAGON KICK, with the robot's face on it and
/// the address underneath, is — and it is the same match, told properly.
///
/// It renders OFFSCREEN to a fixed 1200x630, not as a screengrab, for three
/// reasons: 1200x630 is the size every chat app and social preview wants, a
/// grab would carry whatever window shape the player happens to have (and
/// their HUD with it), and a rig built and struck inside one call cannot be
/// seen by any other camera — nothing else renders in between.
///
/// The whole rig lives and dies inside <see cref="Render"/>. Anything left
/// standing would be a second canvas and a second camera in every subsequent
/// frame of the fight behind it.
/// </summary>
public static class ShareCard
{
    public const int Width = 1200;
    public const int Height = 630;

    // Parked far below the world. The Brawl portrait studio sits at -220 and
    // the fight at 0; nothing in this game is ever a kilometre down.
    static readonly Vector3 Studio = new Vector3(0f, -1400f, 0f);

    static readonly Color Ink = new Color(0.016f, 0.047f, 0.078f);
    static readonly Color Gold = new Color(1f, 0.78f, 0.32f);
    static readonly Color Cyan = new Color(0.25f, 0.84f, 1f);

    /// <summary>Everything the card needs to know about a finished bout.</summary>
    public struct Result
    {
        public string winner;        // roster display name
        public string loser;
        public Texture winnerFace;   // the live portrait RenderTexture, or null
        public string stage;
        public string finisher;      // the move that ended it, when one is known
        public bool knockout;        // false when the deciding round ran out of clock
        public string score;         // "2-1", winner first
        public string pilot;         // signed-in name, or empty
        public bool playerWon;       // false when the human lost, or nobody played
        public bool playerFought;    // false for the AI-vs-AI exhibition
    }

    /// <summary>
    /// The headline the card and the share sheet lead with. Second person on
    /// purpose when the human won: the card is a thing they DID.
    /// </summary>
    public static string Headline(Result r)
    {
        if (!r.playerFought)
            return $"{Up(r.winner)}  WINS";
        return r.playerWon ? $"YOU  WON  AS  {Up(r.winner)}" : $"{Up(r.winner)}  BEAT  YOU";
    }

    /// <summary>
    /// Render the card. Returns null only if the platform refused a
    /// RenderTexture — every caller treats that as "share the link alone".
    /// </summary>
    public static Texture2D Render(Result r)
    {
        GameObject rig = null;
        RenderTexture target = null;
        RenderTexture wasActive = RenderTexture.active;
        try
        {
            rig = new GameObject("ShareCardStudio");
            rig.transform.position = Studio;

            var canvasGo = new GameObject("Card");
            canvasGo.transform.SetParent(rig.transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var canvasRect = canvas.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(Width, Height);
            canvasRect.localScale = Vector3.one;      // one canvas unit == one pixel

            var camGo = new GameObject("CardCamera");
            camGo.transform.SetParent(rig.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 0f, -600f);
            camGo.transform.localRotation = Quaternion.identity;
            var cam = camGo.AddComponent<Camera>();
            // NOT tagged MainCamera, and never enabled: it renders exactly once,
            // by hand, below. An enabled camera would draw the card over the
            // game every frame.
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = Height * 0.5f;
            cam.aspect = (float)Width / Height;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 1200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Ink;
            canvas.worldCamera = cam;

            Compose(canvasRect, r);

            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 2
            };
            cam.targetTexture = target;

            // Without this the canvas has no geometry yet: Unity builds it in
            // its own end-of-frame pass, which is AFTER this manual render.
            // The card came out empty every time until this line existed.
            Canvas.ForceUpdateCanvases();
            cam.Render();

            var picture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            RenderTexture.active = target;
            picture.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
            picture.Apply();
            return picture;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[ShareCard] could not render the card: {e.Message}");
            return null;
        }
        finally
        {
            RenderTexture.active = wasActive;
            if (target != null)
            {
                target.Release();
                Object.Destroy(target);
            }
            if (rig != null)
            {
                // Deactivated before the deferred Destroy, not just handed to
                // it: Destroy does not take effect until the end of the frame,
                // and a live world-space canvas is drawn by any camera that can
                // reach it. Nothing here is within the fight camera's far
                // plane, but a rig that renders once by accident would render
                // a full-screen card over the game, and that is not a bug
                // worth leaving to arithmetic.
                rig.SetActive(false);
                Object.Destroy(rig);
            }
        }
    }

    // ------------------------------------------------------------- the layout

    static void Compose(RectTransform card, Result r)
    {
        // Coordinates run -600..600 across and -315..315 up, from the middle.
        Panel(card, "Bed", Ink, Vector2.zero, new Vector2(Width, Height));

        // A wash of team colour behind the winner's side, so the card reads as
        // a trophy rather than as a form.
        var wash = Panel(card, "Wash", new Color(Cyan.r, Cyan.g, Cyan.b, 0.10f),
            new Vector2(-300f, 0f), new Vector2(600f, Height));
        wash.sprite = MenuArt.FadeDown();

        Panel(card, "TopRule", new Color(1f, 1f, 1f, 0.10f),
            new Vector2(0f, 236f), new Vector2(1104f, 1f));
        Panel(card, "BottomRule", new Color(1f, 1f, 1f, 0.10f),
            new Vector2(0f, -218f), new Vector2(1104f, 1f));

        var brand = Label(card, "Brand", "JET  ARMOR  HEROES   ·   BRAWL", 28, Gold,
            new Vector2(-552f, 272f), new Vector2(700f, 36f));
        brand.alignment = TextAnchor.MiddleLeft;

        if (!string.IsNullOrEmpty(r.stage))
        {
            var stage = Label(card, "Stage", Up(r.stage), 26, new Color(1f, 1f, 1f, 0.5f),
                new Vector2(552f, 272f), new Vector2(700f, 36f));
            stage.alignment = TextAnchor.MiddleRight;
        }

        Portrait(card, r.winnerFace);

        // The right-hand block: who won, over whom, how, by how much. All of
        // it left-aligned off one edge so a long robot name pushes nothing.
        const float TextLeft = -110f;
        var name = Label(card, "Winner", Up(r.winner), 92, Gold,
            new Vector2(TextLeft, 118f), new Vector2(720f, 110f));
        name.alignment = TextAnchor.MiddleLeft;
        name.rectTransform.pivot = new Vector2(0f, 0.5f);
        Gild(name);

        string beat = string.IsNullOrEmpty(r.loser) ? "WINS  THE  BOUT" : $"BEAT  {Up(r.loser)}";
        if (!string.IsNullOrEmpty(r.score))
            beat += $"    {r.score.Replace("-", "  –  ")}";
        var over = Label(card, "Beat", beat, 44, new Color(1f, 1f, 1f, 0.88f),
            new Vector2(TextLeft, 40f), new Vector2(720f, 56f));
        over.alignment = TextAnchor.MiddleLeft;
        over.rectTransform.pivot = new Vector2(0f, 0.5f);

        // The finisher is the line that makes the card worth sending — an
        // identical match with a named move on it reads as a story. A K.O. by
        // mine or by fire has no move to name, and a bout decided on the clock
        // has no K.O. at all; both still get their own true line.
        string blow = !r.knockout ? "WON  ON  THE  CLOCK"
            : string.IsNullOrEmpty(r.finisher) ? "K.O."
            : $"K.O.   ·   {Up(r.finisher)}";
        var finisher = Label(card, "Finisher", blow, 38, Cyan,
            new Vector2(TextLeft, -34f), new Vector2(720f, 50f));
        finisher.alignment = TextAnchor.MiddleLeft;
        finisher.rectTransform.pivot = new Vector2(0f, 0.5f);

        string byLine = string.IsNullOrEmpty(r.pilot)
            ? (r.playerFought ? "CAN  YOU  BEAT  IT?" : "AI  EXHIBITION  BOUT")
            : $"{Up(r.pilot)}   ·   CAN  YOU  BEAT  IT?";
        var by = Label(card, "By", byLine, 30, new Color(1f, 1f, 1f, 0.55f),
            new Vector2(-552f, -262f), new Vector2(760f, 40f));
        by.alignment = TextAnchor.MiddleLeft;

        var site = Label(card, "Site", "PLAY  FREE  ·  PLAY.JAH.CC", 30, Gold,
            new Vector2(552f, -262f), new Vector2(560f, 40f));
        site.alignment = TextAnchor.MiddleRight;
    }

    /// <summary>The winner's face in a gold-silled frame, or a nameplate without one.</summary>
    static void Portrait(RectTransform card, Texture face)
    {
        var at = new Vector2(-360f, 10f);
        var size = new Vector2(268f, 268f);

        var glow = Panel(card, "PortraitGlow", new Color(Gold.r, Gold.g, Gold.b, 0.30f),
            at, size + new Vector2(56f, 56f));
        glow.sprite = MenuArt.GlowRect(28f);
        glow.type = Image.Type.Sliced;

        var frame = Panel(card, "PortraitFrame", new Color(0.03f, 0.08f, 0.13f, 1f), at, size);
        frame.sprite = MenuArt.RoundedRect(14f);
        frame.type = Image.Type.Sliced;

        if (face != null)
        {
            var raw = new GameObject("Face").AddComponent<RawImage>();
            raw.rectTransform.SetParent(frame.transform, false);
            raw.texture = face;
            raw.rectTransform.anchorMin = Vector2.zero;
            raw.rectTransform.anchorMax = Vector2.one;
            raw.rectTransform.offsetMin = new Vector2(6f, 6f);
            raw.rectTransform.offsetMax = new Vector2(-6f, -6f);
        }

        var sill = Panel(frame.rectTransform, "Sill", Gold, new Vector2(0f, -128f),
            new Vector2(size.x - 12f, 8f));
        sill.raycastTarget = false;
    }

    // ------------------------------------------------------------- rect helpers

    static Image Panel(RectTransform parent, string name, Color color, Vector2 at, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        Place(image.rectTransform, at, size);
        return image;
    }

    static Text Label(RectTransform parent, string name, string content, int size, Color color,
        Vector2 at, Vector2 box)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = content;
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        Place(text.rectTransform, at, box);
        return text;
    }

    /// <summary>The title screen's metal treatment, at card scale.</summary>
    static void Gild(Text text)
    {
        var outline = text.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.01f, 0.04f, 0.07f, 0.95f);
        outline.effectDistance = new Vector2(4f, 4f);

        var shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0.02f, 0.05f, 0.6f);
        shadow.effectDistance = new Vector2(0f, -6f);
    }

    static void Place(RectTransform rect, Vector2 at, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = at;
        rect.sizeDelta = size;
    }

    static string Up(string s) => string.IsNullOrEmpty(s) ? "" : s.ToUpperInvariant();
}
