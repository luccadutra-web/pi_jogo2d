using UnityEngine;

/// <summary>
/// BloodHitOffset — offset LOCAL e escala do efeito de sangue para este inimigo.
///
/// ── POR QUE ESPACO LOCAL ─────────────────────────────────────────────────────
///
///   O inimigo usa transform.localScale.x negativo para flipar.
///   Com LOCAL space, TransformPoint resolve automaticamente:
///   o offset acompanha rotacao, escala e flip sem ajuste manual.
///
/// ── ESCALA ───────────────────────────────────────────────────────────────────
///
///   lightScale / heavyScale sao aplicados via startSizeMultiplier no
///   ParticleSystem antes do Play(). Isso nao altera os valores base do
///   prefab — cada instancia do pool recebe o multiplicador correto.
///
///   Valores de referencia:
///     Corvo (pequeno) → lightScale = 0.6 / heavyScale = 0.75
///     Inimigo medio   → lightScale = 1.0 / heavyScale = 1.0
///     Chefe (grande)  → lightScale = 1.6 / heavyScale = 2.0
///
/// ── SETUP ────────────────────────────────────────────────────────────────────
///
///   1. Add Component → BloodHitOffset no prefab do inimigo
///   2. Ajuste os offsets e escalas no Inspector
///   3. Gizmos mostram os pontos no Editor sem precisar dar Play
///
///   Sem este componente, VfxManager usa os valores padrao do seu Inspector.
/// </summary>
public class BloodHitOffset : MonoBehaviour
{
    [Header("Offset (espaco LOCAL do inimigo)")]

    [Tooltip("Offset LOCAL para golpe LEVE.\n" +
             "Ex: (0, 0.3) sobe o efeito acima do pivot.")]
    [SerializeField] private Vector2 lightOffset = new Vector2(0f, 0.3f);

    [Tooltip("Offset LOCAL para golpe PESADO.")]
    [SerializeField] private Vector2 heavyOffset = new Vector2(0f, 0.35f);

    [Header("Escala do ParticleSystem")]

    [Tooltip("Multiplicador de tamanho para golpe LEVE.\n" +
             "1 = tamanho padrao do prefab.\n" +
             "Corvo: ~0.6  |  Inimigo medio: 1.0  |  Chefe: ~1.8")]
    [Min(0.01f)]
    [SerializeField] private float lightScale = 1f;

    [Tooltip("Multiplicador de tamanho para golpe PESADO.\n" +
             "Geralmente >= lightScale.")]
    [Min(0.01f)]
    [SerializeField] private float heavyScale = 1f;

    // ── API publica ───────────────────────────────────────────────────────────

    public Vector2 LightOffset => lightOffset;
    public Vector2 HeavyOffset => heavyOffset;
    public float   LightScale  => lightScale;
    public float   HeavyScale  => heavyScale;

    // ── Gizmos ────────────────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        DrawGizmo(lightOffset, new Color(1f, 0.5f, 0.5f, 0.5f), 0.07f);
        DrawGizmo(heavyOffset, new Color(1f, 0f,   0f,   0.7f), 0.10f);
    }

    private void OnDrawGizmosSelected()
    {
        DrawGizmo(lightOffset, new Color(1f, 0.5f, 0.5f, 1f), 0.09f, "Light");
        DrawGizmo(heavyOffset, new Color(1f, 0f,   0f,   1f), 0.12f, "Heavy");
    }

    private void DrawGizmo(Vector2 localOffset, Color color, float radius, string label = null)
    {
        Vector3 worldPos = transform.TransformPoint(new Vector3(localOffset.x, localOffset.y, 0f));

        Gizmos.color = color;
        Gizmos.DrawSphere(worldPos, radius);
        Gizmos.DrawLine(transform.position, worldPos);

#if UNITY_EDITOR
        if (label != null)
            UnityEditor.Handles.Label(worldPos + Vector3.up * 0.15f, label);
#endif
    }
}