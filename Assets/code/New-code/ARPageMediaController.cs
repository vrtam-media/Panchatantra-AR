using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using Vuforia;

[DisallowMultipleComponent]
public class ARPageMediaController : MonoBehaviour
{
    [Header("Vuforia Tracking")]
    [SerializeField] private ObserverBehaviour vuforiaObserver;
    [SerializeField] private bool treatExtendedTrackedAsTracked = true;

    [Tooltip("If tracking returns within this window, resume from same time. If later, restart from 0.")]
    [SerializeField] private float resumeGraceSeconds = 1f;

    [Header("Parallax (optional)")]
    [SerializeField] private ParallaxLayerStack parallaxStack;

    [Header("Video (one or many players)")]
    [SerializeField] private List<VideoPlayer> videoPlayers = new List<VideoPlayer>();
    [SerializeField] private VideoClip videoClipOverride;
    [SerializeField] private bool muteVideoAudio = true;

    [Header("Audio Localization")]
    [SerializeField] private ARAudioLocalizationDatabase audioDatabase;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private string pageIdOverride;
    [SerializeField] private string currentLanguage = "English";

    [Header("Video Hold and Sync")]
    [Tooltip("Extend video duration to match audio by holding first and last frame.")]
    [SerializeField] private bool autoExtendVideoToAudio = true;

    [Tooltip("0 means add extension mostly to START hold, 1 means add extension mostly to END hold.")]
    [Range(0f, 1f)]
    [SerializeField] private float extendSplitToEnd = 0.5f;

    [SerializeField] private float baseHoldStart = 0f;
    [SerializeField] private float baseHoldEnd = 0f;

    [Header("Replay UI")]
    [SerializeField] private Button replayButton;
    [SerializeField] private bool billboardReplayToCamera = true;
    [SerializeField] private Transform replayBillboardCamera;
    [SerializeField] private Vector3 replayWorldOffsetLocal = new Vector3(0f, 0.02f, 0f);

    [Header("Audio Controls")]
    [Range(0f, 1f)]
    [SerializeField] private float masterVolume = 1f;
    [SerializeField] private bool muteAudio = false;

    // Runtime state
    private bool isTracked;
    private bool isPlaying;
    private bool isCompleted;
    private bool isPrepared;

    private float lostAtTime = -999f;

    private float mediaTime; // seconds since page start
    private float pageTotalDuration;
    private float holdStart;
    private float holdEnd;

    private float longestVideoLength;

    private ARAudioLocalizationDatabase.PageAudio resolvedPage;
    private List<SegmentTimeline> timeline = new List<SegmentTimeline>();
    private int currentSegmentIndex = -1;

    private struct SegmentTimeline
    {
        public float startTime;
        public float endTime;
        public AudioClip clip;
        public float volume;
    }

    private void OnEnable()
    {
        if (vuforiaObserver != null)
            vuforiaObserver.OnTargetStatusChanged += OnTargetStatusChanged;

        if (replayButton != null)
        {
            replayButton.onClick.RemoveListener(OnReplayClicked);
            replayButton.onClick.AddListener(OnReplayClicked);
            replayButton.gameObject.SetActive(false);
        }
    }

    private void OnDisable()
    {
        if (vuforiaObserver != null)
            vuforiaObserver.OnTargetStatusChanged -= OnTargetStatusChanged;

        if (replayButton != null)
            replayButton.onClick.RemoveListener(OnReplayClicked);
    }

    private void Start()
    {
        ConfigureVideoPlayers();
        HideReplay();
    }

    private void Update()
    {
        if (billboardReplayToCamera)
            UpdateReplayBillboard();

        if (!isTracked) return;
        if (!isPlaying) return;

        mediaTime += Time.unscaledDeltaTime;

        ApplyVideoAtMediaTime(mediaTime);
        ApplyAudioAtMediaTime(mediaTime);

        if (mediaTime >= pageTotalDuration)
        {
            CompletePlayback();
        }
    }

