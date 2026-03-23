using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Server-Authoritative movement with Client-Side Prediction + Normal Attack.
///
/// MOVEMENT (see previous comments for full detail)
///   HOST    → moves directly, writes _serverPos.
///   CLIENT  → predicts locally + SubmitInputServerRpc + reconcile.
///   OTHERS  → kinematic, lerp toward _serverPos.
///
/// NORMAL ATTACK
///   Input   → Space (or Left Mouse Button) while IsOwner.
///   Rate    → minimum fireCooldown seconds between shots (default 0.5 s).
///   ServerRpc → server reads its authoritative transform.forward,
///               instantiates NetworkProjectile, calls Spawn + Initialize.
///   Direction → facing direction (transform.forward) projected to XZ.
///               Falls back to Vector3.forward if player never moved.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class MainPlayerController : NetworkBehaviour
{
    // ── Movement ──────────────────────────────────────────────────

    [Header("Movement")]
    public float moveSpeed       = 6f;
    public float turnSpeedDegrees = 720f;

    [Header("Network / Reconciliation")]
    [SerializeField] private float reconcileThreshold  = 0.5f;
    [SerializeField] private float spectatorFollowSpeed = 20f;

    // ── Attack ────────────────────────────────────────────────────

    [Header("Normal Attack")]
    [SerializeField] private GameObject _projectilePrefab;

    [Tooltip("Minimum seconds between shots.")]
    [SerializeField] private float fireCooldown = 0.5f;

    [Tooltip("How far in front of the player the projectile spawns (units).")]
    [SerializeField] private float fireSpawnOffset = 1.2f;

    private float _nextFireTime;

    // ── Server-authoritative position ─────────────────────────────

    private NetworkVariable<Vector3> _serverPos = new NetworkVariable<Vector3>(
        Vector3.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private NetworkVariable<Quaternion> _serverRot = new NetworkVariable<Quaternion>(
        Quaternion.identity,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private Rigidbody _rb;

    // ── Unity lifecycle ───────────────────────────────────────────

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    void Start()
    {
        if (GetComponentInChildren<Renderer>() == null)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Player Visual";
            sphere.transform.SetParent(transform, false);
            var rend = sphere.GetComponent<Renderer>();
            if (rend != null)
                rend.material.color = new Color(0.55f, 0.27f, 0.07f, 1f);
        }
    }

    // ── NGO spawn ─────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        // Only pure spectating clients get a kinematic Rigidbody.
        // Server needs dynamic physics for all players.
        bool spectatingClient = !IsOwner && !IsServer;
        _rb.isKinematic = spectatingClient;
    }

    // ── Per-frame ─────────────────────────────────────────────────

    void Update()
    {
        // Attack input — read in Update so GetKeyDown / GetMouseButtonDown
        // are never missed between fixed steps.
        if (!IsSpawned || !IsOwner) return;
        HandleFireInput();
    }

    void FixedUpdate()
    {
        if (!IsSpawned) return;

        if (IsOwner)          OwnerFixedUpdate();
        else if (IsServer)    ServerPublishPosition();
        else                  SpectatorFixedUpdate();
    }

    // ── Owner: prediction + ServerRpc + reconcile ─────────────────

    void OwnerFixedUpdate()
    {
        float inputX = Input.GetAxisRaw("Horizontal");
        float inputZ = Input.GetAxisRaw("Vertical");

        ApplyMovementToRigidbody(inputX, inputZ);

        if (IsServer)
        {
            _serverPos.Value = transform.position;
            _serverRot.Value = transform.rotation;
        }
        else
        {
            SubmitInputServerRpc(inputX, inputZ);

            if (Vector3.Distance(transform.position, _serverPos.Value) > reconcileThreshold)
            {
                transform.position = _serverPos.Value;
                transform.rotation = _serverRot.Value;
                _rb.linearVelocity = Vector3.zero;
            }
        }
    }

    [ServerRpc]
    void SubmitInputServerRpc(float inputX, float inputZ)
    {
        ApplyMovementToRigidbody(inputX, inputZ);
    }

    void ServerPublishPosition()
    {
        _serverPos.Value = transform.position;
        _serverRot.Value = transform.rotation;
    }

    // ── Spectator ─────────────────────────────────────────────────

    void SpectatorFixedUpdate()
    {
        float t = Mathf.Clamp01(spectatorFollowSpeed * Time.fixedDeltaTime);
        _rb.MovePosition(Vector3.Lerp(transform.position,  _serverPos.Value, t));
        _rb.MoveRotation(Quaternion.Slerp(transform.rotation, _serverRot.Value, t));
    }

    // ── Movement core ─────────────────────────────────────────────

    private void ApplyMovementToRigidbody(float inputX, float inputZ)
    {
        Vector3 input = new Vector3(inputX, 0f, inputZ);
        if (input.sqrMagnitude > 1f) input.Normalize();

        Vector3 vel = input * moveSpeed;
        _rb.linearVelocity = new Vector3(vel.x, _rb.linearVelocity.y, vel.z);

        if (input.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(input, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, targetRot, turnSpeedDegrees * Time.fixedDeltaTime);
        }
    }

    // ── Attack input ──────────────────────────────────────────────

    private void HandleFireInput()
    {
        bool pressed = Input.GetKeyDown(KeyCode.Space)
                    || Input.GetMouseButtonDown(0);

        if (!pressed) return;

        if (Time.time < _nextFireTime)
        {
            Debug.Log($"[Attack] On cooldown. {(_nextFireTime - Time.time):F2}s remaining.");
            return;
        }

        _nextFireTime = Time.time + fireCooldown;
        Debug.Log("[Attack] Sending FireServerRpc...");
        FireServerRpc();
    }

    /// <summary>
    /// Runs on the server. Uses the server's authoritative transform.forward
    /// as the fire direction, then spawns and initialises the projectile.
    /// </summary>
    [ServerRpc]
    private void FireServerRpc()
    {
        if (_projectilePrefab == null)
        {
            Debug.LogError("[Attack] _projectilePrefab is NOT assigned on the NetworkPlayer prefab! " +
                           "Drag NetworkProjectile.prefab into the field in the Inspector.");
            return;
        }

        // Direction: server's current facing, projected to XZ.
        Vector3 dir = transform.forward;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) dir = Vector3.forward;
        dir.Normalize();

        // Spawn position: in front of the player at the player's centre height.
        Vector3 spawnPos = transform.position + dir * fireSpawnOffset;
        float   spawnY   = transform.position.y;

        Debug.Log($"[Attack] Server spawning projectile at {spawnPos}, dir={dir}");

        var go     = Instantiate(_projectilePrefab, spawnPos, Quaternion.LookRotation(dir));
        var netObj = go.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogError("[Attack] NetworkProjectile prefab has no NetworkObject component!");
            Destroy(go);
            return;
        }

        netObj.Spawn(destroyWithScene: true);
        go.GetComponent<NetworkProjectile>().Initialize(dir, OwnerClientId, spawnY);
    }
}
