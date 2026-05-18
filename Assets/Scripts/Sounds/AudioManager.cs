using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AudioManager — singleton central de áudio do jogo.
///
/// ── ARQUITETURA ──────────────────────────────────────────────────────────────
///
///   Três camadas de áudio:
///
///   1. SFX          — efeitos pontuais (ataques, passos, parry, hurt...)
///                     Pool de AudioSources para múltiplos sons simultâneos.
///
///   2. Music        — trilha de fundo com crossfade suave entre faixas.
///                     Dois AudioSources alternados (A/B) para transições.
///
///   3. Ambient      — som ambiente (vento, floresta, dungeon...).
///                     AudioSource dedicado com fade in/out.
///
/// ── COMO CHAMAR DE QUALQUER SCRIPT ───────────────────────────────────────────
///
///   AudioManager.Instance.PlaySFX("player_jump");
///   AudioManager.Instance.PlaySFX("sword_light_1", volume: 0.8f, pitch: 1.1f);
///   AudioManager.Instance.PlayMusic("battle_theme");
///   AudioManager.Instance.StopMusic();
///
/// ── SETUP NO INSPECTOR ───────────────────────────────────────────────────────
///
///   1. Crie um GameObject "AudioManager" na cena (preferencialmente na raiz,
///      num objeto "Managers" que sobreviva ao DontDestroyOnLoad).
///   2. Adicione este componente.
///   3. Arraste os AudioClips nos arrays do Inspector.
///   4. Nomeie cada entrada (key) — esse nome é usado nas chamadas Play.
///   5. Ajuste sfxPoolSize (padrão: 8) se precisar de mais sons simultâneos.
///
/// ── DONTDESTROYONLOAD ────────────────────────────────────────────────────────
///
///   Se o jogo usa múltiplas cenas, marque persistBetweenScenes = true no
///   Inspector. O AudioManager sobreviverá e não duplicará.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public class AudioManager : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────

    public static AudioManager Instance { get; private set; }

    // ── Configuração ──────────────────────────────────────────────────────────

    [Header("Configurações gerais")]
    [Tooltip("Se true, o AudioManager sobrevive entre cenas (DontDestroyOnLoad).")]
    [SerializeField] private bool persistBetweenScenes = true;

    [Header("Volumes mestres")]
    [Range(0f, 1f)] [SerializeField] private float masterVolume  = 1f;
    [Range(0f, 1f)] [SerializeField] private float sfxVolume     = 1f;
    [Range(0f, 1f)] [SerializeField] private float musicVolume   = 0.6f;
    [Range(0f, 1f)] [SerializeField] private float ambientVolume = 0.4f;

    [Header("Pool de SFX")]
    [Tooltip("Quantidade de AudioSources simultâneos para SFX. 8 é suficiente para combate.")]
    [SerializeField] private int sfxPoolSize = 8;

    [Header("Música — crossfade")]
    [Tooltip("Duração do crossfade entre músicas (segundos).")]
    [SerializeField] private float musicCrossfadeDuration = 1.5f;

    [Header("Clips de SFX")]
    [SerializeField] private SfxEntry[] sfxClips;

    [Header("Clips de Música")]
    [SerializeField] private MusicEntry[] musicClips;

    [Header("Clips de Ambiente")]
    [SerializeField] private AmbientEntry[] ambientClips;

    // ── Estado interno ────────────────────────────────────────────────────────

    // Pool de SFX
    private AudioSource[] _sfxPool;
    private int _sfxPoolIndex;

    // Música (crossfade A/B)
    private AudioSource _musicA;
    private AudioSource _musicB;
    private bool _musicOnA = true;
    private Coroutine _crossfadeRoutine;

    // Ambiente
    private AudioSource _ambientSource;
    private Coroutine _ambientFadeRoutine;

    // Lookups rápidos
    private Dictionary<string, AudioClip[]> _sfxMap;
    private Dictionary<string, AudioClip>   _musicMap;
    private Dictionary<string, AudioClip>   _ambientMap;

    // Pitch state (para slomo sem alterar pitch global)
    private float _globalPitchScale = 1f;

    // ── Structs de entrada do Inspector ───────────────────────────────────────

    [System.Serializable]
    public struct SfxEntry
    {
        [Tooltip("Nome usado na chamada PlaySFX(\"nome\").")]
        public string key;

        [Tooltip("Um ou mais clips — um será escolhido aleatoriamente a cada play.\n" +
                 "Ótimo para variação: adicione 2-3 variações do mesmo som.")]
        public AudioClip[] clips;

        [Range(0f, 1f)]
        [Tooltip("Volume base deste SFX. Multiplicado pelo sfxVolume mestre.")]
        public float baseVolume;

        [Tooltip("Pitch mínimo e máximo para variação aleatória.\n" +
                 "Min=Max=1 desativa a variação.")]
        public float pitchMin;
        public float pitchMax;
    }

    [System.Serializable]
    public struct MusicEntry
    {
        public string key;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume; // relativo ao musicVolume mestre
    }

    [System.Serializable]
    public struct AmbientEntry
    {
        public string key;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume;
    }

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Singleton
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (persistBetweenScenes)
            DontDestroyOnLoad(gameObject);

        BuildPool();
        BuildLookups();
    }

    // ── API Pública — SFX ─────────────────────────────────────────────────────

    /// <summary>
    /// Toca um SFX pelo nome registrado no Inspector.
    /// </summary>
    /// <param name="key">Nome do SFX (case-insensitive).</param>
    /// <param name="volume">Multiplicador de volume (0–1). -1 usa o baseVolume da entrada.</param>
    /// <param name="pitch">Pitch fixo. -1 usa a variação aleatória configurada.</param>
    public AudioSource PlaySFX(string key, float volume = -1f, float pitch = -1f)
    {
        if (_sfxMap == null || !_sfxMap.TryGetValue(key.ToLower(), out AudioClip[] clips))
        {
            Debug.LogWarning($"[AudioManager] SFX não encontrado: \"{key}\"");
            return null;
        }

        if (clips == null || clips.Length == 0) return null;

        // Escolhe clip aleatório do array
        AudioClip clip = clips[Random.Range(0, clips.Length)];
        if (clip == null) return null;

        // Busca a entrada para volume/pitch base
        SfxEntry entry = GetSfxEntry(key);
        float finalVolume = volume >= 0f ? volume : entry.baseVolume;
        finalVolume = Mathf.Clamp01(finalVolume * sfxVolume * masterVolume);

        float finalPitch = pitch >= 0f
            ? pitch
            : Random.Range(
                entry.pitchMin > 0f ? entry.pitchMin : 1f,
                entry.pitchMax > 0f ? entry.pitchMax : 1f
              );

        finalPitch *= _globalPitchScale;

        AudioSource source = GetPooledSource();
        source.clip   = clip;
        source.volume = finalVolume;
        source.pitch  = finalPitch;
        source.loop   = false;
        source.Play();

        return source;
    }

    /// <summary>
    /// Toca um AudioClip diretamente, sem precisar estar cadastrado no Inspector.
    /// Útil para sons dinâmicos (ex: clips atribuídos por outros scripts).
    /// </summary>
    public AudioSource PlayClip(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        if (clip == null) return null;

        float finalVolume = Mathf.Clamp01(volume * sfxVolume * masterVolume);
        float finalPitch  = pitch * _globalPitchScale;

        AudioSource source = GetPooledSource();
        source.clip   = clip;
        source.volume = finalVolume;
        source.pitch  = finalPitch;
        source.loop   = false;
        source.Play();

        return source;
    }

    /// <summary>
    /// Interrompe todos os SFX em reprodução.
    /// </summary>
    public void StopAllSFX()
    {
        foreach (var s in _sfxPool)
            if (s.isPlaying) s.Stop();
    }

    // ── API Pública — Música ──────────────────────────────────────────────────

    /// <summary>
    /// Inicia uma música com crossfade suave.
    /// Se a mesma música já estiver tocando, não reinicia.
    /// </summary>
    public void PlayMusic(string key, bool loop = true)
    {
        if (_musicMap == null || !_musicMap.TryGetValue(key.ToLower(), out AudioClip clip))
        {
            Debug.LogWarning($"[AudioManager] Música não encontrada: \"{key}\"");
            return;
        }

        AudioSource current = _musicOnA ? _musicA : _musicB;

        // Evita reiniciar a mesma música
        if (current.isPlaying && current.clip == clip) return;

        if (_crossfadeRoutine != null) StopCoroutine(_crossfadeRoutine);
        _crossfadeRoutine = StartCoroutine(CrossfadeMusic(clip, loop));
    }

    /// <summary>
    /// Para a música com fade out.
    /// </summary>
    public void StopMusic(float fadeDuration = -1f)
    {
        float duration = fadeDuration >= 0f ? fadeDuration : musicCrossfadeDuration;

        if (_crossfadeRoutine != null) StopCoroutine(_crossfadeRoutine);
        _crossfadeRoutine = StartCoroutine(FadeOutMusic(duration));
    }

    /// <summary>
    /// Pausa/retoma a música atual.
    /// </summary>
    public void PauseMusic(bool pause)
    {
        AudioSource current = _musicOnA ? _musicA : _musicB;
        if (pause) current.Pause();
        else       current.UnPause();
    }

    // ── API Pública — Ambiente ────────────────────────────────────────────────

    /// <summary>
    /// Inicia um som ambiente com fade in.
    /// </summary>
    public void PlayAmbient(string key, float fadeDuration = 1f)
    {
        if (_ambientMap == null || !_ambientMap.TryGetValue(key.ToLower(), out AudioClip clip))
        {
            Debug.LogWarning($"[AudioManager] Ambiente não encontrado: \"{key}\"");
            return;
        }

        AmbientEntry entry = GetAmbientEntry(key);
        float targetVolume = Mathf.Clamp01(entry.volume * ambientVolume * masterVolume);

        if (_ambientFadeRoutine != null) StopCoroutine(_ambientFadeRoutine);
        _ambientFadeRoutine = StartCoroutine(FadeAmbient(clip, targetVolume, fadeDuration));
    }

    /// <summary>
    /// Para o som ambiente com fade out.
    /// </summary>
    public void StopAmbient(float fadeDuration = 1f)
    {
        if (_ambientFadeRoutine != null) StopCoroutine(_ambientFadeRoutine);
        _ambientFadeRoutine = StartCoroutine(FadeOutAmbient(fadeDuration));
    }

    // ── API Pública — Volumes ─────────────────────────────────────────────────

    public void SetMasterVolume(float v)
    {
        masterVolume = Mathf.Clamp01(v);
        ApplyMusicVolume();
    }

    public void SetSFXVolume(float v)  => sfxVolume    = Mathf.Clamp01(v);
    public void SetMusicVolume(float v)
    {
        musicVolume = Mathf.Clamp01(v);
        ApplyMusicVolume();
    }

    public void SetAmbientVolume(float v)
    {
        ambientVolume = Mathf.Clamp01(v);
        if (_ambientSource != null)
            _ambientSource.volume = Mathf.Clamp01(ambientVolume * masterVolume);
    }

    // ── API Pública — Pitch global (para slomo do parry) ─────────────────────

    /// <summary>
    /// Escala o pitch de todos os SFX futuros.
    /// Use 1f para normal, menos durante slow motion se quiser o efeito de "câmera lenta".
    /// AudioManager NÃO aplica isso na música (não soa bem).
    /// </summary>
    public void SetGlobalSFXPitch(float scale) => _globalPitchScale = Mathf.Clamp(scale, 0.1f, 3f);

    // ── Internos — Pool ───────────────────────────────────────────────────────

    private void BuildPool()
    {
        _sfxPool = new AudioSource[sfxPoolSize];
        for (int i = 0; i < sfxPoolSize; i++)
        {
            var go = new GameObject($"SFX_Source_{i}");
            go.transform.SetParent(transform);
            _sfxPool[i] = go.AddComponent<AudioSource>();
            _sfxPool[i].playOnAwake = false;
        }

        // Música A/B
        var goA = new GameObject("Music_A");
        goA.transform.SetParent(transform);
        _musicA = goA.AddComponent<AudioSource>();
        _musicA.playOnAwake = false;
        _musicA.loop = true;

        var goB = new GameObject("Music_B");
        goB.transform.SetParent(transform);
        _musicB = goB.AddComponent<AudioSource>();
        _musicB.playOnAwake = false;
        _musicB.loop = true;

        // Ambiente
        var goAmb = new GameObject("Ambient");
        goAmb.transform.SetParent(transform);
        _ambientSource = goAmb.AddComponent<AudioSource>();
        _ambientSource.playOnAwake = false;
        _ambientSource.loop = true;
        _ambientSource.volume = 0f;
    }

    private AudioSource GetPooledSource()
    {
        // Procura primeiro um source parado
        for (int i = 0; i < _sfxPool.Length; i++)
        {
            int idx = (_sfxPoolIndex + i) % _sfxPool.Length;
            if (!_sfxPool[idx].isPlaying)
            {
                _sfxPoolIndex = (idx + 1) % _sfxPool.Length;
                return _sfxPool[idx];
            }
        }

        // Todos ocupados: reutiliza o mais antigo (circular)
        _sfxPoolIndex = (_sfxPoolIndex + 1) % _sfxPool.Length;
        _sfxPool[_sfxPoolIndex].Stop();
        return _sfxPool[_sfxPoolIndex];
    }

    private void BuildLookups()
    {
        _sfxMap = new Dictionary<string, AudioClip[]>();
        if (sfxClips != null)
            foreach (var e in sfxClips)
                if (!string.IsNullOrEmpty(e.key))
                    _sfxMap[e.key.ToLower()] = e.clips;

        _musicMap = new Dictionary<string, AudioClip>();
        if (musicClips != null)
            foreach (var e in musicClips)
                if (!string.IsNullOrEmpty(e.key) && e.clip != null)
                    _musicMap[e.key.ToLower()] = e.clip;

        _ambientMap = new Dictionary<string, AudioClip>();
        if (ambientClips != null)
            foreach (var e in ambientClips)
                if (!string.IsNullOrEmpty(e.key) && e.clip != null)
                    _ambientMap[e.key.ToLower()] = e.clip;
    }

    private SfxEntry GetSfxEntry(string key)
    {
        if (sfxClips == null) return default;
        string lower = key.ToLower();
        foreach (var e in sfxClips)
            if (e.key.ToLower() == lower) return e;
        return default;
    }

    private AmbientEntry GetAmbientEntry(string key)
    {
        if (ambientClips == null) return default;
        string lower = key.ToLower();
        foreach (var e in ambientClips)
            if (e.key.ToLower() == lower) return e;
        return default;
    }

    // ── Internos — Crossfade ──────────────────────────────────────────────────

    private IEnumerator CrossfadeMusic(AudioClip newClip, bool loop)
    {
        AudioSource fadeOut = _musicOnA ? _musicA : _musicB;
        AudioSource fadeIn  = _musicOnA ? _musicB : _musicA;

        float targetVolume = GetMusicEntryVolume(newClip) * musicVolume * masterVolume;

        fadeIn.clip   = newClip;
        fadeIn.loop   = loop;
        fadeIn.volume = 0f;
        fadeIn.Play();

        float startVolume = fadeOut.volume;
        float elapsed = 0f;

        while (elapsed < musicCrossfadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / musicCrossfadeDuration);

            fadeOut.volume = Mathf.Lerp(startVolume,  0f,           t);
            fadeIn.volume  = Mathf.Lerp(0f,           targetVolume, t);

            yield return null;
        }

        fadeOut.Stop();
        fadeOut.clip = null;

        _musicOnA = !_musicOnA;
    }

    private IEnumerator FadeOutMusic(float duration)
    {
        AudioSource current = _musicOnA ? _musicA : _musicB;
        float startVolume = current.volume;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            current.volume = Mathf.Lerp(startVolume, 0f, elapsed / duration);
            yield return null;
        }

        current.Stop();
        current.clip = null;
    }

    private IEnumerator FadeAmbient(AudioClip clip, float targetVolume, float duration)
    {
        // Fade out do anterior
        if (_ambientSource.isPlaying && _ambientSource.clip != clip)
        {
            float startVol = _ambientSource.volume;
            float elapsed  = 0f;
            while (elapsed < duration * 0.5f)
            {
                elapsed += Time.unscaledDeltaTime;
                _ambientSource.volume = Mathf.Lerp(startVol, 0f, elapsed / (duration * 0.5f));
                yield return null;
            }
            _ambientSource.Stop();
        }

        _ambientSource.clip   = clip;
        _ambientSource.volume = 0f;
        _ambientSource.Play();

        float e = 0f;
        while (e < duration)
        {
            e += Time.unscaledDeltaTime;
            _ambientSource.volume = Mathf.Lerp(0f, targetVolume, e / duration);
            yield return null;
        }

        _ambientSource.volume = targetVolume;
    }

    private IEnumerator FadeOutAmbient(float duration)
    {
        float startVol = _ambientSource.volume;
        float elapsed  = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            _ambientSource.volume = Mathf.Lerp(startVol, 0f, elapsed / duration);
            yield return null;
        }

        _ambientSource.Stop();
    }

    private void ApplyMusicVolume()
    {
        AudioSource current = _musicOnA ? _musicA : _musicB;
        if (current.isPlaying)
            current.volume = Mathf.Clamp01(musicVolume * masterVolume);
    }

    private float GetMusicEntryVolume(AudioClip clip)
    {
        if (musicClips == null) return 1f;
        foreach (var e in musicClips)
            if (e.clip == clip) return e.volume > 0f ? e.volume : 1f;
        return 1f;
    }
}