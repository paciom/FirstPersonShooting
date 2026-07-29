// Photon Arena WebGL network bridge.
//
// Thin, dumb pipes only: a WebSocket to the signaling server and an
// RTCPeerConnection with two DataChannels. All protocol logic (host/join
// flow, when to offer, what to do with messages) lives in C# — NetSession.cs
// drives these functions and polls the queues every frame.
//
// The browser test harness Server/signaling/test.html exercises this exact
// protocol shape; if the harness passes and this file mirrors it, the Unity
// build talks the same language.
//
// Strings cross the boundary through caller-supplied buffers (PN_*Poll writes
// UTF-8 into a byte[] C# passes in) — no per-message _malloc, so per-frame
// polling never churns the emscripten heap.
//
// Channels: 0 = "ev" reliable/ordered (events), 1 = "st" unreliable/unordered
// (state snapshots). The host creates both; the guest adopts them by label.

var PhotonNetLib = {
  $PN: {
    ws: null,
    wsState: 0,        // 0 none, 1 connecting, 2 open, 3 closed/error
    sigQueue: [],
    pc: null,
    rtcState: 0,       // 0 none, 1 connecting, 2 connected, 3 failed/closed, 4 degraded
    channels: [null, null],
    chQueues: [[], []],
    localDescQueue: [],
    localIceQueue: [],
    path: "unknown",
    droppedMessages: 0,

    SIG_QUEUE_CAP: 256,
    EV_QUEUE_CAP: 1024,
    ST_QUEUE_CAP: 256,

    // RTC-scoped state only. The signaling queue deliberately survives:
    // PN_RtcStart runs from inside C#'s signaling drain loop, and wiping
    // sigQueue there would destroy an offer/ICE already queued behind the
    // "joined" message that triggered the start.
    reset: function () {
      PN.chQueues = [[], []];
      PN.localDescQueue = [];
      PN.localIceQueue = [];
      PN.path = "unknown";
      PN.droppedMessages = 0;
    },

    pushCapped: function (queue, cap, item) {
      // Empty strings would read as "queue empty" on the C# side.
      if (!item) return;
      queue.push(item);
      // Oldest-first drop: for state snapshots the newest is the valuable one.
      while (queue.length > cap) {
        queue.shift();
        PN.droppedMessages++;
      }
    },

    // Writes str into (ptr, max) as NUL-terminated UTF-8.
    // Returns bytes written (excluding NUL), or -2 if it did not fit.
    writeString: function (str, ptr, max) {
      var size = lengthBytesUTF8(str);
      if (size + 1 > max) return -2;
      stringToUTF8(str, ptr, max);
      return size;
    },

    pollInto: function (queue, ptr, max) {
      while (queue.length > 0) {
        var written = PN.writeString(queue[0], ptr, max);
        queue.shift();
        // A message too big for the buffer is dropped, not truncated —
        // half a JSON payload is worse than none. Try the next one.
        if (written !== -2) return written;
        console.error("PhotonNet: dropped a message larger than the C# buffer");
        PN.droppedMessages++;
      }
      return 0;
    },

    adoptChannel: function (ch) {
      var index = { ev: 0, st: 1 }[ch.label];
      if (index === undefined) return; // unknown label — never clobber a slot
      PN.channels[index] = ch;
      ch.onmessage = function (e) {
        if (typeof e.data !== "string") return;
        if (index === 0) {
          // The reliable channel must never silently drop: an overflow here
          // (C# not polling for minutes) is a corrupted protocol, and the
          // honest outcome is a failed link, not missing events.
          if (PN.chQueues[0].length >= PN.EV_QUEUE_CAP) {
            console.error("PhotonNet: reliable queue overflow — failing the link");
            PN.rtcState = 3;
            return;
          }
          PN.pushCapped(PN.chQueues[0], PN.EV_QUEUE_CAP, e.data);
        } else {
          PN.pushCapped(PN.chQueues[1], PN.ST_QUEUE_CAP, e.data);
        }
      };
      ch.onopen = function () { PN.refreshRtcState(); };
      ch.onclose = function () { PN.refreshRtcState(); };
    },

    refreshRtcState: function () {
      if (!PN.pc) { PN.rtcState = 0; return; }
      var cs = PN.pc.connectionState;
      var channelsOpen =
        PN.channels[0] && PN.channels[0].readyState === "open" &&
        PN.channels[1] && PN.channels[1].readyState === "open";
      if (cs === "failed" || cs === "closed") PN.rtcState = 3;
      else if (cs === "disconnected") PN.rtcState = 4;
      else if (cs === "connected" && channelsOpen) PN.rtcState = 2;
      else PN.rtcState = 1;
    },
  },

  // ---------- signaling ----------

  PN_SigConnect: function (urlPtr) {
    var url = UTF8ToString(urlPtr);
    try {
      if (PN.ws) {
        // Detach EVERYTHING before closing: close() on a still-CONNECTING
        // socket fires an async error event, and a stale onerror writing to
        // the shared state would kill the replacement session.
        PN.ws.onopen = PN.ws.onclose = PN.ws.onerror = PN.ws.onmessage = null;
        try { PN.ws.close(); } catch (err) {}
      }
      PN.sigQueue = [];
      var sock = new WebSocket(url);
      PN.ws = sock;
      PN.wsState = 1;
      // Each handler is bound to its own socket — a late event from a
      // replaced socket must never touch the live session's state.
      sock.onopen = function () { if (PN.ws === sock) PN.wsState = 2; };
      sock.onclose = function () { if (PN.ws === sock) PN.wsState = 3; };
      sock.onerror = function () { if (PN.ws === sock) PN.wsState = 3; };
      sock.onmessage = function (e) {
        if (PN.ws === sock && typeof e.data === "string")
          PN.pushCapped(PN.sigQueue, PN.SIG_QUEUE_CAP, e.data);
      };
    } catch (err) {
      console.error("PN_SigConnect:", err);
      PN.wsState = 3;
    }
  },

  PN_SigState: function () { return PN.wsState; },

  PN_SigSend: function (msgPtr) {
    if (!PN.ws || PN.wsState !== 2) return 0;
    try { PN.ws.send(UTF8ToString(msgPtr)); return 1; }
    catch (err) { console.error("PN_SigSend:", err); return 0; }
  },

  PN_SigPoll: function (ptr, max) { return PN.pollInto(PN.sigQueue, ptr, max); },

  PN_SigClose: function () {
    if (PN.ws) {
      PN.ws.onopen = PN.ws.onclose = PN.ws.onerror = PN.ws.onmessage = null;
      try { PN.ws.close(); } catch (err) {}
      PN.ws = null;
    }
    PN.wsState = 0;
    PN.sigQueue = [];
  },

  // ---------- WebRTC ----------

  PN_RtcStart: function (isHost, iceJsonPtr, forceRelay) {
    try {
      if (PN.pc) { try { PN.pc.close(); } catch (err) {} }
      PN.reset();
      PN.channels = [null, null];

      var config = { iceServers: [] };
      try { config.iceServers = JSON.parse(UTF8ToString(iceJsonPtr)) || []; }
      catch (err) { console.error("PN_RtcStart: bad ice json", err); }
      if (forceRelay) config.iceTransportPolicy = "relay";

      var pc = new RTCPeerConnection(config);
      PN.pc = pc;
      PN.rtcState = 1;

      pc.onicecandidate = function (e) {
        if (e.candidate)
          PN.localIceQueue.push(JSON.stringify(e.candidate.toJSON()));
      };
      pc.onconnectionstatechange = function () { PN.refreshRtcState(); };

      if (isHost) {
        PN.adoptChannel(pc.createDataChannel("ev", { ordered: true }));
        PN.adoptChannel(pc.createDataChannel("st", { ordered: false, maxRetransmits: 0 }));
        pc.createOffer()
          .then(function (d) { return pc.setLocalDescription(d); })
          .then(function () {
            PN.localDescQueue.push(JSON.stringify(pc.localDescription.toJSON()));
          })
          .catch(function (err) {
            console.error("PN_RtcStart offer:", err);
            PN.rtcState = 3;
          });
      } else {
        pc.ondatachannel = function (e) { PN.adoptChannel(e.channel); };
      }
    } catch (err) {
      console.error("PN_RtcStart:", err);
      PN.rtcState = 3;
    }
  },

  // Applies the remote description. When it is an offer (guest side), the
  // answer is created here — an async browser API, so it surfaces through
  // the same local-desc queue the offer does on the host.
  PN_RtcSetRemoteDesc: function (jsonPtr) {
    if (!PN.pc) return;
    var pc = PN.pc;
    var desc;
    try { desc = JSON.parse(UTF8ToString(jsonPtr)); }
    catch (err) { console.error("PN_RtcSetRemoteDesc: bad json", err); return; }
    pc.setRemoteDescription(desc)
      .then(function () {
        if (desc.type !== "offer") return null;
        return pc.createAnswer()
          .then(function (a) { return pc.setLocalDescription(a); })
          .then(function () {
            PN.localDescQueue.push(JSON.stringify(pc.localDescription.toJSON()));
          });
      })
      .catch(function (err) {
        console.error("PN_RtcSetRemoteDesc:", err);
        PN.rtcState = 3;
      });
  },

  PN_RtcAddRemoteIce: function (jsonPtr) {
    if (!PN.pc) return;
    try {
      PN.pc.addIceCandidate(JSON.parse(UTF8ToString(jsonPtr)))
        .catch(function (err) { console.warn("addIceCandidate:", err); });
    } catch (err) { console.warn("PN_RtcAddRemoteIce:", err); }
  },

  PN_RtcPollLocalDesc: function (ptr, max) { return PN.pollInto(PN.localDescQueue, ptr, max); },
  PN_RtcPollLocalIce: function (ptr, max) { return PN.pollInto(PN.localIceQueue, ptr, max); },

  PN_RtcState: function () { return PN.rtcState; },

  PN_RtcSend: function (channel, msgPtr) {
    var ch = PN.channels[channel];
    if (!ch || ch.readyState !== "open") return 0;
    try { ch.send(UTF8ToString(msgPtr)); return 1; }
    catch (err) { return 0; }
  },

  PN_RtcPoll: function (channel, ptr, max) {
    return PN.pollInto(PN.chQueues[channel], ptr, max);
  },

  // getStats is async; C# calls Update then reads Get a frame later.
  PN_RtcUpdatePath: function () {
    if (!PN.pc) return;
    PN.pc.getStats().then(function (stats) {
      var pairs = {}, cands = {}, selectedId = null, fallback = null;
      stats.forEach(function (r) {
        if (r.type === "transport" && r.selectedCandidatePairId)
          selectedId = r.selectedCandidatePairId;
        if (r.type === "candidate-pair") {
          pairs[r.id] = r;
          // Heuristic fallback for browsers without transport stats:
          // "selected" is Firefox-only, "nominated" is the Chrome signal.
          if ((r.selected || r.nominated) && r.state === "succeeded")
            fallback = r;
        }
        if (r.type === "local-candidate" || r.type === "remote-candidate")
          cands[r.id] = r;
      });
      var pair = (selectedId && pairs[selectedId]) || fallback;
      if (!pair) { PN.path = "unknown"; return; }
      var local = cands[pair.localCandidateId];
      var remote = cands[pair.remoteCandidateId];
      PN.path = ((local && local.candidateType === "relay") ||
                 (remote && remote.candidateType === "relay")) ? "relay" : "direct";
    }).catch(function () {});
  },

  PN_RtcGetPath: function (ptr, max) {
    var written = PN.writeString(PN.path, ptr, max);
    return written === -2 ? 0 : written;
  },

  PN_RtcDropped: function () { return PN.droppedMessages; },

  PN_RtcClose: function () {
    if (PN.pc) {
      try { PN.pc.close(); } catch (err) {}
      PN.pc = null;
    }
    PN.channels = [null, null];
    PN.rtcState = 0;
    PN.reset();
  },
};

autoAddDeps(PhotonNetLib, "$PN");
mergeInto(LibraryManager.library, PhotonNetLib);
