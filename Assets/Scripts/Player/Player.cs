using UnityEngine;

namespace Cryptbound {

// The spectral knight: movement, auto-aimed combo attacks, stamina-limited guard and parry timing.
public sealed class Player : MonoBehaviour
{
    // Public members

    public PlayerStats Stats { get; } = new();
    public PartRig Rig { get; private set; }
    public float Hp { get; set; }
    public float Stamina { get; set; }
    public float Yaw { get; set; }
    public bool Guarding { get; private set; }
    public bool Dead { get; private set; }
    public bool Invulnerable { get; set; }
    public bool UndyingUsed { get; set; }
    public float MinZ { get; set; } = -6;
    public float MaxZ { get; set; } = 1000;
    public Vector3 Position => transform.position;
    public Vector3 Chest => transform.position + Vector3.up * 1.2f;
    public Vector3 Forward => Quaternion.Euler(0, Yaw, 0) * Vector3.forward;
    public bool Attacking => _combo >= 0;
    public Vector3 ShieldPoint => transform.TransformPoint(new Vector3(-0.1f, 1.2f, 0.75f));

    Game G => Game.Instance;

    // Internal state

    float _guardPressTime = -10;
    float _stagger;
    float _regenDelay;
    bool _guardLocked;
    int _combo = -1;
    float _attackTime;
    bool _queued, _hitDone;
    Enemy _target;
    float _lunge;
    Vector3 _velocity;
    ParticleSystem _flames;
    Light _light;

    public void Init()
    {
        Rig = PartRig.Create(CharacterDefs.Player, "Rig", transform);
        Hp = Stats.MaxHp;
        Stamina = Stats.MaxStamina;
        _flames = Fx.Instance.Wisps(transform, new Vector3(0, 0.55f, -0.05f), new Color(0.35f, 0.55f, 1), 0.3f, 45, 0.45f, 0.5f, "vfx_flame", 2.2f);
        var lgo = new GameObject("SpiritLight");
        lgo.transform.SetParent(transform, false);
        lgo.transform.localPosition = new Vector3(0, 0.45f, 0.2f);
        _light = lgo.AddComponent<Light>();
        _light.type = LightType.Point;
        _light.color = new Color(0.45f, 0.6f, 1);
        _light.range = 6;
        _light.intensity = 4;
    }

    // Poses

    Pose Rest => Rig.Def.Rest;

    Pose GuardPose => Rest
        .WithBody(Rest.BodyPos, Quaternion.Euler(6, 0, 0))
        .WithRight(new Vector3(0.42f, 0.98f, 0.0f), Pose.Aim(new Vector3(0.15f, 1, 0.05f)))
        .WithLeft(new Vector3(-0.1f, 1.18f, 0.62f), Quaternion.Euler(0, 0, 0));

    Pose ParryPose => Rest
        .WithBody(Rest.BodyPos, Quaternion.Euler(10, 0, 0))
        .WithRight(new Vector3(0.45f, 0.95f, -0.1f), Pose.Aim(new Vector3(0.3f, 1, -0.2f)))
        .WithLeft(new Vector3(-0.02f, 1.3f, 0.85f), Quaternion.Euler(-18, 12, 0));

    Pose StaggerPose => Rest
        .WithBody(Rest.BodyPos + Vector3.down * 0.05f, Quaternion.Euler(-16, 0, 10))
        .WithRight(new Vector3(0.65f, 0.85f, -0.25f), Pose.Aim(new Vector3(0.6f, 0.6f, -0.5f)))
        .WithLeft(new Vector3(-0.65f, 0.9f, -0.1f), Quaternion.Euler(10, -50, 0));

    struct Swing
    {
        public PoseKey[] Keys;
        public float Hit, Chain, Total, Range, Arc, Damage, Knockback;
        public bool Heavy, Vertical, Flip;
    }

