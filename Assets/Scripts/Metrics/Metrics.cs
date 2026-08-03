using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

/// <summary>
/// First-party analytics client (see ANALYTICS_PLAN.md). Fire-and-forget:
/// nothing in here may ever throw into gameplay code, block a frame, or
/// collect anything beyond the anonymous install id + enum-like event props.
/// Inert until Endpoint is set — in the editor events are logged instead.
/// </summary>
public static class Metrics
{
    // Filled in when the ingest Function exists (Phase 2). Empty = disabled.
    public const string Endpoint = "";
    public const string Env = "dev"; // flip to "prod" in release builds via MetricsPump.ResolveEnv

    const string PrefsInstallId = "metrics_install_id";
    const int MaxQueued = 500;      // oldest events drop beyond this — never grow unbounded
    const int MaxPropValue = 64;    // props are enums/numbers, never free text (COPPA posture)
    const int MaxErrorsPerSession = 10;
    internal const float FlushEverySeconds = 15f;
    const int FlushAtCount = 25;

    static string _installId;
    static string _sessionId;
    static int _seq;
    static readonly List<string> _queue = new List<string>();
    static readonly HashSet<int> _errorHashes = new HashSet<int>();
    static int _errorCount;
    static bool _inited;

    public static bool Enabled => _inited && (Endpoint.Length > 0 || Application.isEditor);

    /// <summary>Track("match_start", ("mode","dogfight"), ("squad","2v2"), ("bots",6))</summary>
    public static void Track(string name, params (string key, object value)[] props)
    {
        try
        {
            if (!_inited || string.IsNullOrEmpty(name)) return;
            var sb = new StringBuilder(128);
            sb.Append("{\"e\":\"").Append(Clean(name, 32)).Append("\",\"t\":")
              .Append(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
              .Append(",\"n\":").Append(_seq++);
            if (props != null && props.Length > 0)
            {
                sb.Append(",\"p\":{");
                int written = 0;
                for (int i = 0; i < props.Length && written < 12; i++)
                {
                    var (key, value) = props[i];
                    if (string.IsNullOrEmpty(key) || value == null) continue;
                    if (written > 0) sb.Append(',');
                    sb.Append('\"').Append(Clean(key, 24)).Append("\":");
                    if (value is int || value is long || value is float || value is double || value is bool)
                        sb.Append(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant());
                    else
                        sb.Append('\"').Append(Clean(value.ToString(), MaxPropValue)).Append('\"');
                    written++;
                }
                sb.Append('}');
            }
            sb.Append('}');
            Enqueue(sb.ToString());
            if (Application.isEditor) Debug.Log($"[Metrics] {sb}");
        }
        catch { /* analytics must never break the game */ }
    }

    static void Enqueue(string eventJson)
    {
        _queue.Add(eventJson);
        if (_queue.Count > MaxQueued) _queue.RemoveRange(0, _queue.Count - MaxQueued);
        if (_queue.Count >= FlushAtCount) MetricsPump.RequestFlush();
    }

    internal static void Init()
    {
        if (_inited) return;
        _installId = PlayerPrefs.GetString(PrefsInstallId, "");
        if (_installId.Length != 32)
        {
            _installId = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(PrefsInstallId, _installId);
            PlayerPrefs.Save(); // WebGL persists PlayerPrefs to IndexedDB only on Save
        }
        _sessionId = Guid.NewGuid().ToString("N").Substring(0, 16);
        _inited = true;
        Application.logMessageReceived += OnLog;
        Track("session_start",
            ("platform", Application.platform.ToString()),
            ("app_ver", Application.version),
            ("unity", Application.unityVersion),
            ("src", LaunchSource()));
    }

    static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type != LogType.Exception || _errorCount >= MaxErrorsPerSession) return;
        int hash = (condition ?? "").GetHashCode();
        if (!_errorHashes.Add(hash)) return; // one event per distinct exception text
        _errorCount++;
        Track("error", ("msg", Clean(condition, 120)), ("where", FirstFrame(stackTrace)));
    }

    static string FirstFrame(string stack)
    {
        if (string.IsNullOrEmpty(stack)) return "";
        int nl = stack.IndexOf('\n');
        return Clean(nl > 0 ? stack.Substring(0, nl) : stack, 80);
    }

    // On WebGL the site's Play button appends ?src=site — ties site clicks to game sessions.
    static string LaunchSource()
    {
        string url = Application.absoluteURL;
        if (string.IsNullOrEmpty(url)) return "";
        int i = url.IndexOf("src=", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return "";
        string v = url.Substring(i + 4);
        int amp = v.IndexOf('&');
        return Clean(amp >= 0 ? v.Substring(0, amp) : v, 24);
    }

    // JSON string escape + length cap + newline strip; props stay enum-like by construction.
    static string Clean(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(Math.Min(s.Length, max));
        foreach (char c in s)
        {
            if (sb.Length >= max) break;
            if (c == '\\' || c == '\"') sb.Append('\\').Append(c);
            else if (c == '\n' || c == '\r' || c == '\t') sb.Append(' ');
            else if (c >= ' ') sb.Append(c);
        }
        return sb.ToString();
    }

    internal static string DrainToEnvelope()
    {
        if (_queue.Count == 0) return null;
        var sb = new StringBuilder(1024);
        sb.Append("{\"v\":1,\"app\":\"jah\",\"env\":\"").Append(MetricsPump.ResolveEnv())
          .Append("\",\"iid\":\"").Append(_installId)
          .Append("\",\"sid\":\"").Append(_sessionId)
          .Append("\",\"events\":[");
        for (int i = 0; i < _queue.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(_queue[i]);
        }
        sb.Append("]}");
        _queue.Clear();
        return sb.ToString();
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] internal static extern void MetricsBeacon(string url, string json);
#endif
}

