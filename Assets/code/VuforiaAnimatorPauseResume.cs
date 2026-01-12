using UnityEngine;
using Vuforia;

public class VuforiaAnimatorPauseResume : MonoBehaviour
{
    [Header("Assign in Inspector")]
    public ObserverBehaviour vuforiaObserver;
    public Animator animator;

    [Header("Animation Speed")]
    public float normalSpeed = 1f;

    private void Awake()
    {
        if (vuforiaObserver == null)
            vuforiaObserver = GetComponentInParent<ObserverBehaviour>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        // Start paused until tracking is confirmed
        if (animator != null) animator.speed = 0f;
    }

    private void OnEnable()
    {
        if (vuforiaObserver != null)
            vuforiaObserver.OnTargetStatusChanged += OnTargetStatusChanged;
    }

    private void OnDisable()
    {
        if (vuforiaObserver != null)
            vuforiaObserver.OnTargetStatusChanged -= OnTargetStatusChanged;
    }

    private void OnTargetStatusChanged(ObserverBehaviour ob, TargetStatus status)
    {
        bool tracked =
            status.Status == Status.TRACKED ||
            status.Status == Status.EXTENDED_TRACKED;

        if (animator == null) return;

        animator.speed = tracked ? normalSpeed : 0f; // pause/resume without restarting
    }
}
