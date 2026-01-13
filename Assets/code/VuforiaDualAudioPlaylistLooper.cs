using UnityEngine;
using Vuforia;

public class VuforiaDualAudioPlaylistLooper : MonoBehaviour
{
    [System.Serializable]
    public class ClipWithDelay
    {
        public AudioClip clip;

        [Range(0f, 1f)]
        public float volume = 1f;     // ✅ per-clip volume

        [Min(0f)]
        public float delayAfter = 0f; // delay after this clip ends
    }

    [Header("Vuforia (ImageTarget)")]
    public ObserverBehaviour vuforiaObserver;

    [Header("VOICE (sequential playlist)")]
    public AudioSource voiceSource;
    [Range(0f, 1f)] public float voiceMasterVolume = 1f; // overall voice control
    public ClipWithDelay[] voicePlaylist;

    [Header("BGM (sequential playlist, parallel to voice)")]
    public AudioSource bgmSource;
    [Range(0f, 1f)] public float bgmMasterVolume = 0.6f; // overall bgm control
    public ClipWithDelay[] bgmPlaylist;

    [Header("Options")]
    public bool startOnlyWhenTracked = true;
    public bool treatExtendedTrackedAsTracked = true;

    private class PlaylistState
    {
        public AudioSource source;
        public ClipWithDelay[] list;

        public int index = 0;
        public bool hasStarted = false;

        public bool clipIsRunning = false;

        public bool waitingDelay = false;
        public float delayRemaining = 0f;
    }

    private PlaylistState voice = new PlaylistState();
    private PlaylistState bgm = new PlaylistState();

    private bool isTracked = false;

    void Awake()
    {
        if (vuforiaObserver == null)
            vuforiaObserver = GetComponent<ObserverBehaviour>();

        if (voiceSource == null) voiceSource = gameObject.AddComponent<AudioSource>();
        if (bgmSource == null) bgmSource = gameObject.AddComponent<AudioSource>();

        // We handle looping via playlist, not AudioSource.loop
        voiceSource.loop = false;
        bgmSource.loop = false;

        voice.source = voiceSource;
        voice.list = voicePlaylist;

        bgm.source = bgmSource;
        bgm.list = bgmPlaylist;

        if (startOnlyWhenTracked)
        {
            voiceSource.playOnAwake = false;
            bgmSource.playOnAwake = false;
            isTracked = false;
        }
        else
        {
            isTracked = true;
        }
    }

    void OnEnable()
    {
        if (vuforiaObserver != null)
            vuforiaObserver.OnTargetStatusChanged += OnTargetStatusChanged;

        if (!startOnlyWhenTracked)
            StartOrResumeAll();
        else
            PauseAll();
    }

    void OnDisable()
    {
        if (vuforiaObserver != null)
            vuforiaObserver.OnTargetStatusChanged -= OnTargetStatusChanged;
    }

    void Update()
    {
        if (!isTracked) return;

        voice.list = voicePlaylist;
        bgm.list = bgmPlaylist;

        TickPlaylist(voice, voiceMasterVolume);
        TickPlaylist(bgm, bgmMasterVolume);
    }

    private void OnTargetStatusChanged(ObserverBehaviour ob, TargetStatus status)
    {
        bool tracked = status.Status == Status.TRACKED ||
                       (treatExtendedTrackedAsTracked && status.Status == Status.EXTENDED_TRACKED);

        if (tracked == isTracked) return;

        isTracked = tracked;

        if (isTracked) StartOrResumeAll();
        else PauseAll();
    }

    private void StartOrResumeAll()
    {
        if (voice.source != null) voice.source.UnPause();
        if (bgm.source != null) bgm.source.UnPause();

        EnsureStarted(voice, voiceMasterVolume);
        EnsureStarted(bgm, bgmMasterVolume);
    }

    private void PauseAll()
    {
        if (voice.source != null && voice.source.isPlaying) voice.source.Pause();
        if (bgm.source != null && bgm.source.isPlaying) bgm.source.Pause();
    }

    private void EnsureStarted(PlaylistState p, float masterVol)
    {
        if (p.source == null) return;
        if (p.list == null || p.list.Length == 0) return;

        if (!p.hasStarted)
        {
            p.hasStarted = true;
            p.index = Mathf.Clamp(p.index, 0, p.list.Length - 1);
            PlayCurrentClip(p, masterVol);
        }
        else
        {
            if (p.source.clip == null && !p.waitingDelay)
                PlayCurrentClip(p, masterVol);
        }
    }

    private void TickPlaylist(PlaylistState p, float masterVol)
    {
        if (p.source == null) return;
        if (p.list == null || p.list.Length == 0) return;

        if (p.waitingDelay)
        {
            p.delayRemaining -= Time.deltaTime;
            if (p.delayRemaining <= 0f)
            {
                p.waitingDelay = false;
                p.delayRemaining = 0f;
                AdvanceAndPlay(p, masterVol);
            }
            return;
        }

        if (p.source.isPlaying)
        {
            p.clipIsRunning = true;
            return;
        }

        if (p.clipIsRunning)
        {
            p.clipIsRunning = false;

            float delay = p.list[p.index].delayAfter;
            if (delay > 0f)
            {
                p.waitingDelay = true;
                p.delayRemaining = delay;
            }
            else
            {
                AdvanceAndPlay(p, masterVol);
            }
            return;
        }

        // Edge case: clip stopped externally
        EnsureStarted(p, masterVol);
    }

    private void AdvanceAndPlay(PlaylistState p, float masterVol)
    {
        p.index = (p.index + 1) % p.list.Length;
        PlayCurrentClip(p, masterVol);
    }

    private void PlayCurrentClip(PlaylistState p, float masterVol)
    {
        var item = p.list[p.index];
        if (item.clip == null) return;

        // ✅ per-clip volume × master volume
        p.source.volume = Mathf.Clamp01(item.volume) * Mathf.Clamp01(masterVol);

        p.source.clip = item.clip;
        p.source.time = 0f;

        if (isTracked)
        {
            p.source.Play();
            p.clipIsRunning = true;
        }
    }
}
