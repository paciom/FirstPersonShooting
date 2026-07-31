using UnityEngine;

/// <summary>
/// The solid the physics props bounce OFF: one capsule on the fighter
/// root, no rigidbody — so a tumbling crate caroms away realistically
/// while the robot itself stays script-driven and unshoved. Lives on the
/// Ignore Raycast layer so the terrain probe and the camera never mistake
/// a robot for scenery.
/// </summary>
public class BrawlBodyBumper : MonoBehaviour
{
    public BrawlFighter Owner;
}
