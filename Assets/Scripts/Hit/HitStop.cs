using System.Collections;
using UnityEngine;

/// <summary>
/// HitStop — freeze global de Time.timeScale para reforçar o peso dos impactos.
///
/// ── COMO FUNCIONA ─────────────────────────────────────────────────────────────
///
///   DoHitStop(duration) zera Time.timeScale por `duration` segundos (tempo real),
///   depois restaura para 1. O efeito congela tudo que depende de Time.deltaTime:
///   animações, física, partículas, AI — dando a sensação de "impacto com peso".
///
/// ── CORREÇÕES NESTA VERSÃO ────────────────────────────────────────────────────
///
///   1. PRIORIDADE POR DURAÇÃO
///      Antes: qualquer chamada enquanto o freeze estava ativo era descartada.
///      Problema: parry (0.28s) chegando depois de um light attack (0.04s) era
///      ignorado, cortando o feedback do momento mais importante do combate.
///      Agora: uma chamada com duração MAIOR sempre substitui a atual.
///
///   2. ÁUDIO NÃO PAUSA COM O FREEZE
///      AudioListener.pause = false é garantido explicitamente na rotina.
///      Sem isso, AudioSources em modo padrão silenciam durante o timeScale = 0,
///      e os SFX de impacto nunca seriam ouvidos — justamente quando mais importam.
///
///   3. COMBO NÃO TRAVA DURANTE O FREEZE
///      MeleeWeapon.AttackRoutine() aguardava HitStop.IsActive antes de abrir
///      a janela de combo. Com timeScale = 0, esse while nunca avançava durante
///      o freeze — o player sentia a trava como lag no combate.
///      SOLUÇÃO: remova o bloco abaixo de MeleeWeapon (ver seção MIGRAÇÃO):
///
///        // REMOVER de AttackRoutine(), após a fase active:
///        if (HitStop.Instance != null)
///            while (HitStop.Instance.IsActive)
///                yield return null;
///
///      O player não percebe a diferença porque a tela está congelada de qualquer
///      forma. A janela de combo já estará aberta quando o freeze terminar.
///
/// ── DURAÇÕES RECOMENDADAS ─────────────────────────────────────────────────────
///
///   Light attack   → 0.04s  (quase imperceptível, só "toc")
///   Heavy attack   → 0.14s  (claramente pesado, sem travar o ritmo)
///   Finisher       → 0.28s  (dramático, reservado para o golpe final)
///   Parry          → 0.28s  (igual ao finisher — é o momento mais importante)
///   Counter attack → valor do tipo × counterImpactMultiplier (em MeleeWeapon)
///   Especial       → configurável em PlayerSpecial
///
/// ── SETUP ─────────────────────────────────────────────────────────────────────
///
///   Adicione este componente a qualquer GameObject persistente na cena
///   (ex: o mesmo GameObject do AudioManager ou um "GameManager" dedicado).
///   Não requer referências externas — acesso via HitStop.Instance.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public class HitStop : MonoBehaviour
{
    public static HitStop Instance { get; private set; }

    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Limites de duração")]
    [Tooltip("Duração mínima aceita. Valores abaixo são elevados a este piso.")]
    [SerializeField] private float minDuration = 0.03f;

    [Tooltip("Duração máxima aceita. Protege contra chamadas acidentais com valores altos.")]
    [SerializeField] private float maxDuration = 0.30f;

    // ─── Estado interno ───────────────────────────────────────────────────────

    private Coroutine _routine;
    private float _currentDuration;

    // ─── API pública ──────────────────────────────────────────────────────────

    /// <summary>True enquanto o freeze estiver ativo.</summary>
    public bool IsActive => _routine != null;

    // ─── Unity ────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ─── Métodos públicos ─────────────────────────────────────────────────────

    /// <summary>
    /// Inicia o freeze por <paramref name="duration"/> segundos (tempo real).
    ///
    /// Se já houver um freeze ativo:
    ///   • Nova duração MAIOR  → substitui (parry sobrepõe light attack).
    ///   • Nova duração MENOR  → ignorada  (light attack não corta um finisher).
    /// </summary>
    public void DoHitStop(float duration)
    {
        duration = Mathf.Clamp(duration, minDuration, maxDuration);

        // Já ativo: só substitui se a nova duração for mais longa.
        // Garante que parry (0.28s) nunca seja cortado por um light (0.04s)
        // que chegue logo em seguida no mesmo frame.
        if (_routine != null)
        {
            if (duration <= _currentDuration) return;
            StopCoroutine(_routine);
        }

        _currentDuration = duration;
        _routine = StartCoroutine(HitStopRoutine(duration));
    }

    /// <summary>
    /// Cancela o freeze imediatamente e restaura Time.timeScale.
    /// Use em situações excepcionais (morte do player, troca de cena, pause).
    /// </summary>
    public void Cancel()
    {
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }

        Time.timeScale = 1f;
        AudioListener.pause = false;
    }

    // ─── Rotina interna ───────────────────────────────────────────────────────

    private System.Collections.IEnumerator HitStopRoutine(float duration)
    {
        Time.timeScale = 0f;

        // Garante que o áudio continue durante o freeze.
        // Com timeScale = 0, AudioSources param por padrão — o SFX de impacto
        // nunca seria ouvido justamente no momento em que mais importa.
        AudioListener.pause = false;

        yield return new WaitForSecondsRealtime(duration);

        Time.timeScale = 1f;
        _routine = null;
        _currentDuration = 0f;
    }
}