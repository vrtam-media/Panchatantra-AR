using UnityEngine;
using System;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "AR/Audio Localization Database", fileName = "ARAudioLocalizationDatabase")]
public class ARAudioLocalizationDatabase : ScriptableObject
{
    public static string CurrentLanguage = "English";

    [Serializable]
    public class Segment
    {
        public AudioClip clip;
        [Tooltip("Seconds before this segment starts")]
        public float delayBefore = 0f;
        [Range(0f, 1f)]
        public float volume = 1f;
    }

    [Serializable]
    public class Page
    {
        public string pageId;
        public List<Segment> segments = new List<Segment>();
        public float extraStartSilence = 0f;
        public float extraEndSilence = 0f;
    }

    [Serializable]
    public class Language
    {
        public string languageName = "English";
        public List<Page> pages = new List<Page>();
    }

    public List<Language> languages = new List<Language>();
    public string fallbackLanguage = "English";

    public bool TryGetPage(string languageName, string pageId, out Page page)
    {
        page = null;
        if (string.IsNullOrEmpty(pageId)) return false;

        Language lang = FindLanguage(languageName);
        if (lang != null)
        {
            page = FindPage(lang, pageId);
            if (page != null) return true;
        }

        // fallback
        Language fb = FindLanguage(fallbackLanguage);
        if (fb != null)
        {
            page = FindPage(fb, pageId);
            return page != null;
        }

        return false;
    }

    public float GetTotalDurationSeconds(string languageName, string pageId)
    {
        if (!TryGetPage(languageName, pageId, out Page page) || page == null)
            return 0f;

        float total = 0f;
        total += Mathf.Max(0f, page.extraStartSilence);

        for (int i = 0; i < page.segments.Count; i++)
        {
            var s = page.segments[i];
            if (s == null) continue;
            total += Mathf.Max(0f, s.delayBefore);
            if (s.clip != null) total += s.clip.length;
        }

        total += Mathf.Max(0f, page.extraEndSilence);
        return total;
    }

    public List<string> GetLanguageNames()
    {
        List<string> list = new List<string>();
        for (int i = 0; i < languages.Count; i++)
        {
            if (!string.IsNullOrEmpty(languages[i].languageName))
                list.Add(languages[i].languageName);
        }
        return list;
    }

    Language FindLanguage(string name)
    {
        for (int i = 0; i < languages.Count; i++)
        {
            if (string.Equals(languages[i].languageName, name, StringComparison.OrdinalIgnoreCase))
                return languages[i];
        }
        return null;
    }

    Page FindPage(Language lang, string pageId)
    {
        for (int i = 0; i < lang.pages.Count; i++)
        {
            if (lang.pages[i] != null && lang.pages[i].pageId == pageId)
                return lang.pages[i];
        }
        return null;
    }
}
