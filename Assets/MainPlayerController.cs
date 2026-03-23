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

    private float     _nextFireTime;
    private AimSystem _aimSystem;

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

        // Auto-add AimSystem if not already on this GameObject.
        _aimSystem = GetComponent<AimSystem>();
        if (_aimSystem == null)
            _aimSystem = gameObject.AddComponent<AimSystem>();
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

        // Aim line is only meaningful for the local owner.
        if (!IsOwner && _aimSystem != null)
            _aimSystem.enabled = false;

        // Notify global systems (camera, GameManager) that the local player is ready.
        if (IsOwner)
            GameEvents.RaiseLocalPlayerSpawned(this);
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner)
            GameEvents.RaiseLocalPlayerDespawned(this);
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
        // ── LMB: hold to aim, release to fire ─────────────────────
        if (Input.GetMouseButtonDown(0))
        {
            _aimSystem.StartAim();
        }

        if (Input.GetMouseButtonUp(0))
        {
            Vector3 aimDir = _aimSystem.AimDirection;
            _aimSystem.StopAim();

            if (Time.time < _nextFireTime)
            {
                Debug.Log($"[Attack] On cooldown. {(_nextFireTime - Time.time):F2}s remaining.");
                return;
            }

            _nextFireTime = Time.time + fireCooldown;
            Debug.Log("[Attack] Sending FireServerRpc (aimed direction)...");
            FireServerRpc(aimDir);
        }

        // ── Space: quick-fire in the current facing direction (no aim line) ──
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (Time.time < _nextFireTime)
            {
                Debug.Log($"[Attack] On cooldown. {(_nextFireTime - Time.time):F2}s remaining.");
                return;
            }

            Vector3 quickDir = transform.forward;
            quickDir.y = 0f;
            if (quickDir.sqrMagnitude < 0.001f) quickDir = Vector3.forward;
            quickDir.Normalize();

            _nextFireTime = Time.time + fireCooldown;
            Debug.Log("[Attack] Sending FireServerRpc (quick-fire direction)...");
            FireServerRpc(quickDir);
        }
    }

    /// <summary>
    /// Runs on the server. Receives the aimed direction from the owner client,
    /// then spawns and initialises the projectile.
    /// </summary>
    [ServerRpc]
    private void FireServerRpc(Vector3 direction)
    {
        if (_projectilePrefab == null)
        {
            Debug.LogError("[Attack] _projectilePrefab is NOT assigned on the NetworkPlayer prefab! " +
                           "Drag NetworkProjectile.prefab into the field in the Inspector.");
            return;
        }

        // Validate direction.
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f) direction = Vector3.forward;
        direction.Normalize();

        // Spawn position: in front of the player at the player's centre height.
        Vector3 spawnPos = transform.position + direction * fireSpawnOffset;
        float   spawnY   = transform.position.y;

        Debug.Log($"[Attack] Server spawning projectile at {spawnPos}, dir={direction}");

        var go     = Instantiate(_projectilePrefab, spawnPos, Quaternion.LookRotation(direction));
        var netObj = go.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogError("[Attack] NetworkProjectile prefab has no NetworkObject component!");
            Destroy(go);
            return;
        }

        netObj.Spawn(destroyWithScene: true);
        go.GetComponent<NetworkProjectile>().Initialize(direction, OwnerClientId, spawnY);
    }
}
