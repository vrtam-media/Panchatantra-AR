using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class ParallaxLayerStack : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform cameraTransform;

    [Header("Layers (order matters)")]
    [Tooltip("0 = farthest, last = nearest. Include video planes too.")]
    [SerializeField] private List<Transform> layers = new List<Transform>();
    [SerializeField] private bool autoCollectChildren = true;

    [Header("Stack Layout (manual apply)")]
    [Tooltip("Equal gap applied along local axis when you press Apply Stack Layout.")]
    [SerializeField] private Axis gapAxis = Axis.Y;
    [SerializeField] private float gapPerLayer = 0.002f;

    [Header("Parallax")]
    [SerializeField] private bool parallaxEnabled = true;

    [Tooltip("Enable which axes the parallax OFFSET is allowed to move layers.")]
    [SerializeField] private bool moveX = true;
    [SerializeField] private bool moveY = true;
    [SerializeField] private bool moveZ = false;

    [Tooltip("How much camera POSITION affects parallax.")]
    [Range(0f, 2f)]
    [SerializeField] private float positionSensitivity = 0.25f;

    [Tooltip("How much camera ROTATION affects parallax.")]
    [Range(0f, 0.1f)]
    [SerializeField] private float rotationSensitivity = 0.01f;

    [Tooltip("Clamp camera rotation delta used for parallax to +/- N degrees (per axis). Example: 5 means [-5..+5].")]
    [Range(0f, 45f)]
    [SerializeField] private float rotationClampDegrees = 5f;

    [Tooltip("Clamp parallax when user views from extreme angles (prevents flipping / opposite view).")]
    [Range(10f, 89f)]
    [SerializeField] private float maxViewAngleDegrees = 75f;

    [Tooltip("Max allowed parallax offset on X/Y.")]
    [SerializeField] private Vector2 maxOffsetXY = new Vector2(0.05f, 0.05f);

    [Tooltip("Max allowed parallax offset on Z.")]
    [SerializeField] private float maxOffsetZ = 0.03f;

    [Tooltip("How much FAR layers move (layer 0).")]
    [Range(0f, 2f)]
    [SerializeField] private float farStrength = 0.1f;

    [Tooltip("How much NEAR layers move (last layer).")]
    [Range(0f, 2f)]
    [SerializeField] private float nearStrength = 1f;

    [Tooltip("Higher = smoother, lower = snappier.")]
    [Range(0f, 30f)]
    [SerializeField] private float smoothing = 10f;

    [Header("Edge leak reduction (manual apply)")]
    [SerializeField] private bool overscanEnabled = true;
    [Range(1f, 1.2f)]
    [SerializeField] private float overscanScale = 1.06f;

    [Header("Tracking visibility (driven by controller)")]
    [SerializeField] private bool hideWhenNotTracked = true;

    public enum Axis { X, Y, Z }

    // Runtime state
    private bool isTracked = true;

    private Vector3[] baseLocalPositions = Array.Empty<Vector3>();
    private Vector3[] currentOffsets = Array.Empty<Vector3>();

    private Vector3 referenceCamLocalPos;
    private Vector3 referenceCamLocalEuler; // baseline local euler angles

    private bool hasReference;

    public Transform CameraTransform => cameraTransform;
    public IReadOnlyList<Transform> Layers => layers;

    public void SetTracked(bool tracked)
    {
        isTracked = tracked;

        if (hideWhenNotTracked)
            SetLayersActive(tracked);

        if (tracked)
            CaptureCameraReference();
        else
            hasReference = false;
    }

    public void SetLayersActive(bool active)
    {
        for (int i = 0; i < layers.Count; i++)
        {
            if (!layers[i]) continue;
            layers[i].gameObject.SetActive(active);
        }
    }

    private void Reset()
    {
        cameraTransform = Camera.main ? Camera.main.transform : null;
        autoCollectChildren = true;
    }

    private void OnEnable()
    {
        if (autoCollectChildren)
            CollectFromChildren();

        CaptureCurrentAsBase();

        if (cameraTransform != null)
            CaptureCameraReference();
    }

    private void Update()
    {
        if (!Application.isPlaying) return;
        if (!parallaxEnabled) return;
        if (!isTracked) return;
        if (cameraTransform == null) return;
        if (layers.Count == 0) return;

        if (!hasReference)
            CaptureCameraReference();

        ApplyParallax(Time.unscaledDeltaTime);
    }

    private void ApplyParallax(float dt)
    {
        EnsureArrays();

        Transform root = transform;

        // View angle gating (prevents opposite side extreme motion)
        float angleFactor = 1f;
        {
            Vector3 normal = root.forward; // page normal direction
            Vector3 toCam = (cameraTransform.position - root.position);
            if (toCam.sqrMagnitude > 0.000001f)
            {
                float viewAngle = Vector3.Angle(normal, toCam.normalized);
                // Fade out after maxViewAngleDegrees
                if (viewAngle > maxViewAngleDegrees)
                {
                    float t = Mathf.InverseLerp(maxViewAngleDegrees, 90f, viewAngle);
                    angleFactor = 1f - Mathf.Clamp01(t);
                }
            }
        }

        // Camera deltas in root local space
        Vector3 camLocalPos = root.InverseTransformPoint(cameraTransform.position);
        Vector3 deltaPos = camLocalPos - referenceCamLocalPos;

        // Local euler delta using DeltaAngle (stable wrap)
        Vector3 camLocalEuler = GetLocalEulerStable(root, cameraTransform);
        Vector3 deltaEuler = new Vector3(
            Mathf.DeltaAngle(referenceCamLocalEuler.x, camLocalEuler.x),
            Mathf.DeltaAngle(referenceCamLocalEuler.y, camLocalEuler.y),
            Mathf.DeltaAngle(referenceCamLocalEuler.z, camLocalEuler.z)
        );

        // Clamp rotation used for parallax to +/- rotationClampDegrees
        deltaEuler.x = Mathf.Clamp(deltaEuler.x, -rotationClampDegrees, rotationClampDegrees);
        deltaEuler.y = Mathf.Clamp(deltaEuler.y, -rotationClampDegrees, rotationClampDegrees);
        deltaEuler.z = Mathf.Clamp(deltaEuler.z, -rotationClampDegrees, rotationClampDegrees);

        // Convert rotation delta to parallax offset
        // Yaw -> X shift, Pitch -> Y shift. Roll can be optionally mapped to Z shift.
        Vector3 rotOffset = new Vector3(
            deltaEuler.y * rotationSensitivity,
            -deltaEuler.x * rotationSensitivity,
            deltaEuler.z * rotationSensitivity
        );

        Vector3 posOffset = deltaPos * positionSensitivity;

        Vector3 raw = (posOffset + rotOffset) * angleFactor;

        // Clamp raw offsets
        raw.x = Mathf.Clamp(raw.x, -maxOffsetXY.x, maxOffsetXY.x);
        raw.y = Mathf.Clamp(raw.y, -maxOffsetXY.y, maxOffsetXY.y);
        raw.z = Mathf.Clamp(raw.z, -maxOffsetZ, maxOffsetZ);

        // Axis toggles
        if (!moveX) raw.x = 0f;
        if (!moveY) raw.y = 0f;
        if (!moveZ) raw.z = 0f;

        float lerpK = (smoothing <= 0f) ? 1f : 1f - Mathf.Exp(-smoothing * dt);

        int count = layers.Count;
        for (int i = 0; i < count; i++)
        {
            Transform layer = layers[i];
            if (!layer) continue;

            float depth01 = (count <= 1) ? 1f : (float)i / (count - 1); // 0 far .. 1 near
            float strength = Mathf.Lerp(farStrength, nearStrength, depth01);

            Vector3 targetOffset = raw * strength;

            currentOffsets[i] = Vector3.Lerp(currentOffsets[i], targetOffset, lerpK);

            // Only add offsets, never touch scale here
            layer.localPosition = baseLocalPositions[i] + currentOffsets[i];
        }
    }

    private void CaptureCameraReference()
    {
        if (cameraTransform == null) return;

        Transform root = transform;
        referenceCamLocalPos = root.InverseTransformPoint(cameraTransform.position);
        referenceCamLocalEuler = GetLocalEulerStable(root, cameraTransform);

        hasReference = true;
    }

    private static Vector3 GetLocalEulerStable(Transform root, Transform cam)
    {
        // Convert camera rotation into root local rotation and read euler angles.
        Quaternion localRot = Quaternion.Inverse(root.rotation) * cam.rotation;
        return localRot.eulerAngles;
    }

    private void EnsureArrays()
    {
        int count = layers.Count;
        if (baseLocalPositions.Length != count)
            baseLocalPositions = new Vector3[count];

        if (currentOffsets.Length != count)
            currentOffsets = new Vector3[count];
    }

    // ---------- Editor-facing utilities ----------

    public void CollectFromChildren()
    {
        layers.Clear();

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform c = transform.GetChild(i);
            if (c != null)
                layers.Add(c);
        }

        EnsureArrays();
    }

    public void CaptureCurrentAsBase()
    {
        EnsureArrays();

        for (int i = 0; i < layers.Count; i++)
        {
            if (!layers[i]) continue;
            baseLocalPositions[i] = layers[i].localPosition;
            currentOffsets[i] = Vector3.zero;
        }
    }

    public void ApplyStackLayoutEqualGap()
    {
        // Put all layers on same local X/Z (based on their current base), then apply equal gap along selected axis.
        EnsureArrays();

        for (int i = 0; i < layers.Count; i++)
        {
            Transform layer = layers[i];
            if (!layer) continue;

            Vector3 p = layer.localPosition;

            // Reset all layers to same plane by pinning the chosen axis progression
            Vector3 baseP = p;

            float gap = gapPerLayer * i;

            switch (gapAxis)
            {
                case Axis.X: baseP.x = gap; break;
                case Axis.Y: baseP.y = gap; break;
                case Axis.Z: baseP.z = gap; break;
            }

            layer.localPosition = baseP;
        }

        // After layout, capture as base so parallax uses this as the new stable layout.
        CaptureCurrentAsBase();

        // Apply overscan once (optional)
        if (overscanEnabled)
            ApplyOverscanScale();
    }

    public void ApplyOverscanScale()
    {
        if (!overscanEnabled) return;

        for (int i = 0; i < layers.Count; i++)
        {
            Transform layer = layers[i];
            if (!layer) continue;

            Vector3 s = layer.localScale;
            layer.localScale = new Vector3(s.x * overscanScale, s.y * overscanScale, s.z);
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(ParallaxLayerStack))]
    private class ParallaxLayerStackEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            GUILayout.Space(8);

            var s = (ParallaxLayerStack)target;

            EditorGUILayout.LabelField("Tools", EditorStyles.boldLabel);

            if (GUILayout.Button("Collect From Children"))
            {
                Undo.RecordObject(s, "Collect Layers");
                s.CollectFromChildren();
                EditorUtility.SetDirty(s);
            }

            if (GUILayout.Button("Capture Current As Base"))
            {
                Undo.RecordObjects(s.layers.ToArray(), "Capture Base");
                s.CaptureCurrentAsBase();
                EditorUtility.SetDirty(s);
            }

            if (GUILayout.Button("Apply Stack Layout (Equal Gap)"))
            {
                Undo.RecordObjects(s.layers.ToArray(), "Apply Stack Layout");
                s.ApplyStackLayoutEqualGap();
                EditorUtility.SetDirty(s);
            }

            if (GUILayout.Button("Apply Overscan Scale (once)"))
            {
                Undo.RecordObjects(s.layers.ToArray(), "Apply Overscan");
                s.ApplyOverscanScale();
                EditorUtility.SetDirty(s);
            }
        }
    }
#endif
}
