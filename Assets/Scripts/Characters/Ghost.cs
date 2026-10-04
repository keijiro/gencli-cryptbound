using UnityEngine;

namespace Cryptbound {

// Ranged wraith that keeps its distance and hurls magic bolts. Bolts can be blocked,
// sidestepped, or parried back at the caster.
public sealed class Ghost : Enemy
{
    enum State { Appearing, Hover, Charge, Recover, Stagger }

    State _state;
    float _timer, _cooldown, _charge, _boltSpeed, _prefDist, _phase, _staggerTime;
    bool _lord;
    Color _color;
    Light _light;
    int _pattern;

    public override Vector3 Chest => Position + Vector3.up * (_lord ? 2.2f : 1.95f);
    Vector3 HandPoint => transform.TransformPoint(_lord ? new Vector3(0.9f, 1.75f, 0.8f) : new Vector3(0.78f, 1.6f, 0.7f));

    public void Setup(bool lord, int floor)
    {
        _lord = lord;
        Kind = lord ? EnemyKind.GhostLord : EnemyKind.Ghost;
        Rig = PartRig.Create(lord ? CharacterDefs.GhostLord : CharacterDefs.Ghost, "Rig", transform);
        MaxHp = Hp = (lord ? 220 : 70) * Balance.HpMul(floor);
        Damage = (lord ? 30 : 22) * Balance.DmgMul(floor);
        _charge = Mathf.Max(0.55f, (lord ? 0.85f : 1.05f) / Balance.SpeedMul(floor));
        _boltSpeed = (lord ? 10.5f : 8.0f) * (1 + 0.04f * (floor - 1));
        ExpValue = (lord ? 100 : 40) * Balance.ExpMul(floor);
        Radius = lord ? 0.75f : 0.65f;
        _prefDist = Random.Range(7.0f, 9.5f);
        _phase = Random.value * 10;
        _cooldown = Random.Range(1.0f, 2.2f);
        _color = lord ? new Color(0.75f, 0.35f, 1) : new Color(0.35f, 0.75f, 1);
        Yaw = 180;
        transform.rotation = Quaternion.Euler(0, Yaw, 0);
        Rig.transform.localScale = Vector3.zero;
        Fx.Instance.Wisps(Rig.transform, new Vector3(0, 0.6f, 0), _color, 0.45f, 14, 0.5f, 0.15f, "vfx_glow", 1.2f);
        var lgo = new GameObject("Light");
        lgo.transform.SetParent(transform, false);
        lgo.transform.localPosition = new Vector3(0, 1.6f, 0.6f);
        _light = lgo.AddComponent<Light>();
        _light.type = LightType.Point;
        _light.color = _color;
        _light.range = 4;
        _light.intensity = 1.2f;
        Fx.Instance.Magic(Chest, 40, _color, 3, 0.25f);
        Fx.Instance.Sprite("vfx_glow", Chest, null, 0.5f, 4, _color * 2, 0.6f);
        Sfx.Play("ghost_charge", 0.4f, 1.3f);
    }

    public override void Tick(float dt)
    {
        _timer += dt;
        _phase += dt;
        switch (_state)
        {
            case State.Appearing:
            {
                var t = Mathf.Clamp01(_timer / 0.9f);
                Rig.transform.localScale = Vector3.one * Easing.Apply(Ease.OutBack, t);
                if (t >= 1) { Active = true; SetState(State.Hover); }
                break;
            }
            case State.Hover:
                FacePlayer(dt, 200);
                Drift(dt, 1);
                _cooldown -= dt;
                if (_cooldown <= 0 && DistToPlayer < 15 && G.TryCastToken(this)) StartCharge();
                break;
            case State.Charge:
            {
                FacePlayer(dt, 160);
                Drift(dt, 0.3f);
                var k = _timer / _charge;
                Rig.SetGlow(_color * (k * 0.25f));
                _light.intensity = 1.2f + k * 4;
                if (Random.value < 0.6f) Fx.Instance.Magic(HandPoint, 1, _color, 1.5f, 0.12f);
                if (_timer >= _charge) Fire();
                break;
            }
            case State.Recover:
                FacePlayer(dt, 200);
                Drift(dt, 0.6f);
                if (_timer > 0.7f)
                {
                    G.ReleaseCastToken(this);
                    _cooldown = Random.Range(2.0f, 3.2f) / Balance.SpeedMul(G.Floor);
                    SetState(State.Hover);
                }
                break;
            case State.Stagger:
                if (_timer > _staggerTime) { G.ReleaseCastToken(this); _cooldown = 1; SetState(State.Hover); }
                break;
        }
        if (Alive) Rig.Tick(dt);
    }

    void SetState(State s)
    {
        _state = s;
        _timer = 0;
    }

