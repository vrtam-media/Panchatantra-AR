using UnityEngine;

public static class ARGlobalLanguage
{
    private const string Key = "AR_SELECTED_LANGUAGE";
    public static string DefaultLanguage = "English";

    public static string Get()
    {
        return PlayerPrefs.GetString(Key, DefaultLanguage);
    }

    public static void Set(string language)
    {
        if (string.IsNullOrWhiteSpace(language)) return;
        PlayerPrefs.SetString(Key, language);
        PlayerPrefs.Save();
    }
}
