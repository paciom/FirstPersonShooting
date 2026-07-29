using System;
using System.Globalization;
using System.Text;
using UnityEngine;

public enum NetStatus
{
    Offline,
    /// <summary>Reaching the signaling server / waiting for its answer.</summary>
    Connecting,
    /// <summary>Room created — the match code is up, waiting for a challenger.</summary>
    Hosting,
    /// <summary>Both players present, WebRTC handshake in flight.</summary>
    LinkingUp,
    Connected,
    Failed,
}

/// <summary>
/// The online match session: drives NetBridge through the whole connect flow
/// (signaling → host/join → WebRTC offer/answer/ICE → live peer link) and
/// owns the two DataChannels once they're up.
///
/// Protocol logic lives here on purpose — the jslib is a dumb pipe, and
/// Server/signaling/test.html proves the same flow browser-side. Phase 2's
/// match sync subscribes to <see cref="EventReceived"/> (reliable) and
/// <see cref="StateReceived"/> (unreliable); the built-in ping runs on the
/// unreliable channel and doubles as the link health meter.
/// </summary>
public class NetSession : MonoBehaviour
{
    public static NetSession Instance { get; private set; }

    const string DefaultServerUrl = "ws://localhost:8787";
    const float PingInterval = 0.5f;
    const float PathInterval = 2f;
    const float HandshakeTimeout = 15f;

    public NetStatus Status { get; private set; } = NetStatus.Offline;
    public string MatchCode { get; private set; } = "";
    public bool IsHost { get; private set; }
    /// <summary>Round-trip time over the unreliable channel; -1 until measured.</summary>
    public float RttMs { get; private set; } = -1f;
    /// <summary>"direct", "relay", or "unknown" — which path ICE selected.</summary>
    public string Path { get; private set; } = "unknown";
    public string FailReason { get; private set; } = "";

    /// <summary>Game messages from the reliable channel (phase 2 match sync).</summary>
    public event Action<string> EventReceived;
    /// <summary>Game messages from the unreliable channel (phase 2 snapshots).</summary>
    public event Action<string> StateReceived;

    readonly byte[] _buffer = new byte[16384];
    string _iceJson = "[]";
    bool _rtcRunning;
    bool _sentIntro;
    float _handshakeDeadline;
    float _nextPing;
    float _nextPath;

    [Serializable]
    class SigMsg { public string t; public string code; public string reason; public string ice; public SigPayload data; }
    [Serializable]
    class SigPayload { public string kind; public string payload; }
    [Serializable]
    class SignalOut { public string t = "signal"; public SigPayload data; }

