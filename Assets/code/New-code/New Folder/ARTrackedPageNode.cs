using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

[DisallowMultipleComponent]
public class ARTrackedPageNode : MonoBehaviour
{
    [Header("IDs")]
    [SerializeField] private string pageId = "";

    [Header("References")]
    [SerializeField] private ARMediaManager mediaManager;

    [Header("Videos")]
    [SerializeField] private List<VideoPlayer> mainVideos = new List<VideoPlayer>();
    [SerializeField] private List<VideoPlayer> backgroundLoopVideos = new List<VideoPlayer>();

    [Header("Animators (optional)")]
    [SerializeField] private List<Animator> animators = new List<Animator>();

    [Header("Spline movers (optional)")]
    [SerializeField] private List<ARTrackableSplineMover> splineMovers = new List<ARTrackableSplineMover>();

    [Header("Video Freeze (per page)")]
    [SerializeField] private FreezeMode freezeMode = FreezeMode.None;
    [SerializeField] private float freezeFirstSeconds = 0f;
    [SerializeField] private float freezeLastSeconds = 0f;

    [Header("BGM behavior")]
    [SerializeField] private bool loopBgmUntilVoiceEnds = true;
    [SerializeField] private bool stopBgmWhenVoiceEnds = true;

    public enum FreezeMode { None, FirstSeconds, LastSeconds }

    private bool _isTracked;
    private float _lastLostTime = -999f;

    private readonly HashSet<VideoPlayer> _endedMainVideos = new HashSet<VideoPlayer>();
    private bool _visualCompletedFired;

    public event Action OnVisualCompleted; // for manager compatibility

    public string PageId => pageId;
    public bool IsTracked => _isTracked;
    public float LastLostTime => _lastLostTime;

    public bool LoopBgmUntilVoiceEnds => loopBgmUntilVoiceEnds;
    public bool StopBgmWhenVoiceEnds => stopBgmWhenVoiceEnds;

    public FreezeMode PageFreezeMode => freezeMode;
    public float FreezeFirstSeconds => freezeFirstSeconds;
    public float FreezeLastSeconds => freezeLastSeconds;

    private void Awake()
    {
        foreach (var vp in mainVideos)
        {
            if (vp == null) continue;
            vp.loopPointReached -= OnMainVideoEnded;
            vp.loopPointReached += OnMainVideoEnded;
        }
    }

    private void Update()
    {
        if (!_visualCompletedFired && IsVisualFinished())
        {
            _visualCompletedFired = true;
            OnVisualCompleted?.Invoke();
        }
    }

    // Called by VuforiaTrackHook (keep names)
    public void NotifyFound()
    {
        _isTracked = true;
        mediaManager?.HandlePageFound(this);
    }

    public void NotifyLost()
    {
        _isTracked = false;
        _lastLostTime = Time.time;
        mediaManager?.HandlePageLost(this);
    }

    // Backward-compatible wrappers (older manager versions)
    public void Pause() => PauseAll();
    public void Resume() => ResumeAll();
    public void StartFromBeginning() => RestartAllPageContent();
    public void StopAndReset() => StopAndResetAll();

    public void RestartAllPageContent()
    {
        StopAndResetAll();
        PlayAll();
    }

    public void PlayAll()
    {
        _visualCompletedFired = false;

        _endedMainVideos.Clear();
        foreach (var vp in mainVideos)
        {
            if (vp == null) continue;
            vp.time = 0;
            vp.Play();
        }

        foreach (var vp in backgroundLoopVideos)
        {
            if (vp == null) continue;
            vp.isLooping = true;
            vp.time = 0;
            vp.Play();
        }

        foreach (var a in animators)
        {
            if (a == null) continue;
            a.speed = 1f;
            a.Play(0, 0, 0f);
            a.Update(0f);
        }

        foreach (var m in splineMovers)
        {
            if (m == null) continue;
            m.StartFromBeginning();
        }
    }

    public void PauseAll()
    {
        foreach (var vp in mainVideos) if (vp != null) vp.Pause();
        foreach (var vp in backgroundLoopVideos) if (vp != null) vp.Pause();

        foreach (var a in animators) if (a != null) a.speed = 0f;
        foreach (var m in splineMovers) if (m != null) m.Pause();
    }

    public void ResumeAll()
    {
        foreach (var vp in mainVideos) if (vp != null) vp.Play();
        foreach (var vp in backgroundLoopVideos) if (vp != null) vp.Play();

        foreach (var a in animators)
        {
            if (a == null) continue;
            if (!IsAnimatorFinished(a)) a.speed = 1f;
        }

        foreach (var m in splineMovers) if (m != null) m.Resume();
    }

    public void StopAndResetAll()
    {
        _visualCompletedFired = false;
        _endedMainVideos.Clear();

        foreach (var vp in mainVideos)
        {
            if (vp == null) continue;
            vp.Stop();
            vp.time = 0;
        }

        foreach (var vp in backgroundLoopVideos)
        {
            if (vp == null) continue;
            vp.Stop();
            vp.time = 0;
        }

        foreach (var a in animators)
        {
            if (a == null) continue;
            a.speed = 0f;
            a.Play(0, 0, 0f);
            a.Update(0f);
        }

        foreach (var m in splineMovers)
        {
            if (m == null) continue;
            m.ResetToStart();
        }
    }

    public bool AreMainVideosFinished()
    {
        if (mainVideos == null || mainVideos.Count == 0) return true;

        int validCount = 0;
        foreach (var vp in mainVideos)
        {
            if (vp == null) continue;
            validCount++;
            if (!_endedMainVideos.Contains(vp)) return false;
        }

        return validCount == 0 || _endedMainVideos.Count >= validCount;
    }

    public bool AreAnimatorsFinished()
    {
        if (animators == null || animators.Count == 0) return true;

        foreach (var a in animators)
        {
            if (a == null) continue;
            if (!IsAnimatorFinished(a)) return false;
        }

        return true;
    }

    public bool AreSplineMoversFinished()
    {
        if (splineMovers == null || splineMovers.Count == 0) return true;

        foreach (var m in splineMovers)
        {
            if (m == null) continue;
            if (!m.IsFinished) return false;
        }

        return true;
    }

    public bool IsVisualFinished()
    {
        return AreMainVideosFinished() && AreAnimatorsFinished() && AreSplineMoversFinished();
    }

    private bool IsAnimatorFinished(Animator a)
    {
        if (a.runtimeAnimatorController == null) return true;

        var st = a.GetCurrentAnimatorStateInfo(0);
        bool finished = !a.IsInTransition(0) && st.normalizedTime >= 1f;

        if (finished)
        {
            a.Play(st.fullPathHash, 0, 1f);
            a.Update(0f);
            a.speed = 0f;
        }

        return finished;
    }

    private void OnMainVideoEnded(VideoPlayer vp)
    {
        if (vp == null) return;
        _endedMainVideos.Add(vp);
    }
}
