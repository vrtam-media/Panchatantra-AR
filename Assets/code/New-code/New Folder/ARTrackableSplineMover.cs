using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

[DisallowMultipleComponent]
public class ARTrackableSplineMover : MonoBehaviour
{
    [Header("Spline")]
    public SplineContainer splineContainer;
    public int splineIndex = 0;

    [Serializable]
    public struct Segment
    {
        [Range(0f, 1f)] public float startT;
        [Range(0f, 1f)] public float endT;
        [Min(0f)] public float moveSeconds;
        [Min(0f)] public float waitAfterSeconds;
    }

    [Header("Movement Plan (runs once)")]
    public List<Segment> segments = new List<Segment>()
    {
        new Segment { startT = 0f, endT = 1f, moveSeconds = 2f, waitAfterSeconds = 0f }
    };

    [Header("Rotation")]
    public bool faceAlongSpline = true;
    public Vector3 up = Vector3.up;

    // State
    public bool IsFinished { get; private set; }
    public bool IsPlaying => _isPlaying;
    public bool IsPaused => _isPaused;

    // Compatibility aliases (so older scripts stop breaking)
    public bool IsCompleted => IsFinished;

    // Events (supports both styles you tried earlier)
    public event Action OnPlanCompleted;
    public event Action<ARTrackableSplineMover> OnPlanCompletedWithMover;

    int _segmentIndex;
    float _segmentTimer;
    bool _isPlaying;
    bool _isPaused;

    void Awake()
    {
        // Keep initial pose stable in editor and play mode
        if (!Application.isPlaying) return;
        ResetToStartInternal(setFinishedFalse: true);
    }

    void OnEnable()
    {
        if (!Application.isPlaying) return;
        // Do NOT force reset here. Enable/disable should not snap unless you explicitly call reset.
    }

    void Update()
    {
        if (!_isPlaying || _isPaused || IsFinished) return;

        var spline = GetSpline();
        if (spline == null) { FinishPlan(); return; }

        if (segments == null || segments.Count == 0) { FinishPlan(); return; }
        if (_segmentIndex >= segments.Count) { FinishPlan(); return; }

        var seg = segments[_segmentIndex];

        float dur = Mathf.Max(0.0001f, seg.moveSeconds);
        float t01 = Mathf.Clamp01(_segmentTimer / dur);
        float t = Mathf.Lerp(seg.startT, seg.endT, t01);

        ApplySplinePose(spline, t);

        _segmentTimer += Time.deltaTime;

        // Segment move done
        if (_segmentTimer >= seg.moveSeconds)
        {
            // Hard lock at exact endT to avoid tiny drift
            ApplySplinePose(spline, seg.endT);

            // Wait phase
            if (seg.waitAfterSeconds > 0f)
            {
                // Convert timer into "move + wait" without snapping anywhere
                float extra = _segmentTimer - seg.moveSeconds;
                if (extra < seg.waitAfterSeconds) return;
            }

            // Advance to next segment
            _segmentIndex++;
            _segmentTimer = 0f;

            if (_segmentIndex >= segments.Count)
            {
                FinishPlan();
            }
        }
    }

    Spline GetSpline()
    {
        if (splineContainer == null) return null;
        if (splineIndex < 0 || splineIndex >= splineContainer.Splines.Count) return null;
        return splineContainer.Splines[splineIndex];
    }

    void ApplySplinePose(Spline spline, float t)
    {
        if (spline == null) return;

        // Unity Splines returns float3 (Mathematics)
        float3 pos = spline.EvaluatePosition(t);
        transform.localPosition = (Vector3)pos;

        if (!faceAlongSpline) return;

        float3 tan = spline.EvaluateTangent(t);
        float tanLenSq = math.lengthsq(tan);
        if (tanLenSq < 1e-8f) return;

        // FIX: float3 has no ".normalized", use math.normalizesafe
        float3 fwd3 = math.normalizesafe(tan);
        Vector3 forward = (Vector3)fwd3;

        Vector3 upVec = up.sqrMagnitude > 1e-8f ? up.normalized : Vector3.up;
        transform.localRotation = Quaternion.LookRotation(forward, upVec);
    }

    void FinishPlan()
    {
        _isPlaying = false;
        _isPaused = false;
        IsFinished = true;

        OnPlanCompleted?.Invoke();
        OnPlanCompletedWithMover?.Invoke(this);
    }

    void ResetToStartInternal(bool setFinishedFalse)
    {
        var spline = GetSpline();
        if (spline != null && segments != null && segments.Count > 0)
        {
            ApplySplinePose(spline, segments[0].startT);
        }

        _segmentIndex = 0;
        _segmentTimer = 0f;
        _isPaused = false;
        _isPlaying = false;

        if (setFinishedFalse) IsFinished = false;
    }

    // Public API (use these)
    public void StartFromBeginning()
    {
        ResetToStartInternal(setFinishedFalse: true);
        _isPlaying = true;
        _isPaused = false;
    }

    public void Pause()
    {
        if (!_isPlaying || IsFinished) return;
        _isPaused = true;
    }

    public void Resume()
    {
        if (!_isPlaying || IsFinished) return;
        _isPaused = false;
    }

    public void StopAndReset()
    {
        ResetToStartInternal(setFinishedFalse: true);
    }

    // Backwards compatible wrappers (so your other scripts stop erroring)
    public void ResetToStart() => ResetToStartInternal(setFinishedFalse: true);
    public void ResetMover() => ResetToStartInternal(setFinishedFalse: true);
    public void StopMover() => StopAndReset();
    public void PlayOnce() => StartFromBeginning();
}
