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

  // A QR encoder small enough to live here, so the sheet can draw the link
  // as a code with no library and no network. Byte mode, error level L,
  // versions 1-9 only — that is ~230 bytes where the longest challenge link
  // is under half that, and every one of those versions splits its
  // Reed-Solomon data into EQUAL-size blocks, which keeps the interleave a
  // plain transpose. Mask pattern is fixed at 0: any mask is a valid code,
  // the penalty scoring only optimises scanability at the margin.
  // Returns { count, dark(row,col) } or null (too long / anything threw).
  $JAHQR: function (text) {
    try {
      var data = [];
      var utf = unescape(encodeURIComponent(text));
      for (var i = 0; i < utf.length; i++) data.push(utf.charCodeAt(i) & 0xff);

      var DATA_CW = [19, 34, 55, 80, 108, 136, 156, 194, 232];
      var EC_CW = [7, 10, 15, 20, 26, 18, 20, 24, 30];
      var BLOCKS = [1, 1, 1, 1, 1, 2, 2, 2, 2];
      var ALIGN = [[], [6, 18], [6, 22], [6, 26], [6, 30], [6, 34],
                   [6, 22, 38], [6, 24, 42], [6, 26, 46]];

      var version = 0;
      for (var v = 1; v <= 9; v++)
        if (data.length <= DATA_CW[v - 1] - 2) { version = v; break; }
      if (!version) return null;
      var dcTotal = DATA_CW[version - 1];

      // Bit stream: mode 0100, 8-bit length, the bytes. The terminator's
      // zero bits come free (untouched bits are already 0), then the spec's
      // alternating pad bytes fill out the data codewords.
      var buf = [], bitLen = 0;
      function put(val, len) {
        for (var b = len - 1; b >= 0; b--) {
          buf[bitLen >> 3] = (buf[bitLen >> 3] | 0) | (((val >>> b) & 1) << (7 - (bitLen & 7)));
          bitLen++;
        }
      }
      put(4, 4);
      put(data.length, 8);
      for (var i = 0; i < data.length; i++) put(data[i], 8);
      var codewords = [];
      for (var i = 0; i < dcTotal; i++) codewords.push(buf[i] | 0);
      for (var i = (bitLen + 7) >> 3, alt = 0; i < dcTotal; i++, alt ^= 1)
        codewords[i] = alt ? 0x11 : 0xec;

      // GF(256) on 0x11d, and the generator polynomial for this EC length.
      var EXP = new Array(512), LOG = new Array(256), x = 1;
      for (var i = 0; i < 255; i++) {
        EXP[i] = x; LOG[x] = i;
        x <<= 1; if (x & 0x100) x ^= 0x11d;
      }
      for (var i = 255; i < 512; i++) EXP[i] = EXP[i - 255];

      var ecLen = EC_CW[version - 1];
      var gen = [1];
      for (var i = 0; i < ecLen; i++) {
        var next = new Array(gen.length + 1);
        for (var j = 0; j < next.length; j++) next[j] = 0;
        for (var j = 0; j < gen.length; j++) {
          next[j] ^= gen[j];
          if (gen[j] !== 0) next[j + 1] ^= EXP[(LOG[gen[j]] + i) % 255];
        }
        gen = next;
      }

      function ecFor(block) {
        var res = block.slice();
        for (var i = 0; i < ecLen; i++) res.push(0);
        for (var i = 0; i < block.length; i++) {
          var factor = res[i];
          if (factor === 0) continue;
          var lg = LOG[factor];
          for (var j = 0; j < gen.length; j++)
            if (gen[j] !== 0) res[i + j] ^= EXP[(lg + LOG[gen[j]]) % 255];
        }
        return res.slice(block.length);
      }

      var nb = BLOCKS[version - 1], per = dcTotal / nb;
      var blocks = [], ecs = [];
      for (var b = 0; b < nb; b++) {
        var blk = codewords.slice(b * per, (b + 1) * per);
        blocks.push(blk);
        ecs.push(ecFor(blk));
      }
      var seq = [];
      for (var i = 0; i < per; i++) for (var b = 0; b < nb; b++) seq.push(blocks[b][i]);
      for (var i = 0; i < ecLen; i++) for (var b = 0; b < nb; b++) seq.push(ecs[b][i]);

      // The matrix. null = still free for data; the function patterns claim
      // their modules first and the zigzag walk below fills what is left.
      var count = 17 + version * 4;
      var mat = new Array(count);
      for (var r = 0; r < count; r++) {
        mat[r] = new Array(count);
        for (var c = 0; c < count; c++) mat[r][c] = null;
      }

      function probe(row, col) {
        for (var r = -1; r <= 7; r++) {
          if (row + r < 0 || count <= row + r) continue;
          for (var c = -1; c <= 7; c++) {
            if (col + c < 0 || count <= col + c) continue;
            mat[row + r][col + c] =
              (0 <= r && r <= 6 && (c === 0 || c === 6)) ||
              (0 <= c && c <= 6 && (r === 0 || r === 6)) ||
              (2 <= r && r <= 4 && 2 <= c && c <= 4);
          }
        }
      }
      probe(0, 0); probe(count - 7, 0); probe(0, count - 7);

      var pos = ALIGN[version - 1];
      for (var i = 0; i < pos.length; i++)
        for (var j = 0; j < pos.length; j++) {
          if (mat[pos[i]][pos[j]] !== null) continue;
          for (var r = -2; r <= 2; r++)
            for (var c = -2; c <= 2; c++)
              mat[pos[i] + r][pos[j] + c] =
                (r === -2 || r === 2 || c === -2 || c === 2 || (r === 0 && c === 0));
        }

      for (var i = 8; i < count - 8; i++) {
        if (mat[i][6] === null) mat[i][6] = (i % 2 === 0);
        if (mat[6][i] === null) mat[6][i] = (i % 2 === 0);
      }

      function bchDigit(d) { var n = 0; while (d !== 0) { n++; d >>>= 1; } return n; }

      // Format info: level L (01) + mask 0, BCH(15,5)-protected, both copies.
      var fmtData = (1 << 3) | 0, G15 = 0x537;
      var rem = fmtData << 10;
      while (bchDigit(rem) - bchDigit(G15) >= 0) rem ^= G15 << (bchDigit(rem) - bchDigit(G15));
      var fmt = ((fmtData << 10) | rem) ^ 0x5412;
      for (var i = 0; i < 15; i++) {
        var on = ((fmt >> i) & 1) === 1;
        if (i < 6) mat[i][8] = on;
        else if (i < 8) mat[i + 1][8] = on;
        else mat[count - 15 + i][8] = on;
        if (i < 8) mat[8][count - i - 1] = on;
        else if (i < 9) mat[8][15 - i] = on;
        else mat[8][15 - i - 1] = on;
      }
      mat[count - 8][8] = true;

      // Version info blocks exist only from version 7 up.
      if (version >= 7) {
        var G18 = 0x1f25, vrem = version << 12;
        while (bchDigit(vrem) - bchDigit(G18) >= 0) vrem ^= G18 << (bchDigit(vrem) - bchDigit(G18));
        var vbits = (version << 12) | vrem;
        for (var i = 0; i < 18; i++) {
          var on = ((vbits >> i) & 1) === 1;
          mat[Math.floor(i / 3)][i % 3 + count - 11] = on;
          mat[i % 3 + count - 11][Math.floor(i / 3)] = on;
        }
      }

      // Data placement: the standard two-column zigzag from the bottom-right,
      // skipping the timing column, mask 0 applied as each bit lands.
      var inc = -1, row = count - 1, bitIdx = 7, byteIdx = 0;
      for (var col = count - 1; col > 0; col -= 2) {
        if (col === 6) col--;
        while (true) {
          for (var c = 0; c < 2; c++) {
            if (mat[row][col - c] === null) {
              var dark = false;
              if (byteIdx < seq.length) dark = ((seq[byteIdx] >>> bitIdx) & 1) === 1;
              if ((row + col - c) % 2 === 0) dark = !dark;
              mat[row][col - c] = dark;
              bitIdx--;
              if (bitIdx === -1) { byteIdx++; bitIdx = 7; }
            }
          }
          row += inc;
          if (row < 0 || count <= row) { row -= inc; inc = -inc; break; }
        }
      }

      return { count: count, dark: function (r, c) { return !!mat[r][c]; } };
    } catch (e) {
      return null;
    }
  },

  // One sheet, built fresh each time and torn down on close. `image` is a
  // PNG data URL or "" — Unity rendered it offscreen, so it is exactly the
  // same card whatever shape the player's window happens to be.
  ShareOpenSheet__deps: ["$JAHQR"],
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

      // The same link as a QR code, right by the copy button: two kids in one
      // room don't need a chat app, one points a phone camera at the other's
      // screen. Drawn one module per fillRect at 4x and left pixelated so the
      // CSS upscale keeps every module square. If the encoder refuses (a URL
      // too long for it, or anything thrown) the sheet simply has no QR.
      try {
        var qr = JAHQR(url);
        if (qr) {
          var quiet = 4, scale = 4, total = qr.count + quiet * 2;
          var qrCanvas = document.createElement("canvas");
          qrCanvas.width = total * scale;
          qrCanvas.height = total * scale;
          var ctx = qrCanvas.getContext("2d");
          ctx.fillStyle = "#ffffff";
          ctx.fillRect(0, 0, qrCanvas.width, qrCanvas.height);
          ctx.fillStyle = "#000000";
          for (var qr_r = 0; qr_r < qr.count; qr_r++)
            for (var qr_c = 0; qr_c < qr.count; qr_c++)
              if (qr.dark(qr_r, qr_c))
                ctx.fillRect((qr_c + quiet) * scale, (qr_r + quiet) * scale, scale, scale);

          var qrBox = document.createElement("div");
          qrBox.setAttribute("style", "margin-bottom:14px");
          qrCanvas.setAttribute("style", [
            "width:156px", "height:156px", "display:block", "margin:0 auto 6px",
            "border-radius:8px", "background:#ffffff", "image-rendering:pixelated"
          ].join(";"));
          qrBox.appendChild(qrCanvas);

          var qrHint = document.createElement("div");
          qrHint.textContent = "Point a phone camera here to join the fight";
          qrHint.setAttribute("style", "color:rgba(255,255,255,0.55);font-size:13px");
          qrBox.appendChild(qrHint);
          card.appendChild(qrBox);
        }
      } catch (e) {}

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
