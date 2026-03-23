using UnityEngine;

/// <summary>
/// Brawl Stars-style fixed-angle camera that follows the local (owned) player.
///
/// PATTERN: Observer (subscriber side)
///
///   Previously, NetworkBootstrap called SetupForHost() / SetupForClient()
///   directly (tight coupling). Now the camera listens to GameEvents and
///   configures itself without knowing anything about NetworkBootstrap.
///
///   Previously, TryFindTarget() called FindObjectsByType every LateUpdate
///   in a polling loop. Now it receives the player via GameEvents.OnLocalPlayerSpawned
///   the instant the player spawns — zero per-frame searches.
///
/// EVENT FLOW
///   User clicks Start Host → GameEvents.RaiseHostStarted()
///     → OnHostStarted → camera stores hostYaw
///   Local player spawns → GameEvents.RaiseLocalPlayerSpawned(player)
///     → OnLocalPlayerSpawned → camera locks onto player, snaps instantly
///   User clicks Shutdown → GameEvents.RaiseSessionStopped()
///     → OnSessionStopped → camera clears target
/// </summary>
public class FieldCameraController : MonoBehaviour
{
    [Header("Follow")]
    [SerializeField] private float followSmooth = 12f;

    [Header("Camera Angle (Brawl Stars Style)")]
    [Tooltip("Height and Z-distance from player. X stays 0 for centred look.")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 8f, -11f);
    [Tooltip("Down-tilt in degrees. 55 gives a Brawl-Stars-like isometric feel.")]
    [SerializeField] private float pitch = 55f;

    [Header("Default Yaw Per Role")]
    [Tooltip("Yaw for the host player (0 = camera behind, looking toward +Z).")]
    [SerializeField] public float hostYaw = 0f;
    [Tooltip("Yaw for the client player (180 = camera behind, looking toward -Z).")]
    [SerializeField] public float clientYaw = 180f;

    // ── Runtime state ─────────────────────────────────────────────

    private Transform _target;
    private float     _currentYaw;
    private bool      _sessionStarted;

    // ── Unity lifecycle ───────────────────────────────────────────

    void OnEnable()
    {
        GameEvents.OnHostStarted        += HandleHostStarted;
        GameEvents.OnClientStarted      += HandleClientStarted;
        GameEvents.OnSessionStopped     += HandleSessionStopped;
        GameEvents.OnLocalPlayerSpawned += HandleLocalPlayerSpawned;
    }

    void OnDisable()
    {
        GameEvents.OnHostStarted        -= HandleHostStarted;
        GameEvents.OnClientStarted      -= HandleClientStarted;
        GameEvents.OnSessionStopped     -= HandleSessionStopped;
        GameEvents.OnLocalPlayerSpawned -= HandleLocalPlayerSpawned;
    }

    void LateUpdate()
    {
        if (_target == null) return;

        Quaternion orbit      = Quaternion.Euler(pitch, _currentYaw, 0f);
        Vector3    desiredPos = _target.position + orbit * offset;

        transform.position = Vector3.Lerp(
            transform.position, desiredPos, Time.deltaTime * followSmooth);

        Vector3 lookAt = _target.position + Vector3.up * 1.0f;
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            Quaternion.LookRotation(lookAt - transform.position, Vector3.up),
            Time.deltaTime * followSmooth);
    }

    // ── GameEvents handlers ───────────────────────────────────────

    private void HandleHostStarted()
    {
        _currentYaw     = hostYaw;
        _sessionStarted = true;
        _target         = null;

        // For the host, NGO spawns the player synchronously inside StartHost(),
        // so OnLocalPlayerSpawned fires BEFORE this handler runs and the guard
        // (_sessionStarted == false) blocks it. Check GameManager now that we
        // know we are in a session.
        TryAttachToLocalPlayer();
    }

    private void HandleClientStarted()
    {
        _currentYaw     = clientYaw;
        _sessionStarted = true;
        _target         = null;

        // For a client the player spawns asynchronously (after the connection
        // handshake), so this call will find nothing yet — OnLocalPlayerSpawned
        // will handle it once the spawn arrives.
        TryAttachToLocalPlayer();
    }

    private void HandleSessionStopped()
    {
        _sessionStarted = false;
        _target        = null;
    }

    private void HandleLocalPlayerSpawned(MainPlayerController player)
    {
        if (!_sessionStarted) return;
        _target = player.transform;
        SnapToTarget();
    }

    // ── Internal ──────────────────────────────────────────────────

    /// <summary>
    /// Snaps to the local player if GameManager already has a reference.
    /// Called from HandleHostStarted/HandleClientStarted to handle the case
    /// where the player spawned synchronously before those handlers ran.
    /// </summary>
    private void TryAttachToLocalPlayer()
    {
        var player = GameManager.Instance != null ? GameManager.Instance.LocalPlayer : null;
        if (player == null) return;
        _target = player.transform;
        SnapToTarget();
    }

    private void SnapToTarget()
    {
        Quaternion orbit = Quaternion.Euler(pitch, _currentYaw, 0f);
        transform.position = _target.position + orbit * offset;

        Vector3 lookAt = _target.position + Vector3.up * 1.0f;
        transform.rotation = Quaternion.LookRotation(lookAt - transform.position, Vector3.up);
    }
}
