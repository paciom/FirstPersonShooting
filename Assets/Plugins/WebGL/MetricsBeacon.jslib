mergeInto(LibraryManager.library, {
  // Final-flush path for WebGL: sendBeacon survives page close where fetch/XHR die.
  // Blob is created WITHOUT a content type on purpose — typing it application/json
  // makes sendBeacon a CORS-preflighted request, which beacons cannot perform, and
  // the call fails silently in Chromium. The ingest Function parses JSON regardless.
  MetricsBeacon: function (urlPtr, jsonPtr) {
    var url = UTF8ToString(urlPtr);
    var body = UTF8ToString(jsonPtr);
    try {
      if (navigator.sendBeacon && navigator.sendBeacon(url, new Blob([body]))) return;
    } catch (e) {}
    try {
      fetch(url, { method: 'POST', body: body, keepalive: true, headers: { 'Content-Type': 'text/plain' } });
    } catch (e) {}
  }
});
