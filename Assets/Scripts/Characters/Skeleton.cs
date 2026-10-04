using UnityEngine;

namespace Cryptbound {

// Melee undead. The Lord variant is faster, chains strikes and raises its shield.
public sealed class Skeleton : Enemy
{
    enum State { Rising, Approach, Windup, Recover, Stagger, Block }

    State _state;
    float _timer, _cooldown, _windup, _speed, _range, _blockChance, _staggerTime;
    int _comboLeft;
    bool _lord, _horizontal;

    public override bool IsThreatening => _state == State.Windup;
    public override float ImpactIn => _state == State.Windup ? _windupDuration - _timer : float.MaxValue;

    public void Setup(bool lord, int floor)
    {
        _lord = lord;
        Kind = lord ? EnemyKind.SkeletonLord : EnemyKind.Skeleton;
        Rig = PartRig.Create(lord ? CharacterDefs.SkeletonLord : CharacterDefs.Skeleton, "Rig", transform);
        MaxHp = Hp = (lord ? 240 : 90) * Balance.HpMul(floor);
        Damage = (lord ? 40 : 26) * Balance.DmgMul(floor);
        _windup = Mathf.Max(0.42f, (lord ? 0.62f : 0.8f) / Balance.SpeedMul(floor));
        _speed = (lord ? 2.5f : 2.1f) * Balance.SpeedMul(floor);
        _range = lord ? 1.95f : 1.75f;
        _blockChance = lord ? 0.3f : 0;
        ExpValue = (lord ? 95 : 35) * Balance.ExpMul(floor);
        Radius = lord ? 0.7f : 0.6f;
        _cooldown = Random.Range(0.4f, 1.4f);
        _state = State.Rising;
        Rig.transform.localPosition = Vector3.down * 2.4f;
        Yaw = 180;
        transform.rotation = Quaternion.Euler(0, Yaw, 0);
        Fx.Instance.Dust(Position + Vector3.up * 0.2f, 18, 1.2f);
        Fx.Instance.Bits(Position + Vector3.up * 0.3f, 10, new Color(0.6f, 0.55f, 0.45f));
    }

    public override void Tick(float dt)
    {
        _timer += dt;
        switch (_state)
        {
            case State.Rising:
            {
                var t = Mathf.Clamp01(_timer / 1.1f);
                Rig.transform.localPosition = Vector3.down * (2.4f * (1 - Easing.Apply(Ease.OutCubic, t)));
                if (Random.value < 0.3f) Fx.Instance.Dust(Position, 1, 0.8f);
                if (t >= 1) { Active = true; SetState(State.Approach); }
                break;
            }
            case State.Approach:
            {
                FacePlayer(dt, 300);
                var dist = DistToPlayer;
                if (dist > _range * 0.9f) Move(DirToPlayer * (_speed * dt));
                else Move(Vector3.Cross(Vector3.up, DirToPlayer) * (Mathf.Sin(Time.time * 0.8f + Yaw) * 0.6f * dt));
                _cooldown -= dt;
                if (_cooldown <= 0 && dist < _range + 0.5f && G.TryToken(this))
                {
                    _comboLeft = _lord ? Random.Range(1, 3) : 0;
                    _horizontal = _lord && Random.value < 0.5f;
                    StartWindup(_windup);
                }
                break;
            }
            case State.Windup:
                if (_timer < _windupDuration - 0.15f) FacePlayer(dt, 240);
                if (DistToPlayer > _range * 0.8f) Move(DirToPlayer * (_speed * 0.5f * dt));
                if (_timer >= _windupDuration) Strike();
                break;
            case State.Recover:
                if (_comboLeft > 0 && _timer > 0.28f)
                {
                    _comboLeft--;
                    _horizontal = !_horizontal;
                    StartWindup(Mathf.Max(0.36f, _windup * 0.65f));
                }
                else if (_timer > 0.75f)
                {
                    G.ReleaseToken(this);
                    _cooldown = Random.Range(1.0f, 2.2f);
                    SetState(State.Approach);
                }
                break;
            case State.Stagger:
                if (_timer > _staggerTime)
                {
                    G.ReleaseToken(this);
                    Rig.SetGlow(Color.black);
                    _cooldown = Random.Range(0.6f, 1.2f);
                    SetState(State.Approach);
                }
                break;
            case State.Block:
                FacePlayer(dt, 400);
                if (_timer > 0.7f) { Rig.Base = Rig.Def.Rest; SetState(State.Approach); }
                break;
        }
        if (Rig != null) Rig.Tick(dt);
    }

    void SetState(State s)
    {
        _state = s;
        _timer = 0;
    }

    // Poses

    float _windupDuration;

    Pose WindupPose => _horizontal
        ? Rig.Def.Rest.WithRight(new Vector3(0.8f, 1.35f, -0.1f), Pose.Aim(new Vector3(1, 0.3f, -0.5f))).WithBody(Rig.Def.Rest.BodyPos, Quaternion.Euler(0, 25, 0))
        : Rig.Def.Rest.WithRight(new Vector3(0.35f, 1.85f, -0.2f), Pose.Aim(new Vector3(0.1f, 1, -0.6f))).WithBody(Rig.Def.Rest.BodyPos + Vector3.up * 0.1f, Quaternion.Euler(-10, 0, 0));

