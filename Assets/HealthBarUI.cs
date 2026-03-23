using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// World-space health bar that floats above the player and always faces the camera.
///
/// SETUP
///   Attach this script to an empty child GameObject of the player prefab (e.g. "HealthBar"
///   under the "UI" container). It builds the Canvas, Background, and Fill images
///   entirely in code — no extra scene or prefab setup is needed.
///
/// HOW IT READS HEALTH
///   Finds HealthComponent on the parent hierarchy at Start().
///   Subscribes to HealthComponent.OnHealthChanged (fires on every client whenever
///   the server changes health via NetworkVariable), so the bar is always in sync.
///
/// BILLBOARD
///   LateUpdate rotates the bar to face Camera.main every frame, so it is
///   always readable regardless of the camera angle.
///
/// COLOURS
///   Green (full → 30%) → Red (below 30%). Thresholds and colours are
///   Inspector-configurable so they can differ per character type.
/// </summary>
public class HealthBarUI : MonoBehaviour
{
    // ── Inspector config ──────────────────────────────────────────

    [Header("Bar Size")]
    [Tooltip("World-unit width of the bar (matches the bar's RectTransform width).")]
    [SerializeField] private float barWidth  = 1.2f;
    [SerializeField] private float barHeight = 0.14f;

    [Tooltip("Metres above the player pivot to float the bar.")]
    [SerializeField] private float heightOffset = 1.4f;

    [Header("Colours")]
    [SerializeField] private Color backgroundColour  = new Color(0.08f, 0.08f, 0.08f, 0.75f);
    [SerializeField] private Color fullHealthColour  = new Color(0.18f, 0.82f, 0.22f, 0.95f);
    [SerializeField] private Color lowHealthColour   = new Color(0.88f, 0.14f, 0.10f, 0.95f);

    [Tooltip("Fraction of max health at which the bar switches from green to red.")]
    [Range(0f, 1f)]
    [SerializeField] private float lowHealthThreshold = 0.30f;

    // ── Runtime references ────────────────────────────────────────

    private Image           _fill;
    private HealthComponent _health;
    private Transform       _cameraTransform;

    // ── Unity lifecycle ───────────────────────────────────────────

    void Awake()
    {
        // Position the bar above the player origin.
        transform.localPosition = new Vector3(0f, heightOffset, 0f);

        BuildVisuals();
    }

    void Start()
    {
        // HealthComponent lives on the root player GO — walk up to find it.
        _health = GetComponentInParent<HealthComponent>();
        if (_health != null)
        {
            _health.OnHealthChanged += RefreshBar;
            // Force an immediate refresh so the bar is correct from frame 1.
            RefreshBar(_health.CurrentHealth, _health.MaxHealth);
        }

        // Cache camera transform once; LateUpdate re-reads position each frame.
        _cameraTransform = Camera.main?.transform;
    }

    void OnDestroy()
    {
        // Prevent dangling delegate after the player is despawned / destroyed.
        if (_health != null)
            _health.OnHealthChanged -= RefreshBar;
    }

    void LateUpdate()
    {
        // Billboard: make the bar face the camera regardless of yaw/pitch.
        if (_cameraTransform == null)
        {
            // Camera can change (e.g. after a scene reload) — re-search lazily.
            _cameraTransform = Camera.main?.transform;
            return;
        }

        transform.LookAt(
            transform.position + _cameraTransform.rotation * Vector3.forward,
            _cameraTransform.rotation * Vector3.up);
    }

    // ── Health update ─────────────────────────────────────────────

    private void RefreshBar(float current, float max)
    {
        if (_fill == null || max <= 0f) return;

        float ratio = Mathf.Clamp01(current / max);
        _fill.fillAmount = ratio;
        _fill.color = ratio <= lowHealthThreshold ? lowHealthColour : fullHealthColour;
    }

    // ── Visual construction (runs once in Awake) ──────────────────

    private void BuildVisuals()
    {
        // Canvas ─────────────────────────────────────────────────
        // RectTransform is auto-added when Canvas is added to a GO.
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var rt = GetComponent<RectTransform>();
        rt.sizeDelta   = new Vector2(barWidth, barHeight);
        rt.localScale  = Vector3.one;

        // Background ─────────────────────────────────────────────
        var bgGo  = new GameObject("Background");
        bgGo.transform.SetParent(transform, false);
        bgGo.AddComponent<CanvasRenderer>();

        var bgImg = bgGo.AddComponent<Image>();
        bgImg.color = backgroundColour;

        var bgRt = bgGo.GetComponent<RectTransform>();
        StretchToParent(bgRt);

        // Fill bar ────────────────────────────────────────────────
        var fillGo = new GameObject("Fill");
        fillGo.transform.SetParent(transform, false);
        fillGo.AddComponent<CanvasRenderer>();

        _fill             = fillGo.AddComponent<Image>();
        _fill.color       = fullHealthColour;
        _fill.type        = Image.Type.Filled;
        _fill.fillMethod  = Image.FillMethod.Horizontal;
        _fill.fillOrigin  = (int)Image.OriginHorizontal.Left;
        _fill.fillAmount  = 1f;

        var fillRt = fillGo.GetComponent<RectTransform>();
        StretchToParent(fillRt);
    }

    /// <summary>Anchors and stretches a RectTransform to fill its parent completely.</summary>
    private static void StretchToParent(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
