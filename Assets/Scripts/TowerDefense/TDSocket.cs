using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One tower foundation on the canyon rim — the only ground a tower can
/// stand on. Sockets are the placement law of this mode: pre-surveyed pads
/// mean a tower can never wander onto the lane, block the raiders' route,
/// or hide out of range — every classic free-placement failure priced out
/// at map-build time.
///
/// Occupancy is DERIVED, never stored: a socket is taken while any live
/// structure stands on it. Storing a flag instead would desync the moment a
/// tower is sold (collapse is a coroutine) or a recompile resets the field.
/// </summary>
public class TDSocket : MonoBehaviour
{
    /// <summary>
    /// Every socket on the current map. Maintained by OnEnable/OnDisable —
    /// the pads are children of the map root, so teardown's DestroyImmediate
    /// sweeps the registry clean through OnDisable.
    /// </summary>
    public static readonly List<TDSocket> All = new List<TDSocket>();

    /// <summary>Close enough to this pad's centre to count as standing on it.</summary>
    const float ClaimRadius = 2f;

    public Vector3 Center => transform.position;

    public bool Occupied
    {
        get
        {
            foreach (var building in Building.All)
            {
                if (building == null || !building.IsAlive)
                    continue;
                Vector3 flat = building.transform.position - transform.position;
                flat.y = 0f;
                if (flat.sqrMagnitude <= ClaimRadius * ClaimRadius)
                    return true;
            }
            return false;
        }
    }

    void OnEnable()
    {
        if (!All.Contains(this))
            All.Add(this);
    }

    void OnDisable()
    {
        All.Remove(this);
    }

    /// <summary>The free socket nearest a point, or null if none within reach.</summary>
    public static TDSocket NearestFree(Vector3 point, float within)
    {
        TDSocket best = null;
        float bestSqr = within * within;
        foreach (var socket in All)
        {
            if (socket == null || socket.Occupied)
                continue;
            Vector3 flat = socket.Center - point;
            flat.y = 0f;
            if (flat.sqrMagnitude < bestSqr)
            {
                bestSqr = flat.sqrMagnitude;
                best = socket;
            }
        }
        return best;
    }
}
