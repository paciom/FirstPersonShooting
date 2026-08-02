using System.Runtime.InteropServices;

/// <summary>
/// Raw bridge to Assets/Plugins/WebGL/PhotonNet.jslib — a WebSocket to the
/// signaling server plus a WebRTC peer connection with two DataChannels.
///
/// WebGL builds only; everywhere else the stubs report an unavailable network
/// so menus can say so instead of exploding. All strings cross the boundary
/// through caller-supplied UTF-8 buffers: poll functions write into the buffer
/// and return bytes written, 0 when nothing is queued — no per-message
/// allocation on either side of the boundary, so per-frame polling is free.
/// </summary>
public static class NetBridge
{
    /// <summary>Reliable/ordered channel — game events.</summary>
    public const int ChannelEvents = 0;
    /// <summary>Unreliable/unordered channel — state snapshots and pings.</summary>
    public const int ChannelState = 1;

    // Signaling socket states (PN_SigState).
    public const int SigNone = 0, SigConnecting = 1, SigOpen = 2, SigClosed = 3;

    // Peer connection states (PN_RtcState). Degraded = ICE "disconnected",
    // which browsers often recover from — not yet a failure.
    public const int RtcNone = 0, RtcConnecting = 1, RtcConnected = 2,
        RtcFailed = 3, RtcDegraded = 4;

#if UNITY_WEBGL && !UNITY_EDITOR
    public static bool Available => true;

    [DllImport("__Internal")] public static extern void PN_SigConnect(string url);
    [DllImport("__Internal")] public static extern int PN_SigState();
    [DllImport("__Internal")] public static extern int PN_SigSend(string msg);
    [DllImport("__Internal")] public static extern int PN_SigPoll(byte[] buffer, int size);
    [DllImport("__Internal")] public static extern void PN_SigClose();

    [DllImport("__Internal")] public static extern void PN_RtcStart(int isHost, string iceJson, int forceRelay);
    [DllImport("__Internal")] public static extern void PN_RtcSetRemoteDesc(string json);
    [DllImport("__Internal")] public static extern void PN_RtcAddRemoteIce(string json);
    [DllImport("__Internal")] public static extern int PN_RtcPollLocalDesc(byte[] buffer, int size);
    [DllImport("__Internal")] public static extern int PN_RtcPollLocalIce(byte[] buffer, int size);
    [DllImport("__Internal")] public static extern int PN_RtcState();
    [DllImport("__Internal")] public static extern int PN_RtcSend(int channel, string msg);
    [DllImport("__Internal")] public static extern int PN_RtcPoll(int channel, byte[] buffer, int size);
    [DllImport("__Internal")] public static extern void PN_RtcUpdatePath();
    [DllImport("__Internal")] public static extern int PN_RtcGetPath(byte[] buffer, int size);
    [DllImport("__Internal")] public static extern int PN_RtcDropped();
    [DllImport("__Internal")] public static extern void PN_RtcClose();
#else
    public static bool Available => false;

    public static void PN_SigConnect(string url) { }
    public static int PN_SigState() => SigNone;
    public static int PN_SigSend(string msg) => 0;
    public static int PN_SigPoll(byte[] buffer, int size) => 0;
    public static void PN_SigClose() { }

    public static void PN_RtcStart(int isHost, string iceJson, int forceRelay) { }
    public static void PN_RtcSetRemoteDesc(string json) { }
    public static void PN_RtcAddRemoteIce(string json) { }
    public static int PN_RtcPollLocalDesc(byte[] buffer, int size) => 0;
    public static int PN_RtcPollLocalIce(byte[] buffer, int size) => 0;
    public static int PN_RtcState() => RtcNone;
    public static int PN_RtcSend(int channel, string msg) => 0;
    public static int PN_RtcPoll(int channel, byte[] buffer, int size) => 0;
    public static void PN_RtcUpdatePath() { }
    public static int PN_RtcGetPath(byte[] buffer, int size) => 0;
    public static int PN_RtcDropped() => 0;
    public static void PN_RtcClose() { }
#endif
}
