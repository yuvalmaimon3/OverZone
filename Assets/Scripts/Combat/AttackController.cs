using UnityEngine;
using Unity.Netcode;

/// <summary>
/// The main attack brain. Attach this to the NetworkPlayer prefab.
/// 
/// RESPONSIBILITIES:
///   1. Listen for player input (left click = normal attack, Q = ultimate).
///   2. Check cooldowns before allowing an attack.
///   3. Route the attack to the correct behavior (Melee / Projectile / AoE).
///   4. Track ultimate charge — fills from successful normal attack hits + passive timer.
///   5. Fire the right Animator trigger so the animation plays.
/// 
/// SETUP IN INSPECTOR:
///   - Drag a "Normal Attack" ScriptableObject into 'normalAttackData'.
///   - Drag an "Ultimate Attack" ScriptableObject into 'ultimateAttackData'.
///   - Tune 'chargePerHit' and 'chargePerSecond' to feel right for your game.
/// 
/// TO CREATE ATTACK ASSETS:
///   Right-click in Project window → Create → OverZone → Attack Definition
/// </summary>
public class AttackController : NetworkBehaviour
{
    // ── Inspector fields ───────────────────────────────────────────────────

    [Header("Attack Data (drag ScriptableObjects here)")]
    [Tooltip("Normal attack — fires on Left Mouse Button.")]
    [SerializeField] private AttackDefinition normalAttackData;

    [Tooltip("Ultimate attack — fires on Q key or Right Mouse Button. Requires full charge.")]
    [SerializeField] private AttackDefinition ultimateAttackData;

    [Header("Ultimate Charge Settings")]
    [Tooltip("How much charge (out of 100) a single successful normal-attack hit gives.")]
    [SerializeField] private float chargePerHit = 20f;

    [Tooltip("Charge gained per second passively over time. Set 0 to disable passive charging.")]
    [SerializeField] private float chargePerSecond = 3f;

    // ── Runtime state ───────────────────────────────────────────────────────

    // Counts down to 0; attack is available when timer reaches 0
    private float _normalCooldownTimer;
    private float _ultimateCooldownTimer;

    // Ultimate charge on a 0–100 scale
    private float _ultimateCharge;
    private const float MaxCharge = 100f;

    // References to the attack behavior components (created automatically in Awake)
    private MeleeAttack      _meleeAttack;
    private ProjectileAttack _projectileAttack;

    // Cached once in Awake — avoids GetComponentInChildren every time an attack fires.
    private Animator _animator;

    // ── Public read-only state (used by UI) ────────────────────────────────

    /// <summary>0.0 = empty, 1.0 = full. Use this to drive the ultimate charge bar.</summary>
    public float UltimateChargePercent => _ultimateCharge / MaxCharge;

    /// <summary>True when the normal attack cooldown has expired.</summary>
    public bool CanNormalAttack => _normalCooldownTimer <= 0f;

    /// <summary>True when ultimate is fully charged AND its own cooldown has expired.</summary>
    public bool CanUltimate => _ultimateCooldownTimer <= 0f && _ultimateCharge >= MaxCharge;

    // ── Unity & NGO lifecycle ───────────────────────────────────────────────

    void Awake()
    {
        // Auto-attach attack behaviors if not already present on this GameObject.
        _meleeAttack      = GetComponent<MeleeAttack>()      ?? gameObject.AddComponent<MeleeAttack>();
        _projectileAttack = GetComponent<ProjectileAttack>() ?? gameObject.AddComponent<ProjectileAttack>();

        // Cache the Animator once. TriggerAnimation() is called on every attack
        // so GetComponentInChildren every call would waste CPU.
        _animator = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        // IMPORTANT: Only the owner (the local player) reads input and ticks timers.
        // The non-owner copy on the other client is just a visual puppet — it should
        // not react to this machine's keyboard.
        if (!IsOwner)
            return;

        TickCooldowns();
        TickUltimateCharge();
        HandleInput();
    }

    // ── Input ───────────────────────────────────────────────────────────────

    private void HandleInput()
    {
        // NOTE: LMB (Left Mouse Button) is intentionally NOT handled here.
        // MainPlayerController owns LMB via the AimSystem:
        //   hold LMB = show aim line, release LMB = fire NetworkProjectile via ServerRpc.
        // Handling LMB here as well would fire two different projectile systems at once
        // (a local non-networked Projectile AND a server-authoritative NetworkProjectile).

        // Q key OR Right Mouse Button → ultimate attack
        if (Input.GetKeyDown(KeyCode.Q) || Input.GetMouseButtonDown(1))
            TryUltimateAttack();
    }

