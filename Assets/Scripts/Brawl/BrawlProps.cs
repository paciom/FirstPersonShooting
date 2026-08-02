using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The strikeable furniture registry: crates sign up here, and a
/// fighter's strike that found no robot asks whether it found a prop —
/// kicking a crate across the lane goes through exactly the same fist-bone
/// query as kicking a robot. Same static-lifetime rules as BrawlGround.
/// </summary>
public static class BrawlProps
{
    public interface IStrikeable
    {
        /// <summary>True when the strike connected; the prop reacts itself.</summary>
        bool Strike(Vector3 point, float radius, BrawlFighter attacker);
        void Despawn();
    }

    static readonly List<IStrikeable> Props = new List<IStrikeable>();

    public static void Clear() => Props.Clear();

    public static void Register(IStrikeable prop) => Props.Add(prop);

    public static void Unregister(IStrikeable prop) => Props.Remove(prop);

    public static int Count<T>() where T : class
    {
        int count = 0;
        foreach (var prop in Props)
            if (prop is T)
                count++;
        return count;
    }

    public static bool TryStrike(Vector3 point, float radius, BrawlFighter attacker)
    {
        for (int i = Props.Count - 1; i >= 0; i--)
            if (Props[i].Strike(point, radius, attacker))
                return true;
        return false;
    }

    /// <summary>Round reset: the next round opens on a clean lane.</summary>
    public static void DespawnAll()
    {
        for (int i = Props.Count - 1; i >= 0; i--)
            Props[i].Despawn();
        Props.Clear();
    }
}
