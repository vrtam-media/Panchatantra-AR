using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "AR/Audio Localization Database")]
public class ARAudioLocalizationDatabase : ScriptableObject
{
    [Serializable]
    public class AudioSegment
    {
        [Tooltip("Audio clip to play.")]
        public AudioClip clip;

        [Min(0f)]
        [Tooltip("Seconds to wait before playing this clip.")]
        public float delayBefore = 0f;

        [Min(0f)]
        [Tooltip("Seconds to wait after this clip finishes (before next clip).")]
        public float delayAfter = 0f;

        [Range(0f, 2f)]
        [Tooltip("Per-clip volume multiplier. Default 0.6, max 2.")]
        public float volume = 0.6f;
    }

    [Serializable]
    public class PageAudio
    {
        [Tooltip("Must match ARTrackedPageNode Page Id (example: S1_P1_P_3_4).")]
        public string pageId;

        [Header("Voice Clips (sequential)")]
        public List<AudioSegment> voiceClips = new List<AudioSegment>();

        [Header("BGM Clips (sequential)")]
        public List<AudioSegment> bgmClips = new List<AudioSegment>();
    }

    [Serializable]
    public class LanguagePack
    {
        public string languageName = "English";
        public List<PageAudio> pages = new List<PageAudio>();
    }

    [SerializeField] private List<LanguagePack> languagePacks = new List<LanguagePack>();

    // -----------------------------
    // Public API (recommended)
    // -----------------------------
    public bool TryGetPageAudio(string language, string pageId, out PageAudio pageAudio)
    {
        pageAudio = null;
        if (string.IsNullOrWhiteSpace(pageId)) return false;

        // 1) exact language
        var pack = FindLanguage(language);
        if (pack != null)
        {
            pageAudio = FindPage(pack, pageId);
            if (pageAudio != null) return true;
        }

        // 2) fallback English
        var english = FindLanguage("English");
        if (english != null)
        {
            pageAudio = FindPage(english, pageId);
            if (pageAudio != null) return true;
        }

        return false;
    }

    // -----------------------------
    // Compatibility helpers (older scripts)
    // -----------------------------
    public PageAudio GetPage(string language, string pageId)
    {
        TryGetPageAudio(language, pageId, out var page);
        return page;
    }

    public IReadOnlyList<LanguagePack> GetAllLanguagePacks() => languagePacks;

    // -----------------------------
    // Internals
    // -----------------------------
    private LanguagePack FindLanguage(string language)
    {
        if (languagePacks == null) return null;

        // If caller passes empty, treat as English
        if (string.IsNullOrWhiteSpace(language))
            language = "English";

        for (int i = 0; i < languagePacks.Count; i++)
        {
            var p = languagePacks[i];
            if (p == null) continue;

            if (string.Equals(p.languageName, language, StringComparison.OrdinalIgnoreCase))
                return p;
        }

        return null;
    }

    private PageAudio FindPage(LanguagePack pack, string pageId)
    {
        if (pack == null || pack.pages == null) return null;

        for (int i = 0; i < pack.pages.Count; i++)
        {
            var pg = pack.pages[i];
            if (pg == null) continue;

            if (string.Equals(pg.pageId, pageId, StringComparison.Ordinal))
                return pg;
        }

        return null;
    }
}
