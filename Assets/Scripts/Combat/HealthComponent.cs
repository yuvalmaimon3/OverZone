using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Tracks a player's (or any object's) health and keeps it synchronized
/// across host and all clients automatically.
/// 
/// HOW IT WORKS:
///   - _currentHealth is a NetworkVariable: when the server changes it,
///     all clients automatically receive the new value.
///   - Only the server is allowed to change health (prevents cheating).
///   - Other scripts (UI, death handling) subscribe to OnHealthChanged and OnDeath.
/// 
/// ATTACH TO:
///   The same GameObject as NetworkObject (i.e. your NetworkPlayer prefab).
/// </summary>
public class HealthComponent : NetworkBehaviour
{
    [Header("Settings")]
    [Tooltip("Maximum health this entity starts with and can be healed back to.")]
    [SerializeField] private float maxHealth = 100f;

    // NetworkVariable syncs this value to all clients whenever it changes on the server.
    // ReadPermission = Everyone → all clients can read the current health (for UI).
    // WritePermission = Server → only the server can change it (prevents cheating).
    private NetworkVariable<float> _currentHealth = new NetworkVariable<float>(
        100f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // ── Public read-only accessors ──────────────────────────────────────────

    public float CurrentHealth => _currentHealth.Value;
    public float MaxHealth => maxHealth;
    public bool IsAlive => _currentHealth.Value > 0f;

    // ── Events other scripts can subscribe to ──────────────────────────────

    /// <summary>Fired on all clients when health changes. Parameters: (currentHP, maxHP)</summary>
    public System.Action<float, float> OnHealthChanged;

    /// <summary>Fired on all clients when health reaches 0.</summary>
    public System.Action OnDeath;

    // ── NetworkBehaviour callbacks ──────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        // Reset to full health when this object enters the network
        if (IsServer)
            _currentHealth.Value = maxHealth;

        // Listen for health changes so we can fire our local events (e.g. update UI)
        _currentHealth.OnValueChanged += HandleHealthChanged;
    }

    public override void OnNetworkDespawn()
    {
        // Always unsubscribe to prevent memory leaks or callbacks on destroyed objects
        _currentHealth.OnValueChanged -= HandleHealthChanged;
    }

    // ── Public methods called by attack scripts ─────────────────────────────

    /// <summary>
    /// Reduce health by 'amount'. Only runs on the server.
    /// Called by MeleeAttack, Projectile, and AoE inside AttackController.
    /// </summary>
    public void TakeDamage(float amount)
    {
        // Guard: only the server can modify health
        if (!IsServer || !IsAlive)
            return;

        _currentHealth.Value = Mathf.Max(0f, _currentHealth.Value - amount);

        if (_currentHealth.Value <= 0f)
            OnDeath?.Invoke();
    }

    /// <summary>
    /// Restore health by 'amount' (up to maxHealth). Only runs on the server.
    /// Call this from healing pickups, abilities, etc.
    /// </summary>
    public void Heal(float amount)
    {
        if (!IsServer)
            return;

        _currentHealth.Value = Mathf.Min(maxHealth, _currentHealth.Value + amount);
    }

    // ── Internal ────────────────────────────────────────────────────────────

    /// <summary>
    /// Called automatically on every client when _currentHealth changes on the server.
    /// Fires the public OnHealthChanged event so UI or effects can react.
    /// </summary>
    private void HandleHealthChanged(float oldValue, float newValue)
    {
        OnHealthChanged?.Invoke(newValue, maxHealth);
    }
}
