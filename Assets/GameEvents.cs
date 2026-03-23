using System;

/// <summary>
/// Static event bus for key game state transitions.
///
/// PATTERN: Observer (Event Bus variant)
///
///   Producers fire events with a Raise* call. Consumers subscribe
///   in Awake/OnEnable and unsubscribe in OnDestroy/OnDisable.
///   No direct references between producers and consumers are needed.
///
/// WHY A STATIC BUS (not ScriptableObject events)?
///   The game currently has one scene and one session lifetime.
///   A static class is zero allocation and has no serialization overhead.
///   Upgrade to ScriptableObject events if multi-scene support is added.
///
/// PRODUCER EXAMPLE (NetworkBootstrap):
///   GameEvents.RaiseHostStarted();
///
/// CONSUMER EXAMPLE (FieldCameraController):
///   void Awake()  { GameEvents.OnHostStarted += HandleHostStarted; }
///   void OnDestroy() { GameEvents.OnHostStarted -= HandleHostStarted; }
///   private void HandleHostStarted() { ... }
/// </summary>
public static class GameEvents
{
    // ── Session lifecycle ──────────────────────────────────────────

    /// <summary>
    /// Raised when this peer successfully starts as Host.
    /// Consumers: FieldCameraController sets host yaw, UI can hide start buttons, etc.
    /// </summary>
    public static event Action OnHostStarted;

    /// <summary>
    /// Raised when this peer successfully connects as Client.
    /// Consumers: FieldCameraController sets client yaw, UI updates, etc.
    /// </summary>
    public static event Action OnClientStarted;

    /// <summary>
    /// Raised when the network session ends (Shutdown called on either side).
    /// Consumers: GameManager clears state, UI re-enables start buttons, etc.
    /// </summary>
    public static event Action OnSessionStopped;

    // ── Player lifecycle ───────────────────────────────────────────

    /// <summary>
    /// Raised by MainPlayerController.OnNetworkSpawn when this machine's
    /// owned player enters the game.
    /// Consumers: FieldCameraController locks onto target, GameManager caches reference.
    /// </summary>
    public static event Action<MainPlayerController> OnLocalPlayerSpawned;

    /// <summary>
    /// Raised by MainPlayerController.OnNetworkDespawn when this machine's
    /// owned player leaves the game.
    /// Consumers: GameManager clears cached reference, UI shows respawn screen, etc.
    /// </summary>
    public static event Action<MainPlayerController> OnLocalPlayerDespawned;

    // ── Raise methods (called only by producers) ───────────────────

    public static void RaiseHostStarted()
        => OnHostStarted?.Invoke();

    public static void RaiseClientStarted()
        => OnClientStarted?.Invoke();

    public static void RaiseSessionStopped()
        => OnSessionStopped?.Invoke();

    public static void RaiseLocalPlayerSpawned(MainPlayerController player)
        => OnLocalPlayerSpawned?.Invoke(player);

    public static void RaiseLocalPlayerDespawned(MainPlayerController player)
        => OnLocalPlayerDespawned?.Invoke(player);
}
