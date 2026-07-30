using UnityEngine;

/// <summary>
/// Tag on every hurtbox collider naming its owner and its worth: a strike
/// into a Vital part (torso, head) is damage, anything else is a graze —
/// sparks and sound, no health. The colliders live on the BONES, so the
/// hurtboxes follow whatever pose the animation is in.
/// </summary>
public class BrawlBodyPart : MonoBehaviour
{
    public BrawlFighter Owner;
    public bool Vital;

    /// <summary>"head", "torso", "arm", "leg" — for the debug log.</summary>
    public string Label;
}
