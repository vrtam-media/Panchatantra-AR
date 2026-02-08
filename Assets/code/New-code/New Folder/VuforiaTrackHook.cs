using UnityEngine;
using Vuforia;

[RequireComponent(typeof(ObserverBehaviour))]
public class VuforiaTrackHook : MonoBehaviour
{
    public ARTrackedPageNode PageNode;

    private ObserverBehaviour _observer;
    private bool _isTracked;

    private void Awake()
    {
        _observer = GetComponent<ObserverBehaviour>();
    }

    private void OnEnable()
    {
        if (_observer != null)
            _observer.OnTargetStatusChanged += OnStatusChanged;
    }

    private void OnDisable()
    {
        if (_observer != null)
            _observer.OnTargetStatusChanged -= OnStatusChanged;
    }

    private void OnStatusChanged(ObserverBehaviour behaviour, TargetStatus status)
    {
        bool trackedNow =
            status.Status == Status.TRACKED ||
            status.Status == Status.EXTENDED_TRACKED;

        if (trackedNow == _isTracked) return;
        _isTracked = trackedNow;

        if (PageNode == null) return;

        if (_isTracked) PageNode.NotifyFound();
        else PageNode.NotifyLost();
    }
}
