using System.Collections;
using UnityEngine;

/// <summary>
/// VfxAutoReturn — devolve automaticamente um objeto poolado ao VfxManager
/// após um lifetime configurado.
///
/// Adicionado dinamicamente por VfxManager.EnsureAutoReturn().
/// Não precisa ser adicionado manualmente aos prefabs, mas pode ser
/// pré-adicionado para configurar um lifetime padrão no Inspector.
/// </summary>
public class VfxAutoReturn : MonoBehaviour
{
    private GameObject  _prefab;
    private float       _lifetime;
    private VfxManager  _manager;
    private Coroutine   _routine;

    /// <summary>
    /// Configura e inicia o timer de retorno ao pool.
    /// Chamado por VfxManager após ativar o objeto.
    /// </summary>
    public void Initialize(GameObject instance, GameObject prefab, float lifetime, VfxManager manager)
    {
        _prefab   = prefab;
        _lifetime = lifetime;
        _manager  = manager;

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(ReturnAfterDelay());
    }

    private void OnDisable()
    {
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
    }

    private IEnumerator ReturnAfterDelay()
    {
        yield return new WaitForSeconds(_lifetime);
        _manager?.ReturnToPool(gameObject, _prefab);
    }
}