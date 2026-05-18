using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;


public class PauseMenu : MonoBehaviour
{
    [Header("UI")]
    public GameObject pauseMenu;

    [Header("Cenas")]
    public string mainMenuSceneName = "MainMenu";

    [Header("HitStop — segurança")]
    public float hitStopCooldownGuard = 0.15f;

    
    private bool _isPaused  = false;
    private float _timeSinceHitStop = 999f; 

    private PlayerBehavior _playerBehavior;

    

    private void Start()
    {
        _playerBehavior = FindObjectOfType<PlayerBehavior>();

        if (pauseMenu != null)
            pauseMenu.SetActive(false);
    }

    private void Update()
    {
       
        if (HitStop.Instance != null && HitStop.Instance.IsActive)
        {
            _timeSinceHitStop = 0f;
        }

        else
        {
            _timeSinceHitStop += Time.unscaledDeltaTime;
        }
            

        if (!Keyboard.current.escapeKey.wasPressedThisFrame) return;

        
        if (OptionsMenu.Instance != null && OptionsMenu.Instance.IsOpen)
        {
            OptionsMenu.Instance.Close();
            return;
        }

        if (_isPaused)
        {
            ResumeGame();
        }
        else
        {
            
            if (_timeSinceHitStop < hitStopCooldownGuard) return;
            PauseGame();
        }
    }

    

    public void PauseGame()
    {
        if (_isPaused) return;

        
        HitStop.Instance?.Cancel();

        AudioListener.pause = true;
        AudioManager.Instance?.PauseMusic(true);

        if (_playerBehavior != null)
        {
            _playerBehavior.isLocked = true;
        }

        Time.timeScale = 0f;
        _isPaused = true;

        if (pauseMenu != null)
        {
            pauseMenu.SetActive(true);
        }
           
    }

    public void ResumeGame()
    {
        if (!_isPaused) return;

        
        if (OptionsMenu.Instance != null && OptionsMenu.Instance.IsOpen)
        {
            OptionsMenu.Instance.Close();
        }
            

        if (pauseMenu != null)
        {
            pauseMenu.SetActive(false);
        }
            
        Time.timeScale = 1f;
        _isPaused = false;

        AudioListener.pause = false;
        AudioManager.Instance?.PauseMusic(false);

        if (_playerBehavior != null)
        {
            _playerBehavior.isLocked = false;
        }
            
    }

   
    public void OpenOptions()
    {
        OptionsMenu.Instance?.Open();
    }

    public void GoToMainMenu()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
        _isPaused = false;

        HitStop.Instance?.Cancel();
        AudioManager.Instance?.StopMusic(0.5f);

        SceneManager.LoadScene(mainMenuSceneName);
    }

    public void QuitGame()
    {
        Time.timeScale = 1f;
        Debug.Log("[PauseMenu] Jogo encerrado.");
        Application.Quit();
    }

    private void OnDestroy()
    {
        if (_isPaused)
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;

            if (_playerBehavior != null)
            {
                _playerBehavior.isLocked = false;
            }
                
        }
    }
}