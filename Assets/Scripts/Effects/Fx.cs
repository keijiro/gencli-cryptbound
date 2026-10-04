using System.Collections.Generic;
using UnityEngine;

namespace Cryptbound {

// Visual effects: pooled textured quads plus shared burst particle systems.
public sealed class Fx : MonoBehaviour
{
    public static Fx Instance { get; private set; }

    // Shared resources

    Mesh _quad;
    Material _ringMaterial;
    ParticleSystem _sparks, _embers, _dust, _magic, _bits;

    public Mesh Quad => _quad;
    public Material RingMaterial => _ringMaterial;

    void Awake()
    {
        Instance = this;
        _quad = MakeQuad();
        _ringMaterial = new Material(GameAssets.Instance.EffectShader) { name = "Ring" };
        _ringMaterial.SetTexture("_MainTex", MakeRingTexture());
        _sparks = MakeSystem("Sparks", "vfx_glow", false, true, 0.6f, 0.02f, 4);
        _embers = MakeSystem("Embers", "vfx_glow", false, false, -0.05f, 0, 3);
        _dust = MakeSystem("Dust", "vfx_smoke", true, false, -0.02f, 0, 1);
        _magic = MakeSystem("Magic", "vfx_glow", false, false, 0, 0, 3);
        _bits = MakeSystem("Bits", "vfx_spark", false, false, 0.3f, 0, 2.5f);
    }

    // Particle colors are 8-bit, so HDR intensity comes from the material.
    readonly Dictionary<(string, bool, float), Material> _particleMaterials = new();

    Material ParticleMaterial(string texture, bool smoke, float intensity)
    {
        if (_particleMaterials.TryGetValue((texture, smoke, intensity), out var mat)) return mat;
        mat = new Material(GameAssets.Instance.EffectMaterial(texture, smoke)) { name = $"{texture} x{intensity}" };
        mat.SetColor("_Color", new Color(intensity, intensity, intensity, 1));
        _particleMaterials[(texture, smoke, intensity)] = mat;
        return mat;
    }

    static Mesh MakeQuad()
    {
        var m = new Mesh { name = "FxQuad" };
        m.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        m.RecalculateNormals();
        m.bounds = new Bounds(Vector3.zero, Vector3.one * 2);
        return m;
    }

