using System.Collections;
using UnityEngine;


public class HitStop : MonoBehaviour
{
    public static HitStop Instance { get; private set; }

    [Header("Limites")]
    [SerializeField] private float minDuration = 0.03f;
    [SerializeField] private float maxDuration = 0.25f;

    private Coroutine routine;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void DoHitStop(float duration)
    {
        duration = Mathf.Clamp(duration, minDuration, maxDuration);

        
        if (routine != null) return;

        routine = StartCoroutine(HitStopRoutine(duration));
    }

    private IEnumerator HitStopRoutine(float duration)
    {
        Time.timeScale = 0f;
        yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = 1f;
        routine = null;
    }

    public void Cancel()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }
        Time.timeScale = 1f;
    }
}