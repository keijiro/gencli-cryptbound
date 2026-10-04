using System.Collections.Generic;
using UnityEngine;

namespace Cryptbound {

// Magic bolts, hurled rocks and the player's spectral wave.
public sealed class Projectile : MonoBehaviour
{
    public enum Kind { Bolt, Rock, Wave }

    public Kind Type { get; private set; }
    public bool Hostile { get; private set; }
    public float Damage { get; private set; }
    public Vector3 Velocity { get; private set; }
    public Enemy Source { get; private set; }
    public bool Dead { get; private set; }

    float _life, _radius;
    Color _color;
    Transform _visual, _glow;
    Light _light;
    readonly HashSet<Enemy> _hit = new();

    Game G => Game.Instance;

    public static Projectile Spawn(Kind kind, Vector3 pos, Vector3 velocity, float damage, bool hostile, Enemy source, Color color)
    {
        var go = new GameObject(kind.ToString());
        go.transform.position = pos;
        var p = go.AddComponent<Projectile>();
        (p.Type, p.Velocity, p.Damage, p.Hostile, p.Source, p._color) = (kind, velocity, damage, hostile, source, color);
        p._life = kind == Kind.Wave ? 0.9f : 5;
        p._radius = kind switch { Kind.Rock => 0.7f, Kind.Wave => 1.3f, _ => 0.45f };
        p.BuildVisual();
        return p;
    }

    void BuildVisual()
    {
        switch (Type)
        {
            case Kind.Rock:
            {
                var src = GameAssets.Instance.Model("prop_rubble");
                if (src != null)
                {
                    _visual = Instantiate(src, transform).transform;
                    _visual.localScale = Vector3.one * 0.7f;
                }
                break;
            }
            case Kind.Bolt:
            {
                _visual = MakeQuad("vfx_magic", 0.85f, _color * 2.5f);
                _glow = MakeQuad("vfx_glow", 2.0f, _color * 1.2f);
                _light = gameObject.AddComponent<Light>();
                _light.type = LightType.Point;
                _light.color = _color;
                _light.range = 4.5f;
                _light.intensity = 3;
                break;
            }
            case Kind.Wave:
            {
                _visual = MakeQuad("vfx_slash", 3.2f, _color * 2.5f);
                _visual.localRotation = Quaternion.LookRotation(Vector3.down, -Velocity.normalized);
                break;
            }
        }
    }

    Transform MakeQuad(string texture, float size, Color color)
    {
        var go = new GameObject(texture);
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * size;
        go.AddComponent<MeshFilter>().sharedMesh = Fx.Instance.Quad;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = GameAssets.Instance.EffectMaterial(texture);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var block = new MaterialPropertyBlock();
        block.SetColor("_Color", color);
        mr.SetPropertyBlock(block);
        return go.transform;
    }

    public void Tick(float dt)
    {
        if (Dead) return;
        _life -= dt;

        // Reflected bolts home in on their caster.
        if (!Hostile && Type != Kind.Wave && Source != null && Source.Alive)
        {
            var to = (Source.Chest - transform.position).normalized;
            Velocity = Vector3.RotateTowards(Velocity, to * Velocity.magnitude, dt * 6, 0);
        }

        transform.position += Velocity * dt;
        var cam = Camera.main;
        if (cam != null && Type == Kind.Bolt)
        {
            _visual.rotation = cam.transform.rotation * Quaternion.Euler(0, 0, Time.time * 400);
            _glow.rotation = cam.transform.rotation;
            if (Random.value < 0.7f) Fx.Instance.Magic(transform.position, 1, _color, 0.6f, 0.12f);
        }
        if (Type == Kind.Rock)
        {
            _visual.Rotate(new Vector3(400, 250, 0) * dt);
            if (Random.value < 0.4f) Fx.Instance.Dust(transform.position, 1, 0.5f);
        }
        if (Type == Kind.Wave)
        {
            var k = Mathf.Clamp01(_life / 0.9f);
            _visual.localScale = Vector3.one * (3.2f + (1 - k) * 1.5f);
        }

        if (Hostile) CheckPlayer();
        else CheckEnemies();

        var p = transform.position;
        var hw = G.Dungeon.HalfWidthAt(p.z) + 1.2f;
        if (_life <= 0 || Mathf.Abs(p.x) > hw || p.z < -8 || p.z > G.Dungeon.ArenaEnd) Kill(false);
    }

