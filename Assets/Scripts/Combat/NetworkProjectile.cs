using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Server-authoritative projectile.
///
/// MOVEMENT  — runs on the server only (FixedUpdate guard: IsServer).
///   • Constant speed, no gravity, locked to spawn Y.
///   • SphereCast one step ahead each frame.
///       – Hits environment (no NetworkObject) → Vector3.Reflect bounce.
///       – Hits a player NetworkObject       → ignored here; OverlapSphere handles damage.
///
/// COLLISION — OverlapSphere each frame (server only) after movement.
///   • Ignores owner's own NetworkObject.
///   • Ignores other projectiles (NetworkProjectile component present).
///   • Any other NetworkObject with a HealthComponent → TakeDamage + Despawn.
///
/// SYNC — NetworkTransform (server-authoritative, Interpolate on) keeps
///   all clients' visuals smooth without any client-side physics.
///
/// LIFETIME  — Despawned after <lifetime> seconds regardless.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class NetworkProjectile : NetworkBehaviour
{
    [Header("Projectile")]
    [SerializeField] private float speed    = 12f;
    [SerializeField] private float lifetime = 3f;
    [SerializeField] private float radius   = 0.3f;
    [SerializeField] private float damage   = 20f;

    [Tooltip("Seconds after spawn where player-hit detection is disabled " +
             "so the projectile can clear the owner's collider.")]
    [SerializeField] private float gracePeriod = 0.12f;

    // Runtime state (server only)
    private Vector3 _velocity;
    private float   _spawnY;
    private ulong   _ownerClientId;
    private float   _elapsed;
    private bool    _ready;

    // ── NGO lifecycle ──────────────────────────────────────────────

    /// <summary>
    /// Called by the server immediately after NetworkObject.Spawn().
    /// Sets direction, owner, and the fixed Y the projectile must stay at.
    /// </summary>
    public void Initialize(Vector3 direction, ulong ownerClientId, float spawnY)
    {
        Vector3 flat = new Vector3(direction.x, 0f, direction.z);
        if (flat.sqrMagnitude < 0.001f) flat = Vector3.forward;

        _velocity       = flat.normalized * speed;
        _ownerClientId  = ownerClientId;
        _spawnY         = spawnY;
        _ready          = true;
    }

    // ── Server-only simulation ─────────────────────────────────────

    void FixedUpdate()
    {
        if (!IsServer || !_ready) return;

        _elapsed += Time.fixedDeltaTime;

        if (_elapsed >= lifetime)
        {
            NetworkObject.Despawn(true);
            return;
        }

        MoveWithBounce();
        CheckPlayerHit();
    }

    // ── Movement and bounce ────────────────────────────────────────

    private void MoveWithBounce()
    {
        float dt       = Time.fixedDeltaTime;
        float castDist = speed * dt + radius * 0.1f;
        Vector3 dir    = _velocity.normalized;

        if (Physics.SphereCast(transform.position, radius, dir, out RaycastHit hit, castDist))
        {
            // Did we hit another NetworkObject (player / projectile)?
            // If so, skip the bounce — OverlapSphere handles damage separately.
            bool isNetworkObject = hit.collider.GetComponentInParent<NetworkObject>() != null;

            if (!isNetworkObject)
            {
                // Hard surface → reflect velocity and lock Y.
                Vector3 reflected = Vector3.Reflect(dir, hit.normal);
                reflected.y = 0f;

                // Failsafe: if reflection is near zero (near-parallel hit), reverse direction.
                if (reflected.sqrMagnitude < 0.01f) reflected = -dir;

                _velocity = reflected.normalized * speed;

                // Teleport to just outside the hit surface.
                Vector3 bouncePos = hit.point + hit.normal * (radius + 0.06f);
                bouncePos.y = _spawnY;
                transform.position = bouncePos;
                return;
            }
        }

        // Normal step — advance and lock Y.
        Vector3 next = transform.position + _velocity * dt;
        next.y = _spawnY;
        transform.position = next;
    }

    // ── Player hit detection ───────────────────────────────────────

    private void CheckPlayerHit()
    {
        // Skip grace period so the projectile clears the owner before checking.
        if (_elapsed < gracePeriod) return;

        Collider[] hits = Physics.OverlapSphere(transform.position, radius + 0.05f);
        foreach (Collider col in hits)
        {
            NetworkObject netObj = col.GetComponentInParent<NetworkObject>();
            if (netObj == null) continue;                          // environment
            if (netObj.OwnerClientId == _ownerClientId) continue;  // owner
            if (netObj.GetComponent<NetworkProjectile>() != null) continue; // other projectile

            // Enemy hit — deal damage and destroy.
            netObj.GetComponent<HealthComponent>()?.TakeDamage(damage);
            NetworkObject.Despawn(true);
            return;
        }
    }
}
