using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Brawl's sound set, synthesized at runtime — no assets, no licenses,
/// nothing to download, exactly the way the rest of this project builds
/// its world. Robot-on-robot is INHARMONIC metal: struck steel rings at
/// non-integer overtones (×2.76, ×5.40, ×8.93 of the base — bell physics),
/// which is precisely why human punch foley sounds wrong on these bodies.
///
/// Every id can be OVERRIDDEN by dropping an AudioClip into
/// Resources/BrawlSfx/&lt;name&gt;.wav (hit1..hit3, graze, block, whoosh,
/// jump, blastfire, blasthit, ko, chargeready, roundding, gong, victory)
/// — a found stock sound beats the synth automatically, no code change.
///
/// Clips bake lazily on first use and cache for the session (~a few ms
/// each, once). Playback spawns a throwaway source with ±6% pitch so no
/// two clangs are identical twins.
/// </summary>
public static class BrawlAudio
{
    public enum Id
    {
        Hit, HitHeavy, Graze, Block, Whoosh, Jump,
        BlastFire, BlastHit, KO, ChargeReady,
        RoundDing, Gong, Victory,
    }

    const int Rate = 44100;
    const float Master = 0.85f;

    static readonly Dictionary<Id, AudioClip[]> Cache = new Dictionary<Id, AudioClip[]>();

    /// <summary>Positioned effect (half-3D so the lane pans naturally).</summary>
    public static void Play(Id id, Vector3 position, float volume = 1f)
    {
        Spawn(id, position, volume, 0.5f);
    }

    /// <summary>Screen-space effect — announcements, the charge ping.</summary>
    public static void PlayFlat(Id id, float volume = 1f)
    {
        Spawn(id, Vector3.zero, volume, 0f);
    }

    static void Spawn(Id id, Vector3 position, float volume, float spatial)
    {
        var variants = Get(id);
        if (variants.Length == 0)
            return;
        var clip = variants[Random.Range(0, variants.Length)];

        var go = new GameObject("BrawlSfx");
        go.transform.position = position;
        var source = go.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = Mathf.Clamp01(volume) * Master;
        source.pitch = Random.Range(0.94f, 1.06f);
        // One recording set, three weights: heavies drop a fourth and
        // push, grazes ride up a third and sit back.
        if (id == Id.HitHeavy)
        {
            source.pitch *= 0.8f;
            source.volume = Mathf.Min(1f, source.volume * 1.2f);
        }
        else if (id == Id.Graze)
        {
            source.pitch *= 1.3f;
            source.volume *= 0.55f;
        }
        source.spatialBlend = spatial;
        source.dopplerLevel = 0f;
        source.Play();
        Object.Destroy(go, clip.length / source.pitch + 0.1f);
    }

    static AudioClip[] Get(Id id)
    {
        if (Cache.TryGetValue(id, out var cached))
            return cached;
        var clips = Build(id);
        Cache[id] = clips;
        return clips;
    }

    static AudioClip[] Build(Id id)
    {
        switch (id)
        {
            // Real recordings first: every Resources/BrawlSfx/punch*.wav
            // joins the random pool (ExternalData/Punch, converted). The
            // synth clang is only the no-files fallback.
            case Id.Hit:
            case Id.HitHeavy:
            {
                var punches = LoadFamily("punch");
                if (punches.Length > 0)
                    return punches;
                return id == Id.Hit
                    ? Variants("hit", 3, v => Clang($"hit{v}", 150f + 40f * v, 0.34f, 0.9f, seed: 10 + v))
                    : Variants("hitheavy", 2, v => Clang($"hitheavy{v}", 95f + 25f * v, 0.55f, 1.0f, seed: 20 + v));
            }
            // Grazes are the punch recordings too — pitched up and pulled
            // back in Spawn, so a light contact sounds like a lighter
            // version of a real hit instead of a different instrument.
            case Id.Graze:
            {
                var punches = LoadFamily("punch");
                if (punches.Length > 0)
                    return punches;
                return Variants("graze", 2, v => Clang($"graze{v}", 420f + 90f * v, 0.16f, 0.45f, seed: 30 + v));
            }
            case Id.Block:
                return One("block", ShieldZap());
            case Id.Whoosh:
                return One("whoosh", Whoosh());
            case Id.Jump:
                return One("jump", Servo(280f, 640f, 0.16f));
            case Id.BlastFire:
                return One("blastfire", Zap(760f, 140f, 0.35f));
            case Id.BlastHit:
                return One("blasthit", Zap(500f, 80f, 0.4f));
            case Id.KO:
                return One("ko", Clang("ko", 72f, 1.4f, 1.1f, seed: 50));
            case Id.ChargeReady:
                return One("chargeready", Ding(880f, 0.28f));
            case Id.RoundDing:
                return One("roundding", Layer(Ding(660f, 0.30f), Delay(Ding(990f, 0.30f), 0.09f)));
            // Not a Clang: the tube ring is exactly the sound that got the
            // synth impacts fired. A clean falling two-tone marks the round.
            case Id.Gong:
                return One("gong", Layer(Ding(330f, 0.6f), Delay(Ding(196f, 0.8f), 0.11f)));
            case Id.Victory:
                return One("victory", Layer(Ding(523f, 0.5f),
                    Delay(Ding(659f, 0.5f), 0.12f), Delay(Ding(784f, 0.6f), 0.24f)));
            default:
                return new AudioClip[0];
        }
    }

