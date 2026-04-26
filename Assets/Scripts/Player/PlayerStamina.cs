using UnityEngine;


public class PlayerStamina : MonoBehaviour
{
    
    public static PlayerStamina Instance { get; private set; }

    
    [Header("Vigor")]
    [SerializeField] private float maxStamina     = 100f;
    [SerializeField] private float startStamina   = 100f;

    [Header("Custos")]
    [SerializeField] private float costDash            = 20f;
    [SerializeField] private float costHeavyAttack     = 25f;
    [SerializeField] private float costDefendPerSecond = 15f;

    [Header("Regeneração")]
    [Tooltip("Segundos sem gastar vigor antes de começar a regenerar.")]
    [SerializeField] private float regenDelay = 1.2f;
    [Tooltip("Vigor regenerado por segundo.")]
    [SerializeField] private float regenRate  = 20f;

   
    private float currentStamina;
    private float regenTimer;       // conta regressivamente até 0 para iniciar regen

    
    public System.Action<float, float> OnStaminaChanged; // (current, max)

    
    public float CurrentStamina   => currentStamina;
    public float MaxStamina       => maxStamina;
    public float StaminaNormalized => maxStamina > 0 ? currentStamina / maxStamina : 0f;

    public float CostDash        => costDash;
    public float CostHeavyAttack => costHeavyAttack;

    
    void Awake()
    {
        Instance       = this;
        currentStamina = startStamina;
    }

    void Update()
    {
        HandleRegen();
    }

    
    public bool UseStaminaDash()
    {
        return TrySpend(costDash);
    }

    
    public bool UseStaminaHeavy()
    {
        return TrySpend(costHeavyAttack);
    }

    
    public bool DrainDefendStamina(float deltaTime)
    {
        if (currentStamina <= 0f) return false;

        Spend(costDefendPerSecond * deltaTime);
        return currentStamina > 0f;
    }



    private bool TrySpend(float amount)
    {
        if (currentStamina < amount)
        {
            Debug.Log("[PlayerStamina] Vigor insuficiente.");
            return false;
        }
        Spend(amount);
        return true;
    }

    private void Spend(float amount)
    {
        currentStamina = Mathf.Max(currentStamina - amount, 0f);
        regenTimer     = regenDelay;   // reinicia o delay de regen
        OnStaminaChanged?.Invoke(currentStamina, maxStamina);
    }

    private void HandleRegen()
    {
        if (currentStamina >= maxStamina) return;

        if (regenTimer > 0f)
        {
            regenTimer -= Time.deltaTime;
            return;
        }

        currentStamina = Mathf.Min(currentStamina + regenRate * Time.deltaTime, maxStamina);
        OnStaminaChanged?.Invoke(currentStamina, maxStamina);
    }
}