using System.Collections.Generic;
using UnityEngine;

namespace Cryptbound {

// Places enemy waves along the corridor. Reaching a wave raises a spectral barrier ahead
// until every foe in it has fallen.
public sealed class Encounters
{
    sealed class Wave
    {
        public float Trigger;
        public List<EnemyKind> Kinds = new();
    }

    readonly List<Wave> _waves = new();
    readonly Queue<EnemyKind> _pending = new();
    int _next, _maxAlive, _floor;
    bool _active;
    float _barrierZ, _spawnTimer, _barrierFade;
    float _length;
    Transform _barrier;
    Material _barrierMat;
    Light _barrierLight;

    public bool Active => _active;
    public int Completed => _next;
    public float BarrierZ => _active ? _barrierZ : float.MaxValue;
    public float[] Nodes { get; private set; }

    static readonly int ColorID = Shader.PropertyToID("_Color");

    public void Setup(int floor, float length)
    {
        _floor = floor;
        _length = length;
        _waves.Clear();
        _pending.Clear();
        _next = 0;
        _active = false;
        _maxAlive = Mathf.Min(3 + (floor - 1) / 2, 6);
        var count = Balance.EncounterCount(floor);
        var nodes = new float[count];
        for (var i = 0; i < count; i++)
        {
            var w = new Wave { Trigger = 12 + i * (length - 26) / Mathf.Max(1, count - 1) };
            Compose(w, floor, i);
            _waves.Add(w);
            nodes[i] = w.Trigger / length;
        }
        Nodes = nodes;
        BuildBarrier();
    }

    static void Compose(Wave w, int floor, int index)
    {
        if (floor == 1 && index == 0) { w.Kinds.AddRange(new[] { EnemyKind.Skeleton, EnemyKind.Skeleton }); return; }
        if (floor == 1 && index == 1) { w.Kinds.AddRange(new[] { EnemyKind.Skeleton, EnemyKind.Ghost }); return; }
        var budget = 1.8f + floor * 0.9f + index * 0.4f;
        if (floor == 2 && index == 1) { w.Kinds.Add(EnemyKind.SkeletonLord); budget -= 2.8f; }
        if (floor == 3 && index == 1) { w.Kinds.Add(EnemyKind.GhostLord); budget -= 3.2f; }
        var ghosts = 0;
        var maxGhosts = 2 + floor / 3;
        var guard = 0;
        while (budget > 0.6f && guard++ < 40)
        {
            var options = new List<(EnemyKind kind, float cost, float weight)> { (EnemyKind.Skeleton, 1, 3) };
            if (ghosts < maxGhosts) options.Add((EnemyKind.Ghost, 1.2f, 2));
            if (floor >= 2) options.Add((EnemyKind.SkeletonLord, 2.8f, 0.8f + floor * 0.25f));
            if (floor >= 3 && ghosts < maxGhosts) options.Add((EnemyKind.GhostLord, 3.2f, 0.5f + floor * 0.2f));
            var total = 0f;
            foreach (var o in options) total += o.weight;
            var r = Random.value * total;
            var pick = options[0];
            foreach (var o in options) { if ((r -= o.weight) <= 0) { pick = o; break; } }
            if (pick.cost > budget + 0.5f) pick = options[0];
            w.Kinds.Add(pick.kind);
            budget -= pick.cost;
            if (pick.kind == EnemyKind.Ghost || pick.kind == EnemyKind.GhostLord) ghosts++;
        }
        // Melee foes first so ranged ones arrive as reinforcements.
        w.Kinds.Sort((a, b) => IsRanged(a).CompareTo(IsRanged(b)));
    }

    static bool IsRanged(EnemyKind k) => k == EnemyKind.Ghost || k == EnemyKind.GhostLord;

