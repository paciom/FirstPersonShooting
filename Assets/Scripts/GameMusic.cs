using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Background music for every mode, synthesized at runtime — no assets, no
/// licenses, same doctrine as BrawlAudio. Each mode family gets one looping
/// track baked from a tiny pattern spec (drums / bass / pad / arpeggio over
/// a chord progression); the loop length is computed in whole bars and every
/// note tail WRAPS around to the start, so the loop is seamless.
///
/// Any track can be overridden by dropping an AudioClip at
/// Resources/Music/&lt;name&gt;.ogg (menu, arena, brawl, commander,
/// towerdefense, tankraid, dogfight, chinese, adventure) — a real recording
/// beats the synth automatically, no code change.
///
/// GameModeController's Mode setter is the single seam that sees every mode
/// transition (the same seam analytics rides), so this switches tracks with
/// one call there and nothing per-mode. Big stingers call Duck() to push the
/// music down for a beat. Music sits at a deliberately low default volume —
/// it is a floor for the event sounds to stand on, not a lead instrument.
/// </summary>
public static class GameMusic
{
    public enum Track
    {
        None, Menu, Arena, Brawl, Commander,
        TowerDefense, TankRaid, Dogfight, Chinese, Adventure,
    }

    public static float Volume
    {
        get
        {
            if (_volume < 0f)
                _volume = PlayerPrefs.GetFloat("jah.musicvol", 0.4f);
            return _volume;
        }
        set
        {
            _volume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat("jah.musicvol", _volume);
        }
    }
    static float _volume = -1f;

    /// <summary>Called from GameModeController's Mode setter. Never throws.</summary>
    public static void ModeSwap(GameMode to)
    {
        try { Play(TrackFor(to)); }
        catch (System.Exception e) { Debug.LogWarning("GameMusic: " + e.Message); }
    }

    public static void Play(Track track)
    {
        if (Application.isBatchMode)
            return;
        Player().Play(track);
    }

    /// <summary>Momentarily lower the music under a match-deciding stinger.</summary>
    public static void Duck(float seconds)
    {
        if (Application.isBatchMode)
            return;
        Player().DuckFor(seconds);
    }

    static Track TrackFor(GameMode mode)
    {
        switch (mode)
        {
            case GameMode.Menu:
            case GameMode.ArenaPreview: return Track.Menu;
            case GameMode.PlayerVsAI:
            case GameMode.AIvAI:
            case GameMode.OnlinePvP: return Track.Arena;
            case GameMode.Brawl:
            case GameMode.BrawlWar:
            case GameMode.BrawlShow: return Track.Brawl;
            case GameMode.Commander: return Track.Commander;
            case GameMode.TowerDefense: return Track.TowerDefense;
            case GameMode.TankRaid:
            case GameMode.TankRaidWar: return Track.TankRaid;
            case GameMode.Dogfight:
            case GameMode.DogfightWar: return Track.Dogfight;
            case GameMode.ChineseQuest:
            case GameMode.ChineseRun: return Track.Chinese;
            case GameMode.Adventure: return Track.Adventure;
            // Story is narrated — music under TTS voice muddies the words.
            case GameMode.Story: return Track.None;
            default: return Track.None;
        }
    }

    // The menu is the boot mode and the Mode setter only fires on CHANGE,
    // so the very first track has to start itself.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (!Application.isBatchMode)
            Play(Track.Menu);
    }

    static GameMusicPlayer Player()
    {
        if (_player == null)
        {
            var go = new GameObject("GameMusic");
            Object.DontDestroyOnLoad(go);
            _player = go.AddComponent<GameMusicPlayer>();
        }
        return _player;
    }
    static GameMusicPlayer _player;
}

/// <summary>
/// The hidden runtime half: two AudioSources crossfading between looped
/// tracks, a duck envelope, and the pattern synthesizer that bakes them.
///
/// Audition controls: "." and "," cycle forward/back through every clip in
/// Resources/Music (candidate tracks included), with an AUTO slot that hands
/// control back to the per-mode music. Whenever a track starts, a toast at
/// the top of the screen names the file for a few seconds — that is how the
/// candidates get chosen: play the game, tap ".", read what's playing.
/// </summary>
public class GameMusicPlayer : MonoBehaviour
{
    const int Rate = 32000;
    const float FadeSpeed = 0.8f;   // volume per second while crossfading

