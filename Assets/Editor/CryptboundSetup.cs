using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Cryptbound.Editor {

// One-shot project setup: import settings, materials, the asset catalog, render settings and the scene.
public static class CryptboundSetup
{
    const string EnvDir = "Assets/Textures/Env";

    [MenuItem("Cryptbound/Run Full Setup")]
    public static void RunAll()
    {
        ConfigureImports();
        BuildMaterials();
        BuildCatalog();
        ConfigureRendering();
        BuildScene();
        AssetDatabase.SaveAssets();
        Debug.Log("Cryptbound setup complete");
    }

    // Import settings

    [MenuItem("Cryptbound/Steps/Configure Imports")]
    public static void ConfigureImports()
    {
        // Pack metallic (R) and smoothness (A) for URP Lit.
        foreach (var name in new[] { "floor", "wall", "ceiling" }) PackMask(name);
        AssetDatabase.Refresh();

        foreach (var path in Find("t:Texture2D", EnvDir))
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = path.EndsWith("-normal.png") ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = path.EndsWith("-basecolor.png");
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.anisoLevel = 8;
            ti.maxTextureSize = 1024;
            ti.SaveAndReimport();
        }
        foreach (var path in Find("t:Texture2D", "Assets/Textures/VFX"))
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Default;
            ti.wrapMode = path.Contains("vfx_rune") ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            ti.mipmapEnabled = true;
            ti.maxTextureSize = 1024;
            ti.SaveAndReimport();
        }
        foreach (var path in Find("t:Texture2D", "Assets/Textures/Props", "Assets/Textures/UI"))
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Default;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.mipmapEnabled = !path.Contains("/UI/");
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.SaveAndReimport();
        }
        foreach (var path in Find("t:AudioClip", "Assets/Audio"))
        {
            var ai = (AudioImporter)AssetImporter.GetAtPath(path);
            var s = ai.defaultSampleSettings;
            var music = path.Contains("/Music/");
            var loop = path.Contains("_loop");
            s.loadType = music ? AudioClipLoadType.Streaming : loop ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.7f;
            ai.defaultSampleSettings = s;
            ai.loadInBackground = music;
            ai.SaveAndReimport();
        }
    }

    static void PackMask(string name)
    {
        var metallic = $"{EnvDir}/{name}-metallic.png";
        var smooth = $"{EnvDir}/{name}-smoothness.png";
        var outPath = $"{EnvDir}/{name}-mask.png";
        if (!File.Exists(metallic) || !File.Exists(smooth) || File.Exists(outPath)) return;
        var m = new Texture2D(2, 2);
        m.LoadImage(File.ReadAllBytes(metallic));
        var s = new Texture2D(2, 2);
        s.LoadImage(File.ReadAllBytes(smooth));
        var w = Mathf.Min(m.width, s.width);
        var h = Mathf.Min(m.height, s.height);
        var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var mp = m.GetPixels32();
        var sp = s.GetPixels32();
        var op = new Color32[w * h];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var mv = mp[y * m.width + x].r;
                var sv = sp[y * s.width + x].r;
                op[y * w + x] = new Color32(mv, mv, mv, sv);
            }
        }
        outTex.SetPixels32(op);
        File.WriteAllBytes(outPath, outTex.EncodeToPNG());
    }

    // Materials

    [MenuItem("Cryptbound/Steps/Build Materials")]
    public static void BuildMaterials()
    {
        Directory.CreateDirectory("Assets/Materials");
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        foreach (var (name, tex, smooth, parallax) in new[] { ("Floor", "floor", 0.55f, 0.025f), ("Wall", "wall", 0.4f, 0.03f), ("Ceiling", "ceiling", 0.35f, 0.02f) })
        {
            var mat = LoadOrCreate($"Assets/Materials/{name}.mat", lit);
            mat.SetTexture("_BaseMap", Tex($"{EnvDir}/{tex}-basecolor.png"));
            mat.SetColor("_BaseColor", name == "Ceiling" ? new Color(0.55f, 0.52f, 0.5f) : new Color(0.85f, 0.82f, 0.8f));
            mat.SetTexture("_BumpMap", Tex($"{EnvDir}/{tex}-normal.png"));
            mat.SetFloat("_BumpScale", 1.2f);
            mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_MetallicGlossMap", Tex($"{EnvDir}/{tex}-mask.png"));
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetFloat("_Smoothness", smooth);
            mat.SetTexture("_ParallaxMap", Tex($"{EnvDir}/{tex}-height.png"));
            mat.SetFloat("_Parallax", parallax);
            mat.EnableKeyword("_PARALLAXMAP");
            EditorUtility.SetDirty(mat);
        }
        {
            var mat = LoadOrCreate("Assets/Materials/Banner.mat", lit);
            mat.SetTexture("_BaseMap", Tex("Assets/Textures/Props/banner.png"));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_AlphaClip", 1);
            mat.SetFloat("_Cutoff", 0.4f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_Cull", 0);
            mat.SetFloat("_Smoothness", 0.1f);
            mat.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(mat);
        }
        {
            var mat = LoadOrCreate("Assets/Materials/Iron.mat", lit);
            mat.SetColor("_BaseColor", new Color(0.2f, 0.18f, 0.16f));
            mat.SetFloat("_Metallic", 0.85f);
            mat.SetFloat("_Smoothness", 0.4f);
            EditorUtility.SetDirty(mat);
        }
        AssetDatabase.SaveAssets();
    }

    static Material LoadOrCreate(string path, Shader shader)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;
        mat = new Material(shader);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static Texture2D Tex(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

    // Asset catalog

    [MenuItem("Cryptbound/Steps/Build Catalog")]
    public static void BuildCatalog()
    {
        Directory.CreateDirectory("Assets/Resources");
        const string path = "Assets/Resources/GameAssets.asset";
        var cat = AssetDatabase.LoadAssetAtPath<GameAssets>(path);
        if (cat == null)
        {
            cat = ScriptableObject.CreateInstance<GameAssets>();
            AssetDatabase.CreateAsset(cat, path);
        }
        cat.Models = Find("t:GameObject", "Assets/Models").Where(p => p.EndsWith(".glb"))
                     .Select(AssetDatabase.LoadAssetAtPath<GameObject>).Where(o => o != null).ToArray();
        cat.Clips = Find("t:AudioClip", "Assets/Audio").Select(AssetDatabase.LoadAssetAtPath<AudioClip>).ToArray();
        cat.Textures = Find("t:Texture2D", "Assets/Textures/VFX", "Assets/Textures/Props", "Assets/Textures/UI")
                       .Select(AssetDatabase.LoadAssetAtPath<Texture2D>).ToArray();
        cat.Materials = Find("t:Material", "Assets/Materials").Select(AssetDatabase.LoadAssetAtPath<Material>).ToArray();
        cat.EffectShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Effect.shader");
        EditorUtility.SetDirty(cat);
        AssetDatabase.SaveAssets();
        Debug.Log($"Catalog: {cat.Models.Length} models, {cat.Clips.Length} clips, {cat.Textures.Length} textures, {cat.Materials.Length} materials");
    }

    // Rendering

    [MenuItem("Cryptbound/Steps/Configure Rendering")]
    public static void ConfigureRendering()
    {
        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/URP/URP.asset");
        urp.supportsHDR = true;
        urp.msaaSampleCount = 1;
        urp.shadowDistance = 30;
        EditorUtility.SetDirty(urp);
        var so = new SerializedObject(urp);
        so.FindProperty("m_AdditionalLightsPerObjectLimit").intValue = 8;
        so.FindProperty("m_AdditionalLightShadowsSupported").boolValue = true;
        so.FindProperty("m_AdditionalLightsShadowmapResolution").intValue = 4096;
        so.ApplyModifiedProperties();

        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/URP/DefaultRenderer.asset");
        var rso = new SerializedObject(renderer);
        rso.FindProperty("m_RenderingMode").intValue = 2; // Forward+
        rso.ApplyModifiedProperties();
        EditorUtility.SetDirty(renderer);

        PlayerSettings.productName = "Cryptbound";
        PlayerSettings.defaultScreenWidth = 1920;
        PlayerSettings.defaultScreenHeight = 1080;
        AssetDatabase.SaveAssets();
    }

    // Scene

    [MenuItem("Cryptbound/Steps/Build Scene")]
    public static void BuildScene()
    {
        const string scenePath = "Assets/Main.unity";
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name is "Floor" or "Light Pivot" or "UnityMaterialBall" || root.GetComponent<Light>() != null)
                Object.DestroyImmediate(root);
        }

        var cam = Object.FindAnyObjectByType<Camera>();
        if (cam.GetComponent<CameraRig>() == null) cam.gameObject.AddComponent<CameraRig>();
        if (cam.GetComponent<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
        cam.tag = "MainCamera";

        var ui = Object.FindAnyObjectByType<PanelRenderer>();
        ui.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/Hud.uxml");
        if (ui.GetComponent<Hud>() == null) ui.gameObject.AddComponent<Hud>();

        var game = Object.FindAnyObjectByType<Game>();
        if (game == null) new GameObject("Game").AddComponent<Game>();

        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.15f, 0.12f, 0.11f);
        RenderSettings.fog = true;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
    }

    // Helpers

    static IEnumerable<string> Find(string filter, params string[] folders)
      => AssetDatabase.FindAssets(filter, folders.Where(AssetDatabase.IsValidFolder).ToArray())
                      .Select(AssetDatabase.GUIDToAssetPath).Distinct();
}

} // namespace Cryptbound.Editor
