using System.Collections.Generic;
using UnityEngine;

namespace Cryptbound {

// Procedural one-way corridor plus guardian arena for a single floor.
public sealed class Dungeon : MonoBehaviour
{
    // Public members

    public int Floor { get; private set; }
    public float Length { get; private set; }
    public float ArenaStart => Length;
    public float ArenaCenter => Length + Balance.ArenaLength * 0.5f;
    public float ArenaEnd => Length + Balance.ArenaLength;
    public Vector3 PortalPosition => new Vector3(0, 0, ArenaEnd - 0.8f);
    public bool PortalOpen { get; private set; }
    public FloorTheme Theme { get; private set; }

    public float HalfWidthAt(float z)
      => z > ArenaStart + 0.6f ? Balance.ArenaMoveHalfWidth : Balance.MoveHalfWidth;

    // Internal state

    Transform _root;
    readonly List<TorchLight> _torches = new();
    Transform _gate;
    float _gateY, _gateTarget;
    Transform _portal;
    Light _portalLight;
    float _portalPower;
    Material _floorMat, _wallMat, _ceilMat, _bannerMat;

    sealed class TorchLight
    {
        public Light Light;
        public float Base;
        public float Seed;
    }

    // Build

    public void Build(int floor)
    {
        if (_root != null) Destroy(_root.gameObject);
        _torches.Clear();
        Floor = floor;
        Length = Balance.FloorLength(floor);
        Theme = FloorTheme.Get(floor);
        PortalOpen = false;
        _portalPower = 0;
        _root = new GameObject($"Floor B{floor}").transform;
        _root.SetParent(transform, false);

        var assets = GameAssets.Instance;
        _floorMat = assets.Material("Floor");
        _wallMat = assets.Material("Wall");
        _ceilMat = assets.Material("Ceiling");
        _bannerMat = assets.Material("Banner");

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = Theme.Fog;
        RenderSettings.fogDensity = 0.036f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = Theme.Ambient * 2.2f;

        var rng = new System.Random(floor * 7919 + 13);
        BuildCorridor(rng);
        BuildArena(rng);
    }

