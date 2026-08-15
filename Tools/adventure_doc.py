"""Validate an adventure graph and render it for reading.

The JSON under Assets/Resources/Adventures is the single source of truth: the
game reads it, and this script is the only other thing allowed to. It checks
the shape the reader assumes (every branching node has exactly two choices,
every link resolves, the graph is acyclic, nobody can walk past maxSteps) and
then renders a script anyone can read without a Unity editor.

    python Tools/adventure_doc.py                       # validate every adventure
    python Tools/adventure_doc.py quiet-confirmation    # one, with a rendered doc
    python Tools/adventure_doc.py quiet-confirmation --html out.html

Exit code is non-zero if any check fails, so this can gate a commit.
"""

import argparse
import base64
import html
import io as _io
import json
import os
import sys
from collections import defaultdict, deque

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ADVENTURES = os.path.join(ROOT, "Assets", "Resources", "Adventures")

REQUIRED_TEXT = ("title", "hook", "body", "cliff")
ENDING_KINDS = ("good", "bad", "strange")

# A beat is thirty seconds of screen. Everything below exists so that a beat
# that says it is thirty seconds actually is one.
BEAT_SECONDS = 30
SHOT_RANGE = (4, 6)
WORDS_PER_SECOND = 2.6          # narration pace; 60 words is ~23s of the 30
VO_WORD_CAP = 60
CAMERAS = {"WIDE", "ESTABLISH", "LOW", "HIGH", "ANGLE", "CLOSE", "EXTREME",
           "MACRO", "INSERT", "OVER-SHOULDER", "TRACKING", "VERTICAL",
           "WHIP-PAN", "CRANE", "UP", "DOWN", "TOP-DOWN", "POV", "TWO-SHOT",
           "HOLD"}


def check_shots(node, errors):
    """The shot list is what makes a node a 30-second video rather than a
    paragraph: without these checks a beat quietly drifts to 12 seconds of
    picture with 40 seconds of narration over it."""
    nid = node["id"]
    shots = node.get("shots")
    if not shots:
        errors.append("%s: no shots — a beat needs a shot list to be filmable" % nid)
        return
    if not SHOT_RANGE[0] <= len(shots) <= SHOT_RANGE[1]:
        errors.append("%s: %d shots, expected %d-%d"
                      % (nid, len(shots), SHOT_RANGE[0], SHOT_RANGE[1]))
    total = sum(s.get("t", 0) for s in shots)
    if total != BEAT_SECONDS:
        errors.append("%s: shots total %ds, must be exactly %d"
                      % (nid, total, BEAT_SECONDS))
    keys = [s for s in shots if s.get("key")]
    if len(keys) != 1:
        errors.append("%s: %d shots marked key, need exactly 1 (it is the still)"
                      % (nid, len(keys)))
    words = sum(len(s.get("vo", "").split()) for s in shots)
    if words > VO_WORD_CAP:
        errors.append("%s: %d words of voice-over, over the cap of %d "
                      "(~%.0fs of speech in %ds of picture)"
                      % (nid, words, VO_WORD_CAP, words / WORDS_PER_SECOND, total))
    for i, shot in enumerate(shots, start=1):
        where = "%s shot %d" % (nid, i)
        if not shot.get("action", "").strip():
            errors.append(where + ": no action")
        if not shot.get("cam", "").strip():
            errors.append(where + ": no camera")
        else:
            for word in shot["cam"].replace(",", " ").split():
                if word.upper() not in CAMERAS:
                    errors.append("%s: unknown camera term '%s'" % (where, word))
        # A line must fit the shot it is spoken over, with a little slack for
        # a hold at the end of the shot.
        spoken = len(shot.get("vo", "").split()) / WORDS_PER_SECOND
        if spoken > shot.get("t", 0) + 1.0:
            errors.append("%s: %.0fs of voice-over in a %ds shot"
                          % (where, spoken, shot.get("t", 0)))


def load(story_id):
    path = os.path.join(ADVENTURES, story_id + ".json")
    with open(path, "r", encoding="utf-8") as handle:
        return json.load(handle)