    /// <summary>Every BrawlSfx clip whose name starts with the prefix.</summary>
    static AudioClip[] LoadFamily(string prefix)
    {
        var found = new List<AudioClip>();
        foreach (var clip in Resources.LoadAll<AudioClip>("BrawlSfx"))
            if (clip != null && clip.name.StartsWith(prefix))
                found.Add(clip);
        return found.ToArray();
    }

    static AudioClip[] Variants(string name, int count, System.Func<int, AudioClip> make)
    {
        var clips = new AudioClip[count];
        for (int v = 0; v < count; v++)
            clips[v] = Resources.Load<AudioClip>($"BrawlSfx/{name}{v + 1}") ?? make(v);
        return clips;
    }

    static AudioClip[] One(string name, AudioClip synth)
    {
        var found = Resources.Load<AudioClip>($"BrawlSfx/{name}");
        return new[] { found != null ? found : synth };
    }

    // ---------------------------------------------------------- generators

    /// <summary>
    /// Struck metal: inharmonic partial stack over a low thump and a click
    /// of noise at the moment of contact. `brightness` feeds the upper
    /// partials — grazes are bright and thin, KOs dark and long.
    /// </summary>
    static AudioClip Clang(string name, float baseHz, float seconds, float brightness, int seed)
    {
        float[] ratios = { 1f, 2.76f, 5.40f, 8.93f, 13.34f };
        var random = new System.Random(seed);
        var amps = new float[ratios.Length];
        var decays = new float[ratios.Length];
        for (int p = 0; p < ratios.Length; p++)
        {
            float tier = p == 0 ? 1f : brightness / (p + 0.6f);
            amps[p] = tier * (0.75f + 0.5f * (float)random.NextDouble());
            decays[p] = (3.5f + p * 2.4f) / seconds * (0.8f + 0.4f * (float)random.NextDouble());
        }

        return Bake(name, seconds, (t, noise) =>
        {
            float sample = 0f;
            for (int p = 0; p < ratios.Length; p++)
                sample += amps[p] * Mathf.Exp(-decays[p] * t)
                          * Mathf.Sin(2f * Mathf.PI * baseHz * ratios[p] * t);
            // The body thump under the ring, and the contact click on top.
            sample += 1.1f * Mathf.Exp(-18f * t) * Mathf.Sin(2f * Mathf.PI * (baseHz * 0.45f) * t);
            sample += noise * 0.8f * Mathf.Exp(-90f * t);
            return sample * 0.35f;
        });
    }

    /// <summary>The guard eating a hit: a hum with ring-modulated fizz.</summary>
    static AudioClip ShieldZap()
    {
        return Bake("block", 0.28f, (t, noise) =>
        {
            float envelope = Mathf.Exp(-11f * t);
            float hum = Mathf.Sin(2f * Mathf.PI * 210f * t) * 0.8f;
            float fizz = noise * Mathf.Sin(2f * Mathf.PI * 95f * t);
            return (hum + fizz * 0.9f) * envelope * 0.45f;
        });
    }

