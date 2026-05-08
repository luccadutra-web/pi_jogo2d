using System.Collections;
using UnityEngine;


/// </summary>
public class CameraShake : MonoBehaviour
{
    public static CameraShake Instance { get; private set; }

    [Header("Limites")]
    [SerializeField] private float maxIntensity = 0.5f;
    [SerializeField] private float maxDuration  = 0.5f;

    private Vector3   _originalPos;
    private Coroutine _routine;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance     = this;
        _originalPos = transform.localPosition;
    }

    
    public void Shake(float intensity, float duration)
    {
        intensity = Mathf.Clamp(intensity, 0f, maxIntensity);
        duration  = Mathf.Clamp(duration,  0f, maxDuration);

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(ShakeRoutine(intensity, duration));
    }

    private IEnumerator ShakeRoutine(float intensity, float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime; 

           
            float strength = Mathf.Lerp(intensity, 0f, elapsed / duration);

            float x = Random.Range(-1f, 1f) * strength;
            float y = Random.Range(-1f, 1f) * strength;

            transform.localPosition = _originalPos + new Vector3(x, y, 0f);

            yield return null;
        }

        transform.localPosition = _originalPos;
        _routine = null;
    }

   
    public void Cancel()
    {
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
        transform.localPosition = _originalPos;
    }
}