def validate(story):
    """Returns (errors, stats). Errors are strings; empty means the file is
    safe to ship."""
    errors = []
    nodes = {}
    for node in story["nodes"]:
        if node["id"] in nodes:
            errors.append("duplicate node id: " + node["id"])
        nodes[node["id"]] = node

    start = story["start"]
    if start not in nodes:
        errors.append("start node '%s' does not exist" % start)
        return errors, {}

    for node in story["nodes"]:
        nid = node["id"]
        for field in REQUIRED_TEXT:
            if not node.get(field, "").strip():
                errors.append("%s: empty %s" % (nid, field))
        check_shots(node, errors)
        choices = node.get("choices", [])
        ending = node.get("ending", "")
        if ending:
            if ending not in ENDING_KINDS:
                errors.append("%s: unknown ending kind '%s'" % (nid, ending))
            if choices:
                errors.append("%s: ending nodes must have no choices" % nid)
        else:
            if len(choices) != 2:
                errors.append("%s: has %d choices, expected 2" % (nid, len(choices)))
            for choice in choices:
                if not choice.get("text", "").strip():
                    errors.append("%s: choice with no text" % nid)
                if choice.get("to") not in nodes:
                    errors.append("%s: choice points at missing node '%s'"
                                  % (nid, choice.get("to")))

    if errors:
        return errors, {}

    # Longest path, cycle check and reachability in one walk. Depth is measured
    # in NODES SHOWN (start = 1), because that is what the player counts and
    # what a video mode pays for: depth 10 means ten 30-second clips.
    depth = {start: 1}
    parents = defaultdict(list)
    order = []
    seen = set()
    stack = [(start, iter([c["to"] for c in nodes[start].get("choices", [])]))]
    on_path = {start}
    seen.add(start)
    while stack:
        nid, kids = stack[-1]
        advanced = False
        for kid in kids:
            if kid in on_path:
                errors.append("cycle: %s -> %s (the graph must be a DAG)" % (nid, kid))
                continue
            parents[kid].append(nid)
            if kid not in seen:
                seen.add(kid)
                on_path.add(kid)
                stack.append((kid, iter([c["to"] for c in nodes[kid].get("choices", [])])))
                advanced = True
                break
        if not advanced:
            on_path.discard(nid)
            order.append(nid)
            stack.pop()

    if errors:
        return errors, {}

    # Longest path by relaxing edges in reverse finishing order (topological).
    for nid in reversed(order):
        for choice in nodes[nid].get("choices", []):
            kid = choice["to"]
            depth[kid] = max(depth.get(kid, 0), depth[nid] + 1)

    unreachable = [n["id"] for n in story["nodes"] if n["id"] not in seen]
    for nid in unreachable:
        errors.append("unreachable node: " + nid)

    cap = story.get("maxSteps", 0)
    deepest = max(depth.values())
    if cap and deepest > cap:
        worst = [n for n, d in depth.items() if d == deepest]
        errors.append("longest path is %d nodes, over the cap of %d (at %s)"
                      % (deepest, cap, ", ".join(worst)))

    endings = [n for n in story["nodes"] if n.get("ending")]
    for node in endings:
        if not parents[node["id"]]:
            errors.append("ending %s has no way in" % node["id"])

    stats = {
        "nodes": len(story["nodes"]),
        "branching": len(story["nodes"]) - len(endings),
        "endings": len(endings),
        "by_kind": {k: sum(1 for n in endings if n.get("ending") == k) for k in ENDING_KINDS},
        "longest": deepest,
        "shortest": min(depth[n["id"]] for n in endings),
        "depth": depth,
        "parents": parents,
        "merges": sum(1 for n in story["nodes"] if len(parents[n["id"]]) > 1),
        "words": sum(len((n["hook"] + " " + n["body"] + " " + n["cliff"]).split())
                     for n in story["nodes"]),
        "shots": sum(len(n.get("shots", [])) for n in story["nodes"]),
        "seconds": sum(sum(s.get("t", 0) for s in n.get("shots", []))
                       for n in story["nodes"]),
    }
    return errors, stats


