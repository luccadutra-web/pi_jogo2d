using System.Collections;
using UnityEngine;

/// <summary>
/// OwlGate — portão que bloqueia uma área e abre ao matar N corujas.
///
/// ═══════════════════════════════════════════════════════════════════════════
/// COMO FUNCIONA
/// ═══════════════════════════════════════════════════════════════════════════
///
///   1. O portão começa ativo (bloqueando a passagem).
///   2. O OwlSpawner notifica o OwlGate cada vez que uma coruja morre,
///      chamando OwlGate.OnOwlKilled().
///   3. Ao atingir owlsRequired mortes, o portão abre:
///        - Desativa o(s) GameObject(s) de bloqueio físico (gateObjects)
///        - Toca animação/som de abertura
///        - Exibe UI de "área liberada" (opcional)
///
/// ═══════════════════════════════════════════════════════════════════════════
/// SETUP
/// ═══════════════════════════════════════════════════════════════════════════
///
///   1. Crie um GameObject "Gate" na posição do portão na fase.
///   2. Adicione este componente.
///   3. Em Gate Objects, arraste os GameObjects que formam o portão
///      (sprites, colliders, partículas — tudo que precisa sumir ao abrir).
///   4. No OwlSpawner, arraste este Gate no campo Owl Gate.
///   5. Configure owlsRequired com o número de corujas a matar.
///
/// ═══════════════════════════════════════════════════════════════════════════
/// HIERARQUIA SUGERIDA
/// ═══════════════════════════════════════════════════════════════════════════
///
///   Gate
///    ├── GateVisual      (SpriteRenderer — o sprite do portão)
///    ├── GateCollider    (GameObject com Collider2D — o bloqueio físico)
///    ├── GateParticles   (ParticleSystem — efeito enquanto fechado, opcional)
///    └── OpenParticles   (ParticleSystem — efeito ao abrir, opcional)
///
///   OwlSpawner           (com OwlSpawner.cs — arraste o Gate aqui)
///
/// ═══════════════════════════════════════════════════════════════════════════
/// UI DE CONTAGEM (opcional)
/// ═══════════════════════════════════════════════════════════════════════════
///
///   Crie um TextMeshPro no Canvas com o texto "Corujas: 0 / 5".
///   Arraste no campo Kill Counter Text.
///   O script atualiza automaticamente a cada morte.
///
/// </summary>
public class OwlGate : MonoBehaviour
{
    // ─── Configuração ─────────────────────────────────────────────────────────

    [Header("Condição de abertura")]
    [Tooltip("Número de corujas a matar para abrir o portão.")]
    [SerializeField] private int owlsRequired = 5;

    [Header("Objetos do portão")]
    [Tooltip("GameObjects que formam o bloqueio — serão desativados ao abrir.\n" +
             "Inclua sprites, colliders e partículas de 'fechado'.")]
    [SerializeField] private GameObject[] gateObjects;

    [Tooltip("ParticleSystem tocado ao abrir o portão (opcional).")]
    [SerializeField] private ParticleSystem openParticles;

    [Tooltip("Som tocado ao abrir o portão (opcional).")]
    [SerializeField] private string openSFX = "gate_open";

    [Header("Animação de abertura")]
    [Tooltip("Tempo (segundos) do fade/descida do portão antes de desativar.\n" +
             "0 = desaparece instantaneamente.")]
    [SerializeField] private float openAnimDuration = 0.6f;

    [Tooltip("Se true, o portão desce antes de sumir (requer SpriteRenderer nos gateObjects).")]
    [SerializeField] private bool slideDownOnOpen = true;

    [Tooltip("Distância que o portão desce antes de sumir.")]
    [SerializeField] private float slideDistance = 1.5f;

    [Header("UI — contagem (opcional)")]
    [Tooltip("TextMeshPro que mostra 'Corujas: X / N'. Deixe vazio para ignorar.")]
    [SerializeField] private TMPro.TextMeshProUGUI killCounterText;

    [Tooltip("Texto exibido ao abrir o portão (ex: 'Área liberada!').")]
    [SerializeField] private string openMessage = "Área liberada!";

    [Tooltip("Tempo que a mensagem fica na tela (segundos). 0 = não exibe.")]
    [SerializeField] private float openMessageDuration = 2.5f;

    [Header("Debug")]
    [SerializeField] private bool debugLog = true;

    // ─── Estado ───────────────────────────────────────────────────────────────

    private int  _killCount = 0;
    private bool _isOpen    = false;