    float Rand(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

    void BuildCorridor(System.Random rng)
    {
        const float w = Balance.CorridorHalfWidth, h = Balance.CorridorHeight;
        var start = -8f;
        var len = Length - start;

        Plane("Floor", new Vector3(-w, 0, start), Vector3.right * (2 * w), Vector3.forward * len, _floorMat, 3.2f);
        Plane("Ceiling", new Vector3(-w, h, Length), Vector3.right * (2 * w), Vector3.back * len, _ceilMat, 3.2f);
        Plane("WallL", new Vector3(-w, 0, start), Vector3.forward * len, Vector3.up * h, _wallMat, 3.0f);
        Plane("WallR", new Vector3(w, 0, Length), Vector3.back * len, Vector3.up * h, _wallMat, 3.0f);
        Plane("WallBack", new Vector3(w, 0, start), Vector3.left * (2 * w), Vector3.up * h, _wallMat, 3.0f);

        // Entrance arch behind the starting point
        Prop("prop_arch", new Vector3(0, 0, start + 0.4f), 0, 4.6f);

        // Pillars, torches and banners along both walls
        for (var z = 4f; z < Length - 2; z += 9)
        {
            Prop("prop_pillar", new Vector3(-w + 0.35f, 0, z), 0, new Vector3(3, h, 3));
            Prop("prop_pillar", new Vector3(w - 0.35f, 0, z), 0, new Vector3(3, h, 3));
        }
        Torch(new Vector3(-(w - 0.08f), 2.6f, -0.5f), -1);
        Torch(new Vector3(w - 0.08f, 2.6f, -0.5f), 1);
        var side = 1;
        for (var z = 8.5f; z < Length - 4; z += 9)
        {
            Torch(new Vector3(side * (w - 0.08f), 2.6f, z), side);
            side = -side;
            if (rng.NextDouble() < 0.5)
                Banner(new Vector3(side * (w - 0.04f), 3.0f, z + Rand(rng, -0.4f, 0.4f)), side);
        }

        // Clutter along the walls, kept outside the walkable lane
        var ossuary = Floor == 2;
        string[] clutter = ossuary
            ? new[] { "prop_bones", "prop_bones", "prop_vase", "prop_rubble", "prop_barrel", "prop_bones" }
            : new[] { "prop_barrel", "prop_crate", "prop_vase", "prop_rubble", "prop_bones", "prop_barrel", "prop_crate" };
        for (var z = 2f; z < Length - 3; z += Rand(rng, 2.5f, 5.5f))
        {
            foreach (var s in new[] { -1, 1 })
            {
                if (rng.NextDouble() > 0.55) continue;
                var name = rng.NextDouble() < 0.05 ? "prop_chest" : clutter[rng.Next(clutter.Length)];
                var x = s * (w - Rand(rng, 0.55f, 0.85f));
                Prop(name, new Vector3(x, 0, z + Rand(rng, -0.6f, 0.6f)), Rand(rng, 0, 360), PropScale(name) * Rand(rng, 0.85f, 1.1f));
            }
        }
    }

    static float PropScale(string name) => name switch
    {
        "prop_barrel" => 1.05f,
        "prop_crate" => 0.85f,
        "prop_vase" => 0.8f,
        "prop_rubble" => 1.4f,
        "prop_bones" => 1.1f,
        "prop_chest" => 0.95f,
        _ => 1
    };

    void BuildArena(System.Random rng)
    {
        const float cw = Balance.CorridorHalfWidth;
        var w = Balance.ArenaHalfWidth;
        var h = Balance.ArenaHeight;
        var z0 = ArenaStart;
        var z1 = ArenaEnd;
        var len = z1 - z0;

        Plane("ArenaFloor", new Vector3(-w, 0, z0), Vector3.right * (2 * w), Vector3.forward * len, _floorMat, 3.2f);
        Plane("ArenaCeiling", new Vector3(-w, h, z1), Vector3.right * (2 * w), Vector3.back * len, _ceilMat, 3.2f);
        Plane("ArenaWallL", new Vector3(-w, 0, z0), Vector3.forward * len, Vector3.up * h, _wallMat, 3.0f);
        Plane("ArenaWallR", new Vector3(w, 0, z1), Vector3.back * len, Vector3.up * h, _wallMat, 3.0f);
        Plane("ArenaWallFrontL", new Vector3(-cw, 0, z0), Vector3.left * (w - cw), Vector3.up * h, _wallMat, 3.0f);
        Plane("ArenaWallFrontR", new Vector3(w, 0, z0), Vector3.left * (w - cw), Vector3.up * h, _wallMat, 3.0f);
        Plane("ArenaWallFrontTop", new Vector3(cw, Balance.CorridorHeight, z0), Vector3.left * (2 * cw), Vector3.up * (h - Balance.CorridorHeight), _wallMat, 3.0f);
        Plane("ArenaWallBack", new Vector3(-w, 0, z1), Vector3.right * (2 * w), Vector3.up * h, _wallMat, 3.0f);

        for (var z = z0 + 3; z < z1 - 1; z += 5.5f)
        {
            Prop("prop_pillar", new Vector3(-w + 0.45f, 0, z), 0, new Vector3(4, h, 4));
            Prop("prop_pillar", new Vector3(w - 0.45f, 0, z), 0, new Vector3(4, h, 4));
            Torch(new Vector3(-w + 0.08f, 3.0f, z + 2.75f), -1);
            Torch(new Vector3(w - 0.08f, 3.0f, z + 2.75f), 1);
        }
        AddLight(new Vector3(0, h - 1.5f, ArenaCenter), Theme.Torch, 22, 5);

        // Braziers in the four corners
        foreach (var x in new[] { -w + 2.2f, w - 2.2f })
        {
            foreach (var z in new[] { z0 + 3.0f, z1 - 3.0f })
            {
                Prop("prop_brazier", new Vector3(x, 0, z), Rand(rng, 0, 360), 1.3f);
                var light = AddLight(new Vector3(x, 2.2f, z), Theme.Torch, 18, 30);
                Fx.Instance.Flame(light.transform, new Vector3(0, -0.85f, 0), Theme.Torch, 2.4f, 40);
            }
        }

        // Rubble and bones near the walls
        for (var i = 0; i < 10; i++)
        {
            var s = i % 2 == 0 ? -1 : 1;
            var name = rng.NextDouble() < 0.5 ? "prop_rubble" : "prop_bones";
            Prop(name, new Vector3(s * (w - Rand(rng, 1.2f, 1.8f)), 0, Rand(rng, z0 + 1.5f, z1 - 1.5f)), Rand(rng, 0, 360), PropScale(name) * 1.2f);
        }

        // Exit archway and its (dormant) portal
        Prop("prop_arch", new Vector3(0, 0, z1 - 0.3f), 180, 6.0f);
        _portal = new GameObject("Portal").transform;
        _portal.SetParent(_root, false);
        _portal.position = new Vector3(0, 2.1f, z1 - 0.35f);
        var quad = new GameObject("PortalQuad");
        quad.transform.SetParent(_portal, false);
        quad.transform.localScale = new Vector3(2.6f, 3.6f, 1);
        quad.AddComponent<MeshFilter>().sharedMesh = Fx.Instance.Quad;
        var mr = quad.AddComponent<MeshRenderer>();
        mr.sharedMaterial = new Material(GameAssets.Instance.EffectMaterial("vfx_rune"));
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _portalLight = AddLight(_portal.position + Vector3.back * 1.2f, new Color(0.45f, 0.7f, 1), 12, 0);
        _portal.gameObject.SetActive(false);

        // Portcullis above the arena entrance
        _gate = new GameObject("Gate").transform;
        _gate.SetParent(_root, false);
        BuildGate(_gate, 2 * cw, Balance.CorridorHeight);
        _gateY = _gateTarget = Balance.CorridorHeight;
        _gate.position = new Vector3(0, _gateY, z0);
    }

    void BuildGate(Transform parent, float width, float height)
    {
        var mat = GameAssets.Instance.Material("Iron");
        for (var x = -width / 2 + 0.25f; x <= width / 2 - 0.2f; x += 0.45f)
            Bar(parent, new Vector3(x, height / 2, 0), new Vector3(0.09f, height, 0.09f), mat);
        for (var y = 0.6f; y < height; y += 1.1f)
            Bar(parent, new Vector3(0, y, 0), new Vector3(width, 0.1f, 0.12f), mat);
    }

    static void Bar(Transform parent, Vector3 pos, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    // Helpers

    void Plane(string name, Vector3 origin, Vector3 u, Vector3 v, Material mat, float tile)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_root, false);
        var mesh = new Mesh { name = name };
        var n = Vector3.Cross(v, u).normalized;
        mesh.vertices = new[] { origin, origin + u, origin + v, origin + u + v };
        mesh.normals = new[] { n, n, n, n };
        var uu = u.magnitude / tile;
        var vv = v.magnitude / tile;
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(uu, 0), new Vector2(0, vv), new Vector2(uu, vv) };
        var t = new Vector4(u.normalized.x, u.normalized.y, u.normalized.z, -1);
        mesh.tangents = new[] { t, t, t, t };
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        mesh.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
    }