def layers(story, stats):
    """Nodes grouped by longest-path depth — the reading order that keeps a
    graph legible on a page."""
    grouped = defaultdict(list)
    for node in story["nodes"]:
        grouped[stats["depth"][node["id"]]].append(node)
    return [(d, grouped[d]) for d in sorted(grouped)]


def render_markdown(story, stats):
    out = []
    add = out.append
    add("# %s" % story["title"])
    add("")
    add("*%s*" % story["tagline"])
    add("")
    add("**%s**" % story["logline"])
    add("")
    add(story["setting"])
    add("")
    add("*Generated from `Assets/Resources/Adventures/%s.json` by "
        "`python Tools/adventure_doc.py %s --md <file>`. The JSON is the source of "
        "truth; regenerate rather than editing this.*" % (story["id"], story["id"]))
    add("")
    add("%d nodes · %d endings (%d good, %d bad, %d strange) · longest path %d · "
        "shortest %d · %d merge points · ~%d words"
        % (stats["nodes"], stats["endings"], stats["by_kind"]["good"],
           stats["by_kind"]["bad"], stats["by_kind"]["strange"], stats["longest"],
           stats["shortest"], stats["merges"], stats["words"]))
    add("")
    ids = {n["id"]: n for n in story["nodes"]}
    for depth, group in layers(story, stats):
        add("---")
        add("")
        add("## STEP %d" % depth)
        add("")
        for node in group:
            kind = node.get("ending", "")
            label = " — ENDING (%s)" % kind.upper() if kind else ""
            add("### `%s` %s%s" % (node["id"], node["title"], label))
            add("")
            came = stats["parents"].get(node["id"], [])
            if came:
                add("*from: %s*" % ", ".join("`%s`" % p for p in sorted(set(came))))
                add("")
            add("**%s**" % node["hook"])
            add("")
            add(node["body"])
            add("")
            add("![%s](../Assets/Resources/Adventures/%s/%s.png)"
                % (node["title"], story["id"], node["id"]))
            add("")
            add("*%s*" % node["cliff"])
            add("")
            for choice in node.get("choices", []):
                add("- **%s** → `%s` (%s)" % (choice["text"], choice["to"],
                                              ids[choice["to"]]["title"]))
            if node.get("choices"):
                add("")
            add("| # | s | camera | action | voice-over |")
            add("|---|---|---|---|---|")
            for i, shot in enumerate(node.get("shots", []), start=1):
                add("| %d%s | %d | %s | %s | %s |"
                    % (i, " ★" if shot.get("key") else "", shot["t"], shot["cam"],
                       shot["action"].replace("|", "/"),
                       (shot.get("vo") or "—").replace("|", "/")))
            add("")
    return "\n".join(out)


NODE_W, NODE_H = 168, 44
PITCH_X, PITCH_Y = 186, 96
GUTTER, MARGIN = 78, 24


def map_layout(story, stats):
    """Positions for the graph map: one row per step, ordered inside a row by
    the average position of the nodes that lead into it, so the edges cross as
    little as a two-pass barycentre sweep can manage."""
    rows = [group for _, group in layers(story, stats)]
    order = [[n["id"] for n in group] for group in rows]
    kids = {n["id"]: [c["to"] for c in n.get("choices", [])] for n in story["nodes"]}
    parents = stats["parents"]

    def index_of(nid):
        for row in order:
            if nid in row:
                return row.index(nid), len(row)
        return 0, 1

    def centred(nid):
        i, n = index_of(nid)
        return i - (n - 1) / 2.0

    for _ in range(4):
        for row in order[1:]:
            row.sort(key=lambda nid: (sum(centred(p) for p in parents[nid])
                                      / max(1, len(parents[nid]))))
        for row in reversed(order[:-1]):
            row.sort(key=lambda nid: (sum(centred(k) for k in kids[nid])
                                      / max(1, len(kids[nid])) if kids[nid] else 0))

    width = max(len(row) for row in order)
    positions = {}
    for r, row in enumerate(order):
        for i, nid in enumerate(row):
            x = GUTTER + MARGIN + (width - len(row)) * PITCH_X / 2.0 + i * PITCH_X
            positions[nid] = (x, MARGIN + r * PITCH_Y)
    canvas = (GUTTER + MARGIN * 2 + width * PITCH_X - (PITCH_X - NODE_W),
              MARGIN * 2 + len(order) * PITCH_Y - (PITCH_Y - NODE_H))
    return positions, canvas, [row for row in order]


