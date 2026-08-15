// The browser half of the share loop: the address bar, and the share sheet.
//
// Everything here that touches the clipboard, the filesystem or the OS share
// menu runs inside a REAL DOM click handler, which is the whole reason this
// is a DOM overlay rather than a Unity canvas panel. Unity processes its own
// input inside requestAnimationFrame, one tick after the browser's click —
// far enough out that Safari has already dropped the user activation those
// APIs require, and a "copy" that silently does nothing is worse for a kid
// than no button at all. Unity renders the card and hands it over; the
// browser owns every button that needs permission.
mergeInto(LibraryManager.library, {

  // location.hash is authoritative where Application.absoluteURL is a snapshot
  // taken at startup: a challenge can arrive by the player editing the hash,
  // or by a second link opened in the same tab.
  ShareLocationHash: function () {
    var s = "";
    try { s = window.location.hash || ""; } catch (e) {}
    var len = lengthBytesUTF8(s) + 1;
    var buf = _malloc(len);
    stringToUTF8(s, buf, len);
    return buf;
  },

  // replaceState, not a hash assignment: assigning would push a history entry,
  // so Back would walk the player through every challenge they had consumed.
  ShareClearHash: function () {
    try {
      if (window.history && window.history.replaceState)
        window.history.replaceState(null, "", window.location.pathname + window.location.search);
    } catch (e) {}
  },

  ShareHasNative: function () {
    try { return (navigator.share) ? 1 : 0; } catch (e) { return 0; }
  },

  // One sheet, built fresh each time and torn down on close. `image` is a
  // PNG data URL or "" — Unity rendered it offscreen, so it is exactly the
  // same card whatever shape the player's window happens to be.
  ShareOpenSheet: function (headlinePtr, boastPtr, urlPtr, imagePtr, filenamePtr) {
    var headline = UTF8ToString(headlinePtr);
    var boast = UTF8ToString(boastPtr);
    var url = UTF8ToString(urlPtr);
    var image = UTF8ToString(imagePtr);
    var filename = UTF8ToString(filenamePtr) || "jet-armor-heroes.png";

    try {
      var existing = document.getElementById("jah-share");
      if (existing) existing.parentNode.removeChild(existing);

      var wrap = document.createElement("div");
      wrap.id = "jah-share";
      wrap.setAttribute("style", [
        "position:fixed", "inset:0", "z-index:2147483000",
        "background:rgba(2,8,14,0.86)",
        "display:flex", "align-items:center", "justify-content:center",
        "padding:16px", "box-sizing:border-box",
        "font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif"
      ].join(";"));

      var card = document.createElement("div");
      card.setAttribute("style", [
        "background:#08161f", "border:1px solid rgba(64,214,255,0.35)",
        "border-radius:14px", "padding:20px", "max-width:640px", "width:100%",
        "max-height:100%", "overflow:auto", "box-sizing:border-box",
        "box-shadow:0 24px 60px rgba(0,0,0,0.6)", "text-align:center"
      ].join(";"));

      var title = document.createElement("div");
      title.textContent = headline;
      title.setAttribute("style",
        "color:#40d6ff;font-weight:800;font-size:20px;letter-spacing:0.04em;margin-bottom:4px");
      card.appendChild(title);

      if (boast) {
        var sub = document.createElement("div");
        sub.textContent = boast;
        sub.setAttribute("style",
          "color:rgba(255,255,255,0.62);font-size:14px;margin-bottom:14px");
        card.appendChild(sub);
      }

      var file = null;
      if (image) {
        var shot = document.createElement("img");
        shot.src = image;
        shot.alt = headline;
        shot.setAttribute("style",
          "width:100%;height:auto;border-radius:10px;display:block;margin-bottom:16px");
        card.appendChild(shot);

        // Built once, up front: navigator.share must be called synchronously
        // inside the click, so there is no room to decode the data URL there.
        try {
          var comma = image.indexOf(",");
          var binary = atob(image.substring(comma + 1));
          var bytes = new Uint8Array(binary.length);
          for (var i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
          file = new File([bytes], filename, { type: "image/png" });
        } catch (e) { file = null; }
      }

      var row = document.createElement("div");
      row.setAttribute("style",
        "display:flex;gap:10px;flex-wrap:wrap;justify-content:center;margin-bottom:14px");
      card.appendChild(row);

      function button(label, accent) {
        var b = document.createElement("button");
        b.textContent = label;
        b.setAttribute("style", [
          "flex:1 1 160px", "min-height:52px", "cursor:pointer",
          "border-radius:10px", "font-size:16px", "font-weight:700",
          "letter-spacing:0.03em", "padding:0 18px",
          accent ? "background:#0f9dc7" : "background:#12293a",
          accent ? "color:#02121a" : "color:#cfeeff",
          "border:1px solid " + (accent ? "#40d6ff" : "rgba(64,214,255,0.3)")
        ].join(";"));
        row.appendChild(b);
        return b;
      }

      // The OS share sheet is the shortest path a kid has to a group chat, so
      // it leads wherever it exists. Files first, link-only as the fallback:
      // Safari on older iOS advertises share() but refuses file payloads.
      var canShare = false;
      try { canShare = !!navigator.share; } catch (e) {}
      if (canShare) {
        var shareBtn = button("Share", true);
        shareBtn.onclick = function () {
          var payload = { title: "Jet Armor Heroes", text: boast || headline, url: url };
          try {
            if (file && navigator.canShare && navigator.canShare({ files: [file] }))
              payload.files = [file];
          } catch (e) {}
          try { navigator.share(payload).catch(function () {}); } catch (e) {}
        };
      }

      var copyBtn = button("Copy link", !canShare);
      copyBtn.onclick = function () {
        var done = function () {
          copyBtn.textContent = "Link copied!";
          setTimeout(function () { copyBtn.textContent = "Copy link"; }, 2000);
        };
        try {
          if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(url).then(done, function () { legacyCopy(done); });
            return;
          }
        } catch (e) {}
        legacyCopy(done);
      };

      function legacyCopy(done) {
        try {
          field.focus();
          field.setSelectionRange(0, field.value.length);
          if (document.execCommand("copy")) done();
        } catch (e) {}
      }

      if (image) {
        var save = document.createElement("a");
        save.textContent = "Save picture";
        save.href = image;
        save.download = filename;
        save.setAttribute("style", [
          "flex:1 1 160px", "min-height:52px", "cursor:pointer",
          "display:flex", "align-items:center", "justify-content:center",
          "border-radius:10px", "font-size:16px", "font-weight:700",
          "letter-spacing:0.03em", "text-decoration:none",
          "background:#12293a", "color:#cfeeff",
          "border:1px solid rgba(64,214,255,0.3)"
        ].join(";"));
        row.appendChild(save);
      }

      // The belt-and-braces path, and on a locked-down school Chromebook the
      // only one that always works: the link, visible and pre-selected.
      var field = document.createElement("input");
      field.readOnly = true;
      field.value = url;
      field.setAttribute("style", [
        "width:100%", "box-sizing:border-box", "padding:12px",
        "border-radius:8px", "border:1px solid rgba(64,214,255,0.25)",
        "background:#02121a", "color:#9fdcf5", "font-size:13px",
        "text-align:center", "margin-bottom:14px"
      ].join(";"));
      field.onclick = function () { field.setSelectionRange(0, field.value.length); };
      card.appendChild(field);

      var close = document.createElement("button");
      close.textContent = "Back to the fight";
      close.setAttribute("style", [
        "background:none", "border:none", "cursor:pointer",
        "color:rgba(255,255,255,0.5)", "font-size:14px", "padding:6px"
      ].join(";"));
      card.appendChild(close);

      function dismiss() {
        try {
          if (wrap.parentNode) wrap.parentNode.removeChild(wrap);
          var canvas = document.querySelector("#unity-canvas");
          if (canvas) canvas.focus();
        } catch (e) {}
      }
      close.onclick = dismiss;
      wrap.onclick = function (e) { if (e.target === wrap) dismiss(); };

      wrap.appendChild(card);
      document.body.appendChild(wrap);
      field.focus();
      field.setSelectionRange(0, field.value.length);
    } catch (e) {
      // A share sheet that throws must never take the game down with it.
      try { console.warn("[share] sheet failed", e); } catch (e2) {}
    }
  }
});
