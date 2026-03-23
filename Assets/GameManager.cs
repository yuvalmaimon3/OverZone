using UnityEngine;

/// <summary>
/// Central game-state singleton. Provides one authoritative place to read:
///   • The local player's controller reference (once spawned)
///   • Whether a session is active and what role this peer has
///
/// PATTERN: Singleton
///
///   One instance lives for the lifetime of the session. The instance is
///   auto-created at runtime startup so no scene setup is required — it
///   will appear in the Hierarchy as [GameManager] when the game is played.
///
/// WHY a Singleton here?
///   Without it, every script that needs the local player calls
///   FindObjectsByType<MainPlayerController>() every frame in a loop.
///   A cached reference is O(1); the polling loop was O(n) every LateUpdate.
///
/// HOW TO READ THE LOCAL PLAYER FROM ANY SCRIPT:
///   var player = GameManager.Instance.LocalPlayer;   // null before spawn
///
/// HOW TO READ SESSION STATE:
///   if (GameManager.Instance.IsHostSession) { ... }
/// </summary>
public class GameManager : MonoBehaviour
{
    // ── Singleton ──────────────────────────────────────────────────

    public static GameManager Instance { get; private set; }

    /// <summary>
    /// Auto-creates a GameManager before the first scene loads so no
    /// manual scene setup is required.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("[GameManager]");
        go.AddComponent<GameManager>();
        DontDestroyOnLoad(go);
    }

    // ── State (read-only from the outside) ────────────────────────

    /// <summary>
    /// The MainPlayerController owned by this machine.
    /// Null before the local player spawns, or after they despawn/disconnect.
    /// </summary>
    public MainPlayerController LocalPlayer { get; private set; }

    /// <summary>True when this peer is hosting the session (is also a server).</summary>
    public bool IsHostSession { get; private set; }

    /// <summary>True while any network session is active (host or client).</summary>
    public bool IsSessionActive { get; private set; }

    // ── Unity lifecycle ───────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnEnable()
    {
        GameEvents.OnHostStarted          += HandleHostStarted;
        GameEvents.OnClientStarted        += HandleClientStarted;
        GameEvents.OnSessionStopped       += HandleSessionStopped;
        GameEvents.OnLocalPlayerSpawned   += HandleLocalPlayerSpawned;
        GameEvents.OnLocalPlayerDespawned += HandleLocalPlayerDespawned;
    }

    void OnDisable()
    {
        GameEvents.OnHostStarted          -= HandleHostStarted;
        GameEvents.OnClientStarted        -= HandleClientStarted;
        GameEvents.OnSessionStopped       -= HandleSessionStopped;
        GameEvents.OnLocalPlayerSpawned   -= HandleLocalPlayerSpawned;
        GameEvents.OnLocalPlayerDespawned -= HandleLocalPlayerDespawned;
    }

    // ── Event handlers ────────────────────────────────────────────

    private void HandleHostStarted()
    {
        IsHostSession   = true;
        IsSessionActive = true;
    }

    private void HandleClientStarted()
    {
        IsHostSession   = false;
        IsSessionActive = true;
    }

    private void HandleSessionStopped()
    {
        IsSessionActive = false;
        IsHostSession   = false;
    }

    private void HandleLocalPlayerSpawned(MainPlayerController player)
    {
        LocalPlayer = player;
    }

    private void HandleLocalPlayerDespawned(MainPlayerController player)
    {
        if (LocalPlayer == player)
            LocalPlayer = null;
    }
}