    GameObject Prop(string model, Vector3 pos, float yaw, float scale) => Prop(model, pos, yaw, Vector3.one * scale);

    GameObject Prop(string model, Vector3 pos, float yaw, Vector3 scale)
    {
        var src = GameAssets.Instance.Model(model);
        if (src == null) return null;
        var holder = new GameObject(model);
        holder.transform.SetParent(_root, false);
        holder.transform.position = pos;
        holder.transform.rotation = Quaternion.Euler(0, yaw, 0);
        var go = Instantiate(src, holder.transform);
        go.transform.localRotation = Quaternion.Euler(0, 90, 0);
        // Normalized meshes are centered: lift them so they rest on the floor.
        var bounds = new Bounds();
        var first = true;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds);
        }
        go.transform.localScale = new Vector3(scale.z, scale.y, scale.x);
        go.transform.localPosition = Vector3.up * (bounds.extents.y * scale.y);
        return holder;
    }

    void Torch(Vector3 pos, int side)
    {
        // The sconce's wall plate is on its +X side once turned to face +Z.
        var holder = Prop("prop_torch", pos - Vector3.up * 0.55f, side > 0 ? 0 : 180, 1.1f);
        if (holder != null) holder.transform.position = new Vector3(pos.x - side * 0.22f, pos.y - 0.55f, pos.z);
        var lightPos = new Vector3(pos.x - side * 0.55f, pos.y + 0.55f, pos.z);
        var light = AddLight(lightPos, Theme.Torch, 14, 16);
        Fx.Instance.Flame(light.transform, new Vector3(side * 0.12f, -0.12f, 0), Theme.Torch, 1.0f);
    }

    void Banner(Vector3 pos, int side)
    {
        var go = new GameObject("Banner");
        go.transform.SetParent(_root, false);
        go.transform.position = new Vector3(pos.x - side * 0.02f, pos.y, pos.z);
        go.transform.rotation = Quaternion.Euler(0, side > 0 ? 90 : -90, 0);
        go.transform.localScale = new Vector3(1.2f, 2.4f, 1);
        go.AddComponent<MeshFilter>().sharedMesh = Fx.Instance.Quad;
        go.AddComponent<MeshRenderer>().sharedMaterial = _bannerMat;
    }

    Light AddLight(Vector3 pos, Color color, float range, float intensity)
    {
        var go = new GameObject("Light");
        go.transform.SetParent(_root, false);
        go.transform.position = pos;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
        _torches.Add(new TorchLight { Light = light, Base = intensity * Theme.Boost, Seed = Random.value * 100 });
        return light;
    }

    // Runtime

    public void CloseGate() => _gateTarget = 0;
    public void OpenGate() => _gateTarget = Balance.CorridorHeight;
    public bool GateClosed => _gateY < 0.1f;

    public void OpenPortal()
    {
        PortalOpen = true;
        _portal.gameObject.SetActive(true);
    }

    public void Tick(Vector3 focus, float dt)
    {
        // Gate drop / raise
        if (_gate != null)
        {
            var speed = _gateTarget < _gateY ? 14f : 1.6f;
            _gateY = Mathf.MoveTowards(_gateY, _gateTarget, dt * speed);
            _gate.position = new Vector3(0, _gateY, ArenaStart);
        }

        // Portal spin and glow
        if (PortalOpen)
        {
            _portalPower = Mathf.MoveTowards(_portalPower, 1, dt * 0.7f);
            _portal.GetChild(0).localRotation = Quaternion.Euler(0, 0, Time.time * 25);
            _portal.GetChild(0).localScale = new Vector3(3.2f, 3.2f, 1) * _portalPower;
        }

        // Torch flicker, distance culling and shadows on the nearest few
        var t = Time.time;
        var shadowed = 0;
        foreach (var tl in _torches)
        {
            if (tl.Light == null) continue;
            var d = Vector3.Distance(tl.Light.transform.position, focus);
            var active = d < 32;
            tl.Light.enabled = active;
            if (!active) continue;
            var baseIntensity = tl.Light == _portalLight ? 20 * _portalPower : tl.Base;
            var flicker = 1 + (Mathf.PerlinNoise(tl.Seed, t * 6) - 0.5f) * 0.35f;
            tl.Light.intensity = baseIntensity * flicker;
            var wantShadow = d < 14 && shadowed < 4 && tl.Light != _portalLight;
            if (wantShadow) shadowed++;
            tl.Light.shadows = wantShadow ? LightShadows.Soft : LightShadows.None;
        }
    }
}

} // namespace Cryptbound