    Swing MakeSwing(int index)
    {
        var s = Stats.AttackSpeed;
        var r = Rest;
        switch (index)
        {
            case 0:
                return new Swing
                {
                    Keys = new[]
                    {
                        new PoseKey(r.WithBody(r.BodyPos, Quaternion.Euler(0, 24, 0)).WithRight(new Vector3(0.8f, 1.32f, -0.05f), Pose.Aim(new Vector3(1, 0.25f, -0.55f))), 0.10f / s, Ease.Out),
                        new PoseKey(r.WithBody(r.BodyPos, Quaternion.identity).WithRight(new Vector3(0.2f, 1.18f, 0.88f), Pose.Aim(new Vector3(0.05f, 0.05f, 1))), 0.04f / s, Ease.Linear),
                        new PoseKey(r.WithBody(r.BodyPos, Quaternion.Euler(0, -26, 0)).WithRight(new Vector3(-0.5f, 1.05f, 0.5f), Pose.Aim(new Vector3(-1, -0.1f, 0.3f))), 0.05f / s, Ease.Out),
                        new PoseKey(r.WithBody(r.BodyPos, Quaternion.Euler(0, -26, 0)).WithRight(new Vector3(-0.48f, 1.02f, 0.45f), Pose.Aim(new Vector3(-1, -0.1f, 0.3f))), 0.12f / s, Ease.Linear),
                    },
                    Hit = 0.12f / s, Chain = 0.22f / s, Total = 0.42f / s, Range = 2.5f, Arc = 125, Damage = 1, Knockback = 0.35f, Flip = false
                };
            case 1:
                return new Swing
                {
                    Keys = new[]
                    {
                        new PoseKey(r.WithBody(r.BodyPos, Quaternion.Euler(0, -24, 0)).WithRight(new Vector3(-0.45f, 1.22f, 0.3f), Pose.Aim(new Vector3(-1, 0.25f, -0.25f))), 0.08f / s, Ease.Out),
                        new PoseKey(r.WithBody(r.BodyPos, Quaternion.identity).WithRight(new Vector3(0.15f, 1.15f, 0.9f), Pose.Aim(new Vector3(0, 0, 1))), 0.04f / s, Ease.Linear),
                        new PoseKey(r.WithBody(r.BodyPos, Quaternion.Euler(0, 26, 0)).WithRight(new Vector3(0.85f, 1.05f, 0.3f), Pose.Aim(new Vector3(1, -0.1f, 0.2f))), 0.05f / s, Ease.Out),
                        new PoseKey(r.WithBody(r.BodyPos, Quaternion.Euler(0, 26, 0)).WithRight(new Vector3(0.82f, 1.02f, 0.28f), Pose.Aim(new Vector3(1, -0.1f, 0.2f))), 0.12f / s, Ease.Linear),
                    },
                    Hit = 0.10f / s, Chain = 0.22f / s, Total = 0.42f / s, Range = 2.5f, Arc = 125, Damage = 1.1f, Knockback = 0.35f, Flip = true
                };
            default:
                return new Swing
                {
                    Keys = new[]
                    {
                        new PoseKey(r.WithBody(r.BodyPos + Vector3.up * 0.15f, Quaternion.Euler(-12, 0, 0)).WithRight(new Vector3(0.25f, 2.05f, -0.1f), Pose.Aim(new Vector3(0.1f, 1, -0.6f))).WithLeft(new Vector3(-0.55f, 1.2f, 0.0f), Quaternion.Euler(0, -30, 0)), 0.18f / s, Ease.Out),
                        new PoseKey(r.WithBody(r.BodyPos + new Vector3(0, -0.1f, 0.2f), Quaternion.Euler(16, 0, 0)).WithRight(new Vector3(0.1f, 0.85f, 1.0f), Pose.Aim(new Vector3(0, -0.45f, 1))), 0.07f / s, Ease.In),
                        new PoseKey(r.WithBody(r.BodyPos + new Vector3(0, -0.1f, 0.2f), Quaternion.Euler(16, 0, 0)).WithRight(new Vector3(0.1f, 0.82f, 0.95f), Pose.Aim(new Vector3(0, -0.45f, 1))), 0.2f / s, Ease.Linear),
                    },
                    Hit = 0.24f / s, Chain = 99, Total = 0.62f / s, Range = 2.9f, Arc = 150, Damage = 2, Knockback = 1.4f, Heavy = true, Vertical = true
                };
        }
    }