    // Hover ahead of the player, weaving side to side; back away when crowded.
    void Drift(float dt, float speedMul)
    {
        var target = new Vector3(Mathf.Sin(_phase * 0.6f) * 1.9f, 0, P.Position.z + _prefDist);
        target.z = Mathf.Max(target.z, Position.z - 1.5f);
        var d = target - Position;
        d.y = 0;
        var speed = (DistToPlayer < 3.5f ? 3.6f : 1.8f) * speedMul;
        Move(Vector3.ClampMagnitude(d, 1) * (speed * dt));
    }

    void StartCharge()
    {
        SetState(State.Charge);
        _pattern = _lord ? Random.Range(0, 2) : 0;
        Fx.Instance.Sprite("vfx_magic", HandPoint, null, 0.1f, 0.9f, _color * 2.2f, _charge, spin: 220, fadeIn: 0.3f,
                           follow: transform, ease: Ease.In, hold: true);
        Fx.Instance.Glint(HandPoint, _color * 2, 1.3f);
        Sfx.Play("ghost_charge", 0.7f);
    }

    void Fire()
    {
        SetState(State.Recover);
        Rig.SetGlow(Color.black);
        _light.intensity = 1.2f;
        var origin = HandPoint;
        var aim = P.Chest - origin;
        aim.y = 0;
        aim.Normalize();
        if (_pattern == 0 && _lord)
        {
            foreach (var a in new[] { -14f, 0, 14f })
                G.SpawnBolt(origin, Quaternion.Euler(0, a, 0) * aim * _boltSpeed, Damage, this, _color);
        }
        else
        {
            G.SpawnBolt(origin, aim * _boltSpeed, Damage, this, _color);
            if (_lord) { _burst = 2; _burstTimer = 0.22f; }
        }
        Sfx.Play("ghost_bolt", 0.8f);
        Fx.Instance.Sprite("vfx_glow", origin, null, 0.5f, 2.2f, _color * 2, 0.25f);
    }

    int _burst;
    float _burstTimer;

    void LateUpdate()
    {
        if (_burst <= 0 || !Alive) return;
        _burstTimer -= Time.deltaTime;
        if (_burstTimer > 0) return;
        _burst--;
        _burstTimer = 0.22f;
        var origin = HandPoint;
        var aim = P.Chest - origin;
        aim.y = 0;
        G.SpawnBolt(origin, aim.normalized * _boltSpeed, Damage, this, _color);
        Sfx.Play("ghost_bolt", 0.6f, 1.1f);
    }

    public override float TakeHit(in HitInfo hit)
    {
        var dealt = base.TakeHit(hit);
        if (!Alive) return dealt;
        // Fragile casters: any hit interrupts the spell.
        if (_state == State.Charge || hit.Heavy)
        {
            Rig.SetGlow(Color.black);
            _light.intensity = 1.2f;
            _staggerTime = hit.Counter ? 1.2f : 0.45f;
            SetState(State.Stagger);
        }
        Move(hit.Direction * (hit.Knockback + 0.3f));
        Rig.Play(new PoseKey(Rig.Def.Rest.WithBody(Rig.Def.Rest.BodyPos + Vector3.up * 0.1f, Quaternion.Euler(-15, 0, 8)), 0.06f, Ease.Out),
                 new PoseKey(Rig.Def.Rest, 0.3f, Ease.InOut));
        return dealt;
    }

    protected override void Die(Vector3 dir)
    {
        base.Die(dir);
        _burst = 0;
        Sfx.Play("ghost_death", 0.8f);
        Fx.Instance.Magic(Chest, 60, _color, 4, 0.3f);
        Fx.Instance.Sprite("vfx_glow", Chest, null, 1, 5, _color * 2, 0.6f);
        Fx.Instance.Dust(Chest, 10, 1.2f, new Color(_color.r * 0.4f, _color.g * 0.4f, _color.b * 0.5f, 0.5f));
        gameObject.AddComponent<FadeOut>().Begin(Rig.transform, 0.8f);
        Destroy(_light);
    }
}

// Shrinks and lifts a transform before destroying the owning object.
public sealed class FadeOut : MonoBehaviour
{
    Transform _target;
    float _time, _duration;
    Vector3 _scale;

    public void Begin(Transform target, float duration)
      => (_target, _duration, _scale) = (target, duration, target.localScale);

    void Update()
    {
        _time += Time.deltaTime;
        var k = Mathf.Clamp01(_time / _duration);
        _target.localScale = new Vector3(_scale.x * (1 + k * 0.4f), _scale.y * (1 - k), _scale.z * (1 + k * 0.4f));
        _target.localPosition += Vector3.up * (Time.deltaTime * 1.5f);
        if (k >= 1) Destroy(gameObject);
    }
}

} // namespace Cryptbound