def render_map(story, stats):
    ids = {n["id"]: n for n in story["nodes"]}
    positions, (width, height), rows = map_layout(story, stats)
    out = ['<svg viewBox="0 0 %d %d" role="img" xmlns="http://www.w3.org/2000/svg" '
           'aria-label="Story map: 36 beats over ten steps, with twelve points where '
           'different runs rejoin the same beat.">' % (width, height)]

    for depth, row in enumerate(rows, start=1):
        y = MARGIN + (depth - 1) * PITCH_Y + NODE_H / 2.0
        out.append('<text class="ml" x="%d" y="%.1f">STEP %d</text>' % (MARGIN, y + 4, depth))

    for node in story["nodes"]:
        x0, y0 = positions[node["id"]]
        for choice in node.get("choices", []):
            x1, y1 = positions[choice["to"]]
            merge = len(stats["parents"][choice["to"]]) > 1
            sx, sy = x0 + NODE_W / 2.0, y0 + NODE_H
            ex, ey = x1 + NODE_W / 2.0, y1
            out.append('<path class="e%s" d="M%.1f %.1f C%.1f %.1f %.1f %.1f %.1f %.1f"/>'
                       % (" em" if merge else "", sx, sy, sx, sy + 34, ex, ey - 34, ex, ey))

    for node in story["nodes"]:
        x, y = positions[node["id"]]
        kind = node.get("ending", "")
        klass = "n " + ("n-" + kind if kind else
                        ("n-merge" if len(stats["parents"][node["id"]]) > 1 else "n-beat"))
        out.append('<rect class="%s" x="%.1f" y="%.1f" width="%d" height="%d" rx="6"/>'
                   % (klass, x, y, NODE_W, NODE_H))
        out.append('<text class="nt" x="%.1f" y="%.1f">%s</text>'
                   % (x + NODE_W / 2.0, y + 20, html.escape(node["title"])))
        label = ("ENDING · " + kind.upper()) if kind else node["id"].upper()
        out.append('<text class="ni" x="%.1f" y="%.1f">%s</text>'
                   % (x + NODE_W / 2.0, y + 34, html.escape(label)))

    out.append("</svg>")
    return "".join(out)


