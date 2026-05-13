using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// MainMenu — tela inicial do jogo.
///
/// ── SETUP ────────────────────────────────────────────────────────────────────
///
///   1. Crie uma cena chamada "MainMenu" e adicione ao Build Settings
///      (File → Build Settings → Add Open Scene).
///      A cena "Tutorial" (ou "Game") também deve estar na lista.
///
///   2. Crie um GameObject "MenuManager" na cena MainMenu.
///      Adicione este componente.
///
///   3. Configure os botões:
///        Botão Jogar  → On Click() → MenuManager → MainMenu.PlayGame()
///        Botão Sair   → On Click() → MenuManager → MainMenu.QuitGame()
///
/// ── ORDEM NO BUILD SETTINGS ──────────────────────────────────────────────────
///
///   Index 0 → MainMenu   (carrega primeiro ao iniciar o jogo)
///   Index 1 → Tutorial
///   Index 2 → Game  (se houver cena separada do tutorial)
///
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public class MainMenu : MonoBehaviour
{
    [Tooltip("Nome exato da cena que será carregada ao clicar em Jogar.\n" +
             "Deve bater com o nome no Build Settings (case-sensitive).")]
    public string gameSceneName = "Tutorial";

    /// <summary>
    /// Garante que o timeScale está em 1 ao entrar no menu —
    /// caso o jogador tenha saído pelo PauseMenu sem restaurar.
    /// </summary>
    private void Start()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;

        // Retoma a música do menu, se o AudioManager persistir entre cenas
        AudioManager.Instance?.PlayMusic("music_menu");
    }

    /// <summary>
    /// Carrega a cena do jogo. Chamado pelo botão Jogar.
    /// </summary>
    public void PlayGame()
    {
        AudioManager.Instance?.StopMusic(0.5f);
        SceneManager.LoadScene(gameSceneName);
    }

    /// <summary>
    /// Fecha o jogo. Chamado pelo botão Sair.
    /// Em builds de desenvolvimento, só loga — Application.Quit() não funciona no Editor.
    /// </summary>
    public void QuitGame()
    {
        Debug.Log("[MainMenu] Jogo encerrado.");
        Application.Quit();
    }
}