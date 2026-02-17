using System.Collections;
using UnityEngine;
using UnityEngine.Video;
using Vuforia;

[RequireComponent(typeof(VideoPlayer))]
public class VuforiaVideoFreezeSystem : MonoBehaviour
{
    [Header("Video")]
    public VideoClip videoClip;

    [Header("Vuforia")]
    public ObserverBehaviour vuforiaObserver;
    public bool treatExtendedTrackedAsTracked = true;

    [Header("Loop")]
    public bool loopEveryTime = true;
    public bool freezeFirstEveryLoop = true;
    public bool freezeLastEveryLoop = true;

    [System.Serializable]
    public class FreezeBlock
    {
        public bool enabled = false;
        public AudioClip audio;
        [Range(0f, 1f)] public float volume = 1f;
        public AudioSource source; // optional
    }

    [Header("Freeze First")]
    public FreezeBlock freezeFirst = new FreezeBlock();

    [Header("Freeze Last")]
    public FreezeBlock freezeLast = new FreezeBlock();

    [Header("Global Audio")]
    public bool muteAll = false;

    [Header("Video Audio Control (optional)")]
    public bool controlVideoAudio = true;
    [Range(0f, 1f)] public float videoAudioVolume = 1f;
    public AudioSource videoAudioSource;

    private VideoPlayer vp;
    private bool isTracked = false;
    private Coroutine flowRoutine;

    // frameReady sync (for TRUE first frame)
    private bool waitingFrame = false;
    private long targetFrame = 0;
    private bool frameReached = false;

    void Awake()
    {
        vp = GetComponent<VideoPlayer>();

        // IMPORTANT: avoid conflicts with our script
        vp.playOnAwake = false;
        vp.isLooping = false;                 // ✅ script controls looping
        vp.waitForFirstFrame = true;
        vp.sendFrameReadyEvents = true;
        vp.frameReady += OnFrameReady;

        vp.source = VideoSource.VideoClip;
        vp.clip = videoClip;

        if (vuforiaObserver == null)
            vuforiaObserver = GetComponentInParent<ObserverBehaviour>();

        EnsureAudioSources();
        SetupVideoAudioRouting();
        ApplyVolumes();

        PauseAll();
    }

