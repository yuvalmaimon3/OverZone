using UnityEngine;

/// <summary>
/// Aim-line visualiser for the local player.
///
/// INPUT MODEL
///   LMB held    → show aim line, update direction from mouse each frame
///   LMB released → owner queries AimDirection, fires, line disappears
///
/// VISUAL
///   A LineRenderer drawn in world space.
///   • Width  = projectile diameter (both endpoints) — matches the actual
///              projectile size so the player can see what will hit.
///   • Length = aimRange (default 3 units, represents how far the
///              projectile can reach; change per-attack-type as needed).
///   • Colour = semi-transparent grey gradient (opaque near player,
///              fades toward the end — classic laser-sight look).
///
/// DIRECTION
///   A horizontal plane at the player's Y is intersected by a ray cast
///   from the camera through the mouse cursor.  The vector from the
///   player to that hit point (XZ only) is the aim direction.
///   Falls back to transform.forward when no valid mouse hit is found.
///
/// USAGE (called by MainPlayerController)
///   StartAim()  — show line
///   StopAim()   — hide line
///   AimDirection (property) — normalised XZ direction to fire toward
///
/// NOTE: this component is disabled by the network layer for non-owner
///   players so only the local player ever sees their own aim line.
/// </summary>
public class AimSystem : MonoBehaviour
{
    // ── Inspector config ──────────────────────────────────────────

    [Header("Aim Line — Width")]
    [Tooltip("Width of the aim line. Set this to match 2 × projectile radius " +
             "so the line represents the exact hit corridor of the projectile.")]
    [SerializeField] private float lineWidth = 0.6f;      // default = projectile diameter

    [Header("Aim Line — Length")]
    [Tooltip("Max distance the aim line reaches. Represents the projectile's " +
             "travel range. Infrastructure is ready; change per attack type.")]
    [SerializeField] private float aimRange = 3f;

    [Header("Aim Line — Colour")]
    [SerializeField] private Color nearColour = new Color(0.85f, 0.85f, 0.85f, 0.75f);
    [SerializeField] private Color farColour  = new Color(0.85f, 0.85f, 0.85f, 0.08f);

    // ── Runtime ───────────────────────────────────────────────────

    private LineRenderer _line;
    private bool         _isAiming;
    private Vector3      _aimDirection;

    // ── Public API ────────────────────────────────────────────────

    /// <summary>True while the player is holding the aim button.</summary>
    public bool IsAiming => _isAiming;

    /// <summary>
    /// Normalised XZ direction the player is currently aiming toward.
    /// Falls back to transform.forward when no valid mouse hit is found.
    /// </summary>
    public Vector3 AimDirection => _aimDirection;

    /// <summary>Current configured aim range (can be changed per attack type).</summary>
    public float AimRange
    {
        get => aimRange;
        set { aimRange = value; }
    }

    /// <summary>Current line width (should equal 2 × projectile radius).</summary>
    public float LineWidth
    {
        get => lineWidth;
        set
        {
            lineWidth = value;
            if (_line != null)
            {
                _line.startWidth = value;
                _line.endWidth   = value;
            }
        }
    }

    // ── Unity lifecycle ───────────────────────────────────────────

    void Awake()
    {
        _aimDirection = transform.forward;
        _aimDirection.y = 0f;
        if (_aimDirection.sqrMagnitude < 0.001f) _aimDirection = Vector3.forward;

        BuildLineRenderer();
    }

    void Update()
    {
        if (!_isAiming) return;
        UpdateAimDirection();
        DrawLine();
    }

    // ── Public controls ───────────────────────────────────────────

    /// <summary>Call when the player starts holding the aim button.</summary>
    public void StartAim()
    {
        _isAiming = true;
        _line.enabled = true;
    }

    /// <summary>Call when the player releases the aim button (after firing).</summary>
    public void StopAim()
    {
        _isAiming = false;
        _line.enabled = false;
    }

    // ── Internal ──────────────────────────────────────────────────

    private void UpdateAimDirection()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        // Cast a ray from camera through the mouse cursor.
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        // Intersect with the horizontal plane at the player's current height.
        var groundPlane = new Plane(Vector3.up, new Vector3(0f, transform.position.y, 0f));

        if (groundPlane.Raycast(ray, out float dist))
        {
            Vector3 hitWorld = ray.GetPoint(dist);
            Vector3 dir      = hitWorld - transform.position;
            dir.y = 0f;

            if (dir.sqrMagnitude > 0.01f)
                _aimDirection = dir.normalized;
            // else: too close to player — keep previous direction
        }
        // else: ray parallel to plane (camera looking straight up/down) — keep previous
    }

    private void DrawLine()
    {
        // Start slightly in front of the player so it clears the player sphere.
        float clearance  = lineWidth * 0.5f + 0.1f;
        Vector3 startPos = transform.position + _aimDirection * clearance;
        Vector3 endPos   = transform.position + _aimDirection * aimRange;

        // Lock to player height so the line floats at projectile level.
        startPos.y = transform.position.y;
        endPos.y   = transform.position.y;

        _line.SetPosition(0, startPos);
        _line.SetPosition(1, endPos);
    }

    private void BuildLineRenderer()
    {
        _line = gameObject.GetComponent<LineRenderer>();
        if (_line == null)
            _line = gameObject.AddComponent<LineRenderer>();

        _line.positionCount = 2;
        _line.startWidth    = lineWidth;
        _line.endWidth      = lineWidth;
        _line.useWorldSpace = true;
        _line.shadowCastingMode  = UnityEngine.Rendering.ShadowCastingMode.Off;
        _line.receiveShadows     = false;
        _line.allowOcclusionWhenDynamic = false;
        _line.enabled = false;

        // Gradient: opaque near player, transparent at the tip.
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(nearColour.r, nearColour.g, nearColour.b), 0f),
                new GradientColorKey(new Color(farColour.r,  farColour.g,  farColour.b),  1f)
            },
            new[]
            {
                new GradientAlphaKey(nearColour.a, 0f),
                new GradientAlphaKey(farColour.a,  1f)
            });
        _line.colorGradient = gradient;

        // Use Sprites/Default which supports per-vertex alpha in both built-in and URP.
        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null)
        {
            _line.material = new Material(shader);
        }
        else
        {
            // Fallback: just rely on the colorGradient on whatever default material exists.
            _line.startColor = nearColour;
            _line.endColor   = farColour;
        }
    }
}
