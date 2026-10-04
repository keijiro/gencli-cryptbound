using UnityEngine;

namespace Cryptbound {

// Sound effects pool, crossfading music and ambience.
public sealed class Sfx : MonoBehaviour
{
    public static Sfx Instance { get; private set; }

    AudioSource[] _pool;
    int _next;
    AudioSource[] _music = new AudioSource[2];
    AudioLowPassFilter[] _musicFilters = new AudioLowPassFilter[2];
    int _musicIndex;
    float[] _musicTarget = new float[2];
    float _musicFade = 1;
    AudioSource _ambience;
    float _muffle, _muffleTarget;

    public float MusicVolume { get; set; } = 0.55f;

    void Awake()
    {
        Instance = this;
        _pool = new AudioSource[24];
        for (var i = 0; i < _pool.Length; i++)
        {
            _pool[i] = gameObject.AddComponent<AudioSource>();
            _pool[i].playOnAwake = false;
        }
        for (var i = 0; i < 2; i++)
        {
            var go = new GameObject("Music" + i);
            go.transform.SetParent(transform, false);
            _music[i] = go.AddComponent<AudioSource>();
            _music[i].loop = true;
            _music[i].playOnAwake = false;
            _music[i].volume = 0;
            _musicFilters[i] = go.AddComponent<AudioLowPassFilter>();
            _musicFilters[i].cutoffFrequency = 22000;
        }
        var amb = new GameObject("Ambience");
        amb.transform.SetParent(transform, false);
        _ambience = amb.AddComponent<AudioSource>();
        _ambience.loop = true;
        _ambience.volume = 0.5f;
        _ambience.clip = GameAssets.Instance.Clip("ambience_loop");
        if (_ambience.clip != null) _ambience.Play();
    }

    public static void Play(string name, float volume = 1, float pitch = 1, float jitter = 0.06f)
    {
        if (Instance == null) return;
        var clip = GameAssets.Instance.Clip(name);
        if (clip == null) return;
        var s = Instance.NextSource();
        s.pitch = pitch * (1 + Random.Range(-jitter, jitter));
        s.PlayOneShot(clip, volume);
    }

    AudioSource NextSource()
    {
        // Prefer an idle source; otherwise steal round-robin.
        for (var i = 0; i < _pool.Length; i++)
        {
            var s = _pool[(_next + i) % _pool.Length];
            if (!s.isPlaying) { _next = (_next + i + 1) % _pool.Length; return s; }
        }
        _next = (_next + 1) % _pool.Length;
        _pool[_next].Stop();
        return _pool[_next];
    }

    public void Music(string name, float fade = 1.5f)
    {
        var clip = name != null ? GameAssets.Instance.Clip(name) : null;
        if (clip != null && _music[_musicIndex].clip == clip && _musicTarget[_musicIndex] > 0) return;
        _musicTarget[_musicIndex] = 0;
        _musicIndex ^= 1;
        _musicFade = Mathf.Max(0.01f, fade);
        if (clip == null) return;
        var s = _music[_musicIndex];
        s.clip = clip;
        s.volume = 0;
        s.Play();
        _musicTarget[_musicIndex] = 1;
    }

    // 0: clear, 1: heavily muffled (slow motion)
    public void Muffle(float amount) => _muffleTarget = amount;

    void Update()
    {
        var dt = Time.unscaledDeltaTime;
        for (var i = 0; i < 2; i++)
        {
            var s = _music[i];
            s.volume = Mathf.MoveTowards(s.volume, _musicTarget[i] * MusicVolume, dt / _musicFade * MusicVolume);
            if (s.volume <= 0 && _musicTarget[i] <= 0 && s.isPlaying) s.Stop();
        }
        _muffle = Mathf.MoveTowards(_muffle, _muffleTarget, dt * 4);
        var cutoff = Mathf.Lerp(22000, 700, _muffle);
        foreach (var f in _musicFilters) f.cutoffFrequency = cutoff;
        _ambience.volume = Mathf.Lerp(0.5f, 0.2f, _muffle);
    }
}

} // namespace Cryptbound