    private void OnTargetStatusChanged(ObserverBehaviour behaviour, TargetStatus targetStatus)
    {
        bool trackedNow =
            targetStatus.Status == Status.TRACKED ||
            (treatExtendedTrackedAsTracked && targetStatus.Status == Status.EXTENDED_TRACKED);

        if (trackedNow && !isTracked)
        {
            isTracked = true;
            OnTrackingFound();
        }
        else if (!trackedNow && isTracked)
        {
            isTracked = false;
            OnTrackingLost();
        }
    }

    private void OnTrackingFound()
    {
        if (parallaxStack != null)
            parallaxStack.SetTracked(true);

        float lostDuration = Time.unscaledTime - lostAtTime;

        // Rule: auto play
        if (lostDuration <= resumeGraceSeconds && !isCompleted && mediaTime > 0f)
        {
            ResumeFromMediaTime(mediaTime);
        }
        else
        {
            RestartFromBeginning();
        }
    }

    private void OnTrackingLost()
    {
        lostAtTime = Time.unscaledTime;

        PausePlayback();

        if (parallaxStack != null)
            parallaxStack.SetTracked(false);
    }

    // ---------------------- Playback core ----------------------

    private void RestartFromBeginning()
    {
        BuildPlanForCurrentPage();

        mediaTime = 0f;
        isCompleted = false;

        PrepareVideoPlayers();
        isPrepared = true;

        HideReplay();

        // Start playing immediately; holds handled by ApplyVideoAtMediaTime
        isPlaying = true;

        ApplyVideoAtMediaTime(0f);
        ApplyAudioAtMediaTime(0f);
    }

    private void ResumeFromMediaTime(float t)
    {
        if (!isPrepared)
        {
            PrepareVideoPlayers();
            isPrepared = true;
        }

        isPlaying = true;

        ApplyVideoAtMediaTime(t);
        ApplyAudioAtMediaTime(t);
        HideReplay();
    }

    private void PausePlayback()
    {
        isPlaying = false;

        // Video pause
        for (int i = 0; i < videoPlayers.Count; i++)
        {
            var vp = videoPlayers[i];
            if (!vp) continue;
            vp.Pause();
        }

        // Audio pause
        if (audioSource != null)
            audioSource.Pause();
    }

    private void CompletePlayback()
    {
        isPlaying = false;
        isCompleted = true;

        // Freeze on last frame (all players)
        ApplyVideoAtMediaTime(pageTotalDuration);

        // Stop audio
        if (audioSource != null)
            audioSource.Stop();

        ShowReplay();
    }

    private void OnReplayClicked()
    {
        // Rule 3A: always restart from 0
        RestartFromBeginning();
    }

    // ---------------------- Plan building ----------------------

    private void BuildPlanForCurrentPage()
    {
        string pageId = ResolvePageId();

        resolvedPage = null;
        timeline.Clear();
        currentSegmentIndex = -1;

        float audioDuration = 0f;
        holdStart = Mathf.Max(0f, baseHoldStart);
        holdEnd = Mathf.Max(0f, baseHoldEnd);

        if (audioDatabase != null && audioDatabase.TryGetPage(currentLanguage, pageId, out resolvedPage))
        {
            // Build audio timeline for seeking/resume
            float t = 0f;
            t += Mathf.Max(0f, resolvedPage.extraStartSilence);

            for (int i = 0; i < resolvedPage.segments.Count; i++)
            {
                var seg = resolvedPage.segments[i];
                if (seg == null) continue;

                t += Mathf.Max(0f, seg.delayBefore);

                if (seg.clip != null)
                {
                    float start = t;
                    float end = t + (float)seg.clip.length;

                    timeline.Add(new SegmentTimeline
                    {
                        startTime = start,
                        endTime = end,
                        clip = seg.clip,
                        volume = Mathf.Clamp01(seg.volume)
                    });

                    t = end;
                }
            }

            t += Mathf.Max(0f, resolvedPage.extraEndSilence);
            audioDuration = t;

            if (resolvedPage.overrideVideoHolds)
            {
                holdStart = Mathf.Max(0f, resolvedPage.videoHoldStart);
                holdEnd = Mathf.Max(0f, resolvedPage.videoHoldEnd);
            }
        }

        // Video duration uses longest clip across all players
        longestVideoLength = GetLongestVideoLength();

        // Auto extend video to match audio (only extends video, does not extend audio)
        if (autoExtendVideoToAudio && audioDuration > 0f && longestVideoLength > 0f)
        {
            float baseVideoDuration = holdStart + longestVideoLength + holdEnd;
            if (audioDuration > baseVideoDuration)
            {
                float extra = audioDuration - baseVideoDuration;
                float extraToEnd = extra * Mathf.Clamp01(extendSplitToEnd);
                float extraToStart = extra - extraToEnd;

                holdStart += extraToStart;
                holdEnd += extraToEnd;
            }
        }

        float videoTotal = (longestVideoLength > 0f) ? (holdStart + longestVideoLength + holdEnd) : 0f;
        pageTotalDuration = Mathf.Max(videoTotal, audioDuration);

        // If no media, still avoid 0 duration
        if (pageTotalDuration <= 0f)
            pageTotalDuration = 0.01f;
    }

