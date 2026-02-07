using UnityEngine;
using System;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "AR/Audio Localization Database", fileName = "ARAudioLocalizationDatabase")]
public class ARAudioLocalizationDatabase : ScriptableObject
{
    // Global language for the whole app (your choice A)
    public static string CurrentLanguage { get; private set; } = "English";

    public static event Action<string> OnLanguageChanged;

    private const string PrefKey = "AR_GlobalLanguage";

    [Serializable]
    public class Segment
    {
        public AudioClip clip;
        public float delayBefore = 0f;
        [Range(0f, 1f)] public float volume = 1f;
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

    public static void LoadGlobalLanguage(string defaultLanguage = "English")
    {
        var saved = PlayerPrefs.GetString(PrefKey, defaultLanguage);
        CurrentLanguage = string.IsNullOrEmpty(saved) ? defaultLanguage : saved;
    }

    public static void SetLanguageGlobal(string languageName)
    {
        if (string.IsNullOrEmpty(languageName)) return;
        if (string.Equals(CurrentLanguage, languageName, StringComparison.OrdinalIgnoreCase)) return;

        CurrentLanguage = languageName;
        PlayerPrefs.SetString(PrefKey, CurrentLanguage);
        PlayerPrefs.Save();

        OnLanguageChanged?.Invoke(CurrentLanguage);
    }

    public bool TryGetPage(string languageName, string pageId, out Page page)
    {
        page = null;
        if (string.IsNullOrEmpty(pageId)) return false;

        // try requested
        var lang = FindLanguage(languageName);
        if (lang != null)
        {
            page = FindPage(lang, pageId);
            if (page != null) return true;
        }

        // fallback
        var fb = FindLanguage(fallbackLanguage);
        if (fb != null)
        {
            page = FindPage(fb, pageId);
            return page != null;
        }

        return false;
    }

    public List<string> GetLanguageNames()
    {
        var list = new List<string>();
        foreach (var l in languages)
            if (l != null && !string.IsNullOrEmpty(l.languageName))
                list.Add(l.languageName);
        return list;
    }

    private Language FindLanguage(string name)
    {
        foreach (var l in languages)
        {
            if (l == null) continue;
            if (string.Equals(l.languageName, name, StringComparison.OrdinalIgnoreCase))
                return l;
        }
        return null;
    }

    private Page FindPage(Language lang, string pageId)
    {
        foreach (var p in lang.pages)
        {
            if (p == null) continue;
            if (p.pageId == pageId) return p;
        }
        return null;
    }
}