    // ─── Unity ────────────────────────────────────────────────────────────────

    private void Start()
    {
        UpdateCounterUI();

        // Garante que os objetos do portão estão ativos no início
        SetGateActive(true);
    }

    // ─── API pública — chamada pelo OwlSpawner ────────────────────────────────

    /// <summary>
    /// Chamado pelo OwlSpawner cada vez que uma coruja morre.
    /// </summary>
    public void OnOwlKilled()
    {
        if (_isOpen) return;

        _killCount++;
        Log($"Coruja morta | {_killCount} / {owlsRequired}");

        UpdateCounterUI();

        if (_killCount >= owlsRequired)
            StartCoroutine(OpenGate());
    }

    // ─── Abertura ─────────────────────────────────────────────────────────────

    private IEnumerator OpenGate()
    {
        _isOpen = true;
        Log("Abrindo portão!");

        // Som
        AudioManager.Instance?.PlaySFX(openSFX);

        // Partícula de abertura
        if (openParticles != null)
            openParticles.Play();

        // Mensagem na UI
        if (!string.IsNullOrEmpty(openMessage) && openMessageDuration > 0f)
            StartCoroutine(ShowOpenMessage());

        // Animação de descida
        if (slideDownOnOpen && openAnimDuration > 0f)
            yield return StartCoroutine(SlideDownRoutine());
        else if (openAnimDuration > 0f)
            yield return StartCoroutine(FadeOutRoutine());
        else
            yield return null;

        // Desativa os objetos do portão (remove colisão e visual)
        SetGateActive(false);

        // Atualiza UI com mensagem final
        if (killCounterText != null)
            killCounterText.text = openMessage;

        Log("Portão aberto — passagem liberada.");
    }

    /// <summary>
    /// Desliza os objetos do portão para baixo antes de desativar.
    /// </summary>
    private IEnumerator SlideDownRoutine()
    {
        if (gateObjects == null) yield break;

        Vector3[] startPositions = new Vector3[gateObjects.Length];
        for (int i = 0; i < gateObjects.Length; i++)
            if (gateObjects[i] != null)
                startPositions[i] = gateObjects[i].transform.position;

        float elapsed = 0f;
        while (elapsed < openAnimDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / openAnimDuration;
            float smooth = Mathf.SmoothStep(0f, 1f, t);

            for (int i = 0; i < gateObjects.Length; i++)
            {
                if (gateObjects[i] == null) continue;
                gateObjects[i].transform.position =
                    startPositions[i] + Vector3.down * slideDistance * smooth;
            }

            yield return null;
        }
    }

    /// <summary>
    /// Faz fade out nos SpriteRenderers dos objetos do portão.
    /// </summary>
    private IEnumerator FadeOutRoutine()
    {
        if (gateObjects == null) yield break;

        // Coleta todos os SpriteRenderers
        var renderers = new System.Collections.Generic.List<SpriteRenderer>();
        foreach (var go in gateObjects)
        {
            if (go == null) continue;
            renderers.AddRange(go.GetComponentsInChildren<SpriteRenderer>());
        }

        float elapsed = 0f;
        while (elapsed < openAnimDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, elapsed / openAnimDuration);

            foreach (var sr in renderers)
            {
                if (sr == null) continue;
                Color c = sr.color;
                c.a = alpha;
                sr.color = c;
            }

            yield return null;
        }
    }

    private IEnumerator ShowOpenMessage()
    {
        if (killCounterText == null) yield break;

        killCounterText.text = openMessage;
        yield return new WaitForSeconds(openMessageDuration);

        // Após a mensagem, limpa o texto
        if (killCounterText != null)
            killCounterText.text = "";
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private void SetGateActive(bool active)
    {
        if (gateObjects == null) return;
        foreach (var go in gateObjects)
            if (go != null) go.SetActive(active);
    }

    private void UpdateCounterUI()
    {
        if (killCounterText == null) return;
        killCounterText.text = $"Corujas: {_killCount} / {owlsRequired}";
    }

    private void Log(string msg) { if (debugLog) Debug.Log($"[OwlGate] {msg}"); }

    // ─── Gizmos ───────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        // Mostra a área do portão
        Gizmos.color = new Color(1f, 0.3f, 0f, 0.4f);
        Gizmos.DrawWireCube(transform.position, new Vector3(1f, 3f, 0f));

        // Label com quantas corujas faltam
        #if UNITY_EDITOR
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 1.8f,
            $"Gate: {_killCount}/{owlsRequired}"
        );
        #endif
    }
}