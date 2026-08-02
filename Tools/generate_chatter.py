"""Authors the battlefield radio chatter bank with the MiniMax API.

WHY AN LLM AT ALL. The goal is tens of thousands of distinct lines on screen,
and there are only two ways to get there: combinatorial slot-filling, or an
author. Pure slot-filling produces arithmetic, not speech -- swap eight synonyms
for "Contact" into six templates and you get 48 lines that all feel like the
same line, because the *shape* never changes. What varies between two real radio
calls is sentence shape, who interrupts whom, and whether anyone answers. That
is what an LLM can supply and a grammar cannot.

WHY IT IS BAKED, NOT LIVE. The game ships to WebGL. A runtime API call would
add latency to a bark that has to land within 400 ms of the event, would need a
key in the client, and would cost per match forever. So the model writes once,
offline, and the result ships as a TextAsset. `pack` is the step that turns the
raw JSONL into what Unity loads.

THE DIVISION OF LABOUR is the load-bearing idea. The model writes *phrasing*
with placeholders; the runtime substitutes *specifics* from live match state:

    "Contact! {enemy}, {bearing}, {range}!"
      -> "Contact! Panther, east ridge, forty meters!"

1000 authored conversations times the state that varies underneath them is where
the tens of thousands come from. Neither half gets there alone -- which is also
why PLACEHOLDERS is a strict whitelist: a placeholder the runtime cannot resolve
is a literal "{foe}" printed on a kid's screen, so unknown ones are rejected at
generation time rather than discovered in a build.

MiniMax-M2.5 inlines its reasoning as <think>...</think> at the head of the
content, and reasoning_effort='none' does not stop it (~300 tokens anyway). So
`_strip_think` is mandatory, not defensive, and max_tokens has to cover the
reasoning as well as the answer. Batching 20 conversations per call amortises
that overhead about twentyfold.

Runs are resumable: every accepted conversation is appended to the JSONL as it
lands, so a network failure at batch 47 of 50 costs one batch, not the run.

  python generate_chatter.py generate            # ~50 calls -> chatter.jsonl
  python generate_chatter.py generate --count 200 --workers 4
  python generate_chatter.py pack                # -> Assets/Resources/Chatter/
  python generate_chatter.py stats
"""

import argparse
import json
import os
import re
import sys
import threading
import time
import urllib.error
import urllib.request
from collections import Counter
from concurrent.futures import ThreadPoolExecutor

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
RAW = os.path.join(HERE, "chatter.jsonl")
PACKED = os.path.join(PROJECT, "Assets", "Resources", "Chatter", "conversations.json")

API_URL = "https://api.minimax.io/v1/chat/completions"
MODEL = "MiniMax-M2.5"

# --------------------------------------------------------------------- schema

# Placeholders the runtime can actually resolve. Anything else is a bug that
# would reach the screen as literal braces, so generation rejects it.
PLACEHOLDERS = {
    "{enemy}",      # display name of the spotted enemy robot
    "{ally}",       # display name of a friendly robot
    "{bearing}",    # "east ridge", "left flank", "the ramp" (arena landmarks)
    "{range}",      # "forty meters", "close", "long"
    "{shield}",     # "thirty percent"
    "{weapon}",     # real name from WeaponCatalog, e.g. "Comet Sling"
    "{crate}",      # real name from TreasureCatalog
    "{gold}",       # team gold, an integer
    "{team}",       # speaker's team: CYAN or MAGENTA
    "{enemyteam}",  # the other team
}

# Speech registers, one per robot in RobotRoster. The director matches a
# conversation to the robots actually on the field, so registers are tagged per
# beat rather than per conversation.
REGISTERS = {
    "pro":     "steady professional; complete calm sentences; the clearest speaker",
    "radio":   "strict radio discipline; clipped; grid references; 'copy', 'advise', 'wilco'",
    "rapid":   "over-caffeinated; talks too fast; repeats words; numeric readouts",
    "terse":   "predatory and quiet; two to four words; never wastes a syllable",
    "courtly": "courteous and old-fashioned; calls everyone 'friend'; slightly archaic",
    "slow":    "very. short. sentences. deep and unhurried; long vowels",
    "minimal": "counts things and states them; almost no adjectives; 'Form two.' 'Three left.'",
    "rookie":  "eager and nervous; doubles words; asks if that was okay",
    "brash":   "loud, boastful, interrupts, claims credit for everything",
}

