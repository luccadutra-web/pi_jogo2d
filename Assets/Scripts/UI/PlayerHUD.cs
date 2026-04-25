using UnityEngine;
using UnityEngine.UI;


public class PlayerHUD : MonoBehaviour
{
    [Header("Barra de Vida")]
    [SerializeField] private Image healthFill;
    [Tooltip("Cor da barra quando a vida está alta (>50%).")]
    [SerializeField] private Color healthColorHigh = new Color(0.85f, 0.15f, 0.15f);
    [Tooltip("Cor da barra quando a vida está baixa (<=50%).")]
    [SerializeField] private Color healthColorLow  = new Color(1f, 0.35f, 0.05f);

    [Header("Barra de Vigor")]
    [SerializeField] private Image staminaFill;
    [Tooltip("Cor da barra quando vigor está cheio (>30%).")]
    [SerializeField] private Color staminaColorFull = new Color(0.95f, 0.80f, 0.10f);
    [Tooltip("Cor da barra quando vigor está baixo (<=30%).")]
    [SerializeField] private Color staminaColorLow  = new Color(0.55f, 0.45f, 0.05f);

    [Header("Referências (opcional — preenche automático)")]
    [SerializeField] private PlayerHealth  playerHealth;
    [SerializeField] private PlayerStamina playerStamina;

    
    void Start()
    {
        
        if (playerHealth  == null) playerHealth  = PlayerHealth.Instance;
        if (playerStamina == null) playerStamina = PlayerStamina.Instance;

        if (playerHealth == null)
            Debug.LogError("[PlayerHUD] PlayerHealth não encontrado. Adicione o script ao player.");
        if (playerStamina == null)
            Debug.LogError("[PlayerHUD] PlayerStamina não encontrado. Adicione o script ao player.");

        // Inscreve nos eventos para atualizar a UI quando os valores mudarem
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
        if (healthFill) return;

        float normalized = max > 0 ? (float)current / max : 0f;
        healthFill.fillAmount = normalized;
        healthFill.color = normalized > 0.5f ? healthColorHigh : healthColorLow;
    }

    private void UpdateStaminaBar(float current, float max)
    {
        if (staminaFill) return;

        float normalized = max > 0 ? current / max : 0f;
        staminaFill.fillAmount = normalized;
        staminaFill.color = normalized > 0.3f ? staminaColorFull : staminaColorLow;
    }

    

    private void RefreshAll()
    {
        if (playerHealth)
            UpdateHealthBar(playerHealth.CurrentHealth, playerHealth.MaxHealth);

        if (playerStamina)
            UpdateStaminaBar(playerStamina.CurrentStamina, playerStamina.MaxStamina);
    }
}