    Swing _swing;

    // Update

    public void Tick(float dt, bool input)
    {
        if (Dead) { Rig?.Tick(dt); return; }

        var move = input ? Controls.Move : Vector2.zero;
        var attack = input && Controls.AttackPressed;
        var guardPressed = input && Controls.GuardPressed;
        var guardHeld = input && Controls.GuardHeld;

        _stagger -= dt;
        var canAct = _stagger <= 0;

        // Guard
        if (guardPressed && canAct && !_guardLocked)
        {
            _guardPressTime = Time.time;
            CancelAttack();
        }
        if (_guardLocked && Stamina > Stats.MaxStamina * 0.2f) _guardLocked = false;
        Guarding = guardHeld && canAct && !_guardLocked && !Attacking;
        if (Guarding)
        {
            Stamina -= Balance.GuardDrain * Stats.GuardCostMul * dt;
            _regenDelay = Balance.StaminaRegenDelay;
            if (Stamina <= 0)
            {
                Stamina = 0;
                _guardLocked = true;
                Guarding = false;
                G.Hud.Popup(Chest + Vector3.up * 0.6f, "EXHAUSTED", "player");
            }
        }
        else
        {
            _regenDelay -= dt;
            if (_regenDelay <= 0) Stamina = Mathf.Min(Stats.MaxStamina, Stamina + Stats.StaminaRegen * dt);
        }

        // Attack
        if (attack && canAct && !Guarding)
        {
            if (!Attacking) StartSwing(0);
            else if (_combo + 1 < Stats.ComboLength && _attackTime > _swing.Hit * 0.5f) _queued = true;
        }
        if (Attacking) TickAttack(dt);

        // Movement
        var speed = Stats.MoveSpeed * (Attacking ? 0.35f : Guarding ? 0.5f : 1) * (canAct ? 1 : 0);
        var target = new Vector3(move.x, 0, move.y) * speed;
        _velocity = Vector3.Lerp(_velocity, target, Easing.Damp(16, dt));
        var delta = _velocity * dt;
        if (_lunge > 0 && _target != null && _target.Alive)
        {
            var to = _target.Position - Position;
            to.y = 0;
            var step = Mathf.Min(_lunge, 9 * dt);
            if (to.magnitude - _target.Radius > 1.3f) delta += to.normalized * step;
            _lunge -= step;
        }
        MoveBy(delta);

        // Facing
        UpdateFacing(dt);

        // Base pose follows guard state, leaning into movement.
        var lv = Quaternion.Inverse(transform.rotation) * _velocity;
        var lean = Quaternion.Euler(lv.z * 2.5f, 0, -lv.x * 2.5f);
        var basePose = Guarding ? GuardPose : Rest;
        basePose.BodyRot = lean * basePose.BodyRot;
        Rig.Base = basePose;
        Rig.Tick(dt);

        _light.intensity = 4 + Mathf.Sin(Time.time * 7) * 0.4f;
    }

    public void MoveBy(Vector3 delta)
    {
        var p = Position + delta;
        foreach (var e in G.Enemies)
        {
            if (!e.Alive || !e.Active) continue;
            var d = p - e.Position;
            d.y = 0;
            var min = e.Radius + 0.45f;
            if (d.sqrMagnitude < min * min && d.sqrMagnitude > 1e-5f) p += d.normalized * (min - d.magnitude);
        }
        var hw = G.Dungeon.HalfWidthAt(p.z);
        p.x = Mathf.Clamp(p.x, -hw, hw);
        p.z = Mathf.Clamp(p.z, MinZ, MaxZ);
        p.y = 0;
        transform.position = p;
    }