# Cue types with how many conversations each should get. Sums to 1000.
CUES = {
    "CONTACT":        (100, "spotting an enemy and passing the sighting to a teammate"),
    "INCOMING":       (70,  "warning a teammate that a projectile or attack is inbound"),
    "TAKING_DAMAGE":  (70,  "being hit and reporting it"),
    "SHIELD_CRITICAL":(60,  "shield nearly gone; disengaging or begging for cover"),
    "REQUEST_HELP":   (60,  "asking teammates for help, and their answer"),
    "KILL_CONFIRM":   (80,  "having de-rezzed an enemy, and a teammate reacting"),
    "ALLY_DOWN":      (60,  "a teammate was de-rezzed; the others react and adjust"),
    "WEAPON_OUT":     (50,  "out of charge / weapon timer expiring / switching guns"),
    "REPOSITION":     (60,  "announcing a flank, a climb, or a fallback so others adapt"),
    "OBJECTIVE":      (90,  "a supply crate landing, grabbing it, denying it, or team gold"),
    "TRANSFORM":      (50,  "folding into vehicle form or standing back up as a robot"),
    "COVERING_FIRE":  (50,  "suppressing an enemy so a teammate can move"),
    "RALLY":          (50,  "regrouping, pushing together, holding a line"),
    "VICTORY":        (40,  "the match is won; celebrating, complimenting the losers"),
    "DEFEAT":         (30,  "the match is lost; taking it well, asking for a rematch"),
    "BANTER":         (80,  "idle between-fight teasing, boasting, small talk, bets"),
}

MAX_LINE_CHARS = 90     # radio log column width
MAX_BEATS = 5
MIN_BEATS = 2

SYSTEM = """\
You write radio chatter for robots fighting in a friendly sci-fi arena game for children.

THE GAME'S OWN VOCABULARY -- use these words, they are what the game calls things:
- "de-rez" / "de-rezzed" = defeated (never "killed", "dead", "died")
- "fold" / "folding" = transforming into vehicle form
- "rematerialize" = respawn
- "crate" / "supply drop" = the airdropped weapon pod
- "gold" = the team currency; enough gold builds a new robot
- teams are CYAN and MAGENTA

HARD RULES:
- Nobody is hurt. These are machines that pop into light and come back.
- No blood, death, gore, swearing, insults about looks, or real-world armies,
  countries, wars or politics.
- Every line under 90 characters. Radio calls are short.
- Never use the < or > characters (the UI parses them as markup).
- Write in plain English. No invented alien words.
- Sound like real radio traffic: specific, urgent, overlapping, sometimes funny.
  A call usually gets an answer -- "Copy", "On it", "Negative, I'm pinned".
- Conversations are between TEAMMATES. Speaker A starts. B (and optionally C)
  answer. They cannot hear the enemy team.
"""


def _prompt(cue, description, registers, avoid, batch):
    reg_lines = "\n".join(f'  "{r}": {REGISTERS[r]}' for r in registers)
    avoid_block = ""
    if avoid:
        listed = "\n".join(f"  - {a}" for a in sorted(avoid)[:25])
        avoid_block = (
            "\nDo NOT begin any conversation with these openings; they are already "
            f"used:\n{listed}\n"
        )
    return f"""\
Write {batch} SHORT separate radio conversations for the situation: {cue} -- {description}.

Use only these speaker registers, and tag every beat with the one it uses:
{reg_lines}

You may insert these placeholders, which the game replaces with live match data.
Use them where a real radio call would be specific. Use NO other placeholders:
  {{enemy}} an enemy robot's name      {{ally}} a friendly robot's name
  {{bearing}} a place in the arena     {{range}} a distance
  {{shield}} a shield percentage       {{weapon}} a weapon name
  {{crate}} a supply crate's name      {{gold}} a gold amount
  {{team}} the speaker's team          {{enemyteam}} the other team

PLACEHOLDER GRAMMAR -- these already contain their own words:
- {{range}} expands to "forty meters" or "close". Never write "meters" after it.
  Right: "{{enemy}} at {{range}}."      Wrong: "{{enemy}} at {{range}} meters."
- {{bearing}} expands to "the ramp" or "east ridge". Never write "the" before it.
  Right: "Contact near {{bearing}}."   Wrong: "Contact near the {{bearing}}."
- {{shield}} expands to "thirty percent". Never write "percent" after it.
- To address a teammate, use {{ally}}. Never write the literal words "ally" or
  "teammate" -- robots call each other by name.
- One speaker keeps ONE register for the whole conversation.

Each conversation is {MIN_BEATS} to {MAX_BEATS} beats. Vary the length and the shape:
some are a call and a two-word answer, some are four beats with a correction or
an interruption. Make each one feel different from the others.
{avoid_block}
Output ONLY a JSON array, no prose, no code fence:
[{{"beats":[{{"s":"A","r":"{registers[0]}","t":"..."}},{{"s":"B","r":"{registers[-1]}","t":"..."}}]}}]
"""


