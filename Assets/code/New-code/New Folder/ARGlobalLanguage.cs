using UnityEngine;

public class ARGlobalLanguage : MonoBehaviour
{
    private const string PlayerPrefsKey = "AR_SELECTED_LANGUAGE";

    [SerializeField] private string defaultLanguage = "English";

    public static string CurrentLanguage { get; private set; } = "English";
    public static ARGlobalLanguage Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        var saved = PlayerPrefs.GetString(PlayerPrefsKey, "");
        if (!string.IsNullOrWhiteSpace(saved))
            CurrentLanguage = saved;
        else if (!string.IsNullOrWhiteSpace(defaultLanguage))
            CurrentLanguage = defaultLanguage;
    }

    public static void SetLanguage(string lang)
    {
        if (string.IsNullOrWhiteSpace(lang)) return;

        CurrentLanguage = lang.Trim();
        PlayerPrefs.SetString(PlayerPrefsKey, CurrentLanguage);
        PlayerPrefs.Save();
    }

    public static string GetLanguageOrDefault(string fallback)
    {
        var saved = PlayerPrefs.GetString(PlayerPrefsKey, "");
        if (!string.IsNullOrWhiteSpace(saved))
        {
            CurrentLanguage = saved.Trim();
            return CurrentLanguage;
        }

        if (!string.IsNullOrWhiteSpace(CurrentLanguage))
            return CurrentLanguage;

        if (!string.IsNullOrWhiteSpace(fallback))
        {
            CurrentLanguage = fallback.Trim();
            return CurrentLanguage;
        }

        CurrentLanguage = "English";
        return CurrentLanguage;
    }
}
