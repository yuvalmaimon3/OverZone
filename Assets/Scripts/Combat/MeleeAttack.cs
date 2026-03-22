using UnityEngine;

/// <summary>
/// Melee attack behavior: sword, punch, club, etc.
/// 
/// HOW IT WORKS:
///   Casts a sphere from the player's chest forward.
///   The first object with a HealthComponent inside meleeRange gets damaged.
///   This is instant — no projectile flies through the air.
/// 
/// EXAMPLE USES:
///   - Sword slash
///   - Punch
///   - Shield bash
/// </summary>
public class MeleeAttack : AttackBase
{
    [Tooltip("Only objects on these layers can be hit. By default all layers are included.")]
    [SerializeField] private LayerMask hitLayers = ~0; // ~0 means ALL layers

    public override bool Execute(AttackDefinition data, Transform origin, Vector3 direction)
    {
        // Start the cast from chest height so we don't hit the floor
        Vector3 startPos = origin.position + Vector3.up * 0.5f;

        // SphereCast is like a thick raycast — catches targets slightly to the side too
        // radius 0.4f = roughly half a player's width, feels natural for melee
        if (Physics.SphereCast(startPos, 0.4f, direction, out RaycastHit hit, data.meleeRange, hitLayers))
        {
            // Walk up to the root of the hit object in case a child collider was hit
            var health = hit.collider.GetComponentInParent<HealthComponent>();
            if (health != null)
            {
                health.TakeDamage(data.damage);
                return true; // HIT — AttackController will use this to charge the ultimate
            }
        }

        return false; // MISS
    }
}