    // ── Attack execution ────────────────────────────────────────────────────

    /// <summary>
    /// Attempt a normal attack.
    /// Blocked if: cooldown not expired, or no attack data assigned.
    /// </summary>
    private void TryNormalAttack()
    {
        if (!CanNormalAttack || normalAttackData == null)
            return;

        bool hitSomething = ExecuteAttack(normalAttackData);

        // Start the cooldown timer — player must wait before attacking again
        _normalCooldownTimer = normalAttackData.cooldown;

        // Reward a successful hit with ultimate charge
        if (hitSomething)
            AddUltimateCharge(chargePerHit);
    }

    /// <summary>
    /// Attempt the ultimate attack.
    /// Blocked if: charge not full, cooldown not expired, or no data assigned.
    /// </summary>
    private void TryUltimateAttack()
    {
        if (!CanUltimate || ultimateAttackData == null)
            return;

        ExecuteAttack(ultimateAttackData);

        // Spend ALL charge and start the ultimate's own cooldown
        _ultimateCharge = 0f;
        _ultimateCooldownTimer = ultimateAttackData.cooldown;
    }

    /// <summary>
    /// Routes the attack to the correct behavior based on the data's AttackType.
    /// Plays the animation trigger.
    /// Returns true if at least one target was hit (used for ultimate charge).
    /// </summary>
    private bool ExecuteAttack(AttackDefinition data)
    {
        // Attack fires in the direction the player is currently facing
        Vector3 direction = transform.forward;

        // Fire the animation on the Animator (if one exists and a trigger is set)
        TriggerAnimation(data.animationTrigger);

        // NOTE FOR MULTIPLAYER:
        // Currently attacks run locally on the owner and call TakeDamage directly.
        // For a robust game, wrap this in a ServerRpc so the server validates
        // and replicates the hit. That will be done in the next phase.
        return data.attackType switch
        {
            AttackType.Melee        => _meleeAttack.Execute(data, transform, direction),
            AttackType.Projectile   => _projectileAttack.Execute(data, transform, direction),
            AttackType.AreaOfEffect => ExecuteAoE(data),
            _                       => false
        };
    }

    /// <summary>
    /// Area of Effect: damages every HealthComponent within meleeRange.
    /// Used for explosions, ground slams, shockwaves, etc.
    /// </summary>
    private bool ExecuteAoE(AttackDefinition data)
    {
        bool hitAny = false;

        // Find everything inside the radius
        Collider[] hits = Physics.OverlapSphere(transform.position, data.meleeRange);
        foreach (var col in hits)
        {
            // Never damage yourself
            if (col.transform.IsChildOf(transform))
                continue;

            var health = col.GetComponentInParent<HealthComponent>();
            if (health != null)
            {
                health.TakeDamage(data.damage);
                hitAny = true;
            }
        }

        return hitAny;
    }

    // ── Ultimate charge ─────────────────────────────────────────────────────

    /// <summary>
    /// Add charge from a successful hit. Clamped to MaxCharge.
    /// </summary>
    private void AddUltimateCharge(float amount)
    {
        _ultimateCharge = Mathf.Min(MaxCharge, _ultimateCharge + amount);
    }

    /// <summary>
    /// Passive charge that fills over time even without landing hits.
    /// Stops ticking once the ultimate is full (no wasted charge).
    /// </summary>
    private void TickUltimateCharge()
    {
        if (_ultimateCharge < MaxCharge)
            _ultimateCharge = Mathf.Min(MaxCharge, _ultimateCharge + chargePerSecond * Time.deltaTime);
    }

    // ── Cooldown timers ─────────────────────────────────────────────────────

    /// <summary>
    /// Count both cooldown timers down toward 0 each frame.
    /// </summary>
    private void TickCooldowns()
    {
        if (_normalCooldownTimer > 0f)
            _normalCooldownTimer -= Time.deltaTime;

        if (_ultimateCooldownTimer > 0f)
            _ultimateCooldownTimer -= Time.deltaTime;
    }

    // ── Animation helper ────────────────────────────────────────────────────

    /// <summary>
    /// Fires an Animator trigger by name.
    /// Safe to call even if there is no Animator on the player.
    /// </summary>
    private void TriggerAnimation(string triggerName)
    {
        if (string.IsNullOrEmpty(triggerName) || _animator == null)
            return;

        _animator.SetTrigger(triggerName);
    }
}
