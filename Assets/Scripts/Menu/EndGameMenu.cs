using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;


public class EndGameMenu : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("Painel da tela de vitória. Deve estar desativado no início.")]
    public GameObject endGamePanel;

    [Header("Inimigo")]
    [Tooltip("GameObject do ProtoEnemy (com EnemyBehavior.cs).\n" +
             "Quando ele morrer, a tela de vitória aparece.")]
    public GameObject protoEnemy;

    [Header("Cenas")]
    [Tooltip("Nome exato da cena do menu principal (case-sensitive).")]
    public string mainMenuSceneName = "MainMenu";

    [Header("Timing")]
    [Tooltip("Tempo de espera (segundos reais) após a morte do boss antes de exibir a tela.\n" +
             "Dá tempo para a animação de morte terminar.")]
    public float delayAfterDeath = 2f;

    // ─── Unity ────────────────────────────────────────────────────────────────

    private void Start()
    {
        if (endGamePanel != null)
            endGamePanel.SetActive(false);

        if (protoEnemy == null)
        {
            // Tenta encontrar automaticamente na cena
            var enemy = FindFirstObjectByType<EnemyBehavior>();
            if (enemy != null) protoEnemy = enemy.gameObject;
        }

        if (protoEnemy != null)
        {
            var eb = protoEnemy.GetComponent<EnemyBehavior>();
            if (eb != null)
                eb.OnDeath += OnProtoDied;
            else
                Debug.LogWarning("[EndGameMenu] EnemyBehavior não encontrado no ProtoEnemy.");
        }
        else
        {
            Debug.LogWarning("[EndGameMenu] ProtoEnemy não atribuído e não encontrado na cena.");
        }
    }

    private void OnDestroy()
    {
        if (protoEnemy != null)
        {
            var eb = protoEnemy.GetComponent<EnemyBehavior>();
            if (eb != null) eb.OnDeath -= OnProtoDied;
        }
    }

    // ─── Morte do boss ────────────────────────────────────────────────────────

    private void OnProtoDied()
    {
        StartCoroutine(ShowEndGameAfterDelay());
    }

    private IEnumerator ShowEndGameAfterDelay()
    {
        yield return new WaitForSecondsRealtime(delayAfterDeath);

        // Cancela HitStop/SlowMotion se ativo
        HitStop.Instance?.Cancel();

        // Fecha PauseMenu e Options se estiverem abertos
        var pauseMenu = FindFirstObjectByType<PauseMenu>();
        if (pauseMenu != null && pauseMenu.pauseMenu != null)
            pauseMenu.pauseMenu.SetActive(false);

        if (OptionsMenu.Instance != null && OptionsMenu.Instance.IsOpen)
            OptionsMenu.Instance.Close();

        Time.timeScale = 0f;
        AudioListener.pause = true;
        AudioManager.Instance?.PauseMusic(true);

        if (endGamePanel != null)
            endGamePanel.SetActive(true);
    }

    // ─── Botões ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Volta ao menu principal. Chamado pelo botão "Menu Principal".
    /// </summary>
    public void GoToMainMenu()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
        AudioManager.Instance?.StopMusic(0.5f);
        SceneManager.LoadScene(mainMenuSceneName);
    }

    /// <summary>
    /// Fecha o jogo. Chamado pelo botão "Sair".
    /// </summary>
    public void QuitGame()
    {
        Time.timeScale = 1f;
        Debug.Log("[EndGameMenu] Jogo encerrado.");
        Application.Quit();
    }
}