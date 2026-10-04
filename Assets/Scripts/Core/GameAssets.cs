using System.Collections.Generic;
using UnityEngine;

namespace Cryptbound {

// Name-based catalog of every generated asset, built by the editor setup tool.
public sealed class GameAssets : ScriptableObject
{
    // Public properties

    [field:SerializeField] public GameObject[] Models { get; set; }
    [field:SerializeField] public AudioClip[] Clips { get; set; }
    [field:SerializeField] public Texture2D[] Textures { get; set; }
    [field:SerializeField] public Material[] Materials { get; set; }
    [field:SerializeField] public Shader EffectShader { get; set; }

    // Singleton access

    static GameAssets _instance;

    public static GameAssets Instance
      => _instance != null ? _instance : (_instance = Resources.Load<GameAssets>("GameAssets"));

    // The project enters Play mode without a domain reload, so runtime caches held by
    // the asset survive between sessions while the objects in them are destroyed.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCaches()
    {
        if (_instance != null)
        {
            _instance._map = null;
            _instance._fxMaterials.Clear();
        }
        _instance = null;
    }

    // Lookup

    Dictionary<string, Object> _map;

    void BuildMap()
    {
        _map = new Dictionary<string, Object>();
        void Add<T>(T[] list) where T : Object
        {
            if (list == null) return;
            foreach (var o in list) if (o != null) _map[typeof(T).Name + ":" + o.name] = o;
        }
        Add(Models);
        Add(Clips);
        Add(Textures);
        Add(Materials);
    }

    T Find<T>(string name) where T : Object
    {
        if (_map == null) BuildMap();
        if (_map.TryGetValue(typeof(T).Name + ":" + name, out var o)) return (T)o;
        Debug.LogWarning($"Missing {typeof(T).Name}: {name}");
        return null;
    }

    public GameObject Model(string name) => Find<GameObject>(name);
    public AudioClip Clip(string name) => Find<AudioClip>(name);
    public Texture2D Texture(string name) => Find<Texture2D>(name);
    public Material Material(string name) => Find<Material>(name);

    // Effect material cache

    readonly Dictionary<(string, bool), Material> _fxMaterials = new();

    public Material EffectMaterial(string texture, bool smoke = false)
    {
        if (_fxMaterials.TryGetValue((texture, smoke), out var mat) && mat != null) return mat;
        mat = new Material(EffectShader) { name = texture };
        mat.SetTexture("_MainTex", Texture(texture));
        mat.SetFloat("_Mode", smoke ? 1 : 0);
        mat.SetFloat("_SrcBlend", smoke ? (float)UnityEngine.Rendering.BlendMode.SrcAlpha : 1);
        mat.SetFloat("_DstBlend", smoke ? (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : 1);
        _fxMaterials[(texture, smoke)] = mat;
        return mat;
    }
}

} // namespace Cryptbound