    public static NetSession Ensure()
    {
        if (Instance == null)
            Instance = new GameObject("NetSession").AddComponent<NetSession>();
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

    void OnDestroy()
    {
        if (Instance == this)
        {
            Disconnect();
            Instance = null;
        }
    }

    /// <summary>
    /// Signaling server address: `?net=wss://…` on the page URL wins, so a
    /// deployed build can point at the real server without a rebuild.
    /// </summary>
    public static string ServerUrl
    {
        get
        {
            string absolute = Application.absoluteURL;
            if (!string.IsNullOrEmpty(absolute))
            {
                int at = absolute.IndexOf("net=", StringComparison.Ordinal);
                if (at >= 0)
                {
                    string tail = absolute.Substring(at + 4);
                    int amp = tail.IndexOf('&');
                    if (amp >= 0) tail = tail.Substring(0, amp);
                    tail = Uri.UnescapeDataString(tail);
                    if (tail.StartsWith("ws", StringComparison.Ordinal))
                        return tail;
                }
            }
            return DefaultServerUrl;
        }
    }

    public void Host() => Begin(isHost: true, code: "");

    public void Join(string code) => Begin(isHost: false, SanitizeCode(code));

    void Begin(bool isHost, string code)
    {
        if (!NetBridge.Available)
        {
            FailReason = "online play runs in the web build";
            Status = NetStatus.Failed;
            return;
        }
        if (!isHost && code.Length == 0)
        {
            FailReason = "type the match code first";
            Status = NetStatus.Failed;
            return;
        }

        Disconnect();
        IsHost = isHost;
        MatchCode = code;
        FailReason = "";
        RttMs = -1f;
        Path = "unknown";
        _sentIntro = false;
        Status = NetStatus.Connecting;
        NetBridge.PN_SigConnect(ServerUrl);
    }

    public void Disconnect()
    {
        NetBridge.PN_RtcClose();
        NetBridge.PN_SigClose();
        _rtcRunning = false;
        MatchCode = "";
        RttMs = -1f;
        Path = "unknown";
        Status = NetStatus.Offline;
    }

    void Update()
    {
        if (Status == NetStatus.Offline || Status == NetStatus.Failed)
            return;

        PumpSignaling();
        if (_rtcRunning)
            PumpRtc();

        // Stuck handshakes fail loudly. Hosting is exempt — waiting for a
        // friend to type the code takes as long as it takes.
        bool awaitingHandshake =
            (Status == NetStatus.Connecting && _sentIntro) || Status == NetStatus.LinkingUp;
        if (awaitingHandshake && Time.unscaledTime > _handshakeDeadline)
            Fail("connection timed out");
    }

    void PumpSignaling()
    {
        int sigState = NetBridge.PN_SigState();

        if (Status == NetStatus.Connecting && sigState == NetBridge.SigOpen && !_sentIntro)
        {
            _sentIntro = true;
            _handshakeDeadline = Time.unscaledTime + HandshakeTimeout;
            NetBridge.PN_SigSend(IsHost
                ? "{\"t\":\"host\"}"
                : "{\"t\":\"join\",\"code\":\"" + MatchCode + "\"}");
        }

        if (sigState == NetBridge.SigClosed && Status != NetStatus.Connected)
        {
            // Once the peer link is live the signaling socket is expendable —
            // before that, losing it means the handshake can't finish.
            Fail(_sentIntro ? "lost the match server" : "can't reach the match server");
            return;
        }

        int len;
        while ((len = NetBridge.PN_SigPoll(_buffer, _buffer.Length)) > 0)
            HandleSignal(Encoding.UTF8.GetString(_buffer, 0, len));
    }

    void HandleSignal(string raw)
    {
        SigMsg msg;
        try { msg = JsonUtility.FromJson<SigMsg>(raw); }
        catch { return; }
        if (msg == null || string.IsNullOrEmpty(msg.t))
            return;

        switch (msg.t)
        {
            case "hosted":
                MatchCode = msg.code ?? "";
                if (!string.IsNullOrEmpty(msg.ice)) _iceJson = msg.ice;
                Status = NetStatus.Hosting;
                break;

            case "peer-joined":
                // A challenger arrived — host side builds the link as offerer.
                StartRtc(asOfferer: true);
                break;

            case "joined":
                if (!string.IsNullOrEmpty(msg.ice)) _iceJson = msg.ice;
                StartRtc(asOfferer: false);
                break;

            case "signal":
                if (msg.data == null || !_rtcRunning)
                    break;
                if (msg.data.kind == "desc")
                    NetBridge.PN_RtcSetRemoteDesc(msg.data.payload);
                else if (msg.data.kind == "ice")
                    NetBridge.PN_RtcAddRemoteIce(msg.data.payload);
                break;

            case "peer-left":
                if (Status != NetStatus.Connected)
                {
                    // Challenger bailed mid-handshake: the host goes back to
                    // waiting, a guest has nobody left to join.
                    NetBridge.PN_RtcClose();
                    _rtcRunning = false;
                    if (IsHost)
                        Status = NetStatus.Hosting;
                    else
                        Fail("the host left");
                }
                // Once Connected, the peer link's own state decides.
                break;

            case "error":
                Fail(DescribeServerError(msg.reason));
                break;
        }
    }

    void StartRtc(bool asOfferer)
    {
        // forceRelay stays off for friend matches; it becomes the privacy
        // switch (TURN-only, no IP exposure) if stranger matchmaking ever ships.
        NetBridge.PN_RtcStart(asOfferer ? 1 : 0, _iceJson, forceRelay: 0);
        _rtcRunning = true;
        Status = NetStatus.LinkingUp;
        _handshakeDeadline = Time.unscaledTime + HandshakeTimeout;
    }

    void PumpRtc()
    {
        int len;
        // Local SDP + ICE trickle out through the signaling relay.
        while ((len = NetBridge.PN_RtcPollLocalDesc(_buffer, _buffer.Length)) > 0)
            SendSignal("desc", Encoding.UTF8.GetString(_buffer, 0, len));
        while ((len = NetBridge.PN_RtcPollLocalIce(_buffer, _buffer.Length)) > 0)
            SendSignal("ice", Encoding.UTF8.GetString(_buffer, 0, len));

        int rtc = NetBridge.PN_RtcState();
        if (rtc == NetBridge.RtcConnected && Status != NetStatus.Connected)
        {
            Status = NetStatus.Connected;
            _nextPing = 0f;
            _nextPath = 0f;
        }
        else if (rtc == NetBridge.RtcFailed)
        {
            Fail(Status == NetStatus.Connected ? "connection lost" : "couldn't reach the other player");
            return;
        }

        if (Status != NetStatus.Connected)
            return;

        while ((len = NetBridge.PN_RtcPoll(NetBridge.ChannelEvents, _buffer, _buffer.Length)) > 0)
            EventReceived?.Invoke(Encoding.UTF8.GetString(_buffer, 0, len));
        while ((len = NetBridge.PN_RtcPoll(NetBridge.ChannelState, _buffer, _buffer.Length)) > 0)
            HandleStateMessage(Encoding.UTF8.GetString(_buffer, 0, len));

        if (Time.unscaledTime >= _nextPing)
        {
            _nextPing = Time.unscaledTime + PingInterval;
            SendState("p|" + Time.unscaledTime.ToString("F3", CultureInfo.InvariantCulture));
        }

        if (Time.unscaledTime >= _nextPath)
        {
            _nextPath = Time.unscaledTime + PathInterval;
            // Read the previous async sample, then kick the next one.
            int n = NetBridge.PN_RtcGetPath(_buffer, _buffer.Length);
            if (n > 0)
                Path = Encoding.UTF8.GetString(_buffer, 0, n);
            NetBridge.PN_RtcUpdatePath();
        }
    }

    /// <summary>
    /// Ping/echo runs inside the state channel with reserved "p|"/"q|"
    /// prefixes; everything else is game traffic and passes through.
    /// </summary>
    void HandleStateMessage(string msg)
    {
        if (msg.StartsWith("p|", StringComparison.Ordinal))
        {
            SendState("q|" + msg.Substring(2));
            return;
        }
        if (msg.StartsWith("q|", StringComparison.Ordinal))
        {
            if (float.TryParse(msg.Substring(2), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float sentAt))
                RttMs = Mathf.Max(0f, (Time.unscaledTime - sentAt) * 1000f);
            return;
        }
        StateReceived?.Invoke(msg);
    }

    /// <summary>Send on the reliable channel. False when the link isn't up.</summary>
    public bool SendEvent(string msg) => NetBridge.PN_RtcSend(NetBridge.ChannelEvents, msg) == 1;

    /// <summary>Send on the unreliable channel. False when the link isn't up.</summary>
    public bool SendState(string msg) => NetBridge.PN_RtcSend(NetBridge.ChannelState, msg) == 1;

    void SendSignal(string kind, string payload)
    {
        var wrapped = new SignalOut { data = new SigPayload { kind = kind, payload = payload } };
        NetBridge.PN_SigSend(JsonUtility.ToJson(wrapped));
    }

    void Fail(string reason)
    {
        NetBridge.PN_RtcClose();
        NetBridge.PN_SigClose();
        _rtcRunning = false;
        FailReason = reason;
        Status = NetStatus.Failed;
    }

    /// <summary>Match codes are A–Z / 2–9 only; typed input arrives messier.</summary>
    public static string SanitizeCode(string code)
    {
        if (string.IsNullOrEmpty(code))
            return "";
        var sb = new StringBuilder(code.Length);
        foreach (char c in code)
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToUpperInvariant(c));
        return sb.ToString();
    }

    static string DescribeServerError(string reason) => reason switch
    {
        "no-such-room" => "no match with that code",
        "room-full" => "that match already has two players",
        "room-expired" => "the match code expired — host again",
        _ => "server error: " + reason,
    };

    /// <summary>One-line link status for menus and HUDs.</summary>
    public string StatusLine => Status switch
    {
        NetStatus.Connecting => "calling the match server...",
        NetStatus.Hosting => "waiting for a challenger...",
        NetStatus.LinkingUp => "linking up...",
        NetStatus.Connected =>
            (RttMs >= 0 ? Mathf.RoundToInt(RttMs) + " ms" : "linked") +
            (Path == "direct" ? "  ·  direct link" : Path == "relay" ? "  ·  via relay" : ""),
        NetStatus.Failed => FailReason,
        _ => "",
    };
}