# ----------------------------------------------------------------- api plumbing

class ApiError(Exception):
    pass


def _strip_think(text):
    """Removes MiniMax's inline reasoning, which precedes the real answer.

    Handles the unclosed case too: if the model hit max_tokens mid-thought there
    is an opening tag and no closing one, and everything after it is garbage.
    """
    text = re.sub(r"<think>.*?</think>", "", text, flags=re.S)
    if "<think>" in text:
        text = text.split("<think>")[0]
    return text.strip()


def _extract_json_array(text):
    """Pulls the first JSON array out of a model response.

    The model is asked for bare JSON but sometimes fences it or adds a sentence,
    and a truncated response leaves a half-written final object. Trimming back
    to the last complete object recovers the rest of the batch instead of
    discarding a whole call's work.
    """
    text = _strip_think(text)
    text = re.sub(r"^```(?:json)?|```$", "", text.strip(), flags=re.M).strip()
    start = text.find("[")
    if start < 0:
        raise ApiError("no JSON array in response")
    try:
        return json.loads(text[start:])
    except json.JSONDecodeError:
        pass
    # Truncated: cut back to the last '}' that closes a top-level object.
    depth, last = 0, -1
    for i, ch in enumerate(text[start:], start):
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                last = i
    if last < 0:
        raise ApiError("no complete object in truncated response")
    return json.loads(text[start:last + 1] + "]")


def call_model(prompt, key, max_tokens=6000, attempts=4):
    body = json.dumps({
        "model": MODEL,
        "messages": [
            {"role": "system", "content": SYSTEM},
            {"role": "user", "content": prompt},
        ],
        "max_tokens": max_tokens,
        "temperature": 1.0,   # this is a creativity job, not a precision one
        "top_p": 0.95,
    }).encode()
    delay = 3
    for attempt in range(attempts):
        req = urllib.request.Request(API_URL, data=body, headers={
            "Authorization": "Bearer " + key,
            "Content-Type": "application/json",
        })
        try:
            with urllib.request.urlopen(req, timeout=240) as resp:
                payload = json.load(resp)
            return payload["choices"][0]["message"]["content"]
        except (urllib.error.HTTPError, urllib.error.URLError, TimeoutError,
                KeyError, json.JSONDecodeError) as exc:
            if attempt == attempts - 1:
                raise ApiError(f"{type(exc).__name__}: {exc}") from exc
            time.sleep(delay)
            delay *= 2
    raise ApiError("unreachable")


# ------------------------------------------------------------------ validation

BANNED = re.compile(
    r"\b(kill(ed|s)?|dead|death|die[ds]?|dying|blood|bleed|corpse|murder|"
    r"damn|hell|crap|stupid|idiot|ugly)\b", re.I)

PLACEHOLDER_RE = re.compile(r"\{[a-z_]*\}")

