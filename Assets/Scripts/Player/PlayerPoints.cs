using UnityEngine;

public class PlayerPoints : MonoBehaviour
{
    [Header("Pontos")]
    [SerializeField] private int maxPoints     = 100;
    [SerializeField] private int startingPoints = 0;  

    private int currentPoints;

    void Start()
    {
        currentPoints = startingPoints;
    }

    public void AddPoints(int amount)
    {
        currentPoints = Mathf.Min(currentPoints + amount, maxPoints);
        Debug.Log($"Pontos: {currentPoints}");
    }

    public bool UsePoints(int amount)
    {
        if (currentPoints < amount)
        {
            Debug.Log("Sem pontos suficientes");
            return false;
        }
        currentPoints -= amount;
        Debug.Log($"Gastou {amount} pontos. Restante: {currentPoints}");
        return true;
    }

    public int GetPoints() => currentPoints;

    
    public float GetPointsNormalized() => maxPoints > 0 ? (float)currentPoints / maxPoints : 0f;
}