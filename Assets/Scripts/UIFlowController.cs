using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class UIFlowController : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject splashPanel;
    [SerializeField] private GameObject homePanel;

    [Header("Splash Settings")]
    [SerializeField] private float splashSeconds = 5f;
    [SerializeField] private Button skipButton;

    private Coroutine splashRoutine;

    private void Awake()
    {
        // Safety: start state
        splashPanel.SetActive(true);
        homePanel.SetActive(false);

        if (skipButton != null)
            skipButton.onClick.AddListener(GoToHome);
    }

    private void OnEnable()
    {
        splashRoutine = StartCoroutine(SplashCountdown());
    }

    private IEnumerator SplashCountdown()
    {
        yield return new WaitForSeconds(splashSeconds);
        GoToHome();
    }

    private void GoToHome()
    {
        if (splashRoutine != null)
        {
            StopCoroutine(splashRoutine);
            splashRoutine = null;
        }

        splashPanel.SetActive(false);
        homePanel.SetActive(true);
    }
}
