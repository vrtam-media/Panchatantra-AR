using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class ParallaxLayerStack : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Assign ARCamera transform here. Do NOT assign ImageTarget here.")]
    public Transform cameraTransform;

    [Header("Layers (0 = far, last = near)")]
    [Tooltip("Drag all your layer planes here in order: far to near. Include video planes too.")]
    public List<Transform> layers = new List<Transform>();

    [Header("Gap (Local Y)")]
    [Tooltip("Equal gap applied on LOCAL Y axis between layers.")]
    public float gapY = 0.002f;

    [Tooltip("If ON, gapY is auto-calculated once from current layer Y positions at Start().")]
    public bool autoComputeGapOnStart = true;

    [Header("Parallax (Portal feel)")]
    [Tooltip("Enable or disable parallax.")]
    public bool enableParallax = true;

    [Tooltip("How much layers shift based on camera motion and view angle.")]
    [Range(0f, 0.2f)]
    public float parallaxStrength = 0.03f;

    [Tooltip("How much rotation (degrees) influences parallax. This is clamped to avoid shaking.")]
    [Range(0.5f, 15f)]
    public float rotationClampDegrees = 5f;

    [Header("Fluidity")]
    [Tooltip("Higher = smoother and more fluid (recommended 10 to 20).")]
    [Range(5f, 30f)]
    public float smoothness = 14f;

    // Runtime cache
    private Vector3 camPosAnchorWorld;
    private Quaternion camRotAnchorWorld;

    private Vector3[] baseLocalPositions;
    private Vector3[] vel;

    void Start()
    {
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        if (autoComputeGapOnStart)
            ComputeGapFromCurrentY();

        CaptureCurrentAsBase();
    }

    public void CaptureCurrentAsBase()
    {
        int count = layers.Count;
        baseLocalPositions = new Vector3[count];
        vel = new Vector3[count];

        for (int i = 0; i < count; i++)
            baseLocalPositions[i] = layers[i] ? layers[i].localPosition : Vector3.zero;

        if (cameraTransform)
        {
            camPosAnchorWorld = cameraTransform.position;
            camRotAnchorWorld = cameraTransform.rotation;
        }
    }

    [ContextMenu("Compute Gap From Current Y")]
    public void ComputeGapFromCurrentY()
    {
        if (layers == null || layers.Count < 2) return;

        float total = 0f;
        int pairs = 0;

        for (int i = 0; i < layers.Count - 1; i++)
        {
            if (!layers[i] || !layers[i + 1]) continue;

            float dy = layers[i + 1].localPosition.y - layers[i].localPosition.y;
            total += Mathf.Abs(dy);
            pairs++;
        }

        if (pairs > 0)
            gapY = total / pairs;
    }

    [ContextMenu("Reset Equal Gap (Keep XZ)")]
    public void ResetEqualGapKeepXZ()
    {
        if (layers == null || layers.Count == 0) return;

        float y0 = layers[0].localPosition.y;

        for (int i = 0; i < layers.Count; i++)
        {
            if (!layers[i]) continue;
            Vector3 p = layers[i].localPosition;
            p.y = y0 + gapY * i;
            layers[i].localPosition = p;
        }

        CaptureCurrentAsBase();
    }

    void LateUpdate()
    {
        if (!enableParallax) return;
        if (cameraTransform == null) return;
        if (layers == null || layers.Count == 0) return;
        if (baseLocalPositions == null || baseLocalPositions.Length != layers.Count) return;

        // Camera motion relative to anchor (world)
        Vector3 camDeltaWorld = cameraTransform.position - camPosAnchorWorld;

        // Convert motion into LOCAL space of the stack root
        Vector3 camDeltaLocal = transform.InverseTransformVector(camDeltaWorld);

        // Camera view angle influence (portal feel)
        Quaternion deltaRot = cameraTransform.rotation * Quaternion.Inverse(camRotAnchorWorld);
        Vector3 e = deltaRot.eulerAngles;

        float pitch = ClampAngle(e.x);
        float yaw = ClampAngle(e.y);

        pitch = Mathf.Clamp(pitch, -rotationClampDegrees, rotationClampDegrees);
        yaw = Mathf.Clamp(yaw, -rotationClampDegrees, rotationClampDegrees);

        // Convert rotation influence into local XY offsets
        Vector2 rotOffset = new Vector2(yaw, -pitch) * 0.0025f;

        float dt = Time.deltaTime;
        float t = 1f - Mathf.Exp(-smoothness * dt);

        for (int i = 0; i < layers.Count; i++)
        {
            Transform layer = layers[i];
            if (!layer) continue;

            float depth01 = (layers.Count == 1) ? 1f : (float)i / (layers.Count - 1);

            // Far layers move less, near layers move more
            float layerStrength = Mathf.Lerp(0.15f, 1f, depth01);

            // Portal style: small shift based on camera local movement + view angle
            Vector3 offsetLocal = new Vector3(
                (camDeltaLocal.x + rotOffset.x) * parallaxStrength * layerStrength,
                (camDeltaLocal.y + rotOffset.y) * parallaxStrength * layerStrength,
                0f
            );

            // Base position + equal Y gap + parallax offset
            Vector3 target = baseLocalPositions[i];
            target.y = baseLocalPositions[0].y + gapY * i;
            target += offsetLocal;

            layer.localPosition = Vector3.SmoothDamp(layer.localPosition, target, ref vel[i], 0.05f, Mathf.Infinity, dt);
        }
    }

    float ClampAngle(float a)
    {
        if (a > 180f) a -= 360f;
        return a;
    }
}
