using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;


public class GameOver : MonoBehaviour
{
    [Header("UI")]
    public GameObject gameOverPanel;

    [Header("Cenas")]
    public string gameSceneName = "Game";
    public string mainMenuSceneName = "MainMenu";

    [Header("Timing")]
    public float delayAfterDeath = 1.5f;


    private void Start()
    {
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(false);
        }

        if (PlayerHealth.Instance != null)
        {
            PlayerHealth.Instance.OnDeath += OnPlayerDeath;
        }

        else
        {
            Debug.LogWarning("[GameOver] PlayerHealth.Instance não encontrado. " +
                             "Garanta que PlayerHealth está na cena e usa Instance no Awake.");
        }
            
    }

    private void OnDestroy()
    {
        if (PlayerHealth.Instance != null)
            PlayerHealth.Instance.OnDeath -= OnPlayerDeath;
    }

    

    private void OnPlayerDeath()
    {
        StartCoroutine(ShowGameOverAfterDelay());
    }

    private IEnumerator ShowGameOverAfterDelay()
    {
        yield return new WaitForSecondsRealtime(delayAfterDeath);
        HitStop.Instance?.Cancel();

        var pauseMenu = FindObjectOfType<PauseMenu>();
        if (pauseMenu != null && pauseMenu.pauseMenu != null)
        {
            pauseMenu.pauseMenu.SetActive(false);
        }
           
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);
        }
            
    }

    
    public void Retry()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
        AudioManager.Instance?.StopMusic(0f);
        SceneManager.LoadScene(gameSceneName);
    }

    public void GoToMainMenu()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
        AudioManager.Instance?.StopMusic(0.5f);
        SceneManager.LoadScene(mainMenuSceneName);
    }
}