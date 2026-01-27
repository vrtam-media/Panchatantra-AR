using UnityEngine;
using UnityEngine.Video;
using UnityEngine.UI;
using Vuforia;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class ARPageMediaController : MonoBehaviour
{
    [Header("Tracking (Vuforia)")]
    [Tooltip("Assign ImageTarget's ObserverBehaviour here (ImageTarget Behaviour).")]
    public ObserverBehaviour imageTarget;

    [Tooltip("If tracking comes back within this time, resume. If later, restart.")]
    public float resumeGraceSeconds = 1f;

    [Tooltip("Disable visuals when not tracked.")]
    public bool hideRenderersWhenNotTracked = true;

    [Header("Parallax (optional)")]
    public ParallaxLayerStack parallaxStack;

    [Header("Video (one or many players)")]
    [Tooltip("Add ALL VideoPlayers on this page (can be 1 or more).")]
    public List<VideoPlayer> videoPlayers = new List<VideoPlayer>();

    [Tooltip("Mute video audio so ONLY localized audio plays.")]
    public bool muteVideoAudio = true;

    [Header("Audio (localized)")]
    public ARAudioLocalizationDatabase audioDatabase;
    public AudioSource audioSource;

    [Tooltip("Must match the Page Id in the database exactly.")]
    public string pageId;

    [Tooltip("Overall audio volume multiplier.")]
    [Range(0f, 1f)]
    public float masterVolume = 1f;

    [Tooltip("Mute localized audio (debug).")]
    public bool muteAudio = false;

    [Header("Replay UI (Screen Space Overlay)")]
    [Tooltip("Assign the UI Button. It will be hidden until media finishes.")]
    public Button replayButton;

    [Header("Language UI (Button + Dropdown)")]
    [Tooltip("Button at top-left that opens/closes the dropdown panel.")]
    public Button languageButton;

    [Tooltip("Panel GameObject that contains the dropdown. We toggle it ON/OFF.")]
    public GameObject languagePanel;

    [Tooltip("Dropdown listing languages from the database.")]
    public Dropdown languageDropdown;

    // tracking state
    private bool isTracked;
    private float lostAtTime;

    // completion state
    private int finishedVideos;
    private bool audioFinished;

    // audio state machine
    private ARAudioLocalizationDatabase.Page activePage;
    private int segIndex;
    private float delayRemaining;
    private bool audioRunning;

    // renderers cache
    private Renderer[] cachedRenderers;

    void Awake()
    {
        cachedRenderers = GetComponentsInChildren<Renderer>(true);

        if (replayButton != null)
            replayButton.gameObject.SetActive(false);

        if (languagePanel != null)
            languagePanel.SetActive(false);
    }

    void OnEnable()
    {
        if (imageTarget != null)
            imageTarget.OnTargetStatusChanged += OnTargetStatusChanged;
    }

    void OnDisable()
    {
        if (imageTarget != null)
            imageTarget.OnTargetStatusChanged -= OnTargetStatusChanged;
    }

    void Start()
    {
        SetupReplayButton();
        SetupLanguageUI();
        SetupVideos();

        // Start in paused/off until tracked
        SetTracked(false);
    }

    void SetupReplayButton()
    {
        if (replayButton == null) return;

        replayButton.onClick.RemoveAllListeners();
        replayButton.onClick.AddListener(ReplayAll);
        replayButton.gameObject.SetActive(false);
    }

    void SetupLanguageUI()
    {
        if (languageButton != null)
        {
            languageButton.onClick.RemoveAllListeners();
            languageButton.onClick.AddListener(ToggleLanguagePanel);
        }

        if (audioDatabase == null || languageDropdown == null) return;

        var langs = audioDatabase.GetLanguageNames();
        languageDropdown.ClearOptions();
        languageDropdown.AddOptions(langs);

        int idx = Mathf.Max(0, langs.IndexOf(ARAudioLocalizationDatabase.CurrentLanguage));
        languageDropdown.SetValueWithoutNotify(idx);

        languageDropdown.onValueChanged.RemoveAllListeners();
        languageDropdown.onValueChanged.AddListener(OnLanguageSelected);
    }

    void ToggleLanguagePanel()
    {
        if (languagePanel == null) return;
        languagePanel.SetActive(!languagePanel.activeSelf);
    }

    void OnLanguageSelected(int index)
    {
        if (audioDatabase == null) return;

        var langs = audioDatabase.GetLanguageNames();
        if (index < 0 || index >= langs.Count) return;

        ARAudioLocalizationDatabase.CurrentLanguage = langs[index];

        if (languagePanel != null)
            languagePanel.SetActive(false);

        // For clarity: restart everything in the new language
        RestartFromStart();
    }

    void SetupVideos()
    {
        for (int i = 0; i < videoPlayers.Count; i++)
        {
            VideoPlayer vp = videoPlayers[i];
            if (vp == null) continue;

            vp.playOnAwake = false;
            vp.isLooping = false;
            vp.waitForFirstFrame = true;
            vp.skipOnDrop = true;

            if (muteVideoAudio)
                vp.audioOutputMode = VideoAudioOutputMode.None;

            vp.loopPointReached -= OnVideoCompleted;
            vp.loopPointReached += OnVideoCompleted;

            vp.errorReceived -= OnVideoError;
            vp.errorReceived += OnVideoError;

            vp.prepareCompleted -= OnVideoPrepared;
            vp.prepareCompleted += OnVideoPrepared;
        }
    }

    void OnVideoError(VideoPlayer source, string message)
    {
        Debug.LogWarning("VideoPlayer error: " + message, source);
    }

    void OnVideoPrepared(VideoPlayer vp)
    {
        // Only auto-play when tracked
        if (isTracked)
            vp.Play();
    }

    void OnVideoCompleted(VideoPlayer vp)
    {
        // Force last frame hold (avoid jumping to first frame)
        try
        {
            if (vp.length > 0.01)
            {
                vp.time = vp.length - 0.03;
                vp.Pause();
            }
        }
        catch { }

        finishedVideos++;
        CheckReplayAvailability();
    }

    void OnTargetStatusChanged(ObserverBehaviour obs, TargetStatus status)
    {
        bool trackedNow =
            status.Status == Status.TRACKED ||
            status.Status == Status.EXTENDED_TRACKED;

        SetTracked(trackedNow);
    }

    void SetTracked(bool trackedNow)
    {
        if (trackedNow == isTracked) return;

        isTracked = trackedNow;

        if (!isTracked)
        {
            lostAtTime = Time.time;

            PauseVideos();
            PauseAudio();

            if (hideRenderersWhenNotTracked)
                SetRenderersVisible(false);

            if (replayButton != null)
                replayButton.gameObject.SetActive(false);
        }
        else
        {
            if (hideRenderersWhenNotTracked)
                SetRenderersVisible(true);

            // Resume or restart based on grace
            float lostDuration = Time.time - lostAtTime;

            if (lostDuration <= resumeGraceSeconds)
                ResumeFromPaused();
            else
                RestartFromStart();
        }
    }

    void SetRenderersVisible(bool visible)
    {
        if (cachedRenderers == null) return;
        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            if (cachedRenderers[i] != null)
                cachedRenderers[i].enabled = visible;
        }
    }

    void ResumeFromPaused()
    {
        ResumeVideos();
        ResumeAudio();
    }

    void RestartFromStart()
    {
        if (replayButton != null)
            replayButton.gameObject.SetActive(false);

        finishedVideos = 0;
        audioFinished = false;

        StartVideosFromBeginning();
        StartAudioFromBeginning();
    }

    void ReplayAll()
    {
        RestartFromStart();
    }

    void PauseVideos()
    {
        for (int i = 0; i < videoPlayers.Count; i++)
        {
            var vp = videoPlayers[i];
            if (vp == null) continue;
            if (vp.isPlaying) vp.Pause();
        }
    }

    void ResumeVideos()
    {
        for (int i = 0; i < videoPlayers.Count; i++)
        {
            var vp = videoPlayers[i];
            if (vp == null) continue;

            // If video ended already, do not restart on resume
            if (vp.length > 0.01 && vp.time >= vp.length - 0.05) continue;

            if (!vp.isPrepared)
            {
                vp.Prepare();
            }
            else
            {
                vp.Play();
            }
        }
    }

    void StartVideosFromBeginning()
    {
        for (int i = 0; i < videoPlayers.Count; i++)
        {
            var vp = videoPlayers[i];
            if (vp == null) continue;

            vp.Stop();
            vp.time = 0;

            if (!vp.isPrepared)
                vp.Prepare();
            else
                vp.Play();
        }
    }

    void StartAudioFromBeginning()
    {
        audioRunning = false;
        audioFinished = false;

        if (audioSource != null)
        {
            audioSource.Stop();
            audioSource.mute = muteAudio;
            audioSource.volume = masterVolume;
        }

        if (audioDatabase == null || audioSource == null)
        {
            audioFinished = true;
            CheckReplayAvailability();
            return;
        }

        bool ok = audioDatabase.TryGetPage(ARAudioLocalizationDatabase.CurrentLanguage, pageId, out activePage);
        if (!ok || activePage == null)
        {
            audioFinished = true;
            CheckReplayAvailability();
            return;
        }

        segIndex = -1;
        delayRemaining = Mathf.Max(0f, activePage.extraStartSilence);
        audioRunning = true;
    }

    void PauseAudio()
    {
        if (audioSource == null) return;
        if (audioSource.isPlaying) audioSource.Pause();
    }

    void ResumeAudio()
    {
        if (!audioRunning) return;
        if (audioSource == null) return;

        // If currently in a delay, no need to unpause
        if (audioSource.clip != null && audioSource.time > 0f)
            audioSource.UnPause();
    }

    void Update()
    {
        if (!isTracked) return;

        // Parallax should update only when tracked
        // (ParallaxLayerStack already uses camera, but this avoids "parallax moving while hidden")
        if (parallaxStack != null)
            parallaxStack.enableParallax = true;

        TickAudio(Time.deltaTime);
    }

    void TickAudio(float dt)
    {
        if (!audioRunning) return;
        if (audioFinished) return;
        if (audioSource == null) return;
        if (activePage == null) { audioFinished = true; CheckReplayAvailability(); return; }

        // If we are delaying before next segment
        if (delayRemaining > 0f)
        {
            delayRemaining -= dt;
            if (delayRemaining > 0f) return;

            // Delay finished, advance to next segment
            AdvanceToNextSegment();
            return;
        }

        // If a clip is playing, wait for it to end
        if (audioSource.isPlaying) return;

        // Clip ended or nothing playing
        if (audioSource.clip != null && audioSource.time > 0f && audioSource.time < audioSource.clip.length - 0.01f)
        {
            // tiny gap case, ignore
            return;
        }

        // After segment completed, advance
        AdvanceToNextSegment();
    }

    void AdvanceToNextSegment()
    {
        segIndex++;

        // End of segments, apply end silence then finish
        if (segIndex >= activePage.segments.Count)
        {
            float endSilence = Mathf.Max(0f, activePage.extraEndSilence);
            if (endSilence > 0f)
            {
                delayRemaining = endSilence;
                // mark finished after this delay ends
                audioRunning = false;
                Invoke(nameof(FinishAudioAfterEndSilence), endSilence);
            }
            else
            {
                audioFinished = true;
                CheckReplayAvailability();
            }
            return;
        }

        var seg = activePage.segments[segIndex];
        if (seg == null)
        {
            AdvanceToNextSegment();
            return;
        }

        delayRemaining = Mathf.Max(0f, seg.delayBefore);
        if (delayRemaining > 0f)
            return;

        PlaySegment(seg);
    }

    void FinishAudioAfterEndSilence()
    {
        audioFinished = true;
        CheckReplayAvailability();
    }

    void PlaySegment(ARAudioLocalizationDatabase.Segment seg)
    {
        if (seg.clip == null)
        {
            AdvanceToNextSegment();
            return;
        }

        audioSource.clip = seg.clip;
        audioSource.time = 0f;
        audioSource.volume = masterVolume;
        audioSource.mute = muteAudio;

        audioSource.Play();
    }

    void CheckReplayAvailability()
    {
        // Video must finish AND audio must finish
        bool videosDone = (finishedVideos >= videoPlayers.Count);
        bool audioDone = audioFinished;

        if (videosDone && audioDone)
        {
            if (replayButton != null)
                replayButton.gameObject.SetActive(true);
        }
    }
}