# Two substitution seams the model keeps getting wrong, because the fix depends
# on knowing what the placeholder expands to and the model only sees the brace.
# {range} resolves WITH its unit ("forty meters", "close"), so a trailing
# "meters" doubles it; {bearing} resolves WITH its article ("the ramp", "east
# ridge"), so a leading "the" doubles that. Both are repairs rather than
# rejections -- the line is good, only the seam is wrong.
DOUBLED_UNIT = re.compile(r"(\{range\})\s+(?:meters|meter|metres|metre|m)\b", re.I)
DOUBLED_PERCENT = re.compile(r"(\{shield\})\s*(?:percent|per cent|%)", re.I)
DOUBLED_ARTICLE = re.compile(r"\b(the)\s+(\{bearing\})", re.I)
# The lookarounds are load-bearing: a plain \bally\b also matches the "ally"
# inside an existing {ally}, and rewriting that to {{ally}} puts literal braces
# on screen. Guard against a leading { and a trailing }.
LITERAL_ALLY = re.compile(r"(?<![{\w])(?:ally|teammate)(?![}\w])", re.I)


def repair(text):
    text = DOUBLED_UNIT.sub(r"\1", text)
    text = DOUBLED_PERCENT.sub(r"\1", text)
    text = DOUBLED_ARTICLE.sub(r"\2", text)
    # "Good work, ally" is not how anyone talks; the runtime has a real name.
    text = LITERAL_ALLY.sub("{ally}", text)
    return re.sub(r"\s{2,}", " ", text).strip()


def opening(text):
    """The first three words, normalised -- the diversity key.

    Players only register the head of a line; two calls that open the same way
    read as the same call however different their tails are. So this, not the
    whole string, is what dedupe keys on.
    """
    words = re.findall(r"[a-z']+", text.lower())
    return " ".join(words[:3])


def validate(convo, cue, registers):
    """Returns a cleaned conversation, or raises ValueError with the reason."""
    beats = convo.get("beats")
    if not isinstance(beats, list) or not (MIN_BEATS <= len(beats) <= MAX_BEATS):
        raise ValueError("beat count")

    cleaned = []
    speakers = {}
    for beat in beats:
        if not isinstance(beat, dict):
            raise ValueError("beat not an object")
        speaker = str(beat.get("s", "")).strip().upper()[:1]
        register = str(beat.get("r", "")).strip().lower()
        text = repair(str(beat.get("t", "")).strip())

        if speaker not in ("A", "B", "C"):
            raise ValueError(f"speaker {speaker!r}")
        if register not in registers:
            raise ValueError(f"register {register!r}")
        # A robot does not change how it talks halfway through a conversation.
        # The model occasionally reassigns a speaker's register mid-exchange;
        # the first one it chose wins.
        register = speakers.setdefault(speaker, register)
        if not 2 <= len(text) <= MAX_LINE_CHARS:
            raise ValueError(f"length {len(text)}")
        if "<" in text or ">" in text:
            raise ValueError("angle bracket")   # uGUI Text would eat it as markup
        if BANNED.search(text):
            raise ValueError("banned word")
        for found in PLACEHOLDER_RE.findall(text):
            if found not in PLACEHOLDERS:
                raise ValueError(f"unknown placeholder {found}")

        cleaned.append({"s": speaker, "r": register, "t": text})

    if len(speakers) < 2:
        raise ValueError("monologue")   # a conversation needs two voices
    return {"cue": cue, "beats": cleaned}


# -------------------------------------------------------------------- generate

def _plan(count):
    """Batches to run: (cue, description, register tuple, size).

    Register pairs are walked deterministically so all nine registers get even
    coverage instead of the model's favourites dominating.
    """
    scale = count / float(sum(n for n, _ in CUES.values()))
    names = list(REGISTERS)
    batches, cursor = [], 0
    for cue, (target, description) in CUES.items():
        want = max(1, int(round(target * scale)))
        while want > 0:
            size = min(20, want)
            pair = (names[cursor % 9], names[(cursor * 4 + 3) % 9])
            if pair[0] == pair[1]:
                pair = (pair[0], names[(cursor * 4 + 4) % 9])
            batches.append((cue, description, pair, size))
            want -= size
            cursor += 1
    return batches