    Pose StrikePose => _horizontal
        ? Rig.Def.Rest.WithRight(new Vector3(-0.5f, 1.05f, 0.6f), Pose.Aim(new Vector3(-1, -0.05f, 0.4f))).WithBody(Rig.Def.Rest.BodyPos, Quaternion.Euler(0, -25, 0))
        : Rig.Def.Rest.WithRight(new Vector3(0.2f, 0.75f, 0.8f), Pose.Aim(new Vector3(0, -0.4f, 1))).WithBody(Rig.Def.Rest.BodyPos - Vector3.up * 0.05f, Quaternion.Euler(14, 0, 0));

    Pose MidPose => Rig.Def.Rest.WithRight(new Vector3(0.15f, 1.15f, 0.9f), Pose.Aim(new Vector3(0, 0.05f, 1)));

    void StartWindup(float duration)
    {
        _windupDuration = duration;
        SetState(State.Windup);
        var wp = WindupPose;
        Rig.Play(new PoseKey(wp, duration * 0.75f, Ease.Out), new PoseKey(wp, duration * 0.25f, Ease.Linear));
        Rig.SetGlow(new Color(1, 0.3f, 0.08f) * 0.07f);
        Telegraph(duration, transform.TransformPoint(new Vector3(0.4f, 2.3f, 0)));
    }

    void Strike()
    {
        SetState(State.Recover);
        Rig.SetGlow(Color.black);
        var sp = StrikePose;
        if (_horizontal) Rig.Play(new PoseKey(MidPose, 0.05f, Ease.In), new PoseKey(sp, 0.05f, Ease.Out), new PoseKey(sp, 0.25f, Ease.Linear));
        else Rig.Play(new PoseKey(sp, 0.08f, Ease.In), new PoseKey(sp, 0.3f, Ease.Linear));
        Sfx.Play(_lord ? "sword_swing_heavy" : "sword_swing", 0.8f);
        Fx.Instance.Slash(Chest + Forward * 0.9f, SlashRotation(), _lord ? 2.6f : 2.2f, new Color(1, 0.55f, 0.3f) * 1.6f, !_horizontal);
        if (DistToPlayer < _range + 0.55f && AngleToPlayer < 70)
        {
            var info = new AttackInfo { Source = this, Damage = Damage, Parryable = true, Blockable = true, Origin = Position, Knockback = 0.5f };
            G.ResolveAttack(info);
        }
    }

    Quaternion SlashRotation()
      => _horizontal
        ? Quaternion.LookRotation(Vector3.down, -Forward)
        : Quaternion.LookRotation(Quaternion.Euler(0, Yaw, 0) * Vector3.right, Vector3.up);

    // Reactions

    public override void OnParried()
    {
        _comboLeft = 0;
        _staggerTime = 1.6f;
        SetState(State.Stagger);
        Rig.Play(new PoseKey(Rig.Def.Rest.WithBody(Rig.Def.Rest.BodyPos, Quaternion.Euler(-22, 0, 10))
                                .WithRight(new Vector3(0.75f, 1.6f, -0.4f), Pose.Aim(new Vector3(0.6f, 1, -0.6f))), 0.12f, Ease.Out),
                 new PoseKey(Rig.Def.Rest.WithBody(Rig.Def.Rest.BodyPos, Quaternion.Euler(-18, 0, 8)), 1.2f, Ease.InOut));
        Rig.SetGlow(new Color(0.4f, 0.6f, 1) * 0.06f);
    }

    public override void OnBlocked()
    {
        _comboLeft = Mathf.Min(_comboLeft, 0);
        Move(-Forward * 0.25f);
    }

    public override float TakeHit(in HitInfo hit)
    {
        if (!Active) return 0;
        // Lords raise their shield against light frontal blows when not attacking.
        if (_lord && (_state == State.Approach || _state == State.Block) && !hit.Heavy && AngleToPlayer < 60 && Random.value < _blockChance)
        {
            SetState(State.Block);
            Rig.Base = Rig.Def.Rest.WithLeft(new Vector3(-0.1f, 1.25f, 0.65f), Quaternion.identity);
            Sfx.Play("shield_block", 0.8f, 1.1f);
            Fx.Instance.Hit(transform.TransformPoint(new Vector3(-0.1f, 1.25f, 0.7f)), new Color(1, 0.8f, 0.5f));
            G.Hud.Popup(Chest + Vector3.up * 0.5f, "BLOCKED", "info");
            return base.TakeHit(new HitInfo { Damage = hit.Damage * 0.15f, Direction = hit.Direction });
        }
        var dealt = base.TakeHit(hit);
        if (Alive && (_state == State.Approach || hit.Heavy))
        {
            Move(hit.Direction * hit.Knockback);
            if (_state != State.Stagger && _state != State.Windup && _state != State.Recover)
                Rig.Play(new PoseKey(Rig.Def.Rest.WithBody(Rig.Def.Rest.BodyPos, Quaternion.Euler(-12, 0, 6)), 0.06f, Ease.Out),
                         new PoseKey(Rig.Def.Rest, 0.18f, Ease.InOut));
        }
        if (Alive && hit.Counter) { _staggerTime = Mathf.Max(_staggerTime, 0.9f); if (_state != State.Stagger) SetState(State.Stagger); }
        return dealt;
    }

    protected override void Die(Vector3 dir)
    {
        base.Die(dir);
        Sfx.Play("skeleton_death", 0.9f);
        Fx.Instance.Bits(Chest, 18, new Color(0.75f, 0.7f, 0.6f));
        Fx.Instance.Dust(Position + Vector3.up * 0.5f, 14, 1.0f);
        Rig.Shatter(dir * 3.5f);
        Rig = null;
        Destroy(gameObject, 0.05f);
    }
}

} // namespace Cryptbound