PAGE_CSS = """
:root{
  --ground:#eceff2; --surface:#ffffff; --sunk:#e3e8ec; --line:#c8d2d9;
  --ink:#101c25; --muted:#55656f; --faint:#5f6f7b;
  --warden:#0c7286; --foundry:#a75500;
  --good:#15704a; --bad:#a51f28; --strange:#5f3aa6;
  --display:"Arial Narrow","Helvetica Neue Condensed",
            "Liberation Sans Narrow",Arial,sans-serif;
  --body:Georgia,"Iowan Old Style","Times New Roman",serif;
  --data:ui-monospace,"Cascadia Mono",Consolas,"DejaVu Sans Mono",monospace;
}
@media (prefers-color-scheme:dark){
  :root:not([data-theme="light"]){
    --ground:#070f16; --surface:#0d1a24; --sunk:#0a151d; --line:#1d3040;
    --ink:#dae5ee; --muted:#8fa5b5; --faint:#7a90a2;
    --warden:#3fc9e6; --foundry:#ff9b3d;
    --good:#5ad894; --bad:#ff7070; --strange:#bb90ff;
  }
}
:root[data-theme="dark"]{
  --ground:#070f16; --surface:#0d1a24; --sunk:#0a151d; --line:#1d3040;
  --ink:#dae5ee; --muted:#8fa5b5; --faint:#7a90a2;
  --warden:#3fc9e6; --foundry:#ff9b3d;
  --good:#5ad894; --bad:#ff7070; --strange:#bb90ff;
}
*{box-sizing:border-box}
body{background:var(--ground);color:var(--ink);font-family:var(--body);
     font-size:17px;line-height:1.62;margin:0}
.wrap{max-width:1180px;margin:0 auto;padding:56px 24px 120px}
.eyebrow{font-family:var(--display);text-transform:uppercase;letter-spacing:.26em;
         font-size:13px;color:var(--foundry);margin:0 0 18px}
h1{font-family:var(--display);text-transform:uppercase;letter-spacing:.05em;
   font-size:clamp(44px,8vw,86px);line-height:.94;margin:0;text-wrap:balance;
   color:var(--ink)}
.logline{font-size:clamp(20px,2.4vw,25px);line-height:1.42;max-width:30em;
         margin:22px 0 0;color:var(--ink)}
.setting{max-width:34em;margin:14px 0 0;color:var(--muted);font-size:16px}
.strip{display:flex;flex-wrap:wrap;gap:0;margin:34px 0 0;border:1px solid var(--line);
       border-radius:8px;overflow:hidden;background:var(--surface)}
.stat{flex:1 1 150px;padding:14px 18px;border-right:1px solid var(--line)}
.stat:last-child{border-right:0}
.stat b{display:block;font-family:var(--data);font-size:22px;font-variant-numeric:tabular-nums;
        color:var(--warden);font-weight:600}
.stat span{font-family:var(--display);text-transform:uppercase;letter-spacing:.16em;
           font-size:11px;color:var(--faint)}
figure{margin:56px 0 0}
figure svg{width:100%;height:auto;display:block}
.mapbox{border:1px solid var(--line);border-radius:10px;background:var(--surface);
        padding:18px;overflow-x:auto}
figcaption{color:var(--muted);font-size:15px;margin-top:12px;max-width:44em}
.e{fill:none;stroke:var(--line);stroke-width:1.4}
.e.em{stroke:var(--warden);stroke-width:1.6;opacity:.75}
.n{fill:var(--sunk);stroke:var(--line);stroke-width:1.4}
.n-merge{stroke:var(--warden);stroke-width:2}
.n-good{stroke:var(--good);stroke-width:2}
.n-bad{stroke:var(--bad);stroke-width:2}
.n-strange{stroke:var(--strange);stroke-width:2}
.nt{font-family:var(--display);text-transform:uppercase;letter-spacing:.06em;
    font-size:12.5px;fill:var(--ink);text-anchor:middle}
.ni,.ml{font-family:var(--data);font-size:9.5px;fill:var(--faint);text-anchor:middle;
        letter-spacing:.08em}
.ml{text-anchor:start;font-size:11px;fill:var(--muted)}
.legend{display:flex;flex-wrap:wrap;gap:20px;margin:14px 0 0;font-family:var(--display);
        text-transform:uppercase;letter-spacing:.14em;font-size:11px;color:var(--muted)}
.key{display:inline-block;width:22px;height:10px;border-radius:3px;border:2px solid;
     vertical-align:-1px;margin-right:7px;background:var(--sunk)}
.step{display:grid;grid-template-columns:118px 1fr;gap:26px;margin-top:56px;
      border-top:1px solid var(--line);padding-top:22px}
.step h2{font-family:var(--data);font-size:13px;letter-spacing:.18em;color:var(--muted);
         margin:6px 0 0;font-weight:600;position:sticky;top:20px;align-self:start}
.beats{display:flex;flex-direction:column;gap:22px;min-width:0}
.beat{background:var(--surface);border:1px solid var(--line);border-left:3px solid var(--warden);
      border-radius:8px;padding:22px 26px}
.beat.good{border-left-color:var(--good)}
.beat.bad{border-left-color:var(--bad)}
.beat.strange{border-left-color:var(--strange)}
.beat h3{font-family:var(--display);text-transform:uppercase;letter-spacing:.07em;
         font-size:27px;margin:0;line-height:1.1}
.meta{font-family:var(--data);font-size:11.5px;color:var(--faint);letter-spacing:.06em;
      margin:6px 0 16px}
.badge{font-family:var(--display);text-transform:uppercase;letter-spacing:.16em;
       font-size:11px;padding:3px 9px;border-radius:99px;border:1px solid currentColor;
       margin-left:10px;vertical-align:5px}
.good .badge{color:var(--good)}.bad .badge{color:var(--bad)}.strange .badge{color:var(--strange)}
.hook{font-family:var(--display);text-transform:uppercase;letter-spacing:.03em;
      font-size:20px;line-height:1.28;color:var(--foundry);margin:0 0 12px;max-width:44ch}
.body{margin:0;max-width:66ch}
.cliff{margin:12px 0 0;color:var(--warden);font-style:italic;max-width:66ch}
.picks{list-style:none;margin:18px 0 0;padding:0;display:flex;flex-direction:column;gap:8px}
.picks li{display:flex;gap:12px;align-items:baseline;border-top:1px solid var(--line);
          padding-top:8px;font-size:16px}
.picks .n1{font-family:var(--data);font-size:12px;color:var(--warden)}
.picks .to{font-family:var(--display);text-transform:uppercase;letter-spacing:.1em;
           font-size:11px;color:var(--faint);white-space:nowrap}
.still{display:block;width:100%;height:auto;border-radius:6px;margin:14px 0 18px;
        border:1px solid var(--line)}
.strip{width:100%;border-collapse:collapse;margin:18px 0 0;
        font-family:var(--data);font-size:12.5px}
.strip td{border-top:1px solid var(--line);padding:7px 8px;vertical-align:top;
          color:var(--muted)}
.strip .n{width:38px;color:var(--faint)}
.strip .s{width:36px;color:var(--warden);font-variant-numeric:tabular-nums}
.strip .c{width:132px;color:var(--faint);letter-spacing:.07em}
.strip tr.key td{color:var(--ink)}
.vo{display:block;margin-top:4px;color:var(--foundry);font-family:var(--body);
    font-size:15px;font-style:italic}
@media (max-width:720px){
  .step{grid-template-columns:1fr;gap:12px}
  .step h2{position:static}
  .picks li{flex-direction:column;gap:2px}
}
"""


