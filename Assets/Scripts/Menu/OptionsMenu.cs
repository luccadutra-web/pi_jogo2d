using UnityEngine;
using UnityEngine.UI;
using TMPro;


public class OptionsMenu : MonoBehaviour
{
    
    public static OptionsMenu Instance { get; private set; }

    [Header("Painel")]
    [Tooltip("O GameObject raiz do painel de opções. Começa desativado.")]
    public GameObject optionsPanel;

    [Header("Sliders de áudio")]
    public Slider masterSlider;
    public Slider musicSlider;
    public Slider sfxSlider;

    [Header("Textos de valor (opcional)")]
    [Tooltip("TextMeshPro que mostra o valor do slider em % (ex: '85%'). Pode deixar vazio.")]
    public TextMeshProUGUI masterValueText;
    public TextMeshProUGUI musicValueText;
    public TextMeshProUGUI sfxValueText;

    [Header("Vídeo")]
    public Toggle fullscreenToggle;

    [Header("Ícones de teclas")]
    [Tooltip("Um Image por ação do jogo, na ordem: Mover, Pular, Dash, Ataque Leve, Ataque Pesado, Defesa.")]
    public Image[] keyIconImages;

    [Tooltip("Sprites das teclas na mesma ordem dos Key Icon Images.\n" +
             "Use sprites do Kenney Input Prompts ou similar.")]
    public Sprite[] keySprites;

    // ── Chaves PlayerPrefs ────────────────────────────────────────────────────
    private const string KeyMaster     = "vol_master";
    private const string KeyMusic      = "vol_music";
    private const string KeySFX        = "vol_sfx";
    private const string KeyFullscreen = "fullscreen";

    // ── Estado ────────────────────────────────────────────────────────────────
    private bool _isOpen = false;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        Instance = this;

        if (optionsPanel != null)
            optionsPanel.SetActive(false);
    }

    private void Start()
    {
        LoadSavedSettings();
        ApplyKeyIcons();
        RegisterSliderCallbacks();
    }

    // ── API pública ───────────────────────────────────────────────────────────

    /// <summary>
    /// Abre o painel de opções. Chamado pelo botão Opções no MainMenu ou PauseMenu.
    /// </summary>
    public void Open()
    {
        if (optionsPanel == null) return;
        _isOpen = true;
        optionsPanel.SetActive(true);
        RefreshUI(); // garante que os sliders batem com os valores atuais
    }

    /// <summary>
    /// Fecha o painel e salva tudo. Chamado pelo botão Fechar/Voltar.
    /// </summary>
    public void Close()
    {
        SaveSettings();
        if (optionsPanel != null)
            optionsPanel.SetActive(false);
        _isOpen = false;
    }

    public bool IsOpen => _isOpen;

    // ── Callbacks dos Sliders ─────────────────────────────────────────────────

    public void OnMasterChanged(float value)
    {
        AudioManager.Instance?.SetMasterVolume(value);
        if (masterValueText != null)
            masterValueText.text = Mathf.RoundToInt(value * 100f) + "%";
    }

    public void OnMusicChanged(float value)
    {
        AudioManager.Instance?.SetMusicVolume(value);
        if (musicValueText != null)
            musicValueText.text = Mathf.RoundToInt(value * 100f) + "%";
    }

    public void OnSFXChanged(float value)
    {
        AudioManager.Instance?.SetSFXVolume(value);
        if (sfxValueText != null)
            sfxValueText.text = Mathf.RoundToInt(value * 100f) + "%";
    }

    public void OnFullscreenChanged(bool value)
    {
        Screen.fullScreen = value;
    }

    // ── Internos ──────────────────────────────────────────────────────────────

    private void RegisterSliderCallbacks()
    {
        // Remove listeners antigos antes de adicionar — evita duplicatas se
        // a cena for recarregada com o objeto persistindo.
        if (masterSlider != null)
        {
            masterSlider.onValueChanged.RemoveAllListeners();
            masterSlider.onValueChanged.AddListener(OnMasterChanged);
        }

        if (musicSlider != null)
        {
            musicSlider.onValueChanged.RemoveAllListeners();
            musicSlider.onValueChanged.AddListener(OnMusicChanged);
        }

        if (sfxSlider != null)
        {
            sfxSlider.onValueChanged.RemoveAllListeners();
            sfxSlider.onValueChanged.AddListener(OnSFXChanged);
        }

        if (fullscreenToggle != null)
        {
            fullscreenToggle.onValueChanged.RemoveAllListeners();
            fullscreenToggle.onValueChanged.AddListener(OnFullscreenChanged);
        }
    }

    private void LoadSavedSettings()
    {
        float master = PlayerPrefs.GetFloat(KeyMaster, 1f);
        float music  = PlayerPrefs.GetFloat(KeyMusic,  0.6f);
        float sfx    = PlayerPrefs.GetFloat(KeySFX,    1f);
        bool  fs     = PlayerPrefs.GetInt(KeyFullscreen, Screen.fullScreen ? 1 : 0) == 1;

        // Aplica no AudioManager
        AudioManager.Instance?.SetMasterVolume(master);
        AudioManager.Instance?.SetMusicVolume(music);
        AudioManager.Instance?.SetSFXVolume(sfx);
        Screen.fullScreen = fs;

        RefreshUI();
    }

    private void RefreshUI()
    {
        // Atualiza sliders sem disparar os callbacks (evita loop)
        if (masterSlider != null)
        {
            masterSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat(KeyMaster, 1f));
            if (masterValueText != null)
                masterValueText.text = Mathf.RoundToInt(masterSlider.value * 100f) + "%";
        }

        if (musicSlider != null)
        {
            musicSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat(KeyMusic, 0.6f));
            if (musicValueText != null)
                musicValueText.text = Mathf.RoundToInt(musicSlider.value * 100f) + "%";
        }

        if (sfxSlider != null)
        {
            sfxSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat(KeySFX, 1f));
            if (sfxValueText != null)
                sfxValueText.text = Mathf.RoundToInt(sfxSlider.value * 100f) + "%";
        }

        if (fullscreenToggle != null)
            fullscreenToggle.SetIsOnWithoutNotify(Screen.fullScreen);
    }

    private void SaveSettings()
    {
        if (masterSlider != null) PlayerPrefs.SetFloat(KeyMaster, masterSlider.value);
        if (musicSlider  != null) PlayerPrefs.SetFloat(KeyMusic,  musicSlider.value);
        if (sfxSlider    != null) PlayerPrefs.SetFloat(KeySFX,    sfxSlider.value);
        PlayerPrefs.SetInt(KeyFullscreen, Screen.fullScreen ? 1 : 0);
        PlayerPrefs.Save();
    }

    private void ApplyKeyIcons()
    {
        if (keyIconImages == null || keySprites == null) return;

        int count = Mathf.Min(keyIconImages.Length, keySprites.Length);
        for (int i = 0; i < count; i++)
        {
            if (keyIconImages[i] != null && keySprites[i] != null)
                keyIconImages[i].sprite = keySprites[i];
        }
    }
}