    void CheckPlayer()
    {
        var d = G.Player.Chest - transform.position;
        d.y *= 0.5f;
        if (d.magnitude > _radius + 0.5f) return;
        var info = new AttackInfo
        {
            Source = Source, Projectile = this, Damage = Damage, Parryable = true, Blockable = true,
            Origin = transform.position - Velocity.normalized, Knockback = 0.4f
        };
        var result = G.ResolveAttack(info);
        if (result == DefenseResult.Parried) Reflect();
        else if (result != DefenseResult.Evaded) Kill(true);
    }

    void CheckEnemies()
    {
        foreach (var e in G.Enemies)
        {
            if (!e.Active || !e.Alive || _hit.Contains(e)) continue;
            var d = e.Chest - transform.position;
            d.y *= 0.3f;
            if (d.magnitude > _radius + e.Radius) continue;
            _hit.Add(e);
            G.PlayerDealtDamage(e.TakeHit(new HitInfo { Damage = Damage, Direction = Velocity.normalized, Knockback = 0.8f, Heavy = true }));
            Fx.Instance.Hit(e.Chest, _color * 1.5f, 1.4f);
            Sfx.Play(Type == Kind.Wave ? "hit_ghost" : "magic_impact", 0.9f);
            if (Type != Kind.Wave) { Kill(true); return; }
        }
    }

    public void Reflect()
    {
        Hostile = false;
        Damage = G.Player.Stats.Attack * 3;
        _life = 4;
        _color = new Color(1, 0.85f, 0.4f);
        var dir = Source != null && Source.Alive ? (Source.Chest - transform.position).normalized : -Velocity.normalized;
        Velocity = dir * Velocity.magnitude * 1.6f;
        if (_light != null) _light.color = _color;
        foreach (var mr in GetComponentsInChildren<MeshRenderer>())
        {
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", _color * 2.5f);
            mr.SetPropertyBlock(block);
        }
    }

    public void Kill(bool impact)
    {
        if (Dead) return;
        Dead = true;
        if (impact)
        {
            if (Type == Kind.Rock)
            {
                Fx.Instance.Dust(transform.position, 10, 0.9f);
                Fx.Instance.Bits(transform.position, 10, new Color(0.6f, 0.5f, 0.4f));
            }
            else Fx.Instance.Hit(transform.position, _color * 1.5f);
            if (Type == Kind.Bolt) Sfx.Play("magic_impact", 0.7f);
        }
        Destroy(gameObject);
    }
}

// Experience / healing orb that bursts out of a fallen enemy and homes in on the player.
public sealed class Orb : MonoBehaviour
{
    public float Value { get; private set; }
    public bool Heal { get; private set; }
    Vector3 _velocity;
    float _age;
    Transform _quad;

    public static Orb Spawn(Vector3 pos, float value, bool heal)
    {
        var go = new GameObject(heal ? "HealOrb" : "ExpOrb");
        go.transform.position = pos;
        var o = go.AddComponent<Orb>();
        o.Value = value;
        o.Heal = heal;
        o._velocity = (Random.onUnitSphere + Vector3.up * 1.2f).normalized * Random.Range(3, 6);
        var q = new GameObject("Quad");
        q.transform.SetParent(go.transform, false);
        q.transform.localScale = Vector3.one * (heal ? 0.7f : 0.45f);
        q.AddComponent<MeshFilter>().sharedMesh = Fx.Instance.Quad;
        var mr = q.AddComponent<MeshRenderer>();
        mr.sharedMaterial = GameAssets.Instance.EffectMaterial("vfx_glow");
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var block = new MaterialPropertyBlock();
        block.SetColor("_Color", heal ? new Color(0.3f, 1, 0.4f) * 3 : new Color(0.35f, 0.75f, 1) * 3);
        mr.SetPropertyBlock(block);
        o._quad = q.transform;
        return o;
    }

    // Returns true when collected.
    public bool Tick(float dt, Vector3 target)
    {
        _age += dt;
        var p = transform.position;
        if (_age > 0.45f)
        {
            var to = target - p;
            var accel = 30 + _age * 40;
            _velocity = Vector3.Lerp(_velocity, to.normalized * (8 + _age * 12), Easing.Damp(4, dt)) + to.normalized * (accel * dt * 0.1f);
            if (to.magnitude < 0.6f) return true;
        }
        else
        {
            _velocity *= Mathf.Exp(-3 * dt);
            _velocity += Vector3.down * (3 * dt);
        }
        transform.position = p + _velocity * dt;
        var cam = Camera.main;
        if (cam != null) _quad.rotation = cam.transform.rotation;
        return false;
    }
}

} // namespace Cryptbound
