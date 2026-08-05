using UnityEngine;

/// <summary>
/// Plays a robot's transformation as stop motion: one whole mesh swapped for the
/// next, robot at one end and vehicle at the other. Given a second stage set it
/// plays two acts back to back — robot to tank and home, then robot to jet and
/// home — so one card shows every form the robot has.
///
/// WHY NOT FOLD THE RIG. The old showcase folded the humanoid and called the
/// crouch a vehicle. It reads as a magic trick rather than a transformation
/// because a humanoid mesh has no tracks, no hull and no turret anywhere in it
/// to unfold — there is nothing to become. Each stage here is a separate model
/// built from a frame of a generated transformation video, so the tracks really
/// do appear, because a stage that has tracks is a different mesh.
///
/// The cost is that consecutive stages share no topology, so nothing can be
/// interpolated and every change is a pop. Two things hide that, and both
/// matter more than adding stages:
///   - the holder keeps moving (rise and spin) straight through the swap, so the
///     eye tracks continuous motion and forgives discrete geometry;
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

    [Tooltip("Optional second act, robot first and jet last. Its own stage one " +
             "stays hidden — the robot holds always show the first set's, which " +
             "is the real rig.")]
    public GameObject[] jetStages;

    [Tooltip("Seconds the fully-formed robot and vehicle each hold before moving.")]
    public float holdSeconds = 1.1f;

    [Tooltip("Seconds one transformation takes, end to end.")]
    public float transformSeconds = 1.2f;

    [Tooltip("Degrees per second of turntable spin, applied throughout.")]
    public float degreesPerSecond = 40f;

    [Tooltip("How far the vehicle rides up, so a long low hull stays framed.")]
    public float vehicleLift = 0.18f;

    [Tooltip("Head start through the cycle, so a row of cards is never in step.")]
    public float phaseSeconds;

    /// <summary>
    /// Seconds one full loop takes — two acts when a jet set is assigned, one
    /// otherwise. For callers staggering a row of these by phaseSeconds.
    /// </summary>
    public float CycleSeconds => (HasJetAct ? 4f : 2f) * (holdSeconds + transformSeconds);

    bool HasJetAct => jetStages != null && jetStages.Length > 1;

    Renderer[][] _stageRenderers;
    Renderer[][] _jetRenderers;
    Vector3 _restPosition;
    float _clock;
    int _shown = -1;
    bool _shownJet;

    // Start, not Awake: AddComponent runs Awake immediately, which is before the
    // caller has assigned stages or phaseSeconds.
    void Start()
    {
        _restPosition = transform.localPosition;
        _clock = phaseSeconds;

        if (stages == null || stages.Length == 0)
            return;

        _stageRenderers = CollectRenderers(stages);
        _jetRenderers = HasJetAct ? CollectRenderers(jetStages) : null;

        Show(false, 0);
    }

    static Renderer[][] CollectRenderers(GameObject[] set)
    {
        var renderers = new Renderer[set.Length][];
        for (int i = 0; i < set.Length; i++)
            renderers[i] = set[i] != null
                ? set[i].GetComponentsInChildren<Renderer>(true)
                : new Renderer[0];
        return renderers;
    }

    void Update()
    {
        if (_stageRenderers == null || _stageRenderers.Length < 2)
            return;

        transform.Rotate(0f, degreesPerSecond * Time.deltaTime, 0f);

        float act = 2f * (holdSeconds + transformSeconds);
        float cycle = _jetRenderers != null ? 2f * act : act;
        _clock = (_clock + Time.deltaTime) % cycle;

        bool jetAct = _clock >= act;
        var set = jetAct ? _jetRenderers : _stageRenderers;
        float t = jetAct ? _clock - act : _clock;

        // One act: hold robot, transform out, hold vehicle, transform back.
        int last = set.Length - 1;
        int index;
        if (t < holdSeconds)
            index = 0;
        else if (t < holdSeconds + transformSeconds)
            index = StageAt(set, (t - holdSeconds) / transformSeconds);
        else if (t < 2f * holdSeconds + transformSeconds)
            index = last;
        else
            index = StageAt(set, 1f - (t - 2f * holdSeconds - transformSeconds) / transformSeconds);

        Show(jetAct, index);

        // Ride the lift with the stage, not after it, so the motion is
        // continuous across the pops rather than a slide once they finish.
        float progress = last > 0 ? index / (float)last : 0f;
        transform.localPosition = _restPosition + Vector3.up * (vehicleLift * progress);
    }

    static int StageAt(Renderer[][] set, float normalised)
    {
        int last = set.Length - 1;
        return Mathf.Clamp(Mathf.FloorToInt(normalised * (last + 1)), 0, last);
    }

    void Show(bool jet, int index)
    {
        // Both sets open on a standing robot, but only the first set's is the
        // real rig; showing it for every robot hold keeps the two acts meeting
        // on one model instead of popping between two near-identical robots.
        if (index == 0)
            jet = false;

        if (index == _shown && jet == _shownJet)
            return;

        ShowInSet(_stageRenderers, jet ? -1 : index);
        ShowInSet(_jetRenderers, jet ? index : -1);
        _shown = index;
        _shownJet = jet;
    }

    static void ShowInSet(Renderer[][] set, int shown)
    {
        if (set == null)
            return;
        for (int i = 0; i < set.Length; i++)
        {
            bool on = i == shown;
            foreach (var renderer in set[i])
                if (renderer != null)
                    renderer.enabled = on;
        }
    }
}