    AudioSource[] _sources;
    float[] _targets;               // 0 or 1 per source, before volume/duck
    int _active = -1;
    GameMusic.Track _track = GameMusic.Track.None;
    float _duckT;
    float _duckMul = 1f;
    readonly Dictionary<GameMusic.Track, AudioClip> _baked =
        new Dictionary<GameMusic.Track, AudioClip>();

    // The audition ring: every clip under Resources/Music, "," / "." cycle.
    // -1 = AUTO (the mode picks). A hand-picked clip survives mode changes,
    // so one track can be judged across the whole game.
    AudioClip[] _library;
    int _manualIndex = -1;

    Canvas _toast;
    Text _toastText;
    float _toastUntil;

    void Awake()
    {
        _sources = new AudioSource[2];
        _targets = new float[2];
        for (int i = 0; i < 2; i++)
        {
            _sources[i] = gameObject.AddComponent<AudioSource>();
            _sources[i].loop = true;
            _sources[i].spatialBlend = 0f;
            _sources[i].volume = 0f;
        }
    }

    public void Play(GameMusic.Track track)
    {
        if (track == _track)
            return;
        _track = track;
        // A hand-picked audition track outranks the mode's own music.
        if (_manualIndex >= 0)
            return;
        PlayAuto();
    }

    void PlayAuto()
    {
        if (_track == GameMusic.Track.None)
        {
            for (int i = 0; i < 2; i++)
                _targets[i] = 0f;
            return;
        }

        var clip = GetClip(_track);
        if (clip == null)
            return;
        StartClip(clip);
        Announce(clip.name.StartsWith("music_")
            ? clip.name.Substring(6) + "   ·   built-in synth"
            : clip.name + ".ogg");
    }

    void StartClip(AudioClip clip)
    {
        if (_active >= 0 && _sources[_active].clip == clip
            && _sources[_active].isPlaying && _targets[_active] > 0f)
            return;
        _active = _active == 0 ? 1 : 0;
        _sources[_active].clip = clip;
        _sources[_active].Play();
        _targets[_active] = 1f;
        _targets[1 - _active] = 0f;
    }

    void Cycle(int direction)
    {
        if (_library == null)
        {
            _library = Resources.LoadAll<AudioClip>("Music");
            System.Array.Sort(_library,
                (a, b) => string.CompareOrdinal(a.name, b.name));
        }
        if (_library.Length == 0)
        {
            Announce("no files in Resources/Music");
            return;
        }

        _manualIndex += direction;
        if (_manualIndex >= _library.Length)
            _manualIndex = -1;                     // past the end: back to AUTO
        else if (_manualIndex < -1)
            _manualIndex = _library.Length - 1;

        if (_manualIndex < 0)
        {
            Announce("AUTO   ·   per-mode music");
            // Force the mode's track back on even though _track is unchanged.
            PlayAuto();
        }
        else
        {
            var clip = _library[_manualIndex];
            StartClip(clip);
            Announce($"{clip.name}.ogg   ·   {_manualIndex + 1} / {_library.Length}");
        }
    }

    void Announce(string message)
    {
        EnsureToast();
        _toastText.text = "MUSIC   ·   " + message;
        _toast.enabled = true;
        _toastUntil = Time.unscaledTime + 3.5f;
    }

