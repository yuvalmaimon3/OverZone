using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Brawl Stars-style fixed-angle camera that follows the local (owned) player.
/// The host camera looks from behind the host; the client camera looks from
/// the opposite side so both players see their own character in the foreground.
/// </summary>
public class FieldCameraController : MonoBehaviour
{
    [Header("Follow")]
    [SerializeField] private float followSmooth = 12f;

    [Header("Camera Angle (Brawl Stars Style)")]
    [Tooltip("Height and Z-distance from player. X stays 0 for centered look.")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 8f, -11f);
    [Tooltip("Down-tilt in degrees. 55 gives a Brawl-Stars-like isometric feel.")]
    [SerializeField] private float pitch = 55f;

    [Header("Default Yaw Per Role")]
    [Tooltip("Yaw for the host player (0 = camera behind, looking toward +Z).")]
    [SerializeField] public float hostYaw = 0f;
    [Tooltip("Yaw for the client player (180 = camera behind, looking toward -Z).")]
    [SerializeField] public float clientYaw = 180f;

    // ── runtime state ───────────────────────────────────────────────────────
    private Transform _target;
    private float _currentYaw;
    private bool _setupCalled;

    // ── public API called by NetworkBootstrap ───────────────────────────────

    /// <summary>Call this right after Start Host is confirmed.</summary>
    public void SetupForHost()
    {
        _currentYaw = hostYaw;
        _setupCalled = true;
        _target = null;          // re-search so we pick up the spawned NetworkPlayer
    }

    /// <summary>Call this right after Start Client is confirmed.</summary>
    public void SetupForClient()
    {
        _currentYaw = clientYaw;
        _setupCalled = true;
        _target = null;
    }

    // ── Unity callbacks ─────────────────────────────────────────────────────

    void LateUpdate()
    {
        // Keep searching for the owned NetworkPlayer until one appears.
        if (_target == null && _setupCalled)
            TryFindTarget();

        if (_target == null)
            return;

        Quaternion orbit = Quaternion.Euler(pitch, _currentYaw, 0f);
        Vector3 desiredPos = _target.position + orbit * offset;

        transform.position = Vector3.Lerp(transform.position, desiredPos,
            Time.deltaTime * followSmooth);

        Vector3 lookAt = _target.position + Vector3.up * 1.0f;
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            Quaternion.LookRotation(lookAt - transform.position, Vector3.up),
            Time.deltaTime * followSmooth);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private void TryFindTarget()
    {
        var players = FindObjectsByType<MainPlayerController>(FindObjectsSortMode.None);
        foreach (var p in players)
        {
            if (p.IsOwner)
            {
                _target = p.transform;
                SnapToTarget();
                return;
            }
        }
    }

    /// <summary>Instantly place the camera so there is no slide-in at spawn.</summary>
    private void SnapToTarget()
    {
        Quaternion orbit = Quaternion.Euler(pitch, _currentYaw, 0f);
        transform.position = _target.position + orbit * offset;

        Vector3 lookAt = _target.position + Vector3.up * 1.0f;
        transform.rotation = Quaternion.LookRotation(lookAt - transform.position, Vector3.up);
    }
}
