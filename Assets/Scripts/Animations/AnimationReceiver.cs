/// <summary>
/// Receptor vazio de Animation Events.
///
/// Adicione este componente em qualquer GameObject que compartilhe clipes de
/// animação com o Player mas não possua MeleeWeapon ou PlayerHealth — por
/// exemplo, inimigos usados para testes visuais...
///
/// Nenhum dos métodos faz nada: eles existem apenas para que o Unity encontre
/// um receptor e pare de emitir o warning "has no receiver".
/// Quando o Enemy ganhar seu próprio sistema de combate, remova este componente.
/// </summary>
public class AnimationEventReceiver : UnityEngine.MonoBehaviour
{
    public void OnAttackActiveStart()  { }
    public void OnAttackActiveEnd()    { }
    public void OnAttackRecoveryEnd()  { }
    public void OnComboWindowOpen()    { }
    public void OnComboWindowClose()   { }
    public void OnParryWindowOpen()    { }
    public void OnParryWindowClose()   { }
}