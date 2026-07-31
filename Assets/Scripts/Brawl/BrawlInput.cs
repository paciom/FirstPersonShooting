using UnityEngine;

/// <summary>
/// The player's hands on P1: keyboard edges and holds folded into the
/// fighter's Intent every frame. Touch arrives in the polish phase through
/// the same struct — the fighter never knows the difference.
///
/// Runs before every fighter (execution order): a KeyDown edge written after
/// the fighter's read would be overwritten and lost by the next frame's
/// rewrite, which reads as dropped inputs — the one sin a fighting game
/// cannot commit.
/// </summary>
[DefaultExecutionOrder(-10)]
public class BrawlInput : MonoBehaviour
{
    public BrawlFighter Fighter { get; set; }

    void Update()
    {
        if (Fighter == null)
            return;
        // Movement is OPPONENT-RELATIVE — the kid-simple frame for an
        // arena brawler: W/S close in and back off, A/D circle around.
        // The fighter auto-faces, so 'forward' always means 'at them'.
        float inOut = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f)
                      + BrawlTouch.Move;
        float circle = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
        Vector3 forward = Fighter.FacingDir;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 world = forward * inOut + right * circle;
        if (world.sqrMagnitude > 1f)
            world.Normalize();

        // Keyboard and touch merge here; either hand can drive any part.
        Fighter.Driven = new BrawlFighter.Intent
        {
            move = new Vector2(world.x, world.z),
            jump = Input.GetKeyDown(KeyCode.Space) || BrawlTouch.ConsumeJump(),
            punch = Input.GetKeyDown(KeyCode.J) || BrawlTouch.ConsumePunch(),
            kick = Input.GetKeyDown(KeyCode.K) || BrawlTouch.ConsumeKick(),
            block = Input.GetKey(KeyCode.C) || Input.GetKey(KeyCode.LeftShift)
                    || BrawlTouch.BlockHeld,
            blast = Input.GetKeyDown(KeyCode.L) || BrawlTouch.ConsumeBlast(),
        };
    }
}
