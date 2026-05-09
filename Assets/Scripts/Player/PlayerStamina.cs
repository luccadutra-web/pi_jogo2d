using UnityEngine;


public class PlayerStamina : MonoBehaviour
{
    public static PlayerStamina Instance { get; private set; }

    [Header("Vigor")]
    [SerializeField] private float maxStamina   = 100f;
    [SerializeField] private float startStamina = 100f;

    [Header("Custos")]
    [SerializeField] private float costDash        = 20f;
    [SerializeField] private float costHeavyAttack = 25f;

    [Header("Regeneração")]
    [SerializeField] private float regenDelay = 1.2f;
    [SerializeField] private float regenRate  = 20f;

    private float currentStamina;
    private float regenTimer;

    public System.Action<float, float> OnStaminaChanged; 
    public System.Action               OnStaminaEmpty;   

    public float CurrentStamina    => currentStamina;
    public float MaxStamina        => maxStamina;
    public float StaminaNormalized => maxStamina > 0 ? currentStamina / maxStamina : 0f;

    void Awake()
    {
        Instance       = this;
        currentStamina = startStamina;
    }

    void Update()
    {
        HandleRegen();
    }

    

    public bool UseDash()
    {
        return TrySpend(costDash);
    }

    public bool UseHeavyAttack()
    {
        return TrySpend(costHeavyAttack);
    }

    
    public bool Spend(float amount)
    {
        if (currentStamina <= 0f)
        {
            OnStaminaEmpty?.Invoke();
            Debug.Log("Stamina= "+currentStamina + "/" + maxStamina);
            return false; 
            
        }

        currentStamina = Mathf.Max(currentStamina - amount, 0f);
        regenTimer     = regenDelay;
        OnStaminaChanged?.Invoke(currentStamina, maxStamina);

        if (currentStamina <= 0f)
        {
            OnStaminaEmpty?.Invoke();
            return false;
        }

        return true;
    }

   

    private bool TrySpend(float amount)
    {
        if (currentStamina < amount) return false;
        Spend(amount);
        return true;
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