    void UpdateFacing(float dt)
    {
        float targetYaw;
        if (Attacking && _target != null && _target.Alive) targetYaw = YawTo(_target.Position);
        else
        {
            var t = FindTarget(Guarding ? 12 : 9, Guarding);
            targetYaw = t.HasValue ? YawTo(t.Value) : 0;
        }
        Yaw = Mathf.MoveTowardsAngle(Yaw, targetYaw, (Attacking || Guarding ? 900 : 420) * dt);
        transform.rotation = Quaternion.Euler(0, Yaw, 0);
    }

    float YawTo(Vector3 p)
    {
        var d = p - Position;
        return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
    }

    // Picks the most relevant thing to face: threats first while guarding, otherwise the nearest foe.
    Vector3? FindTarget(float maxDist, bool guarding)
    {
        Vector3? best = null;
        var bestScore = float.MaxValue;
        foreach (var e in G.Enemies)
        {
            if (!e.Active || !e.Alive) continue;
            var d = e.Position - Position;
            d.y = 0;
            var dist = d.magnitude - e.Radius;
            if (dist > maxDist) continue;
            var score = dist + (d.z < -0.5f ? 3 : 0) - (guarding && e.IsThreatening ? 5 : 0);
            if (score < bestScore) { bestScore = score; best = e.Position; }
        }
        if (guarding)
        {
            foreach (var p in G.Projectiles)
            {
                if (p.Dead || !p.Hostile) continue;
                var d = p.transform.position - Position;
                d.y = 0;
                if (Vector3.Dot(p.Velocity, -d) <= 0) continue;
                var score = d.magnitude - 6;
                if (score < bestScore) { bestScore = score; best = p.transform.position; }
            }
        }
        return best;
    }

    Enemy FindAttackTarget()
    {
        Enemy best = null;
        var bestScore = float.MaxValue;
        foreach (var e in G.Enemies)
        {
            if (!e.Active || !e.Alive) continue;
            var d = e.Position - Position;
            d.y = 0;
            var dist = d.magnitude - e.Radius;
            if (dist > 6.5f) continue;
            var score = dist + Vector3.Angle(Forward, d) * 0.015f;
            if (score < bestScore) { bestScore = score; best = e; }
        }
        return best;
    }

    // Attacks

    void StartSwing(int index)
    {
        _combo = index;
        _attackTime = 0;
        _queued = false;
        _hitDone = false;
        _swing = MakeSwing(index);
        Rig.Play(_swing.Keys);
        _target = FindAttackTarget();
        if (_target != null)
        {
            Yaw = YawTo(_target.Position);
            transform.rotation = Quaternion.Euler(0, Yaw, 0);
            var to = _target.Position - Position;
            to.y = 0;
            _lunge = Mathf.Clamp(to.magnitude - _target.Radius - 1.4f, 0, 1.4f);
        }
        else _lunge = 0;
        Sfx.Play(_swing.Heavy ? "sword_swing_heavy" : "sword_swing", 0.8f, _swing.Heavy ? 0.9f : 1.05f);
    }

    void TickAttack(float dt)
    {
        _attackTime += dt;
        if (!_hitDone && _attackTime >= _swing.Hit) { _hitDone = true; DoHit(); }
        if (_queued && _attackTime >= _swing.Chain) StartSwing(_combo + 1);
        else if (_attackTime >= _swing.Total) _combo = -1;
    }

    void CancelAttack()
    {
        if (!Attacking) return;
        _combo = -1;
        _lunge = 0;
        Rig.Stop();
    }

