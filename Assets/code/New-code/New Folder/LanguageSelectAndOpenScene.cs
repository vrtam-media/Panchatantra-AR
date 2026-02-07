using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LanguageSelectAndOpenScene : MonoBehaviour
{
    [Header("Config")]
    public string languageName = "English";
    public string targetSceneName = "AR-main-scene";

    [Header("Optional")]
    public Button button;

    private void Reset()
    {
        button = GetComponent<Button>();
    }

    private void Awake()
    {
        if (button == null) button = GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveListener(OnClick);
            button.onClick.AddListener(OnClick);
        }
    }

    private void OnClick()
    {
        ARGlobalLanguage.Set(languageName);
        SceneManager.LoadScene(targetSceneName);
    }
}
