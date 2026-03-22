using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

// ─────────────────────────────────────────────────────────────────────────────
// PROJECTILE BEHAVIOUR TYPES
//
// Each type changes what happens when the projectile hits something.
// Set this on the prefab in the Inspector so each prefab has its own personality.
//
//  DestroyOnAnyContact   – basic bullet: disappears on the first thing it touches
//  PierceSoftTargets     – arrow style: passes through damageable targets (characters),
//                          stops at hard surfaces (walls, terrain)
//  BounceOffHardSurfaces – ricochets off walls, damages on character contact
//  PierceEverything      – laser / beam: nothing stops it (only lifetime destroys it)
//  ExplodeOnContact      – artillery shell: AoE damage on hard OR character contact
// ─────────────────────────────────────────────────────────────────────────────
public enum ProjectileBehaviourType
{
    DestroyOnAnyContact,
    PierceSoftTargets,
    BounceOffHardSurfaces,
    PierceEverything,
    ExplodeOnContact
}

/// <summary>
/// Core projectile component. Attach to any projectile prefab.
///
/// COLLIDER SETUP REQUIRED ON THE PREFAB:
///   Add a Collider component and check "Is Trigger" on it.
///   The Rigidbody is added automatically via [RequireComponent].
///
/// HARD vs SOFT surfaces:
///   - "Soft"  = anything that has a HealthComponent  (characters, barrels, etc.)
///   - "Hard"  = everything else that is on 'hardLayers' (walls, terrain, floors)
///   Hard surfaces stop or bounce the projectile. Soft ones may be pierced.
///
/// OWNER / TEAM IMMUNITY:
///   The projectile is told who fired it at Initialize() time.
///   It will never damage the shooter or anyone on the same team.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Projectile : MonoBehaviour
{
    // ── Behaviour type ──────────────────────────────────────────────────────
    [Header("Behaviour")]
    [Tooltip("Controls what happens on collision. Each type suits a different weapon style.")]
    [SerializeField] private ProjectileBehaviourType behaviourType = ProjectileBehaviourType.DestroyOnAnyContact;

    // ── Surface detection ───────────────────────────────────────────────────
    [Header("Surface Layers")]
    [Tooltip("Layers that count as HARD surfaces (walls, terrain). " +
             "These stop or bounce the projectile.\n" +
             "Objects with a HealthComponent are always treated as SOFT regardless of layer.")]
    [SerializeField] private LayerMask hardLayers = ~0; // Default: all layers are hard

    // ── Behaviour limits ────────────────────────────────────────────────────
    [Header("Pierce Settings (PierceSoftTargets only)")]
    [Tooltip("How many soft targets (characters) can be pierced before the projectile dies.")]
    [SerializeField] private int maxPierceCount = 3;

    [Header("Bounce Settings (BounceOffHardSurfaces only)")]
    [Tooltip("How many times the projectile can bounce off hard surfaces.")]
    [SerializeField] private int maxBounceCount = 3;

    [Header("Explosion Settings (ExplodeOnContact only)")]
    [Tooltip("Radius of the splash damage dealt when the projectile explodes.")]
    [SerializeField] private float splashRadius = 2.5f;

    // ── Physics ─────────────────────────────────────────────────────────────
    [Header("Physics")]
    [Tooltip("Seconds before auto-destroy if nothing is hit.")]
    [SerializeField] private float lifetime = 4f;

    [Tooltip("Tick this to give the projectile a gravity arc (grenade, mortar shell).")]
    [SerializeField] private bool useGravity = false;

    // ── Runtime state ───────────────────────────────────────────────────────
    private float  _damage;
    private float  _speed;          // kept for re-launching after a bounce
    private Rigidbody _rb;

    // Who fired this — used to prevent the shooter from damaging themselves
    private ulong _ownerClientId = ulong.MaxValue;

    // Team ID of the shooter — team-mates are immune (0 = no team)
    private int _ownerTeamId = -1;

    // Counters that count down as the projectile pierces or bounces
    private int _pierceRemaining;
    private int _bounceRemaining;

    // Track already-hit objects so PierceSoftTargets doesn't hit the same target twice
    private readonly HashSet<GameObject> _alreadyHit = new HashSet<GameObject>();

    // ── Init ────────────────────────────────────────────────────────────────

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _pierceRemaining = maxPierceCount;
        _bounceRemaining = maxBounceCount;
    }

    /// <summary>
    /// Called by ProjectileAttack right after the projectile is spawned.
    /// </summary>
    /// <param name="damage">Damage dealt per hit.</param>
    /// <param name="speed">Travel speed (units/second).</param>
    /// <param name="direction">World-space direction to travel.</param>
    /// <param name="ownerClientId">NGO ClientId of the player who fired this. Pass ulong.MaxValue if unknown.</param>
    /// <param name="ownerTeamId">Team index of the shooter. Pass -1 if no team system is used.</param>
    public void Initialize(float damage, float speed, Vector3 direction,
                           ulong ownerClientId = ulong.MaxValue, int ownerTeamId = -1)
    {
        _damage        = damage;
        _speed         = speed;
        _ownerClientId = ownerClientId;
        _ownerTeamId   = ownerTeamId;

        _rb.useGravity = useGravity;
        // linearVelocity is the Unity 6 API (replaces the deprecated 'velocity')
        _rb.linearVelocity = direction.normalized * speed;

        Destroy(gameObject, lifetime);
    }

    // ── Collision ───────────────────────────────────────────────────────────

    /// <summary>
    /// Fired when the trigger collider overlaps another collider.
    /// Requires "Is Trigger" to be ticked on the projectile's Collider.
    /// </summary>
    void OnTriggerEnter(Collider other)
    {
        // Never collide with other projectiles — prevents two bullets cancelling each other
        if (other.GetComponent<Projectile>() != null)
            return;

        // Skip if this exact object was already registered as hit (prevents double-damage)
        GameObject root = other.transform.root.gameObject;
        if (_alreadyHit.Contains(root))
            return;

        // ── Immunity checks ─────────────────────────────────────────────────

        // Check if the hit object belongs to the shooter or their team
        if (IsImmune(other))
            return;

        // ── Determine surface type ──────────────────────────────────────────

        bool isSoftTarget = other.GetComponentInParent<HealthComponent>() != null;
        bool isHardSurface = !isSoftTarget && IsOnHardLayer(other.gameObject);

        // ── Dispatch to the correct behaviour ───────────────────────────────

        switch (behaviourType)
        {
            case ProjectileBehaviourType.DestroyOnAnyContact:
                HandleDestroyOnAnyContact(other, isSoftTarget, isHardSurface, root);
                break;

            case ProjectileBehaviourType.PierceSoftTargets:
                HandlePierceSoftTargets(other, isSoftTarget, isHardSurface, root);
                break;

            case ProjectileBehaviourType.BounceOffHardSurfaces:
                HandleBounceOffHardSurfaces(other, isSoftTarget, isHardSurface, root);
                break;

            case ProjectileBehaviourType.PierceEverything:
                HandlePierceEverything(other, root);
                break;

            case ProjectileBehaviourType.ExplodeOnContact:
                HandleExplodeOnContact();
                break;
        }
    }

    // ── Behaviour implementations ────────────────────────────────────────────

    /// <summary>
    /// DestroyOnAnyContact — basic bullet.
    /// Damages the first thing touched and disappears immediately.
    /// </summary>
    private void HandleDestroyOnAnyContact(Collider other, bool isSoft, bool isHard, GameObject root)
    {
        if (isSoft || isHard)
        {
            DealDamage(other);
            Destroy(gameObject);
        }
        // Hits nothing that qualifies (e.g. a trigger zone) — keep flying
    }

    /// <summary>
    /// PierceSoftTargets — arrow / spear style.
    /// Passes through characters (soft), consuming one pierce charge per target.
    /// Destroyed when it hits a hard surface OR runs out of pierce charges.
    /// </summary>
    private void HandlePierceSoftTargets(Collider other, bool isSoft, bool isHard, GameObject root)
    {
        if (isHard)
        {
            // Hard surface always kills the projectile
            Destroy(gameObject);
            return;
        }

        if (isSoft)
        {
            DealDamage(other);
            _alreadyHit.Add(root); // mark so we don't hit the same target twice
            _pierceRemaining--;

            if (_pierceRemaining <= 0)
                Destroy(gameObject);
            // Otherwise keep flying through
        }
    }

    /// <summary>
    /// BounceOffHardSurfaces — billiard ball / magic bolt style.
    /// Reflects direction off hard surfaces. Deals damage to characters and keeps going.
    /// Destroyed when bounce counter runs out OR it hits a character.
    /// </summary>
    private void HandleBounceOffHardSurfaces(Collider other, bool isSoft, bool isHard, GameObject root)
    {
        if (isSoft)
        {
            DealDamage(other);
            _alreadyHit.Add(root);
            // Projectile continues moving after hitting a soft target
            return;
        }

        if (isHard)
        {
            if (_bounceRemaining <= 0)
            {
                Destroy(gameObject);
                return;
            }

            // Reflect the velocity off the surface normal
            // ContactPoint normal is not directly available in OnTriggerEnter,
            // so we use a quick raycast to get the surface normal for reflection
            Vector3 incomingDir = _rb.linearVelocity.normalized;
            if (Physics.Raycast(transform.position, incomingDir, out RaycastHit hit, 1.5f, hardLayers))
            {
                Vector3 reflected = Vector3.Reflect(incomingDir, hit.normal);
                _rb.linearVelocity = reflected * _speed;
                transform.forward = reflected;
            }

            _bounceRemaining--;
        }
    }

    /// <summary>
    /// PierceEverything — laser / beam style.
    /// Damages everything it touches. Nothing stops it; only lifetime destroys it.
    /// </summary>
    private void HandlePierceEverything(Collider other, GameObject root)
    {
        DealDamage(other);
        _alreadyHit.Add(root); // still avoid double-damage on the same target
    }

    /// <summary>
    /// ExplodeOnContact — artillery shell / grenade style.
    /// Triggers an AoE damage burst around the impact point and self-destructs.
    /// </summary>
    private void HandleExplodeOnContact()
    {
        // Splash damage: hurt everyone in the blast radius (except owner / team-mates)
        Collider[] splashHits = Physics.OverlapSphere(transform.position, splashRadius);
        foreach (var col in splashHits)
        {
            if (IsImmune(col))
                continue;

            DealDamage(col);
        }

        // TODO: spawn explosion VFX prefab here
        Destroy(gameObject);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Apply damage to the HealthComponent on the hit collider (if any).
    /// </summary>
    private void DealDamage(Collider other)
    {
        var health = other.GetComponentInParent<HealthComponent>();
        if (health != null)
            health.TakeDamage(_damage);
    }

    /// <summary>
    /// Returns true if the hit collider belongs to the shooter or a team-mate.
    /// These objects should never be damaged by this projectile.
    /// </summary>
    private bool IsImmune(Collider other)
    {
        // ── Self-immunity (shooter cannot be hit by their own projectile) ──
        var netObj = other.GetComponentInParent<NetworkObject>();
        if (netObj != null && netObj.OwnerClientId == _ownerClientId)
            return true;

        // ── Team immunity (team-mates are safe from friendly fire) ──
        // TeamComponent is a small script on players that stores their team index.
        // If _ownerTeamId == -1 it means no team system is in use → skip check.
        if (_ownerTeamId >= 0)
        {
            var hitTeam = other.GetComponentInParent<TeamComponent>();
            if (hitTeam != null && hitTeam.TeamId == _ownerTeamId)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true if the object is on one of the configured hard layers.
    /// </summary>
    private bool IsOnHardLayer(GameObject obj)
    {
        // LayerMask bit check: (hardLayers.value & (1 << obj.layer)) != 0
        return (hardLayers.value & (1 << obj.layer)) != 0;
    }
}
