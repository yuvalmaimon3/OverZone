using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Projectile attack behavior: bullet, fireball, arrow, etc.
///
/// HOW IT WORKS:
///   Spawns the prefab defined in AttackDefinition.projectilePrefab.
///   Passes the shooter's identity (ClientId + TeamId) to the projectile so it
///   can skip the shooter and team-mates in its collision logic.
///
/// The projectile prefab drives its own behaviour type (pierce, bounce, explode…)
/// via the ProjectileBehaviourType field on the Projectile component.
/// </summary>
public class ProjectileAttack : AttackBase
{
    public override bool Execute(AttackDefinition data, Transform origin, Vector3 direction)
    {
        if (data.projectilePrefab == null)
        {
            Debug.LogWarning($"[ProjectileAttack] '{data.attackName}' has no projectile prefab assigned!");
            return false;
        }

        // ── Resolve shooter identity ─────────────────────────────────────────
        // Walk up to the root to find NetworkObject (handles the case where
        // origin is a child transform, e.g. a weapon bone).
        var netObj  = origin.GetComponentInParent<NetworkObject>();
        ulong ownerId = netObj != null ? netObj.OwnerClientId : ulong.MaxValue;

        // TeamComponent is optional — if not present, -1 disables friendly-fire checks
        var teamComp = origin.GetComponentInParent<TeamComponent>();
        int ownerTeamId = teamComp != null ? teamComp.TeamId : -1;

        // ── Spawn the projectile ─────────────────────────────────────────────
        // Offset slightly in front of and above the shooter so the projectile
        // doesn't immediately trigger the shooter's own collider.
        Vector3 spawnPos = origin.position + Vector3.up * 0.5f + direction * 0.7f;
        Quaternion spawnRot = Quaternion.LookRotation(direction);

        GameObject go = Instantiate(data.projectilePrefab, spawnPos, spawnRot);

        // ── Initialize with all required context ─────────────────────────────
        var proj = go.GetComponent<Projectile>();
        if (proj != null)
        {
            proj.Initialize(data.damage, data.projectileSpeed, direction, ownerId, ownerTeamId);
        }
        else
        {
            Debug.LogWarning($"[ProjectileAttack] Prefab '{data.projectilePrefab.name}' " +
                             $"is missing a Projectile component.");
        }

        // Projectile attacks always count as "fired successfully".
        // The actual hit (and ultimate charge) is determined by the projectile on impact.
        return true;
    }
}
