using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// C# side of Assets/Plugins/WebGL/ShareBridge.jslib, plus the editor and
/// desktop fallbacks that let the whole share loop be exercised without a
/// browser.
///
/// Nothing here throws and nothing here blocks. A share that fails is a share
/// that did not happen; it is never a broken game — which is why every call
/// is wrapped and why the desktop path writes the card to disk and tells the
/// console where it went rather than reporting failure to the player.
/// </summary>
public static class ShareBridge
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern string ShareLocationHash();
    [DllImport("__Internal")] static extern void ShareClearHash();
    [DllImport("__Internal")] static extern int ShareHasNative();
    [DllImport("__Internal")] static extern void ShareOpenSheet(
        string headline, string boast, string url, string image, string filename);
#endif

    /// <summary>The live `#…` on the address bar, or "" off the web.</summary>
    public static string LocationHash()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        try { return ShareLocationHash() ?? ""; }
        catch (System.Exception) { return ""; }
#else
        return "";
#endif
    }

    /// <summary>
    /// Drop the challenge off the address bar once it has been played, so a
    /// refresh restarts the game rather than the sender's fight.
    /// </summary>
    public static void ClearLocationHash()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        try { ShareClearHash(); } catch (System.Exception) { }
#endif
    }

    /// <summary>True where the OS share sheet exists — phones and tablets.</summary>
    public static bool HasNativeShare
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try { return ShareHasNative() != 0; }
            catch (System.Exception) { return false; }
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// Put the share sheet up: the card, the link, and the buttons that need
    /// a real browser gesture. <paramref name="card"/> may be null — the
    /// sheet degrades to a link-only panel rather than refusing to open.
    ///
    /// Returns what the player needs TOLD, or null when the platform showed
    /// them something itself. Off the web there is no sheet to raise, so the
    /// share happens silently — clipboard, a file on disk — and a caller that
    /// drops this string leaves the button looking broken. Web returns null
    /// on success (the sheet is its own receipt) and a string when raising it
    /// failed, which is the one case a browser also has nothing to show.
    /// </summary>
    public static string OpenSheet(string headline, string boast, string url, Texture2D card,
        string filename = "jet-armor-heroes-win.png")
    {
        byte[] png = null;
        if (card != null)
        {
            try { png = card.EncodeToPNG(); }
            catch (System.Exception e) { Debug.LogWarning($"[Share] could not encode the card: {e.Message}"); }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        string image = png != null
            ? "data:image/png;base64," + System.Convert.ToBase64String(png)
            : "";
        try
        {
            ShareOpenSheet(headline ?? "", boast ?? "", url ?? "", image, filename);
            return null;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Share] sheet failed: {e.Message}");
            return "Could not open the share sheet.";
        }
#else
        // Editor and desktop: the two halves of the sheet that CAN happen off
        // the web. Both are how this path gets tested without a deploy.
        GUIUtility.systemCopyBuffer = url ?? "";
        if (png != null)
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, filename);
                File.WriteAllBytes(path, png);
                Debug.Log($"[Share] {headline} — link on the clipboard, card written to {path}");
                return "Link copied to your clipboard.\nCard saved to " + path;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Share] could not write the card: {e.Message}");
            }
        }
        Debug.Log($"[Share] {headline} — link on the clipboard: {url}");
        return "Link copied to your clipboard.";
#endif
    }
}
