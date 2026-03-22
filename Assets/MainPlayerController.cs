using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Simple local player controller (WASD movement) meant to be wired later into the networking layer.
/// </summary>
public class MainPlayerController : NetworkBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;
    public float turnSpeedDegrees = 720f;

    // Visual-only representation (spawned at runtime for now).
    [SerializeField] private Transform playerVisual;

    void Start()
    {
        if (playerVisual == null)
        {
            // If the scene already contains a visual (for editor-time color/material),
            // reuse it and avoid spawning a duplicate at runtime.
            var existingRenderer = GetComponentInChildren<Renderer>();
            if (existingRenderer != null)
            {
                playerVisual = existingRenderer.transform;
                return;
            }

            // Spawn a sphere so the player is visible immediately.
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Player Visual (Sphere)";
            sphere.transform.SetParent(transform, false);
            sphere.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            sphere.transform.localScale = Vector3.one;

            // Apply brown color so the main player is easy to see.
            var renderer = sphere.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = new Color(0.55f, 0.27f, 0.07f, 1f);
            }

            playerVisual = sphere.transform;
        }
    }

    void Update()
    {
        if (IsSpawned && !IsOwner)
            return;

        // Movement input (A/W/S/D => left/forward/back/right on world axes).
        float inputX = Input.GetAxisRaw("Horizontal"); // A/D
        float inputZ = Input.GetAxisRaw("Vertical");   // W/S

        Vector3 input = new Vector3(inputX, 0f, inputZ);
        if (input.sqrMagnitude > 1f) input.Normalize();

        if (input.sqrMagnitude < 0.0001f)
            return;
        Vector3 moveDir = input.normalized;

        // Rotate the player towards the movement direction.
        Quaternion desiredRot = Quaternion.LookRotation(moveDir, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, desiredRot, turnSpeedDegrees * Time.deltaTime);

        // Move on XZ plane.
        transform.position += moveDir * (moveSpeed * Time.deltaTime);
    }
}