def still_uri(story_id, node_id):
    """The beat's still, inlined. The review page has to survive being mailed
    around as one file, and an artifact host blocks every external request, so
    there is nowhere for a linked image to live."""
    path = os.path.join(ADVENTURES, story_id, node_id + ".png")
    if not os.path.exists(path):
        return None
    try:
        from PIL import Image
    except ImportError:
        return None
    image = Image.open(path).convert("RGB")
    image.thumbnail((640, 640))
    buffer = _io.BytesIO()
    image.save(buffer, "JPEG", quality=78, optimize=True)
    return "data:image/jpeg;base64," + base64.b64encode(buffer.getvalue()).decode()


def render_html(story, stats):
    ids = {n["id"]: n for n in story["nodes"]}
    esc = html.escape
    out = ['<meta charset="utf-8"><title>%s — Photon Arena side file</title>' % esc(story["title"]),
           "<style>%s</style>" % PAGE_CSS, '<div class="wrap">',
           '<p class="eyebrow">%s</p>' % esc(story["tagline"]),
           "<h1>%s</h1>" % esc(story["title"]),
           '<p class="logline">%s</p>' % esc(story["logline"]),
           '<p class="setting">%s</p>' % esc(story["setting"])]

    kinds = stats["by_kind"]
    out.append('<div class="strip">')
    for value, label in ((stats["nodes"], "beats, 30 seconds each"),
                         (stats["endings"], "endings (%d good · %d bad · %d strange)"
                          % (kinds["good"], kinds["bad"], kinds["strange"])),
                         ("%d / %d" % (stats["shortest"], stats["longest"]),
                          "shortest / longest run"),
                         (stats["merges"], "beats more than one path reaches"),
                         ("%d / %d min" % (stats["shots"], stats["seconds"] // 60),
                          "shots storyboarded / total runtime")):
        out.append('<div class="stat"><b>%s</b><span>%s</span></div>' % (value, esc(label)))
    out.append("</div>")

    out.append('<figure><div class="mapbox">%s</div>' % render_map(story, stats))
    out.append('<figcaption>Every route through the file. Rows are steps, so the '
               'bottom row is the furthest anyone can get before it ends. The paths '
               'keep rejoining — %d beats are reached from more than one direction, '
               'which is what keeps %d beats covering every run instead of the '
               'thousand a tree would need.</figcaption></figure>'
               % (stats["merges"], stats["nodes"]))
    out.append('<div class="legend">'
               '<span><i class="key" style="border-color:var(--warden)"></i>rejoins</span>'
               '<span><i class="key" style="border-color:var(--good)"></i>good ending</span>'
               '<span><i class="key" style="border-color:var(--bad)"></i>bad ending</span>'
               '<span><i class="key" style="border-color:var(--strange)"></i>strange ending</span>'
               "</div>")

    for depth, group in layers(story, stats):
        out.append('<section class="step"><h2>STEP %02d</h2><div class="beats">' % depth)
        for node in group:
            kind = node.get("ending", "")
            out.append('<article class="beat %s">' % kind)
            badge = ('<span class="badge">%s ending</span>' % esc(kind)) if kind else ""
            out.append("<h3>%s%s</h3>" % (esc(node["title"]), badge))
            came = sorted(set(stats["parents"].get(node["id"], [])))
            trail = "  ←  " + ", ".join(ids[p]["title"] for p in came) if came else "  ·  OPENS THE FILE"
            canon = "  ·  MATCHES THE EPISODE" if node.get("canon") else ""
            out.append('<p class="meta">%s%s%s</p>' % (esc(node["id"].upper()), esc(trail), canon))
            uri = still_uri(story["id"], node["id"])
            if uri:
                out.append('<img class="still" src="%s" alt="%s" loading="lazy">'
                           % (uri, esc(node["title"])))
            out.append('<p class="hook">%s</p>' % esc(node["hook"]))
            for para in node["body"].split("\n\n"):
                out.append('<p class="body">%s</p>' % esc(para.strip()))
            out.append('<p class="cliff">%s</p>' % esc(node["cliff"]))
            if node.get("choices"):
                out.append('<ul class="picks">')
                for i, choice in enumerate(node["choices"], start=1):
                    out.append('<li><span class="n1">%d</span><span>%s</span>'
                               '<span class="to">&rarr; %s</span></li>'
                               % (i, esc(choice["text"]), esc(ids[choice["to"]]["title"])))
                out.append("</ul>")
            out.append('<table class="strip"><tbody>')
            for i, shot in enumerate(node.get("shots", []), start=1):
                out.append('<tr%s><td class="n">%d%s</td><td class="s">%ds</td>'
                           '<td class="c">%s</td><td class="a">%s%s</td></tr>'
                           % (' class="key"' if shot.get("key") else "", i,
                              " &#9733;" if shot.get("key") else "", shot["t"],
                              esc(shot["cam"]), esc(shot["action"]),
                              ('<span class="vo">&ldquo;%s&rdquo;</span>' % esc(shot["vo"]))
                              if shot.get("vo") else ""))
            out.append("</tbody></table>")
            out.append("</article>")
        out.append("</div></section>")

    out.append("</div>")
    return "".join(out)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("story", nargs="?", help="adventure id (default: all)")
    parser.add_argument("--md", help="write the readable script here")
    parser.add_argument("--html", help="write an HTML script here")
    args = parser.parse_args()

    if args.story:
        ids = [args.story]
    else:
        ids = sorted(f[:-5] for f in os.listdir(ADVENTURES) if f.endswith(".json"))

    failed = False
    for story_id in ids:
        story = load(story_id)
        errors, stats = validate(story)
        if errors:
            failed = True
            print("FAIL %s" % story_id)
            for error in errors:
                print("   - %s" % error)
            continue
        print("OK   %s: %d nodes, %d endings, longest path %d/%d, %d merges, %d words"
              % (story_id, stats["nodes"], stats["endings"], stats["longest"],
                 story.get("maxSteps", 0), stats["merges"], stats["words"]))
        if args.md:
            with open(args.md, "w", encoding="utf-8") as handle:
                handle.write(render_markdown(story, stats))
            print("     wrote %s" % args.md)
        if args.html:
            with open(args.html, "w", encoding="utf-8") as handle:
                handle.write(render_html(story, stats))
            print("     wrote %s" % args.html)
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
