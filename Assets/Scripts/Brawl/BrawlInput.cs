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
        // Keyboard and touch merge here; either hand can drive any part.
        Fighter.Driven = new BrawlFighter.Intent
        {
            move = Mathf.Clamp(
                (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f)
                + BrawlTouch.Move, -1f, 1f),
            jump = Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.W)
                   || BrawlTouch.ConsumeJump(),
            punch = Input.GetKeyDown(KeyCode.J) || BrawlTouch.ConsumePunch(),
            kick = Input.GetKeyDown(KeyCode.K) || BrawlTouch.ConsumeKick(),
            block = Input.GetKey(KeyCode.S) || BrawlTouch.BlockHeld,
            blast = Input.GetKeyDown(KeyCode.L) || BrawlTouch.ConsumeBlast(),
        };
    }
}
