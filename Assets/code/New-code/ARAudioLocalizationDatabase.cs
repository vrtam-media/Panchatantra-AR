using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "AR/Audio Localization Database", fileName = "ARAudioLocalizationDatabase")]
public class ARAudioLocalizationDatabase : ScriptableObject
{
    [Serializable]
    public class Segment
    {
        public AudioClip clip;
        [Tooltip("Seconds of silence before this clip starts.")]
        public float delayBefore = 0f;

        [Range(0f, 1f)]
        public float volume = 1f;
    }

    [Serializable]
    public class PageAudio
    {
        public string pageId;

        [Tooltip("One page can have 1, 2, or 3 clips. They play in order.")]
        public List<Segment> segments = new List<Segment>();

        [Tooltip("Extra silence added at the start of the whole page audio timeline.")]
        public float extraStartSilence = 0f;

        [Tooltip("Extra silence added at the end of the whole page audio timeline.")]
        public float extraEndSilence = 0f;

        [Header("Optional per-page override for video holds")]
        public bool overrideVideoHolds = false;
        public float videoHoldStart = 0f;
        public float videoHoldEnd = 0f;
    }

    [Serializable]
    public class LanguagePack
    {
        public string languageName = "English";
        public List<PageAudio> pages = new List<PageAudio>();
    }

    [Header("Languages")]
    public List<LanguagePack> languages = new List<LanguagePack>();

    [Header("Fallback")]
    public string fallbackLanguage = "English";

    public bool TryGetPage(string language, string pageId, out PageAudio page)
    {
        page = null;

        if (string.IsNullOrWhiteSpace(pageId))
            return false;

        // 1) exact language
        if (TryGetPageInternal(language, pageId, out page))
            return true;

        // 2) fallback language
        if (!string.IsNullOrWhiteSpace(fallbackLanguage) && TryGetPageInternal(fallbackLanguage, pageId, out page))
            return true;

        // 3) any language (last resort)
        for (int i = 0; i < languages.Count; i++)
        {
            var lp = languages[i];
            if (lp == null) continue;

            for (int p = 0; p < lp.pages.Count; p++)
            {
                if (lp.pages[p] != null && string.Equals(lp.pages[p].pageId, pageId, StringComparison.OrdinalIgnoreCase))
                {
                    page = lp.pages[p];
                    return true;
                }
            }
        }

        return false;
    }

    private bool TryGetPageInternal(string language, string pageId, out PageAudio page)
    {
        page = null;
        if (string.IsNullOrWhiteSpace(language))
            return false;

        for (int i = 0; i < languages.Count; i++)
        {
            var lp = languages[i];
            if (lp == null) continue;

            if (!string.Equals(lp.languageName, language, StringComparison.OrdinalIgnoreCase))
                continue;

            for (int p = 0; p < lp.pages.Count; p++)
            {
                if (lp.pages[p] == null) continue;
                if (string.Equals(lp.pages[p].pageId, pageId, StringComparison.OrdinalIgnoreCase))
                {
                    page = lp.pages[p];
                    return true;
                }
            }

            return false;
        }

        return false;
    }

    public float ComputeTotalDuration(PageAudio page)
    {
        if (page == null) return 0f;

        float t = 0f;
        t += Mathf.Max(0f, page.extraStartSilence);

        for (int i = 0; i < page.segments.Count; i++)
        {
            var seg = page.segments[i];
            if (seg == null) continue;

            t += Mathf.Max(0f, seg.delayBefore);
            if (seg.clip != null)
                t += Mathf.Max(0f, (float)seg.clip.length);
        }

        t += Mathf.Max(0f, page.extraEndSilence);
        return t;
    }
}