    /// <summary>Air over a swinging limb: shaped noise, no tone.</summary>
    static AudioClip Whoosh()
    {
        float[] history = new float[1];
        return Bake("whoosh", 0.22f, (t, noise) =>
        {
            // One-pole lowpass whose cutoff rides the envelope — the sweep.
            float envelope = Mathf.Sin(Mathf.Clamp01(t / 0.22f) * Mathf.PI);
            float alpha = 0.06f + 0.24f * envelope;
            history[0] += alpha * (noise - history[0]);
            return history[0] * envelope * 1.6f * 0.5f;
        });
    }

    /// <summary>A little motor: swept sine with a flutter.</summary>
    static AudioClip Servo(float fromHz, float toHz, float seconds)
    {
        return Bake("servo", seconds, (t, noise) =>
        {
            float u = t / seconds;
            float hz = Mathf.Lerp(fromHz, toHz, u);
            float flutter = 1f + 0.06f * Mathf.Sin(2f * Mathf.PI * 37f * t);
            return Mathf.Sin(2f * Mathf.PI * hz * flutter * t)
                   * Mathf.Sin(u * Mathf.PI) * 0.28f;
        });
    }

    /// <summary>Energy discharge: falling FM sweep over noise.</summary>
    static AudioClip Zap(float fromHz, float toHz, float seconds)
    {
        return Bake("zap", seconds, (t, noise) =>
        {
            float u = t / seconds;
            float hz = Mathf.Lerp(fromHz, toHz, u * u);
            float body = Mathf.Sin(2f * Mathf.PI * hz * t
                + 2.5f * Mathf.Sin(2f * Mathf.PI * hz * 1.5f * t));
            float envelope = Mathf.Exp(-6f * u) * Mathf.Min(1f, t * 200f);
            return (body + noise * 0.25f) * envelope * 0.5f;
        });
    }

    /// <summary>A clean bright ping — announcements, the charge meter.</summary>
    static AudioClip Ding(float hz, float seconds)
    {
        return Bake("ding", seconds, (t, noise) =>
        {
            float envelope = Mathf.Exp(-9f * t) * Mathf.Min(1f, t * 400f);
            return (Mathf.Sin(2f * Mathf.PI * hz * t)
                    + 0.4f * Mathf.Sin(2f * Mathf.PI * hz * 2f * t)) * envelope * 0.4f;
        });
    }

    // ------------------------------------------------------------ plumbing

    static AudioClip Bake(string name, float seconds, System.Func<float, float, float> sample)
    {
        int count = Mathf.CeilToInt(Rate * seconds);
        var data = new float[count];
        var random = new System.Random(name.GetHashCode());
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)Rate;
            float noise = (float)(random.NextDouble() * 2.0 - 1.0);
            // tanh soft-clip keeps stacked partials from ever cracking.
            data[i] = (float)System.Math.Tanh(sample(t, noise));
        }
        // A short fade-out guards against end clicks.
        int fade = Mathf.Min(count, Rate / 100);
        for (int i = 0; i < fade; i++)
            data[count - 1 - i] *= i / (float)fade;

        var clip = AudioClip.Create(name, count, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static AudioClip Layer(params AudioClip[] parts)
    {
        int count = 0;
        foreach (var part in parts)
            count = Mathf.Max(count, part.samples);
        var mixed = new float[count];
        var buffer = new float[count];
        foreach (var part in parts)
        {
            System.Array.Clear(buffer, 0, buffer.Length);
            part.GetData(buffer, 0);
            for (int i = 0; i < part.samples; i++)
                mixed[i] += buffer[i];
        }
        for (int i = 0; i < count; i++)
            mixed[i] = (float)System.Math.Tanh(mixed[i]);

        var clip = AudioClip.Create("layer", count, 1, Rate, false);
        clip.SetData(mixed, 0);
        return clip;
    }

    static AudioClip Delay(AudioClip part, float seconds)
    {
        int offset = Mathf.CeilToInt(Rate * seconds);
        var data = new float[part.samples + offset];
        var buffer = new float[part.samples];
        part.GetData(buffer, 0);
        System.Array.Copy(buffer, 0, data, offset, buffer.Length);
        var clip = AudioClip.Create("delay", data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