    private string ResolvePageId()
    {
        if (!string.IsNullOrWhiteSpace(pageIdOverride))
            return pageIdOverride.Trim();

        if (vuforiaObserver != null && !string.IsNullOrWhiteSpace(vuforiaObserver.TargetName))
            return vuforiaObserver.TargetName;

        // last fallback
        return gameObject.name;
    }

    private float GetLongestVideoLength()
    {
        float maxLen = 0f;

        for (int i = 0; i < videoPlayers.Count; i++)
        {
            var vp = videoPlayers[i];
            if (!vp) continue;

            VideoClip clip = videoClipOverride != null ? videoClipOverride : vp.clip;
            if (clip != null)
            {
                float len = (float)clip.length;
                if (len > maxLen) maxLen = len;
            }
        }

        return maxLen;
    }

    // ---------------------- Video handling ----------------------

    private void ConfigureVideoPlayers()
    {
        for (int i = 0; i < videoPlayers.Count; i++)
        {
            var vp = videoPlayers[i];
            if (!vp) continue;

            vp.playOnAwake = false;
            vp.isLooping = false;
            vp.waitForFirstFrame = true;
            vp.skipOnDrop = true;
            vp.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;

            if (muteVideoAudio)
            {
                // Mute direct audio tracks if any
                try
                {
                    vp.audioOutputMode = VideoAudioOutputMode.Direct;
                    ushort tracks = vp.audioTrackCount;
                    for (ushort t = 0; t < tracks; t++)
                        vp.SetDirectAudioMute(t, true);
                }
                catch
                {
                    // Safe fallback: do nothing
                }
            }
        }
    }

    private void PrepareVideoPlayers()
    {
        for (int i = 0; i < videoPlayers.Count; i++)
        {
            var vp = videoPlayers[i];
            if (!vp) continue;

            if (videoClipOverride != null)
                vp.clip = videoClipOverride;

            vp.Stop();
            vp.Prepare();
        }
    }

    private void ApplyVideoAtMediaTime(float t)
    {
        if (videoPlayers.Count == 0) return;

        float clipLen = longestVideoLength;

        // During holdStart: freeze at first frame (time 0)
        if (t < holdStart)
        {
            SetAllVideoTime(0f, playing: false);
            return;
        }

        float clipTime = t - holdStart;

        // During main clip
        if (clipTime <= clipLen)
        {
            // Keep near-sync without setting time every frame if already close
            SetAllVideoTime(clipTime, playing: true);
            return;
        }

        // During holdEnd: freeze at last frame
        SetAllVideoTime(Mathf.Max(0f, clipLen - 0.02f), playing: false);
    }