/// <summary>Hidden pump: periodic flush + lifecycle flushes. Auto-created after scene load.</summary>
public class MetricsPump : MonoBehaviour
{
    static MetricsPump _instance;
    static bool _flushRequested;
    float _timer;
    bool _sending;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (_instance != null) return;
        var go = new GameObject("MetricsPump");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<MetricsPump>();
        Metrics.Init();
    }

    public static string ResolveEnv() => Debug.isDebugBuild || Application.isEditor ? "dev" : Metrics.Env;

    public static void RequestFlush() => _flushRequested = true;

    void Update()
    {
        _timer += Time.unscaledDeltaTime;
        if (_flushRequested || _timer >= Metrics.FlushEverySeconds)
        {
            _timer = 0f;
            _flushRequested = false;
            Flush();
        }
    }

    void OnApplicationPause(bool paused)
    {
        if (paused) FinalFlush(); // mobile background = maybe never coming back
    }

    void OnApplicationQuit()
    {
        Metrics.Track("session_end");
        FinalFlush();
    }

    void Flush()
    {
        if (_sending || Metrics.Endpoint.Length == 0) { if (Metrics.Endpoint.Length == 0) DiscardDrained(); return; }
        string body = Metrics.DrainToEnvelope();
        if (body == null) return;
        StartCoroutine(Send(body));
    }

    void DiscardDrained()
    {
        // No endpoint configured: drop drained events so the queue never grows.
        Metrics.DrainToEnvelope();
    }

    IEnumerator Send(string body)
    {
        _sending = true;
        // text/plain keeps the request CORS-simple (no preflight); the Function parses JSON regardless.
        using (var req = new UnityWebRequest(Metrics.Endpoint, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)) { contentType = "text/plain" };
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = 10;
            yield return req.SendWebRequest();
            // Failures are accepted losses — never retry-storm a kids' game client.
        }
        _sending = false;
    }

    void FinalFlush()
    {
        if (Metrics.Endpoint.Length == 0) return;
        string body = Metrics.DrainToEnvelope();
        if (body == null) return;
#if UNITY_WEBGL && !UNITY_EDITOR
        // UnityWebRequest dies with the page; navigator.sendBeacon survives tab close.
        Metrics.MetricsBeacon(Metrics.Endpoint, body);
#else
        StartCoroutine(Send(body));
#endif
    }
}
