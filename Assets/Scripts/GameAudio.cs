using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The game-wide MAJOR-EVENT sound set — one sound per thing the player
/// actually feels (I got hit, I scored, the wave broke through), never one
/// per bullet. Two guards keep a 16-robot firefight from becoming a wall
/// of noise: each id has a minimum re-trigger gap, and the total number of
/// live one-shot voices is capped — past the cap a sound simply doesn't
/// spawn, which in a barrage is inaudible anyway.
///
/// Synthesized at runtime out of BrawlAudio's generators (drums and air,
/// no melodic dings), same as the rest of the project's audio. Any id can
/// be overridden by dropping an AudioClip at Resources/GameSfx/&lt;name&gt;.wav
/// (matchstart, victory, defeat, playerhit, playerdown, enemydown,
/// explosion, warning, wavestart, waveclear, respawn) — the file wins
/// automatically, no code change.
/// </summary>
public static class GameAudio
{
    public enum Id
    {
        MatchStart,   // the opening gong of any mode
        Victory,      // the player's side won
        Defeat,       // the player's side lost
        PlayerHit,    // the player took real damage
        PlayerDown,   // the player de-rezzed / tank destroyed / jet down
        EnemyDown,    // the PLAYER scored a takedown (not bot-on-bot)
        Explosion,    // a big thing died: building, boss, bomber
        Warning,      // base under attack / leak / boss incoming
        WaveStart,    // tower defense wave rolls out
        WaveClear,    // wave survived
        Respawn,      // the player re-materializes
    }

    const float Master = 0.9f;
    const int MaxVoices = 12;

    static readonly Dictionary<Id, AudioClip> Cache = new Dictionary<Id, AudioClip>();
    static readonly Dictionary<Id, float> LastPlay = new Dictionary<Id, float>();
    static readonly List<GameObject> Live = new List<GameObject>();

    /// <summary>Positioned event — explosions, takedowns out in the world.</summary>
    public static void Play(Id id, Vector3 position, float volume = 1f)
    {
        Spawn(id, position, volume, 0.6f);
    }

    /// <summary>Screen-space event — it happened to YOU, or to the match.</summary>
    public static void PlayFlat(Id id, float volume = 1f)
    {
        Spawn(id, Vector3.zero, volume, 0f);
    }

    static void Spawn(Id id, Vector3 position, float volume, float spatial)
    {
        if (Application.isBatchMode)
            return;
        // Re-trigger gap: a shotgun spread of simultaneous hits is ONE hit
        // to the ear. Warnings get a long gap so a swarm at the gate can't
        // turn the alarm into a drumroll.
        float gap = MinGap(id);
        if (LastPlay.TryGetValue(id, out float last)
            && Time.unscaledTime - last < gap)
            return;
        LastPlay[id] = Time.unscaledTime;

        Live.RemoveAll(go => go == null);
        if (Live.Count >= MaxVoices)
            return;

        var clip = Get(id);
        if (clip == null)
            return;

        var go = new GameObject("GameSfx");
        go.transform.position = position;
        var source = go.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = Mathf.Clamp01(volume) * Master;
        source.pitch = Random.Range(0.96f, 1.04f);
        source.spatialBlend = spatial;
        source.dopplerLevel = 0f;
        source.minDistance = 6f;
        source.maxDistance = 90f;
        source.Play();
        Live.Add(go);
        Object.Destroy(go, clip.length / source.pitch + 0.1f);

        // The match-deciding stingers push the music out of the way.
        if (id == Id.Victory || id == Id.Defeat || id == Id.PlayerDown)
            GameMusic.Duck(1.6f);
    }

    static float MinGap(Id id)
    {
        switch (id)
        {
            case Id.Warning: return 3.0f;
            case Id.PlayerHit: return 0.18f;
            case Id.Explosion: return 0.15f;
            case Id.EnemyDown: return 0.12f;
            default: return 0.10f;
        }
    }

    static AudioClip Get(Id id)
    {
        if (Cache.TryGetValue(id, out var cached))
            return cached;
        string name = id.ToString().ToLowerInvariant();
        var clip = Resources.Load<AudioClip>("GameSfx/" + name);
        if (clip == null)
            clip = Build(id);
        Cache[id] = clip;
        return clip;
    }

    static AudioClip Build(Id id)
    {
        switch (id)
        {
            case Id.MatchStart:
                return BrawlAudio.Boom(90f, 0.45f);
            case Id.Victory:
                return BrawlAudio.Layer(BrawlAudio.Boom(70f, 0.8f),
                    BrawlAudio.Delay(BrawlAudio.Swell(0.65f), 0.06f));
            // Defeat is the victory drum falling: two booms stepping DOWN.
            case Id.Defeat:
                return BrawlAudio.Layer(BrawlAudio.Boom(64f, 0.6f),
                    BrawlAudio.Delay(BrawlAudio.Boom(46f, 1.1f), 0.35f));
            // Getting hit is felt, not heard from outside: a body thud with
            // a static crackle, short enough to never smear under fire.
            case Id.PlayerHit:
                return BrawlAudio.Layer(
                    BrawlAudio.NoiseBurst("playerhit", 0.16f, 0.5f, 0.12f, 6f),
                    BrawlAudio.Boom(120f, 0.16f));
            case Id.PlayerDown:
                return BrawlAudio.Layer(
                    BrawlAudio.Clang("playerdown", 66f, 1.2f, 0.9f, seed: 77),
                    BrawlAudio.Boom(50f, 0.9f));
            // The score sound: a bright ring over a small drum — readable
            // over everything without being a slot machine.
            case Id.EnemyDown:
                return BrawlAudio.Layer(
                    BrawlAudio.Clang("enemydown", 210f, 0.35f, 0.8f, seed: 88),
                    BrawlAudio.Boom(90f, 0.25f));
            case Id.Explosion:
                return BrawlAudio.Layer(
                    BrawlAudio.NoiseBurst("explosion", 0.5f, 0.5f, 0.04f, 3.5f),
                    BrawlAudio.Boom(55f, 0.6f));
            case Id.Warning:
                return BrawlAudio.Layer(BrawlAudio.Boom(80f, 0.3f),
                    BrawlAudio.Delay(BrawlAudio.Boom(80f, 0.3f), 0.22f));
            case Id.WaveStart:
                return BrawlAudio.Boom(95f, 0.4f);
            case Id.WaveClear:
                return BrawlAudio.Swell(0.5f);
            case Id.Respawn:
                return BrawlAudio.Swell(0.35f);
            default:
                return null;
        }
    }
}