    private void SetAllVideoTime(float time, bool playing)
    {
        for (int i = 0; i < videoPlayers.Count; i++)
        {
            var vp = videoPlayers[i];
            if (!vp) continue;

            // If not prepared yet, do minimal setup and try anyway
            if (videoClipOverride != null && vp.clip != videoClipOverride)
                vp.clip = videoClipOverride;

            // Seek only if drift is noticeable
            if (Mathf.Abs((float)vp.time - time) > 0.03f)
            {
                vp.time = time;
            }

            if (playing)
            {
                if (!vp.isPlaying)
                    vp.Play();
            }
            else
            {
                if (vp.isPlaying)
                    vp.Pause();
                else
                    vp.Pause();
            }
        }
    }

    // ---------------------- Audio handling ----------------------

    private void ApplyAudioAtMediaTime(float t)
    {
        if (audioSource == null) return;

        audioSource.volume = muteAudio ? 0f : masterVolume;

        if (timeline.Count == 0)
        {
            if (audioSource.isPlaying)
                audioSource.Stop();
            return;
        }

        // Find active segment by time
        int segIndex = -1;
        for (int i = 0; i < timeline.Count; i++)
        {
            if (t >= timeline[i].startTime && t < timeline[i].endTime)
            {
                segIndex = i;
                break;
            }
        }

        // In silence parts
        if (segIndex < 0)
        {
            if (audioSource.isPlaying)
                audioSource.Pause();
            currentSegmentIndex = -1;
            return;
        }

        var seg = timeline[segIndex];
        float clipTime = t - seg.startTime;

        // If we changed segment, swap clip and play from correct time
        if (segIndex != currentSegmentIndex || audioSource.clip != seg.clip)
        {
            audioSource.clip = seg.clip;
            audioSource.time = Mathf.Clamp(clipTime, 0f, seg.clip.length - 0.01f);
            audioSource.volume = (muteAudio ? 0f : masterVolume) * seg.volume;
            audioSource.Play();
            currentSegmentIndex = segIndex;
            return;
        }

        // Same segment: keep in sync if drift
        if (Mathf.Abs(audioSource.time - clipTime) > 0.05f)
        {
            audioSource.time = Mathf.Clamp(clipTime, 0f, seg.clip.length - 0.01f);
        }

        if (!audioSource.isPlaying)
            audioSource.Play();
    }

    // ---------------------- Replay UI ----------------------

    private void ShowReplay()
    {
        if (replayButton == null) return;
        replayButton.gameObject.SetActive(true);
        UpdateReplayBillboard();
    }

    private void HideReplay()
    {
        if (replayButton == null) return;
        replayButton.gameObject.SetActive(false);
    }

    private void UpdateReplayBillboard()
    {
        if (replayButton == null) return;
        if (!replayButton.gameObject.activeInHierarchy) return;

        Transform cam = replayBillboardCamera != null ? replayBillboardCamera : (Camera.main ? Camera.main.transform : null);
        if (cam == null) return;

        // Position: relative to this object (page root)
        replayButton.transform.position = transform.TransformPoint(replayWorldOffsetLocal);

        // Face camera
        Vector3 toCam = cam.position - replayButton.transform.position;
        if (toCam.sqrMagnitude > 0.0001f)
        {
            replayButton.transform.rotation = Quaternion.LookRotation(toCam.normalized, Vector3.up);
        }
    }

    // ---------------------- Public helpers ----------------------

    public void SetLanguage(string languageName)
    {
        currentLanguage = string.IsNullOrWhiteSpace(languageName) ? currentLanguage : languageName;
        // If currently tracked, restart to apply new language immediately
        if (isTracked)
            RestartFromBeginning();
    }

    public void SetMuteAudio(bool mute)
    {
        muteAudio = mute;
        if (audioSource != null)
            audioSource.volume = muteAudio ? 0f : masterVolume;
    }

    public void SetMasterVolume(float v01)
    {
        masterVolume = Mathf.Clamp01(v01);
        if (audioSource != null)
            audioSource.volume = muteAudio ? 0f : masterVolume;
    }
}
