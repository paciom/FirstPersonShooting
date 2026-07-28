using UnityEngine;

/// <summary>
/// Plays a robot's transformation as stop motion: one whole mesh swapped for the
/// next, robot at one end and vehicle at the other.
///
/// WHY NOT FOLD THE RIG. The old showcase folded the humanoid and called the
/// crouch a vehicle. It reads as a magic trick rather than a transformation
/// because a humanoid mesh has no tracks, no hull and no turret anywhere in it
/// to unfold — there is nothing to become. Each stage here is a separate model
/// built from a frame of a generated transformation video, so the tracks really
/// do appear, because a stage that has tracks is a different mesh.
///
/// The cost is that consecutive stages share no topology, so nothing can be
/// interpolated and every change is a pop. Three things hide that, and all three
/// matter more than adding stages:
///   - the holder keeps moving (rise and spin) straight through the swap, so the
///     eye tracks continuous motion and forgives discrete geometry;
///   - a light burst fires on each swap, which is how the films cut around it;
///   - stages are only ~150ms apart, below the point where a still registers.
///
/// Stage models are instantiated once and toggled by renderer, never by
/// SetActive on the root: the robot stage carries an Animator, and deactivating
/// it would reset the state machine every cycle.
/// </summary>
public class StopMotionTransformer : MonoBehaviour
{
    [Tooltip("Stages in order: robot first, vehicle last.")]
    public GameObject[] stages;

    [Tooltip("Seconds the fully-formed robot and vehicle each hold before moving.")]
    public float holdSeconds = 1.1f;

    [Tooltip("Seconds the whole transformation takes, end to end.")]
    public float transformSeconds = 1.2f;

    [Tooltip("Degrees per second of turntable spin, applied throughout.")]
    public float degreesPerSecond = 40f;

    [Tooltip("How far the vehicle rides up, so a long low hull stays framed.")]
    public float vehicleLift = 0.18f;

    [Tooltip("Colour of the burst that covers each stage swap.")]
    public Color burstColor = new Color(0.2f, 0.9f, 1f);

    [Tooltip("Head start through the cycle, so a row of cards is never in step.")]
    public float phaseSeconds;

    Renderer[][] _stageRenderers;
    Vector3 _restPosition;
    float _clock;
    int _shown = -1;

    // Start, not Awake: AddComponent runs Awake immediately, which is before the
    // caller has assigned stages or phaseSeconds.
    void Start()
    {
        _restPosition = transform.localPosition;
        _clock = phaseSeconds;

        if (stages == null || stages.Length == 0)
            return;

        _stageRenderers = new Renderer[stages.Length][];
        for (int i = 0; i < stages.Length; i++)
            _stageRenderers[i] = stages[i] != null
                ? stages[i].GetComponentsInChildren<Renderer>(true)
                : new Renderer[0];

        Show(0);
    }

    void Update()
    {
        if (_stageRenderers == null || _stageRenderers.Length < 2)
            return;

        transform.Rotate(0f, degreesPerSecond * Time.deltaTime, 0f);

        float cycle = 2f * (holdSeconds + transformSeconds);
        _clock = (_clock + Time.deltaTime) % cycle;

        // One cycle: hold robot, transform out, hold vehicle, transform back.
        int last = _stageRenderers.Length - 1;
        float t = _clock;
        int index;
        if (t < holdSeconds)
            index = 0;
        else if (t < holdSeconds + transformSeconds)
            index = StageAt((t - holdSeconds) / transformSeconds);
        else if (t < 2f * holdSeconds + transformSeconds)
            index = last;
        else
            index = StageAt(1f - (t - 2f * holdSeconds - transformSeconds) / transformSeconds);

        Show(index);

        // Ride the lift with the stage, not after it, so the motion is
        // continuous across the pops rather than a slide once they finish.
        float progress = last > 0 ? index / (float)last : 0f;
        transform.localPosition = _restPosition + Vector3.up * (vehicleLift * progress);
    }

    int StageAt(float normalised)
    {
        int last = _stageRenderers.Length - 1;
        return Mathf.Clamp(Mathf.FloorToInt(normalised * (last + 1)), 0, last);
    }

    void Show(int index)
    {
        if (index == _shown)
            return;

        for (int i = 0; i < _stageRenderers.Length; i++)
        {
            bool on = i == index;
            foreach (var renderer in _stageRenderers[i])
                if (renderer != null)
                    renderer.enabled = on;
        }

        // Not on the first assignment — a burst as the card appears looks like
        // a glitch rather than a transformation.
        if (_shown >= 0)
            VfxUtil.Explosion(transform.position, burstColor, 0.5f);
        _shown = index;
    }
}
