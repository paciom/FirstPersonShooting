using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// The player's account: talks to the signaling server's /api/* endpoints
/// (register / login / logout / me) and keeps the session token in
/// PlayerPrefs so a returning player is signed in without typing anything.
///
/// Accounts are OPTIONAL — every mode plays fine signed out. Signing in is
/// what makes a name portable between devices (and, later, what stats and
/// leaderboards hang off).
///
/// All requests are fire-and-callback coroutines on this singleton;
/// <see cref="Changed"/> fires whenever the signed-in state moves, which is
/// all the menu badge needs to stay honest.
/// </summary>
public class AccountClient : MonoBehaviour
{
    public static AccountClient Instance { get; private set; }

    const string TokenPref = "account.token";
    const string NamePref = "account.name";
    const int TimeoutSeconds = 10;

    public bool SignedIn { get; private set; }
    public string Username { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string UserId { get; private set; } = "";
    /// <summary>A request is in flight — menus disable their buttons on it.</summary>
    public bool Busy { get; private set; }

    /// <summary>Raised on sign-in, sign-out, and resume.</summary>
    public static event Action Changed;

    bool _resumeStarted;

    [Serializable]
    class AuthReply
    {
        public bool ok;
        public string token;
        public string userId;
        public string username;
        public string email;
        public string error;
        public string message;
    }

    [Serializable]
    class RegisterBody { public string username; public string password; public string email; }
    [Serializable]
    class LoginBody { public string id; public string password; }

    public static AccountClient Ensure()
    {
        if (Instance == null)
        {
            Instance = new GameObject("AccountClient").AddComponent<AccountClient>();
            DontDestroyOnLoad(Instance.gameObject);
        }
        return Instance;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// The auth API lives on the signaling server, so its address is the
    /// same single source of truth (`?net=` override included) with the
    /// scheme swapped ws->http. Null when no server is configured.
    /// </summary>
    public static string ApiBase
    {
        get
        {
            string ws = NetSession.ServerUrl;
            if (string.IsNullOrEmpty(ws))
                return null;
            if (ws.StartsWith("wss://", StringComparison.Ordinal))
                return "https://" + ws.Substring(6);
            if (ws.StartsWith("ws://", StringComparison.Ordinal))
                return "http://" + ws.Substring(5);
            return null;
        }
    }

    /// <summary>The saved session token ("" when signed out) for sibling
    /// clients that call other bearer-authenticated endpoints.</summary>
    public static string SessionToken => PlayerPrefs.GetString(TokenPref, "");

    /// <summary>The name to show for this player anywhere a name is shown.</summary>
    public static string DisplayName =>
        Instance != null && Instance.SignedIn ? Instance.Username : "GUEST";

    /// <summary>
    /// Try to restore the saved session. Safe to call every time the menu
    /// builds — only the first call does anything. A dead server keeps the
    /// token for next launch; only a rejected token clears it.
    /// </summary>
    public void Resume()
    {
        if (_resumeStarted)
            return;
        _resumeStarted = true;
        string token = PlayerPrefs.GetString(TokenPref, "");
        if (token.Length == 0 || ApiBase == null)
            return;
        StartCoroutine(Request("GET", "/api/me", null, token, (reply, netError) =>
        {
            if (reply != null && reply.ok)
                ApplySession(token, reply);
            else if (reply != null)
                ClearLocal();   // the server said no: token is dead
            // netError: offline or local server not running — stay quiet.
        }));
    }

    public void Register(string username, string password, string email,
        Action<bool, string> done)
    {
        var body = new RegisterBody { username = username, password = password, email = email };
        Send("/api/register", JsonUtility.ToJson(body), done);
    }

    /// <summary>Sign in with a username or an email in <paramref name="id"/>.</summary>
    public void Login(string id, string password, Action<bool, string> done)
    {
        var body = new LoginBody { id = id, password = password };
        Send("/api/login", JsonUtility.ToJson(body), done);
    }

    /// <summary>
    /// Signs out locally right away; the server-side token delete rides on a
    /// best-effort request (an offline logout must still work).
    /// </summary>
    public void Logout()
    {
        string token = PlayerPrefs.GetString(TokenPref, "");
        ClearLocal();
        if (token.Length > 0 && ApiBase != null)
            StartCoroutine(Request("POST", "/api/logout", "{}", token, (_, __) => { }));
    }

    void Send(string path, string json, Action<bool, string> done)
    {
        if (ApiBase == null)
        {
            done?.Invoke(false, "no account server is set up");
            return;
        }
        if (Busy)
        {
            done?.Invoke(false, "still working on it...");
            return;
        }
        Busy = true;
        StartCoroutine(Request("POST", path, json, null, (reply, netError) =>
        {
            Busy = false;
            if (reply != null && reply.ok)
            {
                ApplySession(reply.token, reply);
                done?.Invoke(true, "");
            }
            else if (reply != null)
            {
                done?.Invoke(false, string.IsNullOrEmpty(reply.message)
                    ? "something went wrong, try again"
                    : reply.message);
            }
            else
            {
                done?.Invoke(false, netError);
            }
        }));
    }

    /// <summary>
    /// One shaped request. The callback gets a parsed reply (even for 4xx —
    /// the friendly message rides in the JSON) or null + a network error.
    /// </summary>
    IEnumerator Request(string method, string path, string json, string bearer,
        Action<AuthReply, string> done)
    {
        using (var req = new UnityWebRequest(ApiBase + path, method))
        {
            if (json != null)
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.SetRequestHeader("Content-Type", "application/json");
            }
            req.downloadHandler = new DownloadHandlerBuffer();
            if (!string.IsNullOrEmpty(bearer))
                req.SetRequestHeader("Authorization", "Bearer " + bearer);
            req.timeout = TimeoutSeconds;

            yield return req.SendWebRequest();

            AuthReply reply = null;
            string text = req.downloadHandler.text;
            if (!string.IsNullOrEmpty(text))
            {
                try { reply = JsonUtility.FromJson<AuthReply>(text); }
                catch { /* not JSON — treat as a network-level failure */ }
            }
            if (reply != null)
                done(reply, null);
            else
                done(null, "can't reach the account server");
        }
    }

    void ApplySession(string token, AuthReply reply)
    {
        if (!string.IsNullOrEmpty(token))
            PlayerPrefs.SetString(TokenPref, token);
        PlayerPrefs.SetString(NamePref, reply.username ?? "");
        PlayerPrefs.Save();
        SignedIn = true;
        Username = reply.username ?? "";
        Email = reply.email ?? "";
        UserId = reply.userId ?? "";
        Changed?.Invoke();
    }

    void ClearLocal()
    {
        PlayerPrefs.DeleteKey(TokenPref);
        PlayerPrefs.DeleteKey(NamePref);
        PlayerPrefs.Save();
        SignedIn = false;
        Username = "";
        Email = "";
        UserId = "";
        Changed?.Invoke();
    }
}
