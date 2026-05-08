using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Exibe a barra de postura do inimigo sobre ele no mundo.
/// Aparece ao receber o primeiro hit e some quando a postura está cheia.
///
/// Setup:
/// 1. Crie um Canvas (World Space) filho do inimigo.
/// 2. Adicione um Slider no Canvas e configure como mostrado abaixo.
/// 3. Adicione este script no GameObject do inimigo.
/// 4. Arraste o Slider para o campo poiseSlider no Inspector.
/// </summary>
public class PoiseIndicator : MonoBehaviour
{
    [Header("Referências")]
    [SerializeField] private Slider poiseSlider;
    [Tooltip("GameObject pai do canvas de UI (para ativar/desativar).")]
    [SerializeField] private GameObject uiRoot;

    [Header("Visibilidade")]
    [Tooltip("Quanto tempo a barra permanece visível após a última atualização.")]
    [SerializeField] private float hideDelay = 2.0f;

    private EnemyPoise _poise;
    private float      _hideTimer;

    void Awake()
    {
        _poise = GetComponent<EnemyPoise>();
        if (_poise == null)
        {
            Debug.LogWarning("[PoiseIndicator] EnemyPoise não encontrado no mesmo GameObject.");
            return;
        }

        _poise.OnPoiseChanged += UpdateBar;
        _poise.OnPoiseBreak   += OnBreak;
        _poise.OnPoiseRecover += OnRecover;

        if (uiRoot != null) uiRoot.SetActive(false);
    }

    void OnDestroy()
    {
        if (_poise == null) return;
        _poise.OnPoiseChanged -= UpdateBar;
        _poise.OnPoiseBreak   -= OnBreak;
        _poise.OnPoiseRecover -= OnRecover;
    }

    void Update()
    {
        if (uiRoot == null || !uiRoot.activeSelf) return;

        _hideTimer -= Time.deltaTime;
        if (_hideTimer <= 0f)
            uiRoot.SetActive(false);
    }

    private void UpdateBar(float current, float max)
    {
        if (poiseSlider != null)
            poiseSlider.value = max > 0f ? current / max : 0f;

        if (uiRoot != null) uiRoot.SetActive(true);
        _hideTimer = hideDelay;
    }

    private void OnBreak()
    {
        // Pisca a barra ou muda de cor ao quebrar a postura
        if (uiRoot != null) uiRoot.SetActive(false);
    }

    private void OnRecover()
    {
        // Opcional: resetar cor da barra após stagger
    }
}