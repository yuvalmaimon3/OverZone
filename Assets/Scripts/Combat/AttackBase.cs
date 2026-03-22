using UnityEngine;

/// <summary>
/// Abstract base class for all attack behaviors.
/// 
/// WHY abstract?
///   - Forces every attack type (Melee, Projectile, AoE) to implement Execute().
///   - AttackController only calls Execute() and doesn't care HOW it works internally.
///   - Adding a new attack type = create a new class that extends AttackBase. Nothing else changes.
/// 
/// HOW TO ADD A NEW ATTACK TYPE:
///   1. Create a new class: public class LaserAttack : AttackBase { ... }
///   2. Override Execute() with your logic.
///   3. Add a new value to the AttackType enum in AttackDefinition.cs.
///   4. Add a case for it in AttackController.ExecuteAttack().
/// </summary>
public abstract class AttackBase : MonoBehaviour
{
    /// <summary>
    /// Perform the attack.
    /// </summary>
    /// <param name="data">The ScriptableObject holding this attack's stats (damage, range, etc.)</param>
    /// <param name="origin">The Transform of the player firing the attack (used for position and direction).</param>
    /// <param name="direction">The world-space direction the attack travels or checks into.</param>
    /// <returns>True if the attack successfully hit at least one target. Used by AttackController to charge the ultimate.</returns>
    public abstract bool Execute(AttackDefinition data, Transform origin, Vector3 direction);
}