    void BuildBarrier()
    {
        if (_barrier != null) Object.Destroy(_barrier.gameObject);
        var go = new GameObject("Barrier");
        _barrier = go.transform;
        _barrier.SetParent(Game.Instance.Dungeon.transform, false);
        var quad = new GameObject("Quad");
        quad.transform.SetParent(_barrier, false);
        quad.transform.localPosition = new Vector3(0, Balance.CorridorHeight * 0.5f, 0);
        quad.transform.localScale = new Vector3(Balance.CorridorHalfWidth * 2, Balance.CorridorHeight, 1);
        quad.AddComponent<MeshFilter>().sharedMesh = Fx.Instance.Quad;
        var mr = quad.AddComponent<MeshRenderer>();
        _barrierMat = new Material(GameAssets.Instance.EffectMaterial("vfx_rune")) { mainTextureScale = new Vector2(4, 2.9f) };
        mr.sharedMaterial = _barrierMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var lgo = new GameObject("Light");
        lgo.transform.SetParent(_barrier, false);
        lgo.transform.localPosition = new Vector3(0, 2, -0.8f);
        _barrierLight = lgo.AddComponent<Light>();
        _barrierLight.type = LightType.Point;
        _barrierLight.color = new Color(0.4f, 0.75f, 1);
        _barrierLight.range = 8;
        go.SetActive(false);
    }

    public void Tick(float dt)
    {
        var g = Game.Instance;
        UpdateBarrier(dt);
        if (!_active)
        {
            if (_next < _waves.Count && g.Player.Position.z > _waves[_next].Trigger) Begin(_waves[_next]);
            return;
        }
        var alive = 0;
        foreach (var e in g.Enemies) if (e.Alive && !e.IsBoss) alive++;
        _spawnTimer -= dt;
        if (_pending.Count > 0 && alive < _maxAlive && _spawnTimer <= 0)
        {
            Spawn(_pending.Dequeue(), _waves[_next].Trigger, alive);
            _spawnTimer = 0.9f;
        }
        else if (_pending.Count == 0 && alive == 0) End();
    }

    void Begin(Wave w)
    {
        _active = true;
        _barrierZ = w.Trigger + 15;
        foreach (var k in w.Kinds) _pending.Enqueue(k);
        var g = Game.Instance;
        // Opening group spawns at once, the rest trickle in.
        var initial = Mathf.Min(_pending.Count, _maxAlive);
        for (var i = 0; i < initial; i++) Spawn(_pending.Dequeue(), w.Trigger, i);
        _spawnTimer = 2;
        _barrier.position = new Vector3(0, 0, _barrierZ);
        _barrier.gameObject.SetActive(true);
        _barrierFade = 0;
        Sfx.Play("barrier_up", 0.8f);
        if (g.Floor == 1 && _next == 0) g.Hud.Toast("Tap Z the instant a blow lands to PARRY — then strike back with X", 6);
    }

    void Spawn(EnemyKind kind, float trigger, int slot)
    {
        var g = Game.Instance;
        var ranged = IsRanged(kind);
        var z = ranged ? trigger + Random.Range(10.5f, 13) : trigger + Random.Range(6.5f, 10);
        z = Mathf.Max(z, g.Player.Position.z + (ranged ? 8 : 5));
        z = Mathf.Min(z, _barrierZ - 1);
        var x = Mathf.Clamp(((slot % 3) - 1) * 1.6f + Random.Range(-0.5f, 0.5f), -2.2f, 2.2f);
        g.SpawnEnemy(kind, new Vector3(x, 0, z));
    }

    void End()
    {
        _active = false;
        _next++;
        Sfx.Play("barrier_down", 0.8f);
        Game.Instance.OnWaveCleared();
        Fx.Instance.Magic(_barrier.position + Vector3.up * 2.5f, 60, new Color(0.4f, 0.75f, 1), 4, 0.25f);
    }

    void UpdateBarrier(float dt)
    {
        if (_barrier == null || !_barrier.gameObject.activeSelf) return;
        _barrierFade = Mathf.MoveTowards(_barrierFade, _active ? 1 : 0, dt * (_active ? 1.5f : 2.5f));
        var pulse = 1 + Mathf.Sin(Time.time * 3) * 0.15f;
        _barrierMat.SetColor(ColorID, new Color(0.35f, 0.7f, 1) * (0.38f * _barrierFade * pulse));
        _barrierMat.mainTextureOffset = new Vector2(Time.time * 0.05f, 0);
        _barrierLight.intensity = 3 * _barrierFade;
        if (!_active && _barrierFade <= 0) _barrier.gameObject.SetActive(false);
    }

    public void ClearPending() => _pending.Clear();

    public void SkipAll()
    {
        _pending.Clear();
        _active = false;
        _next = _waves.Count;
    }
}

} // namespace Cryptbound
