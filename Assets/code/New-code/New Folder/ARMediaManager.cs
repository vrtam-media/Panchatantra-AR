using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class ARMediaManager : MonoBehaviour
{
    [Header("Database + Audio")]
    public ARAudioLocalizationDatabase audioDatabase;
    public AudioSource globalAudioSource;

    [Header("UI")]
    public Button replayButton;

    [Header("Behavior")]
    public float resumeGraceSeconds = 2f;

    private ARTrackedPageNode _active;
    private float _lostAt = -999f;
    private Coroutine _audioRoutine;
    private bool _paused;

    private bool _audioDone;
    private bool _mainVideosDone;

    private void Awake()
    {
        //ARAudioLocalizationDatabase.LoadGlobalLanguage("English");
        ARAudioLocalizationDatabase.LoadGlobalLanguage(ARGlobalLanguage.Get());


        if (replayButton != null)
        {
            replayButton.onClick.RemoveAllListeners();
            replayButton.onClick.AddListener(Replay);
            replayButton.gameObject.SetActive(false);
        }
    }

    public void OnFound(ARTrackedPageNode node)
    {
        if (node == null) return;

        if (_active != node)
        {
            SwitchActive(node);
            return;
        }

        float lostDuration = Time.time - _lostAt;

        if (lostDuration <= resumeGraceSeconds)
        {
            _paused = false;
            _active.ResumeVisuals();
            ResumeAudio();
        }
        else
        {
            _paused = false;
            RestartFromZero();
        }
    }

    public void OnLost(ARTrackedPageNode node)
    {
        if (node == null) return;
        if (_active != node) return;

        _lostAt = Time.time;
        _paused = true;

        _active.PauseVisuals();
        PauseAudio();
    }

    private void SwitchActive(ARTrackedPageNode next)
    {
        if (_active != null)
        {
            _active.StopVisuals(resetToZero: true);
            _active.SetVisible(false);
        }

        StopAudio();

        _active = next;
        _lostAt = -999f;
        _paused = false;

        _active.SetVisible(true);
        RestartFromZero();
    }

    private void RestartFromZero()
    {
        if (_active == null) return;

        _audioDone = false;
        _mainVideosDone = false;

        if (replayButton != null) replayButton.gameObject.SetActive(false);

        _active.ResetVisualsToZero();
        _active.PlayVisualsFromZeroOnce(() =>
        {
            _mainVideosDone = true;
            TryShowReplay();
        });

        StartAudioFromZero();
    }

    private void Replay()
    {
        if (_active == null) return;
        RestartFromZero();
    }

    private void TryShowReplay()
    {
        if (_audioDone && _mainVideosDone)
        {
            if (replayButton != null) replayButton.gameObject.SetActive(true);
        }
    }

    // ------------ audio ------------
    private void StartAudioFromZero()
    {
        StopAudio();

        if (audioDatabase == null || globalAudioSource == null || _active == null)
        {
            _audioDone = true;
            TryShowReplay();
            return;
        }

        if (!audioDatabase.TryGetPage(ARAudioLocalizationDatabase.CurrentLanguage, _active.pageId, out var page))
        {
            _audioDone = true;
            TryShowReplay();
            return;
        }

        _audioRoutine = StartCoroutine(PlaySegments(page));
    }

    private IEnumerator PlaySegments(ARAudioLocalizationDatabase.Page page)
    {
        if (page.extraStartSilence > 0f)
            yield return WaitSecondsPausable(page.extraStartSilence);

        foreach (var seg in page.segments)
        {
            if (seg == null) continue;

            if (seg.delayBefore > 0f)
                yield return WaitSecondsPausable(seg.delayBefore);

            if (seg.clip == null) continue;

            globalAudioSource.clip = seg.clip;
            globalAudioSource.volume = Mathf.Clamp01(seg.volume);
            globalAudioSource.Play();

            while (globalAudioSource.clip != null && globalAudioSource.time < globalAudioSource.clip.length)
            {
                if (_paused)
                {
                    yield return null;
                    continue;
                }

                // stopped due to switch/restart
                if (!globalAudioSource.isPlaying && globalAudioSource.time <= 0.001f)
                    break;

                yield return null;
            }
        }

        if (page.extraEndSilence > 0f)
            yield return WaitSecondsPausable(page.extraEndSilence);

        _audioDone = true;
        TryShowReplay();
    }

    private IEnumerator WaitSecondsPausable(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            if (!_paused)
                t += Time.deltaTime;

            yield return null;
        }
    }

    private void StopAudio()
    {
        if (_audioRoutine != null)
        {
            StopCoroutine(_audioRoutine);
            _audioRoutine = null;
        }

        if (globalAudioSource != null)
        {
            globalAudioSource.Stop();
            globalAudioSource.clip = null;
        }

        _paused = false;
    }

    private void PauseAudio()
    {
        _paused = true;
        if (globalAudioSource != null)
            globalAudioSource.Pause();
    }

    private void ResumeAudio()
    {
        _paused = false;
        if (globalAudioSource != null && globalAudioSource.clip != null)
            globalAudioSource.UnPause();
    }
}