    void DoHit()
    {
        var fwd = Forward;
        var slashPos = Chest + fwd * 1.0f + Vector3.up * (_swing.Vertical ? 0.2f : 0);
        var slashRot = _swing.Vertical
            ? Quaternion.LookRotation(Quaternion.Euler(0, Yaw, 0) * Vector3.right, Vector3.up)
            : Quaternion.LookRotation(Vector3.down, -fwd);
        var blue = new Color(0.55f, 0.8f, 1) * 2.2f;
        Fx.Instance.Slash(slashPos, slashRot, _swing.Heavy ? 3.4f : 2.6f, blue, _swing.Flip);

        var dealt = 0f;
        var hitAny = false;
        var hitKind = EnemyKind.Skeleton;
        foreach (var e in G.Enemies)
        {
            if (!e.Active || !e.Alive) continue;
            var d = e.Position - Position;
            d.y = 0;
            if (d.magnitude - e.Radius > _swing.Range) continue;
            if (Vector3.Angle(fwd, d) > _swing.Arc * 0.5f && d.magnitude > e.Radius + 0.3f) continue;
            var dmg = Stats.Attack * _swing.Damage * Random.Range(0.9f, 1.1f);
            var hitPoint = e.Chest - d.normalized * e.Radius * 0.8f;
            hitKind = e.Kind;
            dealt += e.TakeHit(new HitInfo { Damage = dmg, Direction = d.normalized, Knockback = _swing.Knockback, Heavy = _swing.Heavy });
            Fx.Instance.Hit(hitPoint, e.Kind == EnemyKind.Golem ? new Color(1, 0.6f, 0.3f) : new Color(0.8f, 0.9f, 1), _swing.Heavy ? 1.6f : 1);
            hitAny = true;
        }
        if (hitAny)
        {
            var sfx = hitKind switch { EnemyKind.Golem => "hit_stone", EnemyKind.Ghost or EnemyKind.GhostLord => "hit_ghost", _ => "hit_bone" };
            Sfx.Play(sfx, 0.9f);
            G.HitStop(_swing.Heavy ? 0.09f : 0.045f);
            CameraRig.Instance.Shake(_swing.Heavy ? 0.3f : 0.14f);
            G.PlayerDealtDamage(dealt);
        }

        // Spectral Wave: the final strike of a full combo fires a piercing wave.
        var last = _combo == Stats.ComboLength - 1;
        if (last && Stats.Rank(SkillId.SpectralWave) > 0)
            G.SpawnWave(Chest + fwd * 0.8f, fwd * 15, Stats.Attack * 0.9f);
    }

    // Counter strike, played during the parry cinematic.
    public void PlayCounterPose(float speed)
    {
        CancelAttack();
        var r = Rest;
        Rig.Play(
            new PoseKey(r.WithBody(r.BodyPos + Vector3.up * 0.2f, Quaternion.Euler(-14, 30, 0)).WithRight(new Vector3(0.6f, 2.1f, -0.3f), Pose.Aim(new Vector3(0.5f, 1, -0.6f))), 0.12f / speed, Ease.Out),
            new PoseKey(r.WithBody(r.BodyPos + new Vector3(0, -0.15f, 0.3f), Quaternion.Euler(20, -30, 0)).WithRight(new Vector3(-0.4f, 0.8f, 1.0f), Pose.Aim(new Vector3(-0.6f, -0.5f, 1))), 0.06f / speed, Ease.In),
            new PoseKey(r.WithBody(r.BodyPos + new Vector3(0, -0.15f, 0.3f), Quaternion.Euler(20, -30, 0)).WithRight(new Vector3(-0.42f, 0.78f, 0.95f), Pose.Aim(new Vector3(-0.6f, -0.5f, 1))), 0.35f / speed, Ease.Linear));
    }

    public void PlayParryPose() => Rig.Play(new PoseKey(ParryPose, 0.05f, Ease.Out), new PoseKey(ParryPose, 0.4f, Ease.Linear));

    public void FaceTowards(Vector3 p)
    {
        Yaw = YawTo(p);
        transform.rotation = Quaternion.Euler(0, Yaw, 0);
    }

    // Defense

    public bool CanParry(in AttackInfo a)
    {
        if (!a.Parryable || !Guarding) return false;
        if (!IsFacing(a.Origin)) return false;
        return Time.time - _guardPressTime <= Stats.ParryWindow;
    }

    bool IsFacing(Vector3 origin)
    {
        var d = origin - Position;
        d.y = 0;
        return d.sqrMagnitude < 0.01f || Vector3.Angle(Forward, d) < 100;
    }