    void OnDestroy()
    {
        if (vp != null) vp.frameReady -= OnFrameReady;
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

    void Update() => ApplyVolumes();

    private void OnTargetStatusChanged(ObserverBehaviour ob, TargetStatus status)
    {
        bool tracked =
            status.Status == Status.TRACKED ||
            (treatExtendedTrackedAsTracked && status.Status == Status.EXTENDED_TRACKED);

        if (tracked == isTracked) return;
        isTracked = tracked;

        if (!isTracked)
        {
            PauseAll();
            return;
        }

        // tracking found
        if (flowRoutine == null)
            flowRoutine = StartCoroutine(FlowLoop());
        else
            ResumeAllAudio(); // video resume is controlled by coroutine state
    }

    private IEnumerator FlowLoop()
    {
        while (true)
        {
            while (!isTracked) yield return null;

            vp.clip = videoClip;
            vp.Prepare();
            while (!vp.isPrepared) yield return null;

            // ---- FIRST FREEZE (every loop) ----
            if (freezeFirstEveryLoop && freezeFirst.enabled && freezeFirst.audio != null)
                yield return FreezeOnExactFirstFrame(freezeFirst);

            // ---- PLAY VIDEO FROM START ----
            vp.time = 0;
            vp.Play();
            UnpauseVideoAudio();

            // ---- WAIT UNTIL VIDEO ENDS (time-based, reliable) ----
            yield return WaitUntilEndByTime();

            // ---- LAST FREEZE (every loop) ----
            if (freezeLastEveryLoop && freezeLast.enabled && freezeLast.audio != null)
                yield return FreezeOnLastFrameByTime(freezeLast);

            if (!loopEveryTime)
            {
                PauseAll();
                flowRoutine = null;
                yield break;
            }

            // next cycle repeats
            vp.time = 0;
        }
    }

    private IEnumerator WaitUntilEndByTime()
    {
        while (true)
        {
            while (!isTracked) yield return null;

            // If still playing, continue
            if (vp.isPlaying)
            {
                yield return null;
                continue;
            }

            // When it stops, confirm it's actually near end
            if (vp.isPrepared && vp.length > 0.1f && vp.time >= vp.length - 0.05f)
                yield break;

            yield return null;
        }
    }

    // ---- Freeze 1: Guaranteed FIRST frame (frameReady) ----
    private IEnumerator FreezeOnExactFirstFrame(FreezeBlock block)
    {
        waitingFrame = true;
        frameReached = false;
        targetFrame = 0;

        vp.frame = 0;
        vp.Play();

        while (!frameReached)
        {
            if (!isTracked)
            {
                PauseAll();
                while (!isTracked) yield return null;
                vp.Play();
            }
            yield return null;
        }

        vp.Pause();
        yield return PlayFreezeAudio(block);
    }

    private void OnFrameReady(VideoPlayer source, long frameIdx)
    {
        if (!waitingFrame) return;

        if (targetFrame == 0 && frameIdx <= 1)
        {
            frameReached = true;
            waitingFrame = false;
        }
    }

    // ---- Freeze 2: LAST frame (time seek to end, reliable) ----
    private IEnumerator FreezeOnLastFrameByTime(FreezeBlock block)
    {
        // jump to end and freeze
        vp.time = vp.length;
        vp.Play();
        yield return null; // allow render
        vp.Pause();

        yield return PlayFreezeAudio(block);
    }

    private IEnumerator PlayFreezeAudio(FreezeBlock block)
    {
        if (block.source == null)
            block.source = gameObject.AddComponent<AudioSource>();

        block.source.clip = block.audio;
        block.source.time = 0f;
        block.source.Play();

        while (block.source.isPlaying)
        {
            if (!isTracked)
            {
                PauseAll();
                while (!isTracked) yield return null;
                block.source.UnPause();
            }
            yield return null;
        }
    }

    // ---------------- Audio setup ----------------
    private void EnsureAudioSources()
    {
        if (freezeFirst.source == null && freezeFirst.enabled) freezeFirst.source = gameObject.AddComponent<AudioSource>();
        if (freezeLast.source == null && freezeLast.enabled) freezeLast.source = gameObject.AddComponent<AudioSource>();
    }

    private void SetupVideoAudioRouting()
    {
        if (!controlVideoAudio)
        {
            vp.audioOutputMode = VideoAudioOutputMode.Direct;
            return;
        }

        if (videoAudioSource == null)
            videoAudioSource = gameObject.AddComponent<AudioSource>();

        vp.audioOutputMode = VideoAudioOutputMode.AudioSource;
        vp.EnableAudioTrack(0, true);
        vp.SetTargetAudioSource(0, videoAudioSource);
    }

    private void ApplyVolumes()
    {
        float g = muteAll ? 0f : 1f;

        if (freezeFirst.source != null) freezeFirst.source.volume = g * freezeFirst.volume;
        if (freezeLast.source != null) freezeLast.source.volume = g * freezeLast.volume;
        if (videoAudioSource != null) videoAudioSource.volume = g * videoAudioVolume;
    }

    // ---------------- Pause / Resume ----------------
    private void PauseAll()
    {
        if (vp.isPlaying) vp.Pause();

        if (freezeFirst.source != null && freezeFirst.source.isPlaying) freezeFirst.source.Pause();
        if (freezeLast.source != null && freezeLast.source.isPlaying) freezeLast.source.Pause();

        if (videoAudioSource != null && videoAudioSource.isPlaying) videoAudioSource.Pause();
    }

    private void ResumeAllAudio()
    {
        if (freezeFirst.source != null) freezeFirst.source.UnPause();
        if (freezeLast.source != null) freezeLast.source.UnPause();
        UnpauseVideoAudio();
    }

    private void UnpauseVideoAudio()
    {
        if (videoAudioSource != null) videoAudioSource.UnPause();
    }
}
