using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// MainMenu — Menu principal do jogo.
///
/// Setup:
///   1. Crie uma cena chamada "MainMenu" e adicione ao Build Settings.
///   2. Crie um Canvas com os botões e atribua no Inspector.
///   3. O nome da cena do jogo deve ser colocado em "gameSceneName".
/// </summary>
public class MainMenu : MonoBehaviour
{
    [Header("Painéis")]
    public GameObject mainPanel;
    public GameObject optionsPanel;

    [Header("Botões — Main")]
    public Button playButton;
    public Button optionsButton;
    public Button quitButton;

    [Header("Botões — Options")]
    public Button backButton;
    public Slider musicSlider;
    public Slider sfxSlider;

    [Header("Configuração")]
    public string gameSceneName = "Tutorial"; 

    [Header("Animação")]
    public float fadeInDuration = 0.5f;
    public CanvasGroup canvasGroup;

    void Start()
    {
        
        if (musicSlider) musicSlider.value = PlayerPrefs.GetFloat("MusicVolume", 1f);
        if (sfxSlider)   sfxSlider.value   = PlayerPrefs.GetFloat("SFXVolume",   1f);

        
        playButton?.onClick.AddListener(PlayGame);
        optionsButton?.onClick.AddListener(OpenOptions);
        quitButton?.onClick.AddListener(QuitGame);
        backButton?.onClick.AddListener(CloseOptions);
        musicSlider?.onValueChanged.AddListener(SetMusicVolume);
        sfxSlider?.onValueChanged.AddListener(SetSFXVolume);

        ShowMain();

        
        if (canvasGroup != null)
            StartCoroutine(FadeIn());
    }

    
    public void PlayGame()
    {
        StartCoroutine(LoadWithFade(gameSceneName));
    }

    public void OpenOptions()
    {
        mainPanel?.SetActive(false);
        optionsPanel?.SetActive(true);
    }

    public void CloseOptions()
    {
        PlayerPrefs.Save();
        ShowMain();
    }

    public void QuitGame()
    {
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        Application.Quit();
        #endif
    }

    void ShowMain()
    {
        mainPanel?.SetActive(true);
        optionsPanel?.SetActive(false);
    }

    
    void SetMusicVolume(float value)
    {
        PlayerPrefs.SetFloat("MusicVolume", value);
        AudioListener.volume = value; 
    }

    void SetSFXVolume(float value)
    {
        PlayerPrefs.SetFloat("SFXVolume", value);
    }

    
    IEnumerator FadeIn()
    {
        canvasGroup.alpha = 0f;
        float t = 0f;
        while (t < fadeInDuration)
        {
            t += Time.deltaTime;
            canvasGroup.alpha = Mathf.Clamp01(t / fadeInDuration);
            yield return null;
        }
        canvasGroup.alpha = 1f;
    }

    IEnumerator LoadWithFade(string sceneName)
    {
        if (canvasGroup != null)
        {
            float t = fadeInDuration;
            while (t > 0f)
            {
                t -= Time.deltaTime;
                canvasGroup.alpha = Mathf.Clamp01(t / fadeInDuration);
                yield return null;
            }
        }
        SceneManager.LoadScene(sceneName);
    }
}