    static Texture2D MakeRingTexture()
    {
        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var r = new Vector2(x - size / 2 + 0.5f, y - size / 2 + 0.5f).magnitude / (size / 2);
                var v = Mathf.Exp(-Mathf.Pow((r - 0.86f) / 0.035f, 2)) + 0.35f * Mathf.Exp(-Mathf.Pow((r - 0.8f) / 0.12f, 2));
                var b = (byte)(Mathf.Clamp01(v) * 255);
                px[y * size + x] = new Color32(b, b, b, 255);
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    ParticleSystem MakeSystem(string name, string texture, bool smoke, bool stretch, float gravity, float velocityScale, float intensity)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.maxParticles = 3000;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = gravity;
        main.startSpeed = 0;
        var emission = ps.emission;
        emission.enabled = false;
        var shape = ps.shape;
        shape.enabled = false;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                  new[] { new GradientAlphaKey(smoke ? 0 : 1, 0), new GradientAlphaKey(1, smoke ? 0.15f : 0.5f), new GradientAlphaKey(0, 1) });
        col.color = g;
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1, smoke ? AnimationCurve.Linear(0, 0.6f, 1, 1.4f) : AnimationCurve.Linear(0, 1, 1, 0.15f));
        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.drag = smoke ? 2.5f : 1.2f;
        limit.dampen = 0;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.material = ParticleMaterial(texture, smoke, intensity);
        r.renderMode = stretch ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
        r.velocityScale = velocityScale;
        r.lengthScale = 1.5f;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        ps.Play();
        return ps;
    }

    static void Emit(ParticleSystem ps, Vector3 pos, int count, Vector2 speed, Vector2 size, Vector2 life,
                     Color color, Vector3 bias, float jitter = 0.1f)
    {
        var p = new ParticleSystem.EmitParams();
        for (var i = 0; i < count; i++)
        {
            p.position = pos + Random.insideUnitSphere * jitter;
            p.velocity = (Random.onUnitSphere + bias).normalized * Random.Range(speed.x, speed.y);
            p.startSize = Random.Range(size.x, size.y);
            p.startLifetime = Random.Range(life.x, life.y);
            p.startColor = color;
            p.rotation = Random.Range(0, 360);
            ps.Emit(p, 1);
        }
    }

    // Pooled sprite quads

    sealed class SpriteFx
    {
        public Transform T;
        public MeshRenderer R;
        public Material Mat;
        public float Time, Life, Size0, Size1, Spin, FadeIn;
        public Vector2 Aspect;
        public Color Color;
        public bool Billboard, Unscaled;
        public Quaternion Rot;
        public Vector3 Velocity;
        public Transform Follow;
        public Vector3 Offset;
        public Ease Ease;
        public bool Hold;
    }

    readonly List<SpriteFx> _live = new();
    readonly Stack<SpriteFx> _pool = new();
    MaterialPropertyBlock _block;
    static readonly int ColorID = Shader.PropertyToID("_Color");

    SpriteFx Spawn(Material mat)
    {
        var s = _pool.Count > 0 ? _pool.Pop() : null;
        if (s == null)
        {
            s = new SpriteFx();
            var go = new GameObject("FxSprite");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _quad;
            s.R = go.AddComponent<MeshRenderer>();
            s.R.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            s.R.receiveShadows = false;
            s.T = go.transform;
        }
        s.T.gameObject.SetActive(true);
        s.R.sharedMaterial = mat;
        s.Mat = mat;
        s.Time = 0;
        s.Spin = 0;
        s.FadeIn = 0;
        s.Velocity = Vector3.zero;
        s.Follow = null;
        s.Aspect = Vector2.one;
        _live.Add(s);
        return s;
    }

    public void Sprite(Material mat, Vector3 pos, Quaternion? rot, float size0, float size1, Color color, float life,
                       bool unscaled = false, float spin = 0, float fadeIn = 0, Vector2? aspect = null, Transform follow = null,
                       Ease ease = Ease.OutCubic, bool hold = false)
    {
        var s = Spawn(mat);
        s.T.position = pos;
        s.Billboard = !rot.HasValue;
        s.Rot = rot ?? Quaternion.identity;
        s.Size0 = size0;
        s.Size1 = size1;
        s.Color = color;
        s.Life = life;
        s.Unscaled = unscaled;
        s.Spin = spin;
        s.FadeIn = fadeIn;
        s.Aspect = aspect ?? Vector2.one;
        s.Follow = follow;
        s.Ease = ease;
        s.Hold = hold;
        if (follow != null) s.Offset = pos - follow.position;
        UpdateSprite(s, 0);
    }

    public void Sprite(string texture, Vector3 pos, Quaternion? rot, float size0, float size1, Color color, float life,
                       bool unscaled = false, float spin = 0, float fadeIn = 0, Vector2? aspect = null, Transform follow = null,
                       Ease ease = Ease.OutCubic, bool hold = false)
      => Sprite(GameAssets.Instance.EffectMaterial(texture), pos, rot, size0, size1, color, life, unscaled, spin, fadeIn, aspect, follow, ease, hold);

    void LateUpdate()
    {
        for (var i = _live.Count - 1; i >= 0; i--)
        {
            var s = _live[i];
            var dt = s.Unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
            s.Time += dt;
            if (s.Time >= s.Life)
            {
                s.T.gameObject.SetActive(false);
                _live.RemoveAt(i);
                _pool.Push(s);
                continue;
            }
            UpdateSprite(s, dt);
        }
    }

    void UpdateSprite(SpriteFx s, float dt)
    {
        var k = s.Time / s.Life;
        var size = Mathf.Lerp(s.Size0, s.Size1, Easing.Apply(s.Ease, k));
        if (s.Follow != null) s.T.position = s.Follow.position + s.Offset;
        s.T.position += s.Velocity * dt;
        var cam = Camera.main;
        var rot = s.Billboard && cam != null ? cam.transform.rotation : s.Rot;
        s.T.rotation = rot * Quaternion.Euler(0, 0, s.Spin * s.Time);
        s.T.localScale = new Vector3(size * s.Aspect.x, size * s.Aspect.y, size);
        var alpha = s.FadeIn > 0 && k < s.FadeIn ? k / s.FadeIn : s.Hold ? 1 : 1 - Mathf.Clamp01((k - s.FadeIn) / (1 - s.FadeIn));
        _block ??= new MaterialPropertyBlock();
        var c = s.Color;
        c.a *= alpha;
        _block.SetColor(ColorID, c);
        s.R.SetPropertyBlock(_block);
    }

    // Effect recipes

    public void Slash(Vector3 pos, Quaternion rot, float size, Color color, bool flip, float life = 0.24f)
    {
        var r = flip ? rot * Quaternion.Euler(0, 180, 0) : rot;
        Sprite("vfx_slash", pos, r, size * 0.85f, size * 1.15f, color, life);
        Sprite("vfx_slash", pos, r, size * 0.7f, size * 1.0f, color * 0.6f, life * 1.4f);
    }

    public void Hit(Vector3 pos, Color color, float scale = 1)
    {
        Emit(_sparks, pos, Mathf.RoundToInt(14 * scale), new Vector2(3, 9) * Mathf.Sqrt(scale), new Vector2(0.05f, 0.12f), new Vector2(0.15f, 0.4f), color, Vector3.up * 0.3f);
        Sprite("vfx_spark", pos, null, 0.6f * scale, 1.5f * scale, color, 0.18f, spin: 200);
        Sprite("vfx_glow", pos, null, 1.2f * scale, 2.2f * scale, color * 0.6f, 0.2f);
    }

    public void Parry(Vector3 pos)
    {
        var gold = new Color(1.0f, 0.85f, 0.5f) * 3;
        Emit(_sparks, pos, 60, new Vector2(6, 16), new Vector2(0.06f, 0.16f), new Vector2(0.4f, 1.2f), gold, Vector3.zero);
        Sprite("vfx_spark", pos, null, 1.0f, 4.5f, gold, 0.45f, true, 120);
        Sprite("vfx_glow", pos, null, 2.5f, 6.0f, new Color(1, 0.9f, 0.7f) * 1.5f, 0.5f, true);
        Sprite(_ringMaterial, pos, null, 0.5f, 7.0f, new Color(1, 0.85f, 0.55f) * 2, 0.6f, true);
    }

    public void Glint(Vector3 pos, Color color, float size = 1)
    {
        Sprite("vfx_spark", pos, null, 0.2f * size, 1.6f * size, color, 0.35f, spin: 360);
        Sprite("vfx_glow", pos, null, 0.4f * size, 1.4f * size, color * 0.7f, 0.35f);
    }

    public void Dust(Vector3 pos, int count, float scale = 1, Color? color = null)
      => Emit(_dust, pos, count, new Vector2(0.6f, 2.5f) * scale, new Vector2(0.8f, 1.8f) * scale, new Vector2(0.8f, 1.6f),
              color ?? new Color(0.35f, 0.3f, 0.26f, 0.55f), new Vector3(0, 0.6f, 0), 0.4f * scale);

    public void Embers(Vector3 pos, int count, Color color, float speed = 2)
      => Emit(_embers, pos, count, new Vector2(0.5f, 1.5f) * speed, new Vector2(0.04f, 0.1f), new Vector2(0.6f, 1.6f), color, Vector3.up * 1.2f, 0.3f);

    public void Magic(Vector3 pos, int count, Color color, float speed = 2, float size = 0.15f)
      => Emit(_magic, pos, count, new Vector2(0.3f, 1.0f) * speed, new Vector2(0.5f, 1.2f) * size, new Vector2(0.3f, 0.8f), color, Vector3.zero, 0.15f);

    public void Bits(Vector3 pos, int count, Color color)
      => Emit(_bits, pos, count, new Vector2(2, 6), new Vector2(0.15f, 0.35f), new Vector2(0.2f, 0.5f), color, Vector3.up * 0.4f);

    public void Ring(Vector3 pos, Color color, float size0, float size1, float life, bool unscaled = false)
      => Sprite(_ringMaterial, pos + Vector3.up * 0.05f, Quaternion.Euler(90, 0, 0), size0, size1, color, life, unscaled);

    public void Shockwave(Vector3 pos, Color color, float radius, bool rune = false)
    {
        Ring(pos, color, 0.5f, radius * 2.2f, 0.5f);
        Ring(pos, color * 0.5f, 0.5f, radius * 1.6f, 0.7f);
        Ring(pos, color * 0.3f, 0.2f, radius * 1.1f, 0.9f);
        Dust(pos, 30, 1.6f);
        if (rune) Sprite("vfx_rune", pos + Vector3.up * 0.06f, Quaternion.Euler(90, 0, 0), radius * 1.2f, radius * 2.0f, color * 0.8f, 0.8f, spin: 60);
    }

    public void LevelUp(Transform target)
    {
        var p = target.position;
        var gold = new Color(1, 0.8f, 0.4f) * 2.5f;
        Ring(p, gold, 0.5f, 5, 0.8f, true);
        Sprite("vfx_rune", p + Vector3.up * 0.06f, Quaternion.Euler(90, 0, 0), 1.5f, 4.0f, new Color(0.7f, 0.9f, 1) * 1.6f, 1.2f, true, 90);
        Emit(_magic, p + Vector3.up * 0.8f, 60, new Vector2(0.5f, 2.5f), new Vector2(0.08f, 0.2f), new Vector2(0.8f, 1.6f), gold, Vector3.up * 2.5f, 0.6f);
    }

    // Persistent emitters

    public ParticleSystem Flame(Transform parent, Vector3 localPos, Color color, float scale, float rate = 22, float intensity = 2.5f)
    {
        var go = new GameObject("Flame");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.65f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f * scale, 0.9f * scale);
        main.startSize = new ParticleSystem.MinMaxCurve(0.32f * scale, 0.55f * scale);
        main.startRotation = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
        main.startColor = color;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 200;
        var emission = ps.emission;
        emission.rateOverTime = rate;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 8;
        shape.radius = 0.06f * scale;
        shape.rotation = new Vector3(-90, 0, 0);
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(1, 0.5f, 0.3f), 1) },
                  new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.15f), new GradientAlphaKey(0, 1) });
        col.color = g;
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.EaseInOut(0, 1, 1, 0.3f));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.material = ParticleMaterial("vfx_flame", false, intensity);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        ps.Play();
        return ps;
    }

    public ParticleSystem Wisps(Transform parent, Vector3 localPos, Color color, float radius, float rate, float size, float lift, string texture = "vfx_flame", float intensity = 2)
    {
        var go = new GameObject("Wisps");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.0f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.3f);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
        main.startRotation = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
        main.startColor = color;
        main.gravityModifier = -lift;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 300;
        var emission = ps.emission;
        emission.rateOverTime = rate;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = radius;
        shape.rotation = new Vector3(90, 0, 0);
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                  new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.2f), new GradientAlphaKey(0, 1) });
        col.color = g;
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0.2f));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.material = ParticleMaterial(texture, false, intensity);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        ps.Play();
        return ps;
    }
}

} // namespace Cryptbound
