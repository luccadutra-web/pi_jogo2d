using UnityEngine;
using UnityEngine.UI;

public class PlayerHUD : MonoBehaviour
{
    [Header("Barra de Vida")]
    [SerializeField] private Image healthFill;
    [SerializeField] private Color healthColorHigh = new Color(0.85f, 0.15f, 0.15f);
    [SerializeField] private Color healthColorLow  = new Color(1f, 0.35f, 0.05f);

    [Header("Barra de Vigor")]
    [SerializeField] private Image staminaFill;
    [SerializeField] private Color staminaColorFull = new Color(0.95f, 0.80f, 0.10f);
    [SerializeField] private Color staminaColorLow  = new Color(0.55f, 0.45f, 0.05f);

    [Header("Referências (opcional — preenche automático)")]
    [SerializeField] private PlayerHealth  playerHealth;
    [SerializeField] private PlayerStamina playerStamina;

    void Start()
    {
        if (playerHealth  == null) playerHealth  = PlayerHealth.Instance;
        if (playerStamina == null) playerStamina = PlayerStamina.Instance;

        if (playerHealth  == null) Debug.LogError("[PlayerHUD] PlayerHealth não encontrado.");
        if (playerStamina == null) Debug.LogError("[PlayerHUD] PlayerStamina não encontrado.");

        if (playerHealth  != null) playerHealth.OnHealthChanged   += UpdateHealthBar;
        if (playerStamina != null) playerStamina.OnStaminaChanged += UpdateStaminaBar;

        RefreshAll();
    }

    void OnDestroy()
    {
        if (playerHealth  != null) playerHealth.OnHealthChanged   -= UpdateHealthBar;
        if (playerStamina != null) playerStamina.OnStaminaChanged -= UpdateStaminaBar;
    }

    private void UpdateHealthBar(int current, int max)
    {
        // FIX: era "if (healthFill) return" — saía quando a referência existia,
        // nunca atualizava. Trocado para guard de nulo: sai só se não tiver referência
        if (!healthFill) return;

        float normalized      = max > 0 ? (float)current / max : 0f;
        healthFill.fillAmount = normalized;
        healthFill.color      = normalized > 0.5f ? healthColorHigh : healthColorLow;
    }

    private void UpdateStaminaBar(float current, float max)
    {
        // FIX: mesmo problema — era "if (staminaFill) return"
        if (!staminaFill) return;

        float normalized       = max > 0 ? current / max : 0f;
        staminaFill.fillAmount = normalized;
        staminaFill.color      = normalized > 0.3f ? staminaColorFull : staminaColorLow;
    }

    private void RefreshAll()
    {
        if (playerHealth)
            UpdateHealthBar(playerHealth.CurrentHealth, playerHealth.MaxHealth);

        if (playerStamina)
            UpdateStaminaBar(playerStamina.CurrentStamina, playerStamina.MaxStamina);
    }
}