    void EnsureToast()
    {
        if (_toast != null)
            return;
        var canvasGo = new GameObject("MusicToast");
        canvasGo.transform.SetParent(transform, false);
        _toast = canvasGo.AddComponent<Canvas>();
        _toast.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above every mode's HUD: a toast that hides under the score bar
        // answers the exact question it exists for with silence.
        _toast.sortingOrder = 60;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var textGo = new GameObject("Name");
        textGo.transform.SetParent(canvasGo.transform, false);
        _toastText = textGo.AddComponent<Text>();
        _toastText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _toastText.fontSize = 30;
        _toastText.alignment = TextAnchor.UpperCenter;
        _toastText.color = new Color(1f, 1f, 1f, 0.95f);
        _toastText.raycastTarget = false;
        var outline = textGo.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        var rect = _toastText.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -64f);
        rect.sizeDelta = new Vector2(1500f, 70f);
    }

    public void DuckFor(float seconds)
    {
        _duckT = Mathf.Max(_duckT, seconds);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Period))
            Cycle(1);
        else if (Input.GetKeyDown(KeyCode.Comma))
            Cycle(-1);
        if (_toast != null && _toast.enabled && Time.unscaledTime > _toastUntil)
            _toast.enabled = false;

        _duckT -= Time.unscaledDeltaTime;
        float duckGoal = _duckT > 0f ? 0.3f : 1f;
        _duckMul = Mathf.MoveTowards(_duckMul, duckGoal,
            Time.unscaledDeltaTime * 2.5f);

        for (int i = 0; i < 2; i++)
        {
            float goal = _targets[i] * GameMusic.Volume * _duckMul;
            var source = _sources[i];
            source.volume = Mathf.MoveTowards(source.volume, goal,
                Time.unscaledDeltaTime * FadeSpeed);
            if (source.volume <= 0f && _targets[i] <= 0f && source.isPlaying)
                source.Stop();
        }
    }

    AudioClip GetClip(GameMusic.Track track)
    {
        if (_baked.TryGetValue(track, out var cached))
            return cached;
        string name = track.ToString().ToLowerInvariant();
        var clip = Resources.Load<AudioClip>("Music/" + name);
        if (clip == null)
            clip = Render(name, SpecFor(track));
        _baked[track] = clip;
        return clip;
    }

    // ------------------------------------------------------------- specs

    class Spec
    {
        public float bpm;
        public int root;          // MIDI note; pad plays here, bass -12, arp +12
        public int[] scale;       // semitone offsets from root
        public int[] chords;      // scale degree per bar; length = bars in loop
        public string kick, snare, hat;   // 16-step patterns, 'x' = hit
        public string bass;       // 16-step: r root, o octave, f fifth, . rest
        public int arpEvery;      // arp note every N steps (0 = off)
        public int arpOctave;     // extra semitones on top of root+12
        public float drums, bassLvl, pad, arp;
    }

    static readonly int[] Minor = { 0, 2, 3, 5, 7, 8, 10 };
    static readonly int[] Major = { 0, 2, 4, 5, 7, 9, 11 };
    static readonly int[] Penta = { 0, 2, 4, 7, 9 };

    static Spec SpecFor(GameMusic.Track track)
    {
        switch (track)
        {
            // Warm and unhurried — the lobby, not the fight.
            case GameMusic.Track.Menu: return new Spec
            {
                bpm = 96, root = 50, scale = Major,
                chords = new[] { 0, 4, 5, 3, 0, 4, 3, 4 },
                kick  = "x.......x.......",
                snare = "................",
                hat   = "..x...x...x...x.",
                bass  = "r.......r.......",
                arpEvery = 2, arpOctave = 0,
                drums = 0.5f, bassLvl = 0.35f, pad = 0.55f, arp = 0.16f,
            };
            // The core shooter: four-on-the-floor and a driving eighth bass.
            case GameMusic.Track.Arena: return new Spec
            {
                bpm = 128, root = 45, scale = Minor,
                chords = new[] { 0, 0, 5, 5, 2, 2, 6, 6 },
                kick  = "x...x...x...x...",
                snare = "....x.......x...",
                hat   = "..x...x...x...x.",
                bass  = "r.r.r.r.r.r.r.r.",
                arpEvery = 1, arpOctave = 12,
                drums = 1.0f, bassLvl = 0.5f, pad = 0.35f, arp = 0.22f,
            };
            // Syncopated and mean — a fight bell away from a boxing gym.
            case GameMusic.Track.Brawl: return new Spec
            {
                bpm = 134, root = 43, scale = Minor,
                chords = new[] { 0, 0, 5, 6, 0, 0, 3, 6 },
                kick  = "x..x....x..x....",
                snare = "....x.......x..x",
                hat   = "x.x.x.x.x.x.x.x.",
                bass  = "r..r..r.r..r..o.",
                arpEvery = 1, arpOctave = 12,
                drums = 1.0f, bassLvl = 0.55f, pad = 0.3f, arp = 0.18f,
            };
            // The war room: slow pad weather, a pulse instead of a beat.
            case GameMusic.Track.Commander: return new Spec
            {
                bpm = 92, root = 48, scale = Minor,
                chords = new[] { 0, 0, 3, 3, 5, 5, 4, 4 },
                kick  = "x.......x.......",
                snare = "................",
                hat   = "....x.......x...",
                bass  = "r.....r.........",
                arpEvery = 2, arpOctave = 0,
                drums = 0.6f, bassLvl = 0.4f, pad = 0.6f, arp = 0.12f,
            };
            case GameMusic.Track.TowerDefense: return new Spec
            {
                bpm = 112, root = 43, scale = Minor,
                chords = new[] { 0, 6, 5, 6, 0, 6, 3, 4 },
                kick  = "x...x...x...x...",
                snare = "....x.......x...",
                hat   = "..x...x...x...x.",
                bass  = "r.r.o.r.r.r.o.r.",
                arpEvery = 2, arpOctave = 12,
                drums = 0.85f, bassLvl = 0.5f, pad = 0.4f, arp = 0.2f,
            };
            // Chip-tune march for the vertical scroller.
            case GameMusic.Track.TankRaid: return new Spec
            {
                bpm = 140, root = 47, scale = Minor,
                chords = new[] { 0, 0, 5, 6, 0, 0, 5, 6 },
                kick  = "x...x...x...x...",
                snare = "....x.......x...",
                hat   = "x.x.x.x.x.x.x.x.",
                bass  = "r.r.r.r.r.r.r.r.",
                arpEvery = 1, arpOctave = 12,
                drums = 1.0f, bassLvl = 0.5f, pad = 0.25f, arp = 0.3f,
            };
            case GameMusic.Track.Dogfight: return new Spec
            {
                bpm = 138, root = 50, scale = Minor,
                chords = new[] { 0, 6, 5, 6, 0, 6, 2, 6 },
                kick  = "x...x...x...x...",
                snare = "....x.......x...",
                hat   = "..x...x...x...x.",
                bass  = "r.r.r.r.r.r.r.o.",
                arpEvery = 1, arpOctave = 12,
                drums = 0.95f, bassLvl = 0.5f, pad = 0.35f, arp = 0.26f,
            };
            // Pentatonic, plucked, calm — a classroom, not an arena.
            case GameMusic.Track.Chinese: return new Spec
            {
                bpm = 88, root = 48, scale = Penta,
                chords = new[] { 0, 3, 1, 4, 0, 3, 4, 0 },
                kick  = "x.......x.......",
                snare = "................",
                hat   = "..x.....x....x..",
                bass  = "r.......r.......",
                arpEvery = 2, arpOctave = 12,
                drums = 0.4f, bassLvl = 0.3f, pad = 0.45f, arp = 0.3f,
            };
            // Ambient mystery: almost all pad, no drums to rush the reading.
            case GameMusic.Track.Adventure: return new Spec
            {
                bpm = 76, root = 45, scale = Minor,
                chords = new[] { 0, 5, 3, 6, 0, 5, 4, 6 },
                kick  = "................",
                snare = "................",
                hat   = "................",
                bass  = "r...............",
                arpEvery = 4, arpOctave = 12,
                drums = 0f, bassLvl = 0.35f, pad = 0.7f, arp = 0.15f,
            };
            default: return null;
        }
    }

    // ------------------------------------------------------ the synthesizer

    static float Freq(int midi)
    {
        return 440f * Mathf.Pow(2f, (midi - 69) / 12f);
    }

    /// <summary>Diatonic triad on a scale degree, as semitone offsets.</summary>
    static int[] ChordSemis(int[] scale, int degree)
    {
        var tones = new int[3];
        for (int k = 0; k < 3; k++)
        {
            int idx = degree + 2 * k;
            tones[k] = scale[idx % scale.Length] + 12 * (idx / scale.Length);
        }
        return tones;
    }

    static AudioClip Render(string name, Spec s)
    {
        if (s == null)
            return null;
        int bars = s.chords.Length;
        float secPerStep = 60f / s.bpm / 4f;
        int frames = Mathf.CeilToInt(bars * 16 * secPerStep * Rate);
        var left = new float[frames];
        var right = new float[frames];
        var random = new System.Random(name.GetHashCode());

        int arpIndex = 0;
        for (int bar = 0; bar < bars; bar++)
        {
            int[] chord = ChordSemis(s.scale, s.chords[bar]);
            float barStart = bar * 16 * secPerStep;

            if (s.pad > 0f)
                Pad(left, right, (int)(barStart * Rate), 16 * secPerStep,
                    s.root, chord, s.pad);

            for (int step = 0; step < 16; step++)
            {
                int at = (int)((barStart + step * secPerStep) * Rate);
                if (s.drums > 0f)
                {
                    if (s.kick[step] == 'x') Kick(left, right, at, s.drums);
                    if (s.snare[step] == 'x') Snare(left, right, at, s.drums, random);
                    if (s.hat[step] == 'x') Hat(left, right, at, s.drums, random);
                }
                if (s.bassLvl > 0f && s.bass[step] != '.')
                {
                    int semi = s.bass[step] == 'o' ? chord[0] + 12
                        : s.bass[step] == 'f' ? chord[0] + 7 : chord[0];
                    Bass(left, right, at,
                        BassLen(s.bass, step) * secPerStep * 0.95f,
                        Freq(s.root - 12 + semi), s.bassLvl);
                }
                if (s.arpEvery > 0 && s.arp > 0f && step % s.arpEvery == 0)
                {
                    int tone = chord[arpIndex % 3] + (arpIndex % 4 == 3 ? 12 : 0);
                    Pluck(left, right, at, s.arpEvery * secPerStep * 0.9f,
                        Freq(s.root + 12 + s.arpOctave + tone), s.arp,
                        arpIndex % 2 == 0 ? -0.25f : 0.25f);
                    arpIndex++;
                }
            }
        }

        // Normalize toward a healthy level, then soft-clip: the pad stack
        // can pile up on downbeats and hard clipping reads as a broken
        // speaker, especially on the laptop hardware kids actually use.
        float peak = 0.001f;
        for (int i = 0; i < frames; i++)
            peak = Mathf.Max(peak, Mathf.Max(Mathf.Abs(left[i]), Mathf.Abs(right[i])));
        float gain = Mathf.Min(1.5f, 0.8f / peak);
        var data = new float[frames * 2];
        for (int i = 0; i < frames; i++)
        {
            data[i * 2] = (float)System.Math.Tanh(left[i] * gain * 1.3f) * 0.8f;
            data[i * 2 + 1] = (float)System.Math.Tanh(right[i] * gain * 1.3f) * 0.8f;
        }

        var clip = AudioClip.Create("music_" + name, frames, 2, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>Steps until the next bass note (wrapping), capped at 4.</summary>
    static int BassLen(string pattern, int step)
    {
        for (int n = 1; n < 4; n++)
            if (pattern[(step + n) % 16] != '.')
                return n;
        return 4;
    }

    // Every voice writes with wrap-around indexing, so a tail that runs off
    // the end of the loop lands at the start — that is what makes the loop
    // point inaudible.
    static void Add(float[] buf, int at, int i, float v)
    {
        buf[(at + i) % buf.Length] += v;
    }

    static void Kick(float[] l, float[] r, int at, float level)
    {
        int n = (int)(0.3f * Rate);
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            phase += 2f * Mathf.PI * (44f + 85f * Mathf.Exp(-30f * t)) / Rate;
            float v = Mathf.Sin(phase) * Mathf.Exp(-11f * t) * 0.85f * level;
            Add(l, at, i, v);
            Add(r, at, i, v);
        }
    }

    static void Snare(float[] l, float[] r, int at, float level, System.Random random)
    {
        int n = (int)(0.22f * Rate);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float noise = (float)(random.NextDouble() * 2.0 - 1.0);
            float v = (noise * 0.6f * Mathf.Exp(-25f * t)
                + Mathf.Sin(2f * Mathf.PI * 190f * t) * 0.35f * Mathf.Exp(-40f * t))
                * level;
            Add(l, at, i, v);
            Add(r, at, i, v);
        }
    }

    static void Hat(float[] l, float[] r, int at, float level, System.Random random)
    {
        int n = (int)(0.07f * Rate);
        float prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float noise = (float)(random.NextDouble() * 2.0 - 1.0);
            float v = (noise - prev) * Mathf.Exp(-60f * t) * 0.35f * level;
            prev = noise;
            Add(l, at, i, v * 0.8f);
            Add(r, at, i, v);
        }
    }

    static void Bass(float[] l, float[] r, int at, float seconds, float hz, float level)
    {
        int n = (int)(seconds * Rate);
        float p1 = 0f, p2 = 0f, lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            p1 += hz * 1.004f / Rate; p1 -= Mathf.Floor(p1);
            p2 += hz * 0.996f / Rate; p2 -= Mathf.Floor(p2);
            float saw = (p1 * 2f - 1f) + (p2 * 2f - 1f);
            // Cutoff falls out of the attack: the classic filter pluck.
            float a = Mathf.Lerp(0.35f, 0.08f, Mathf.Clamp01(t * 6f));
            lp += a * (saw - lp);
            float env = Mathf.Min(1f, t * 250f)
                * Mathf.Min(1f, (seconds - t) * 40f)
                * (0.35f + 0.65f * Mathf.Exp(-4f * t));
            float v = lp * env * 0.5f * level;
            Add(l, at, i, v);
            Add(r, at, i, v);
        }
    }

    static void Pad(float[] l, float[] r, int at, float seconds, int root,
        int[] chord, float level)
    {
        int n = (int)(seconds * Rate);
        // Chord tones plus the root an octave up, each two detuned saws
        // split across the channels — the width comes free.
        var midis = new[] { chord[0], chord[1], chord[2], chord[0] + 12 };
        foreach (int semi in midis)
        {
            float hz = Freq(root + semi);
            float p1 = 0f, p2 = 0f, lpL = 0f, lpR = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                p1 += hz * 1.003f / Rate; p1 -= Mathf.Floor(p1);
                p2 += hz * 0.997f / Rate; p2 -= Mathf.Floor(p2);
                float env = Mathf.Min(1f, t / 0.35f)
                    * Mathf.Min(1f, (seconds - t) / 0.3f);
                lpL += 0.10f * ((p1 * 2f - 1f) - lpL);
                lpR += 0.10f * ((p2 * 2f - 1f) - lpR);
                Add(l, at, i, lpL * env * 0.11f * level);
                Add(r, at, i, lpR * env * 0.11f * level);
            }
        }
    }

    static void Pluck(float[] l, float[] r, int at, float seconds, float hz,
        float level, float pan)
    {
        int n = (int)(seconds * Rate);
        float phase = 0f, lp = 0f;
        float lGain = Mathf.Clamp01(1f - pan);
        float rGain = Mathf.Clamp01(1f + pan);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            phase += hz / Rate; phase -= Mathf.Floor(phase);
            float square = phase < 0.5f ? 1f : -1f;
            lp += 0.25f * (square - lp);
            float v = lp * Mathf.Exp(-9f * t) * Mathf.Min(1f, t * 400f)
                * 0.3f * level;
            Add(l, at, i, v * lGain);
            Add(r, at, i, v * rGain);
        }
    }
}
