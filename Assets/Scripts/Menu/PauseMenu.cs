using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

/// <summary>
/// PauseMenu — pausa o jogo com ESC e exibe o menu de pausa.
///
/// ── INTEGRAÇÃO COM O PROJETO ──────────────────────────────────────────────────
///
///   O projeto usa HitStop (timeScale = 0) e SlowMotion (timeScale < 1) durante
///   o combate. O PauseMenu precisa:
///
///     1. Não confundir timeScale = 0 do HitStop com um pause real.
///        → Usa uma flag booleana própria (isPaused), não checa timeScale.
///
///     2. Cancelar o HitStop antes de pausar, para não sobrepor dois zeros.
///        → Chama HitStop.Instance?.Cancel() em PauseGame().
///
///     3. Pausar o áudio via AudioListener.pause — o AudioManager já deixa
///        AudioListener.pause = false durante o HitStop. Ao pausar o jogo,
///        revertemos para true para silenciar tudo de forma limpa.
///
///     4. Bloquear o input do player durante a pausa sem precisar modificar
///        PlayerBehavior — PlayerBehavior já respeita isLocked.
///        → Seta behavior.isLocked = true em PauseGame().
///
/// ── SETUP NA CENA ────────────────────────────────────────────────────────────
///
///   1. Crie um Canvas na cena do jogo.
///   2. Dentro do Canvas, crie um GameObject "PauseMenuPanel" com os botões:
///        - Continuar   → On Click() → PauseMenuManager → PauseMenu.ResumeGame()
///        - Menu        → On Click() → PauseMenuManager → PauseMenu.GoToMainMenu()
///        - Sair        → On Click() → PauseMenuManager → PauseMenu.QuitGame()
///   3. Crie um GameObject "PauseMenuManager" e adicione este componente.
///   4. No Inspector, arraste o "PauseMenuPanel" no campo Pause Menu.
///   5. O PauseMenuPanel deve começar desativado (uncheck no Inspector).
///
/// ── HIERARQUIA SUGERIDA ───────────────────────────────────────────────────────
///
///   Canvas
///    └── PauseMenuPanel          ← pauseMenu (inicia desativado)
///         ├── Background (Image)
///         ├── Título (Text)
///         ├── BotãoContinuar
///         ├── BotãoMenu
///         └── BotãoSair
///
///   PauseMenuManager             ← este componente
///
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public class PauseMenu : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("Painel do menu de pausa. Deve estar desativado no início.")]
    public GameObject pauseMenu;

    [Header("Cenas")]
    [Tooltip("Nome exato da cena do menu principal (case-sensitive).")]
    public string mainMenuSceneName = "MainMenu";

    // ── Estado ────────────────────────────────────────────────────────────────

    private bool _isPaused = false;

    // Cache do PlayerBehavior para bloquear input durante a pausa
    private PlayerBehavior _playerBehavior;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Start()
    {
        // Busca o PlayerBehavior na cena — feito uma vez para evitar Find em Update
        _playerBehavior = FindObjectOfType<PlayerBehavior>();

        // Garante que o painel começa oculto e o jogo rodando normalmente
        if (pauseMenu != null)
            pauseMenu.SetActive(false);
    }

    private void Update()
    {
        // Lê ESC via New Input System (mesmo padrão do resto do projeto)
        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (_isPaused)
                ResumeGame();
            else
                PauseGame();
        }
    }

    // ── API Pública ───────────────────────────────────────────────────────────

    /// <summary>
    /// Pausa o jogo: exibe o menu, zera timeScale e bloqueia o player.
    /// </summary>
    public void PauseGame()
    {
        if (_isPaused) return;

        // Cancela HitStop/SlowMotion antes de setar timeScale = 0 —
        // evita que o HitStop restaure timeScale = 1 depois do pause,
        // desfazendo a pausa sem o jogador perceber.
        HitStop.Instance?.Cancel();

        // Pausa o áudio de forma limpa
        // AudioListener.pause silencia todos os AudioSources do projeto
        AudioListener.pause = true;
        AudioManager.Instance?.PauseMusic(true);

        // Bloqueia o input do player (PlayerBehavior já respeita isLocked)
        if (_playerBehavior != null)
            _playerBehavior.isLocked = true;

        Time.timeScale = 0f;
        _isPaused = true;

        if (pauseMenu != null)
            pauseMenu.SetActive(true);
    }

    /// <summary>
    /// Retoma o jogo: oculta o menu e restaura o estado normal.
    /// </summary>
    public void ResumeGame()
    {
        if (!_isPaused) return;

        if (pauseMenu != null)
            pauseMenu.SetActive(false);

        Time.timeScale = 1f;
        _isPaused = false;

        // Restaura o áudio
        AudioListener.pause = false;
        AudioManager.Instance?.PauseMusic(false);

        // Libera o player
        if (_playerBehavior != null)
            _playerBehavior.isLocked = false;
    }

    /// <summary>
    /// Volta ao menu principal. Restaura tudo antes de trocar de cena.
    /// </summary>
    public void GoToMainMenu()
    {
        // Restaura estado global antes de sair — essencial porque
        // singletons com DontDestroyOnLoad (AudioManager, HitStop) persistem
        // entre cenas e precisam estar em estado limpo ao chegar no menu.
        Time.timeScale = 1f;
        AudioListener.pause = false;
        _isPaused = false;

        HitStop.Instance?.Cancel();
        AudioManager.Instance?.StopMusic(0.5f);

        SceneManager.LoadScene(mainMenuSceneName);
    }

    /// <summary>
    /// Fecha o jogo.
    /// </summary>
    public void QuitGame()
    {
        // Restaura timeScale por segurança (afeta o Editor ao parar o play mode)
        Time.timeScale = 1f;
        Debug.Log("[PauseMenu] Jogo encerrado.");
        Application.Quit();
    }

    // ── Segurança: garante limpeza se o objeto for destruído durante o pause ──

    private void OnDestroy()
    {
        if (_isPaused)
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;

            if (_playerBehavior != null)
                _playerBehavior.isLocked = false;
        }
    }
}