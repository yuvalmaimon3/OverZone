using UnityEngine;

/// <summary>
/// ScriptableObject that holds all the data for ONE attack type.
/// 
/// HOW TO USE:
///   Right-click in Project window → Create → OverZone → Attack Definition
///   Fill in the values, then drag the asset into AttackController's fields.
/// 
/// WHY ScriptableObject?
///   - Lives as a file in the project (not on a GameObject).
///   - Multiple players can share the same attack data, or each can have their own.
///   - Changing stats (damage, cooldown) only requires editing the asset, not the code.
/// </summary>
[CreateAssetMenu(fileName = "NewAttack", menuName = "OverZone/Attack Definition")]
public class AttackDefinition : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Display name shown in UI and debug logs.")]
    public string attackName = "Basic Attack";

    [Header("Damage")]
    [Tooltip("How much damage this attack deals per hit.")]
    public float damage = 10f;

    [Header("Timing")]
    [Tooltip("Seconds the player must wait before using this attack again (cooldown).")]
    public float cooldown = 0.5f;

    [Header("Attack Type & Range")]
    [Tooltip("Determines which attack behavior runs when this attack is used.")]
    public AttackType attackType = AttackType.Projectile;

    [Tooltip("Radius (metres) for melee and AoE attacks. Ignored for projectile attacks.")]
    public float meleeRange = 1.5f;

    [Header("Projectile Settings")]
    [Tooltip("The prefab that gets spawned when attack type is Projectile. Must have a Projectile component.")]
    public GameObject projectilePrefab;

    [Tooltip("How fast the projectile travels (units per second).")]
    public float projectileSpeed = 15f;

    [Header("Animation")]
    [Tooltip("Name of the Trigger parameter in the player's Animator to fire when this attack is used.")]
    public string animationTrigger = "Attack";
}

/// <summary>
/// Controls how the attack physically works in the game world.
/// 
/// Melee       → instant hit check in front of the player (sword, punch)
/// Projectile  → spawns a moving object (bullet, fireball, arrow)
/// AreaOfEffect → hits everything inside a radius (explosion, shockwave)
/// </summary>
public enum AttackType
{
    Melee,
    Projectile,
    AreaOfEffect
}
