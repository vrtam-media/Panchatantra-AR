using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using Vuforia;

public class VuforiaSplineMoveWaitLoop : MonoBehaviour
{
    public enum LostTrackingAction
    {
        FreezeWhereItIs,   // default: pause everything in-place
        ResetToStart,      // snap to spline start when lost
        HideObject         // disable GameObject when lost (will re-enable when found)
    }

    [Header("Spline")]
    public SplineContainer splineContainer;
    [Min(0)] public int splineIndex = 0;

    [Header("Vuforia")]
    public ObserverBehaviour vuforiaObserver;

    [Header("Animator")]
    public Animator animator;
    public float normalAnimSpeed = 1f;

    [Header("Timing (seconds)")]
    [Min(0.01f)] public float moveSeconds = 3f;
    [Min(0f)] public float waitAtEndSeconds = 6f;

    [Header("Rotation")]
    public bool faceAlongSpline = true;
    public Vector3 up = Vector3.up;

    [Header("When tracking is lost")]
    public LostTrackingAction lostTrackingAction = LostTrackingAction.FreezeWhereItIs;

    private enum State { Moving, WaitingAtEnd }
    private State state = State.Moving;

    private bool isTracked = false;

    // Normalized spline position 0..1
    private float t = 0f;

    // End-wait timer
    private float waitTimer = 0f;

    void Awake()
    {
        if (vuforiaObserver == null)
            vuforiaObserver = GetComponentInParent<ObserverBehaviour>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (splineContainer == null)
            splineContainer = FindObjectOfType<SplineContainer>(true);

        // Start with everything stopped until tracking is valid
        SetAnimatorRunning(false);
        isTracked = false;
    }

    void OnEnable()
    {
        if (vuforiaObserver != null)
            vuforiaObserver.OnTargetStatusChanged += OnTargetStatusChanged;
    }

    void OnDisable()
    {
        if (vuforiaObserver != null)
            vuforiaObserver.OnTargetStatusChanged -= OnTargetStatusChanged;
    }

    void Update()
    {
        // HARD STOP: no spline motion, no timers, no looping while not tracked
        if (!isTracked)
            return;

        if (splineContainer == null || splineContainer.Splines == null || splineContainer.Splines.Count == 0)
            return;

        int idx = math.clamp(splineIndex, 0, splineContainer.Splines.Count - 1);
        var spline = splineContainer.Splines[idx];

        if (state == State.Moving)
        {
            t += Time.deltaTime / moveSeconds;

            if (t >= 1f)
            {
                t = 1f;
                state = State.WaitingAtEnd;
                waitTimer = 0f;
            }

            ApplySplinePose(spline, t);
        }
        else // WaitingAtEnd
        {
            // Freeze at end pose (but still tracked)
            ApplySplinePose(spline, 1f);

            waitTimer += Time.deltaTime;
            if (waitTimer >= waitAtEndSeconds)
            {
                // Loop: snap to start, begin moving again
                t = 0f;
                state = State.Moving;
                ApplySplinePose(spline, 0f);
            }
        }
    }

    private void OnTargetStatusChanged(ObserverBehaviour ob, TargetStatus status)
    {
        bool nowTracked =
            status.Status == Status.TRACKED ||
            status.Status == Status.EXTENDED_TRACKED;

        if (nowTracked == isTracked)
            return; // no change

        isTracked = nowTracked;

        if (isTracked)
        {
            // Tracking FOUND: resume animation + spline logic
            if (lostTrackingAction == LostTrackingAction.HideObject)
                gameObject.SetActive(true);

            SetAnimatorRunning(true);
        }
        else
        {
            // Tracking LOST: stop animation + stop spline logic immediately
            SetAnimatorRunning(false);

            if (lostTrackingAction == LostTrackingAction.ResetToStart)
            {
                ResetToStartPose();
            }
            else if (lostTrackingAction == LostTrackingAction.HideObject)
            {
                // Disabling will also stop Update completely
                gameObject.SetActive(false);
            }
            // FreezeWhereItIs: do nothing (state/t/waitTimer remain as-is, but Update is halted)
        }
    }

    private void SetAnimatorRunning(bool running)
    {
        if (animator == null) return;
        animator.speed = running ? normalAnimSpeed : 0f;
    }

    private void ResetToStartPose()
    {
        if (splineContainer == null || splineContainer.Splines == null || splineContainer.Splines.Count == 0)
            return;

        int idx = math.clamp(splineIndex, 0, splineContainer.Splines.Count - 1);
        var spline = splineContainer.Splines[idx];

        t = 0f;
        waitTimer = 0f;
        state = State.Moving;

        ApplySplinePose(spline, 0f);
    }

    private void ApplySplinePose(Spline spline, float normalizedT)
    {
        float3 localPos = spline.EvaluatePosition(normalizedT);
        float3 localTan = spline.EvaluateTangent(normalizedT);

        Vector3 worldPos = splineContainer.transform.TransformPoint((Vector3)localPos);
        Vector3 worldTan = splineContainer.transform.TransformDirection((Vector3)localTan);

        transform.position = worldPos;

        if (faceAlongSpline && worldTan.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(worldTan.normalized, up);
    }
}
