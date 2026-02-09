using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

public class ARTrackedPageNode : MonoBehaviour
{
    [Header("IDs")]
    [SerializeField] private string pageId;

    [Header("References")]
    [SerializeField] private ARMediaManager mediaManager;

    [Header("Videos")]
    [SerializeField] private List<VideoPlayer> mainVideos = new();
    [SerializeField] private List<VideoPlayer> backgroundLoopVideos = new();

    [Header("Animators (optional)")]
    [SerializeField] private List<Animator> animators = new();

    [Header("Spline movers (optional)")]
    [SerializeField] private List<ARTrackableSplineMover> splineMovers = new();

    [Header("Video Freeze (per page)")]
    [SerializeField] private VuforiaVideoFrameFreezeController.FreezeMode freezeMode = VuforiaVideoFrameFreezeController.FreezeMode.None;
    [Min(0f)] public float freezeFirstSeconds = 0f;
    [Min(0f)] public float freezeLastSeconds = 0f;

    [Header("BGM behavior")]
    [SerializeField] private bool loopBgmUntilVoiceEnds = true;
    [SerializeField] private bool stopBgmWhenVoiceEnds = true;

    private bool _isTracked;
    private float _lastLostTime = -999f;

    public string PageId => pageId;
    public bool IsTracked => _isTracked;
    public bool LoopBgmUntilVoiceEnds => loopBgmUntilVoiceEnds;
    public bool StopBgmWhenVoiceEnds => stopBgmWhenVoiceEnds;

    private void Awake()
    {
        if (mediaManager == null)
        {
#if UNITY_2023_1_OR_NEWER
            mediaManager = Object.FindFirstObjectByType<ARMediaManager>();
#else
            mediaManager = Object.FindObjectOfType<ARMediaManager>();
#endif
        }

        if (splineMovers.Count == 0)
        {
            var mover = GetComponentInChildren<ARTrackableSplineMover>(true);
            if (mover != null) splineMovers.Add(mover);
        }

        if (animators.Count == 0)
        {
            var anim = GetComponentInChildren<Animator>(true);
            if (anim != null) animators.Add(anim);
        }
    }

    private void OnEnable()
    {
        if (mediaManager != null) mediaManager.RegisterNode(this);
    }

    private void OnDisable()
    {
        if (mediaManager != null) mediaManager.UnregisterNode(this);
    }

    // Called by VuforiaTrackHook
    public void NotifyFound()
    {
        _isTracked = true;

        if (mediaManager != null)
            mediaManager.NotifyTrackingFound(this);
        else
            StartFromBeginning();
    }

    // Called by VuforiaTrackHook
    public void NotifyLost()
    {
        _isTracked = false;
        _lastLostTime = Time.time;

        if (mediaManager != null)
            mediaManager.NotifyTrackingLost(this);
        else
            PauseVisuals();
    }

    public bool CanResume(float graceSeconds)
    {
        if (_lastLostTime < 0f) return false;
        return (Time.time - _lastLostTime) <= graceSeconds;
    }

    public void OnBecameInactiveByManager()
    {
        PauseVisuals();
    }

    public void StartFromBeginning()
    {
        // Videos restart
        RestartVideos(mainVideos);
        RestartVideos(backgroundLoopVideos);

        // Animators restart
        for (int i = 0; i < animators.Count; i++)
        {
            var a = animators[i];
            if (a == null) continue;
            a.speed = 1f;
            a.Rebind();
            a.Update(0f);
        }

        // Spline restart and play
        for (int i = 0; i < splineMovers.Count; i++)
        {
            var m = splineMovers[i];
            if (m == null) continue;

            m.Stop();
            m.ResetToStart();
            m.PlayOnce();
        }
    }

    public void PauseVisuals()
    {
        // Pause videos (only if active)
        PauseVideos(mainVideos);
        PauseVideos(backgroundLoopVideos);

        // Pause animators
        for (int i = 0; i < animators.Count; i++)
        {
            var a = animators[i];
            if (a == null) continue;
            a.speed = 0f;
        }

        // Pause spline movers
        for (int i = 0; i < splineMovers.Count; i++)
        {
            var m = splineMovers[i];
            if (m == null) continue;
            m.Pause();
        }
    }

    public void ResumeVisuals()
    {
        // Resume videos
        ResumeVideos(mainVideos);
        ResumeVideos(backgroundLoopVideos);

        // Resume animators
        for (int i = 0; i < animators.Count; i++)
        {
            var a = animators[i];
            if (a == null) continue;
            a.speed = 1f;
        }

        // Resume spline movers
        for (int i = 0; i < splineMovers.Count; i++)
        {
            var m = splineMovers[i];
            if (m == null) continue;
            m.Resume();
        }
    }

    private static void RestartVideos(List<VideoPlayer> list)
    {
        if (list == null) return;

        for (int i = 0; i < list.Count; i++)
        {
            var vp = list[i];
            if (vp == null) continue;
            if (!vp.gameObject.activeInHierarchy) continue;

            vp.time = 0;
            vp.Play();
        }
    }

    private static void PauseVideos(List<VideoPlayer> list)
    {
        if (list == null) return;

        for (int i = 0; i < list.Count; i++)
        {
            var vp = list[i];
            if (vp == null) continue;
            if (!vp.gameObject.activeInHierarchy) continue;

            if (vp.isPlaying) vp.Pause();
        }
    }

    private static void ResumeVideos(List<VideoPlayer> list)
    {
        if (list == null) return;

        for (int i = 0; i < list.Count; i++)
        {
            var vp = list[i];
            if (vp == null) continue;
            if (!vp.gameObject.activeInHierarchy) continue;

            // VideoPlayer has no UnPause, Play() resumes from paused frame
            vp.Play();
        }
    }
}
