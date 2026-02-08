using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class ARMediaManager : MonoBehaviour
{
    [Header("Global UI")]
    [SerializeField] private Button replayButton;

    [Header("Audio")]
    [SerializeField] private ARAudioLocalizationDatabase audioDatabase;
    [SerializeField] private AudioSource voiceSource;
    [SerializeField] private AudioSource bgmSource;

    [Range(0f, 2f)]
    [SerializeField] private float defaultVoiceVolume = 0.6f;

    [Range(0f, 2f)]
    [SerializeField] private float defaultBgmVolume = 0.6f;

    [Header("Behavior")]
    [Min(0f)]
    [SerializeField] private float resumeGraceSeconds = 2f;

    [SerializeField] private string defaultLanguage = "English";

    private ARTrackedPageNode _activePage;

    private Coroutine _lostGraceRoutine;
    private Coroutine _voiceRoutine;

    private bool _audioPaused;
    private bool _voiceFinished;

    private void Awake()
    {
        if (replayButton != null)
        {
            replayButton.onClick.RemoveListener(OnReplayClicked);
            replayButton.onClick.AddListener(OnReplayClicked);
            replayButton.gameObject.SetActive(false);
        }

        if (voiceSource == null)
            voiceSource = GetComponent<AudioSource>();

        if (voiceSource == null)
            voiceSource = gameObject.AddComponent<AudioSource>();

        voiceSource.playOnAwake = false;
        voiceSource.loop = false;
        voiceSource.volume = Mathf.Clamp(defaultVoiceVolume, 0f, 2f);

        if (bgmSource == null)
            bgmSource = gameObject.AddComponent<AudioSource>();

        bgmSource.playOnAwake = false;
        bgmSource.loop = true;
        bgmSource.volume = Mathf.Clamp(defaultBgmVolume, 0f, 2f);
    }

    private void Update()
    {
        if (_activePage == null) return;

        // Never show replay while tracking is lost
        if (!_activePage.IsTracked)
        {
            if (replayButton != null) replayButton.gameObject.SetActive(false);
            return;
        }

        bool visualsDone = _activePage.IsVisualFinished();
        bool audioDone = _voiceFinished;

        if (visualsDone && audioDone)
        {
            if (replayButton != null) replayButton.gameObject.SetActive(true);
        }
        else
        {
            if (replayButton != null) replayButton.gameObject.SetActive(false);
        }
    }

    public void HandlePageFound(ARTrackedPageNode page)
    {
        if (page == null) return;

        if (_activePage != page)
        {
            StopAllForCurrentPage();
            _activePage = page;
        }

        if (_lostGraceRoutine != null)
        {
            StopCoroutine(_lostGraceRoutine);
            _lostGraceRoutine = null;
        }

        if (replayButton != null) replayButton.gameObject.SetActive(false);

        bool resume = (Time.time - page.LastLostTime) <= resumeGraceSeconds;

        if (resume)
        {
            page.ResumeAll();
            ResumeAudio();
        }
        else
        {
            page.RestartAllPageContent();
            StartAudioForPage(page);
        }
    }

    public void HandlePageLost(ARTrackedPageNode page)
    {
        if (page == null) return;
        if (_activePage != page) return;

        if (replayButton != null) replayButton.gameObject.SetActive(false);

        page.PauseAll();
        PauseAudio();

        if (_lostGraceRoutine != null) StopCoroutine(_lostGraceRoutine);
        _lostGraceRoutine = StartCoroutine(LostGraceThenReset(page));
    }

    private IEnumerator LostGraceThenReset(ARTrackedPageNode page)
    {
        float t = 0f;
        while (t < resumeGraceSeconds)
        {
            t += Time.deltaTime;
            yield return null;
        }

        if (_activePage == page && !page.IsTracked)
        {
            StopAllForCurrentPage();
            page.StopAndResetAll();
        }

        _lostGraceRoutine = null;
    }

    private void OnReplayClicked()
    {
        if (_activePage == null) return;

        if (replayButton != null) replayButton.gameObject.SetActive(false);

        _activePage.RestartAllPageContent();
        StartAudioForPage(_activePage);
    }

    private void StopAllForCurrentPage()
    {
        if (_voiceRoutine != null)
        {
            StopCoroutine(_voiceRoutine);
            _voiceRoutine = null;
        }

        if (voiceSource != null) voiceSource.Stop();
        if (bgmSource != null) bgmSource.Stop();

        _audioPaused = false;
        _voiceFinished = false;
    }

    private void StartAudioForPage(ARTrackedPageNode page)
    {
        _voiceFinished = false;
        _audioPaused = false;

        if (audioDatabase == null)
        {
            _voiceFinished = true;
            return;
        }

        string lang = ARGlobalLanguage.GetLanguageOrDefault(defaultLanguage);

        if (!audioDatabase.TryGetPageAudio(lang, page.PageId, out var pageAudio) || pageAudio == null)
        {
            _voiceFinished = true;
            return;
        }

        // BGM
        if (pageAudio.bgmClips != null && pageAudio.bgmClips.Count > 0)
        {
            var seg = pageAudio.bgmClips[0];
            if (seg.clip != null)
            {
                bgmSource.clip = seg.clip;
                bgmSource.volume = Mathf.Clamp(seg.volume > 0f ? seg.volume : defaultBgmVolume, 0f, 2f);
                bgmSource.loop = page.LoopBgmUntilVoiceEnds;
                bgmSource.Play();
            }
        }
        else
        {
            bgmSource.Stop();
        }

        if (_voiceRoutine != null) StopCoroutine(_voiceRoutine);
        _voiceRoutine = StartCoroutine(PlayVoiceSequence(page, pageAudio));
    }

    private IEnumerator PlayVoiceSequence(ARTrackedPageNode page, ARAudioLocalizationDatabase.PageAudio pageAudio)
    {
        if (pageAudio.voiceClips == null || pageAudio.voiceClips.Count == 0)
        {
            _voiceFinished = true;
            yield break;
        }

        for (int i = 0; i < pageAudio.voiceClips.Count; i++)
        {
            var seg = pageAudio.voiceClips[i];
            if (seg.clip == null) continue;

            yield return WaitSecondsPausable(seg.delayBefore);

            voiceSource.clip = seg.clip;
            voiceSource.volume = Mathf.Clamp(seg.volume > 0f ? seg.volume : defaultVoiceVolume, 0f, 2f);
            voiceSource.Play();

            while (voiceSource.isPlaying)
            {
                while (_audioPaused) yield return null;
                yield return null;
            }

            yield return WaitSecondsPausable(seg.delayAfter);
        }

        _voiceFinished = true;

        if (page != null && page.StopBgmWhenVoiceEnds)
            bgmSource.Stop();
    }

    private IEnumerator WaitSecondsPausable(float seconds)
    {
        float wait = Mathf.Max(0f, seconds);
        float t = 0f;
        while (t < wait)
        {
            if (!_audioPaused)
                t += Time.deltaTime;
            yield return null;
        }
    }

    private void PauseAudio()
    {
        _audioPaused = true;
        if (voiceSource != null && voiceSource.isPlaying) voiceSource.Pause();
        if (bgmSource != null && bgmSource.isPlaying) bgmSource.Pause();
    }

    private void ResumeAudio()
    {
        _audioPaused = false;
        if (voiceSource != null) voiceSource.UnPause();
        if (bgmSource != null) bgmSource.UnPause();
    }
}
