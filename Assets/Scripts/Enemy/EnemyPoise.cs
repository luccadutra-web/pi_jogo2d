using System.Collections;
using UnityEngine;

/// <summary>
/// Sistema de Postura (Poise) — inspirado em Blasphemous e Sekiro.
///
/// Cada hit acumula dano de postura. Quando a postura zera, o inimigo entra
/// em Stagger. A postura se regenera automaticamente após um delay sem receber hits.
///
/// Como conectar:
/// 1. Adicione este componente no mesmo GameObject do EnemyBehavior.
/// 2. Em MeleeWeapon.ApplyHit, chame enemyPoise.ReceivePoiseHit(poiseDamage).
/// 3. Inscreva-se em OnPoiseBreak para disparar o stagger visual (animação, lock, etc).
/// 4. Opcionalmente exiba a barra de postura na UI quando o inimigo está em combate.
/// </summary>
public class EnemyPoise : MonoBehaviour
{
    [Header("Postura")]
    [Tooltip("Postura máxima. Valores maiores = inimigo mais resistente a stagger.")]
    [SerializeField] private float maxPoise         = 100f;

    [Header("Regeneração")]
    [Tooltip("Tempo em segundos sem receber hit para a postura começar a regenerar.")]
    [SerializeField] private float poiseRegenDelay  = 2.5f;
    [Tooltip("Postura regenerada por segundo após o delay.")]
    [SerializeField] private float poiseRegenRate   = 40f;

    [Header("Stagger")]
    [Tooltip("Quanto tempo o inimigo fica em stagger após a postura quebrar.")]
    [SerializeField] private float staggerDuration  = 1.8f;

    [Header("Debug")]
    [SerializeField] private bool debugPoise = false;

    private float _currentPoise;
    private float _regenTimer;
    private bool  _isStaggered;

    // Escute este evento para disparar lógica de stagger no EnemyBehavior
    public System.Action OnPoiseBreak;
    // Escute para saber quando o inimigo se recupera do stagger
    public System.Action OnPoiseRecover;
    // Para UI — (currentPoise, maxPoise)
    public System.Action<float, float> OnPoiseChanged;

    public float CurrentPoise    => _currentPoise;
    public float MaxPoise        => maxPoise;
    public float PoiseNormalized => maxPoise > 0f ? _currentPoise / maxPoise : 0f;
    public bool  IsStaggered     => _isStaggered;

    void Awake()
    {
        _currentPoise = maxPoise;
    }

    void Update()
    {
        if (_isStaggered) return;
        if (_currentPoise >= maxPoise) return;

        if (_regenTimer > 0f)
        {
            _regenTimer -= Time.deltaTime;
            return;
        }

        _currentPoise = Mathf.Min(_currentPoise + poiseRegenRate * Time.deltaTime, maxPoise);
        OnPoiseChanged?.Invoke(_currentPoise, maxPoise);
    }

    /// <summary>
    /// Chame este método de MeleeWeapon ao acertar o inimigo.
    /// poiseDamage sugerido: light = 20, heavy = 45, finisher = 100 (quebra sempre).
    /// </summary>
    public void ReceivePoiseHit(float poiseDamage)
    {
        if (_isStaggered) return;

        _currentPoise -= poiseDamage;
        _regenTimer    = poiseRegenDelay;
        OnPoiseChanged?.Invoke(_currentPoise, maxPoise);

        if (debugPoise)
            Debug.Log($"[Poise] Hit recebido: -{poiseDamage} | Postura: {_currentPoise:F1}/{maxPoise}");

        if (_currentPoise <= 0f)
            StartCoroutine(StaggerRoutine());
    }

    private IEnumerator StaggerRoutine()
    {
        _isStaggered  = true;
        _currentPoise = maxPoise; // reseta ao quebrar

        if (debugPoise) Debug.Log("[Poise] POSTURA QUEBRADA — stagger!");

        OnPoiseBreak?.Invoke();
        OnPoiseChanged?.Invoke(_currentPoise, maxPoise);

        yield return new WaitForSeconds(staggerDuration);

        _isStaggered = false;
        OnPoiseRecover?.Invoke();

        if (debugPoise) Debug.Log("[Poise] Inimigo se recuperou do stagger.");
    }

    /// <summary>
    /// Reseta a postura instantaneamente (use ao matar ou respawnar o inimigo).
    /// </summary>
    public void ResetPoise()
    {
        StopAllCoroutines();
        _currentPoise = maxPoise;
        _isStaggered  = false;
        _regenTimer   = 0f;
        OnPoiseChanged?.Invoke(_currentPoise, maxPoise);
    }
}