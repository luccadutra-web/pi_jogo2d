using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;


public class MainMenu : MonoBehaviour
{
    [Tooltip("Nome exato da cena que será carregada ao clicar em Jogar (case-sensitive).")]
    public string gameSceneName = "Tutorial";

    private void Start()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
        AudioManager.Instance?.PlayMusic("music_menu");
    }

    private void Update()
    {
        
        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (OptionsMenu.Instance != null && OptionsMenu.Instance.IsOpen)
            {
                OptionsMenu.Instance.Close();
            }
                
        }
    }

  
    public void PlayGame()
    {
        AudioManager.Instance?.StopMusic(0.5f);
        SceneManager.LoadScene(gameSceneName);
    }

    
    public void OpenOptions()
    {
        OptionsMenu.Instance?.Open();
    }

    public void QuitGame()
    {
        Debug.Log("[MainMenu] Jogo encerrado.");
        Application.Quit();
    }
}