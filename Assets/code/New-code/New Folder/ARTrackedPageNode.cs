using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;
using Vuforia;

public class ARTrackedPageNode : MonoBehaviour
{
    [Header("IDs")]
    public string pageId;

    [Header("References")]
    public DefaultObserverEventHandler observer;
    public ARMediaManager mediaManager;

    [Tooltip("Root object that contains the visuals for this page (parallax layers, meshes, etc).")]
    public GameObject contentRoot;

    [Header("Videos")]
    [Tooltip("These must finish to allow Replay.")]
    public List<VideoPlayer> mainVideos = new List<VideoPlayer>();

    [Tooltip("These can loop forever and are ignored for Replay.")]
    public List<VideoPlayer> backgroundLoopVideos = new List<VideoPlayer>();

    [Header("Animators (optional)")]
    public List<Animator> animators = new List<Animator>();

    private int _mainRemaining;
    private Action _onMainVideosFinished;

    private void Awake()
    {
        if (observer == null) observer = GetComponent<DefaultObserverEventHandler>();
        if (contentRoot == null) contentRoot = gameObject;

        // Default safe behavior
        SetVisible(false);
    }

    private void OnEnable()
    {
        if (observer != null)
        {
            observer.OnTargetFound.AddListener(OnFound);
            observer.OnTargetLost.AddListener(OnLost);
        }
    }

    private void OnDisable()
    {
        if (observer != null)
        {
            observer.OnTargetFound.RemoveListener(OnFound);
            observer.OnTargetLost.RemoveListener(OnLost);
        }
    }

    private void OnFound()
    {
        if (mediaManager != null) mediaManager.OnFound(this);
    }

    private void OnLost()
    {
        if (mediaManager != null) mediaManager.OnLost(this);
    }

    public void SetVisible(bool visible)
    {
        if (contentRoot != null) contentRoot.SetActive(visible);
    }

    public void ResetVisualsToZero()
    {
        // Main videos reset
        foreach (var v in mainVideos)
        {
            if (v == null) continue;
            v.Stop();
            v.time = 0;
            v.isLooping = false;
        }

        // Background videos reset
        foreach (var v in backgroundLoopVideos)
        {
            if (v == null) continue;
            v.Stop();
            v.time = 0;
            // keep loop as configured in inspector, but default is looping for sky
        }

        // Animators reset
        foreach (var a in animators)
        {
            if (a == null) continue;
            a.enabled = true;
            a.Rebind();
            a.Update(0f);
            a.speed = 0f;
        }
    }

    public void PlayVisualsFromZeroOnce(Action onMainFinished)
    {
        _onMainVideosFinished = onMainFinished;

        // Animators play (if used)
        foreach (var a in animators)
        {
            if (a == null) continue;
            a.speed = 1f;
            a.Play(0, 0, 0f);
        }

        // Background videos play (ignored for completion)
        foreach (var v in backgroundLoopVideos)
        {
            if (v == null) continue;
            v.Play();
        }

        // Main videos play and track completion
        _mainRemaining = 0;

        foreach (var v in mainVideos)
        {
            if (v == null) continue;
            _mainRemaining++;
            v.loopPointReached += OnMainVideoEnded;
            v.Play();
        }

        // If no main videos, completion is immediate (manager will still wait for audio end)
        if (_mainRemaining == 0)
            _onMainVideosFinished?.Invoke();
    }

    private void OnMainVideoEnded(VideoPlayer vp)
    {
        if (vp != null) vp.loopPointReached -= OnMainVideoEnded;

        _mainRemaining--;
        if (_mainRemaining <= 0)
            _onMainVideosFinished?.Invoke();
    }

    public void StopVisuals(bool resetToZero)
    {
        foreach (var v in mainVideos)
        {
            if (v == null) continue;
            v.loopPointReached -= OnMainVideoEnded;
            v.Stop();
            if (resetToZero) v.time = 0;
        }

        foreach (var v in backgroundLoopVideos)
        {
            if (v == null) continue;
            v.Stop();
            if (resetToZero) v.time = 0;
        }

        foreach (var a in animators)
        {
            if (a == null) continue;
            a.speed = 0f;
            if (resetToZero)
            {
                a.Rebind();
                a.Update(0f);
            }
        }
    }

    public void PauseVisuals()
    {
        foreach (var v in mainVideos)
        {
            if (v == null) continue;
            if (v.isPlaying) v.Pause();
        }

        foreach (var v in backgroundLoopVideos)
        {
            if (v == null) continue;
            if (v.isPlaying) v.Pause();
        }

        foreach (var a in animators)
        {
            if (a == null) continue;
            a.speed = 0f;
        }
    }

    public void ResumeVisuals()
    {
        foreach (var v in mainVideos)
        {
            if (v == null) continue;
            v.Play();
        }

        foreach (var v in backgroundLoopVideos)
        {
            if (v == null) continue;
            v.Play();
        }

        foreach (var a in animators)
        {
            if (a == null) continue;
            a.speed = 1f;
        }
    }
}
