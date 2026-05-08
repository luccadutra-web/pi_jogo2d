using UnityEngine;

/// <summary>
/// Gerencia a barra de especial do player.
/// Carrega chamando AddKill() na morte de cada inimigo.
/// Chame TrySpend() antes de ativar o especial.
/// </summary>
public class PlayerSpecialGauge : MonoBehaviour
{
    public static PlayerSpecialGauge Instance { get; private set; }

    [Header("Barra")]
    [SerializeField] private float maxGauge      = 100f;
    [SerializeField] private float startGauge    = 0f;

    [Header("Ganho")]
    [Tooltip("Quanto de gauge ganha por inimigo morto")]
    [SerializeField] private float gaugePerKill  = 34f;   // 3 kills = barra cheia

    [Header("Custo")]
    [SerializeField] private float specialCost   = 100f;

    private float _currentGauge;

    // Eventos para UI
    public System.Action<float, float> OnGaugeChanged;  // (current, max)
    public System.Action               OnGaugeFull;
    public System.Action               OnGaugeSpent;

    public float CurrentGauge    => _currentGauge;
    public float MaxGauge        => maxGauge;
    public float GaugeNormalized => maxGauge > 0f ? _currentGauge / maxGauge : 0f;
    public bool  IsFull          => _currentGauge >= maxGauge;

    void Awake()
    {
        Instance       = this;
        _currentGauge  = Mathf.Clamp(startGauge, 0f, maxGauge);
    }

    /// <summary>
    /// Chame este método na morte de um inimigo.
    /// Exemplo: GetComponent<PlayerSpecialGauge>()?.AddKill();
    /// </summary>
    public void AddKill(float multiplier = 1f)
    {
        bool wasFull = IsFull;

        _currentGauge = Mathf.Min(_currentGauge + gaugePerKill * multiplier, maxGauge);
        OnGaugeChanged?.Invoke(_currentGauge, maxGauge);

        if (!wasFull && IsFull)
            OnGaugeFull?.Invoke();
    }

    /// <summary>
    /// Tenta gastar a barra para ativar o especial.
    /// Retorna true se havia gauge suficiente.
    /// </summary>
    public bool TrySpend()
    {
        if (_currentGauge < specialCost) return false;

        _currentGauge = Mathf.Max(_currentGauge - specialCost, 0f);
        OnGaugeChanged?.Invoke(_currentGauge, maxGauge);
        OnGaugeSpent?.Invoke();
        return true;
    }

    /// <summary>
    /// Devolve o custo à barra (chamado se o especial for cancelado por erro).
    /// </summary>
    public void Refund()
    {
        _currentGauge = Mathf.Min(_currentGauge + specialCost, maxGauge);
        OnGaugeChanged?.Invoke(_currentGauge, maxGauge);
    }
}