def generate(count, workers):
    key = os.environ.get("MINIMAX_API_KEY")
    if not key:
        sys.exit("MINIMAX_API_KEY is not set")

    seen_openings, by_cue = set(), Counter()
    if os.path.exists(RAW):
        for convo in _load_raw():
            seen_openings.add(opening(convo["beats"][0]["t"]))
            by_cue[convo["cue"]] += 1
        print(f"resuming: {sum(by_cue.values())} already banked")

    lock = threading.Lock()
    handle = open(RAW, "a", encoding="utf-8")
    stats = Counter()

    def run(batch):
        cue, description, registers, size = batch
        with lock:
            avoid = {o for o in seen_openings}
        prompt = _prompt(cue, description, list(registers), avoid, size)
        try:
            raw = call_model(prompt, key)
            items = _extract_json_array(raw)
        except (ApiError, json.JSONDecodeError, ValueError) as exc:
            with lock:
                stats["call_failed"] += 1
            print(f"  {cue:<16} FAILED {exc}")
            return

        kept = 0
        for item in items:
            try:
                convo = validate(item, cue, registers)
            except ValueError as exc:
                with lock:
                    stats[f"reject:{exc}"] += 1
                continue
            head = opening(convo["beats"][0]["t"])
            with lock:
                if head in seen_openings:
                    stats["reject:duplicate opening"] += 1
                    continue
                seen_openings.add(head)
                handle.write(json.dumps(convo, ensure_ascii=False) + "\n")
                handle.flush()
                by_cue[cue] += 1
                stats["kept"] += 1
            kept += 1
        print(f"  {cue:<16} {'/'.join(registers):<18} kept {kept}/{len(items)}")

    batches = _plan(count)
    print(f"{len(batches)} batches, {workers} workers, target ~{count}")
    started = time.time()
    with ThreadPoolExecutor(max_workers=workers) as pool:
        list(pool.map(run, batches))
    handle.close()

    print(f"\ndone in {time.time() - started:.0f}s -- {stats['kept']} new, "
          f"{sum(by_cue.values())} total")
    for reason, n in stats.most_common():
        if reason != "kept":
            print(f"  {reason}: {n}")


# ------------------------------------------------------------------ pack/stats

def _load_raw():
    out = []
    with open(RAW, encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if line:
                out.append(json.loads(line))
    return out


def pack():
    """Writes the Unity-side TextAsset.

    Wrapped in an object with a named array because Unity's JsonUtility cannot
    deserialize a bare top-level array. Keys stay short: this file is parsed on
    a phone at match start.
    """
    convos = _load_raw()
    seen, unique = set(), []
    for convo in convos:
        head = opening(convo["beats"][0]["t"])
        if head in seen:
            continue
        seen.add(head)
        convo["id"] = f"c{len(unique):04d}"
        unique.append(convo)

    os.makedirs(os.path.dirname(PACKED), exist_ok=True)
    with open(PACKED, "w", encoding="utf-8") as handle:
        json.dump({"conversations": unique}, handle, ensure_ascii=False, indent=0)

    lines = sum(len(c["beats"]) for c in unique)
    size = os.path.getsize(PACKED) / 1024.0
    print(f"packed {len(unique)} conversations / {lines} lines -> {size:.0f} KB")
    print(f"  {PACKED}")
    if len(convos) != len(unique):
        print(f"  dropped {len(convos) - len(unique)} duplicate openings")


def stats():
    convos = _load_raw()
    by_cue = Counter(c["cue"] for c in convos)
    by_reg = Counter(b["r"] for c in convos for b in c["beats"])
    by_len = Counter(len(c["beats"]) for c in convos)
    lines = sum(len(c["beats"]) for c in convos)
    print(f"{len(convos)} conversations, {lines} lines\n")
    print("by cue:")
    for cue in CUES:
        print(f"  {cue:<17} {by_cue.get(cue, 0)}")
    print("\nby register:")
    for reg in REGISTERS:
        print(f"  {reg:<9} {by_reg.get(reg, 0)}")
    print("\nbeats per conversation:", dict(sorted(by_len.items())))
    placeholders = Counter(
        p for c in convos for b in c["beats"] for p in PLACEHOLDER_RE.findall(b["t"]))
    print("placeholder use:", dict(placeholders.most_common()))


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    gen = sub.add_parser("generate")
    gen.add_argument("--count", type=int, default=1000)
    gen.add_argument("--workers", type=int, default=6)
    sub.add_parser("pack")
    sub.add_parser("stats")
    args = parser.parse_args()

    if args.command == "generate":
        generate(args.count, args.workers)
    elif args.command == "pack":
        pack()
    else:
        stats()


if __name__ == "__main__":
    main()
