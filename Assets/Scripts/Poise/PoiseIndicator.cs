using UnityEngine;

/// <summary>
/// PoiseIndicator — componente auxiliar no GameObject do inimigo.
///
/// Com o HUD centralizado (PlayerHUD), este componente NÃO é necessário
/// para exibir a barra de postura — o PlayerHUD já escuta os eventos de
/// EnemyPoise diretamente.
///
/// Use este componente SOMENTE se quiser uma barra World Space flutuando
/// sobre o inimigo (estilo barra de boss). Para isso:
///   1. Crie um Canvas (World Space) filho do inimigo.
///   2. Adicione um filho com Image (Filled, Horizontal) dentro do Canvas.
///   3. Preencha os campos abaixo e ative useWorldSpaceBar = true.
///
/// Caso contrário, deixe este script fora da cena — o PlayerHUD cuida de tudo.
/// </summary>
public class PoiseIndicator : MonoBehaviour
{
    [Tooltip("Ative apenas se quiser barra World Space sobre o inimigo além do HUD.")]
    [SerializeField] private bool useWorldSpaceBar = false;

    [Header("World Space Bar (opcional)")]
    [SerializeField] private UnityEngine.UI.Image fillImage;
    [SerializeField] private GameObject           uiRoot;
    [SerializeField] private float                hideDelay = 2.0f;

    private EnemyPoise _poise;
    private float      _hideTimer;

    void Awake()
    {
        if (!useWorldSpaceBar) return;

        _poise = GetComponent<EnemyPoise>();
        if (_poise == null) { Debug.LogWarning("[PoiseIndicator] EnemyPoise não encontrado."); return; }

        _poise.OnPoiseChanged += OnPoiseChanged;
        _poise.OnPoiseBreak   += OnPoiseBreak;
        _poise.OnPoiseRecover += OnPoiseRecover;

        if (uiRoot != null) uiRoot.SetActive(false);
    }

    void OnDestroy()
    {
        if (!useWorldSpaceBar || _poise == null) return;
        _poise.OnPoiseChanged -= OnPoiseChanged;
        _poise.OnPoiseBreak   -= OnPoiseBreak;
        _poise.OnPoiseRecover -= OnPoiseRecover;
    }

    void Update()
    {
        if (!useWorldSpaceBar || uiRoot == null || !uiRoot.activeSelf) return;
        _hideTimer -= Time.deltaTime;
        if (_hideTimer <= 0f) uiRoot.SetActive(false);
    }

    private void OnPoiseChanged(float current, float max)
    {
        if (fillImage != null) fillImage.fillAmount = max > 0f ? current / max : 0f;
        if (uiRoot != null) uiRoot.SetActive(true);
        _hideTimer = hideDelay;
    }

    private void OnPoiseBreak()
    {
        if (uiRoot != null) uiRoot.SetActive(false);
    }

    private void OnPoiseRecover() { }
}