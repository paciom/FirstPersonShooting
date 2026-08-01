using UnityEngine;

/// <summary>
/// The vertical scroller's eye: high, behind, and pitched steeply enough that
/// the battlefield reads as a map rather than as a horizon.
///
/// THE HERO IS NOT IN THE MIDDLE. It rides about a third up the screen, which
/// buys the top two thirds for the thing that actually matters — what is coming
/// down the field at you. A camera centred on the tank spends half its pixels on
/// ground already driven over.
///
/// It follows the FRONTIER rather than the hero. The frontier only ever
/// advances, so the view can never scroll backwards: a hero that reverses slides
/// down the screen toward the bottom edge instead of dragging the world with it,
/// which is what makes the mode a scroller rather than a field with a tank on
/// it. Sideways is a soft follow, at less than one to one, so an outside lane
/// still reads as an outside lane.
///
/// THE PITCH IS FIXED, NOT AIMED AT A LOOK POINT. A camera that turns to look at
/// something has a pitch that moves with it, and a tilted camera's top edge runs
/// away to the horizon as the pitch shallows — at which point the far edge of
/// the view is unbounded and nothing can be spawned reliably off screen. Sixty
/// degrees keeps the top ray well below the horizon, which pins the visible
/// strip to <see cref="VisibleAhead"/> metres and lets the mode know exactly
/// where "off the top of the screen" is. See <see cref="TankField"/>, whose
/// width is set against the same numbers.
/// </summary>
public class TankRaidCamera : MonoBehaviour
{
    const float Height = 26f;

    /// <summary>Degrees below the horizontal. See the class note.</summary>
    const float Pitch = 60f;

    /// <summary>Metres behind the frontier the camera sits. Chosen so the hero,
    /// which lives in the strip just behind the frontier, rides about a third up
    /// the screen.</summary>
    const float Behind = 13f;

    /// <summary>
    /// How far past the frontier the top of the screen reaches, at a 16:9 view.
    /// Read by the mode to decide where raiders may appear; the arithmetic is
    /// <c>Height / tan(Pitch - halfFov) - Behind</c>, and it is a constant
    /// because both angles are.
    /// </summary>
    public const float VisibleAhead = 26f;

    /// <summary>Fraction of the hero's own x the camera adopts.</summary>
    const float LaneFollow = 0.5f;

    Transform _hero;
    float _x;
    float _z;
    float _shake;
    bool _snapped;

    public void Follow(Transform hero)
    {
        _hero = hero;
        _snapped = false;
    }

    /// <summary>A knock the player felt — a shell landing, or their own de-rez.</summary>
    public void Shake(float amount) => _shake = Mathf.Max(_shake, amount);

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        float heroX = _hero != null ? _hero.position.x : 0f;
        float wantZ = TankField.Frontier;

        if (!_snapped)
        {
            _snapped = true;
            _x = heroX * LaneFollow;
            _z = wantZ;
        }
        else
        {
            _x = Mathf.Lerp(_x, heroX * LaneFollow, 1f - Mathf.Exp(-5f * dt));
            // Never eased backwards. Frontier is monotonic, and the moment the
            // hero outruns it the two are the same number — so a lerp here would
            // show the field slowing down at exactly the moment it should not.
            _z = Mathf.Max(_z, Mathf.Lerp(_z, wantZ, 1f - Mathf.Exp(-9f * dt)));
        }

        if (_shake > 0.001f)
            _shake = Mathf.Max(0f, _shake - dt * 1.8f);
        Vector3 jolt = _shake > 0.001f
            ? new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0f) * (_shake * 0.5f)
            : Vector3.zero;

        transform.position = new Vector3(_x, Height, _z - Behind) + jolt;
        transform.rotation = Quaternion.Euler(Pitch, 0f, 0f);
    }

    /// <summary>
    /// Where a screen point lands on the battlefield floor.
    ///
    /// This is how the mouse aims the turret on a desktop — the ground plane at
    /// y = 0 is the whole world here, so there is nothing to raycast against and
    /// a plane intersection is both exact and free. Returns false behind the
    /// camera, where the ray never reaches the floor at all.
    /// </summary>
    public static bool GroundUnder(Camera camera, Vector3 screenPoint, out Vector3 ground)
    {
        ground = Vector3.zero;
        if (camera == null)
            return false;
        var ray = camera.ScreenPointToRay(screenPoint);
        if (Mathf.Abs(ray.direction.y) < 1e-5f)
            return false;
        float t = -ray.origin.y / ray.direction.y;
        if (t <= 0f)
            return false;
        ground = ray.GetPoint(t);
        return true;
    }
}
