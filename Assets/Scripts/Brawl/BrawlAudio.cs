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
            // Nothing below carries a sustained tone: the shield hum and
            // the FM laser zaps were the last beeps standing.
            case Id.Block:
                return One("block", NoiseBurst("block", 0.22f, 0.45f, 0.10f, 5f));
            case Id.Whoosh:
                return One("whoosh", Whoosh());
            // A jump is just air — the servo wheep read as cartoon.
            case Id.Jump:
                return One("jump", Whoosh());
            case Id.BlastFire:
                return One("blastfire", NoiseBurst("blastfire", 0.30f, 0.55f, 0.06f, 4f));
            case Id.BlastHit:
                return One("blasthit",
                    Layer(NoiseBurst("blasthit", 0.30f, 0.50f, 0.08f, 4.5f), Boom(75f, 0.45f)));
            case Id.KO:
                return One("ko", Clang("ko", 72f, 1.4f, 1.1f, seed: 50));
            // No melodies anywhere below: sine dings and arpeggios are the
            // cartoon register. Cues are drums (pitch-dropping booms) and
            // rising air — an arena, not a cereal commercial.
            case Id.ChargeReady:
                return One("chargeready", Swell(0.35f));
            case Id.RoundDing:
                return One("roundding", Boom(95f, 0.40f));
            case Id.Gong:
                return One("gong", Boom(62f, 0.75f));
            case Id.Victory:
                return One("victory", Layer(Boom(70f, 0.8f), Delay(Swell(0.65f), 0.06f)));
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

    /// <summary>
    /// A tone-free energy crack: filtered noise whose colour falls from
    /// bright to dark as it decays. Blocks, blaster fire, blaster impact —
    /// all static and air, no hum to read as a beep.
    /// </summary>
    static AudioClip NoiseBurst(string name, float seconds, float cutoffStart,
        float cutoffEnd, float punch)
    {
        float[] lowpass = new float[1];
        return Bake(name, seconds, (t, noise) =>
        {
            float u = Mathf.Clamp01(t / seconds);
            lowpass[0] += Mathf.Lerp(cutoffStart, cutoffEnd, u) * (noise - lowpass[0]);
            float envelope = Mathf.Exp(-punch * u) * Mathf.Min(1f, t * 500f);
            return lowpass[0] * envelope * 2f;
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

    /// <summary>
    /// A drum, not a bell: low body whose pitch falls out of the attack,
    /// noise slap on top, no sustained tone to read as a beep.
    /// </summary>
    static AudioClip Boom(float hz, float seconds)
    {
        return Bake("boom", seconds, (t, noise) =>
        {
            float envelope = Mathf.Exp(-4.5f * t / seconds);
            float droop = 1f + 0.45f * Mathf.Exp(-28f * t);
            float body = Mathf.Sin(2f * Mathf.PI * hz * droop * t);
            float slap = noise * Mathf.Exp(-80f * t) * 0.7f;
            return (body * envelope + slap) * 0.6f;
        });
    }

    /// <summary>Rising air — a filtered-noise swell, tone-free.</summary>
    static AudioClip Swell(float seconds)
    {
        float[] lowpass = new float[1];
        return Bake("swell", seconds, (t, noise) =>
        {
            float u = Mathf.Clamp01(t / seconds);
            float envelope = Mathf.Sin(u * Mathf.PI);
            lowpass[0] += (0.04f + 0.30f * u) * (noise - lowpass[0]);
            return lowpass[0] * envelope * 2.2f * 0.55f;
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
