using UnityEngine;

/// <summary>
/// Stores which team this player (or object) belongs to.
/// Attach this to the NetworkPlayer prefab.
///
/// TEAM IDS:
///   0 = Team A (e.g. host side)
///   1 = Team B (e.g. client side)
///  -1 = No team (FFA — free for all, anyone can hit this)
///
/// HOW FRIENDLY FIRE WORKS:
///   Projectile reads the shooter's TeamId at fire time.
///   If the hit target has the same TeamId → the hit is ignored.
///   Set TeamId = -1 to make an object hittable by everyone.
/// </summary>
public class TeamComponent : MonoBehaviour
{
    [Tooltip("Team index. 0 and 1 are the two player teams. -1 means no team (free for all).")]
    public int TeamId = -1;
}