    public DefenseResult Defend(in AttackInfo a)
    {
        if (Dead || Invulnerable) return DefenseResult.Evaded;
        var dir = a.Origin - Position;
        dir.y = 0;
        dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
        if (CanParry(a)) return DefenseResult.Parried;
        if (Guarding && a.Blockable && IsFacing(a.Origin))
        {
            var cost = a.Damage * Balance.BlockCost * Stats.GuardCostMul;
            if (Stamina >= cost)
            {
                Stamina -= cost;
                _regenDelay = Balance.StaminaRegenDelay;
                Sfx.Play("shield_block", 0.9f);
                Fx.Instance.Hit(ShieldPoint, new Color(1, 0.85f, 0.6f), 1.2f);
                Rig.Flash(new Color(0.6f, 0.7f, 1) * 0.5f);
                CameraRig.Instance.Shake(0.18f);
                MoveBy(-dir * (0.25f + a.Knockback * 0.2f));
                if (Stats.Thorns > 0 && a.Source != null && a.Source.Alive && a.Projectile == null)
                    G.PlayerDealtDamage(a.Source.TakeHit(new HitInfo { Damage = a.Damage * Stats.Thorns, Direction = -dir, Knockback = 0.3f }));
                a.Source?.OnBlocked();
                return DefenseResult.Blocked;
            }
            Stamina = 0;
            _guardLocked = true;
            Sfx.Play("guard_break", 1);
            G.Hud.Popup(Chest + Vector3.up * 0.6f, "GUARD BREAK", "player");
            TakeDamage(a.Damage * 0.6f, dir, 1.0f, a.Knockback + 0.5f);
            return DefenseResult.GuardBroken;
        }
        TakeDamage(a.Damage, dir, 0.25f, a.Knockback);
        return DefenseResult.Hit;
    }

    void TakeDamage(float dmg, Vector3 dir, float stagger, float knockback)
    {
        dmg = Mathf.Round(dmg);
        Hp -= dmg;
        Rig.Flash(new Color(1, 0.2f, 0.15f) * 0.9f, 0.2f);
        // Swings carry hyper armor: only heavy blows interrupt an attack in progress.
        var heavy = stagger >= 0.5f || knockback >= 1;
        if (!Attacking || heavy)
        {
            CancelAttack();
            _stagger = stagger;
            Rig.Play(new PoseKey(StaggerPose, 0.06f, Ease.Out), new PoseKey(StaggerPose, stagger * 0.8f, Ease.Linear));
        }
        MoveBy(-dir * knockback);
        G.Hud.Popup(Chest + Vector3.up * 0.4f, Mathf.RoundToInt(dmg).ToString(), "player");
        PostFx.Instance.Hurt(Mathf.Clamp01(0.4f + dmg / Stats.MaxHp * 3));
        CameraRig.Instance.Shake(0.4f);
        Sfx.Play("player_hurt", 0.9f);
        if (Hp > 0) return;
        if (Stats.Rank(SkillId.Undying) > 0 && !UndyingUsed)
        {
            UndyingUsed = true;
            Hp = 1;
            Fx.Instance.LevelUp(transform);
            Sfx.Play("heal", 1, 0.8f);
            G.Hud.Toast("Undying — you refuse to fall!", 2.5f);
            return;
        }
        Die(dir);
    }

    void Die(Vector3 dir)
    {
        Hp = 0;
        Dead = true;
        Guarding = false;
        var em = _flames.emission;
        em.enabled = false;
        _light.enabled = false;
        Rig.Shatter(-dir * 2.0f, 999);
        Rig = null;
        Sfx.Play("player_death", 1);
        G.OnPlayerDeath();
    }

    public void Heal(float amount, bool popup = true)
    {
        if (Dead || amount <= 0) return;
        var before = Hp;
        Hp = Mathf.Min(Stats.MaxHp, Hp + amount);
        if (popup && Hp - before >= 1) G.Hud.Popup(Chest + Vector3.up * 0.5f, "+" + Mathf.RoundToInt(Hp - before), "heal");
    }
}

} // namespace Cryptbound
