using UnityEngine;

namespace Cryptbound {

public enum EnemyKind { Skeleton, SkeletonLord, Ghost, GhostLord, Golem }

public enum DefenseResult { Hit, Blocked, Parried, GuardBroken, Evaded }

public struct AttackInfo
{
    public Enemy Source;
    public Projectile Projectile;
    public float Damage;
    public bool Parryable;
    public bool Blockable;
    public Vector3 Origin;
    public float Knockback;
}

public struct HitInfo
{
    public float Damage;
    public Vector3 Direction;
    public float Knockback;
    public bool Heavy;
    public bool Counter;
}

// Shared enemy behaviour: health, facing, movement constraints, hit feedback and death.
public abstract class Enemy : MonoBehaviour
{
    // Public members

    public EnemyKind Kind { get; protected set; }
    public float MaxHp { get; protected set; }
    public float Hp { get; protected set; }
    public float Damage { get; protected set; }
    public float Radius { get; protected set; } = 0.6f;
    public float ExpValue { get; protected set; }
    public bool Active { get; protected set; }
    public bool Alive => Hp > 0;
    public bool IsBoss => Kind == EnemyKind.Golem;
    public PartRig Rig { get; protected set; }
    public Vector3 Position => transform.position;
    public virtual Vector3 Chest => Position + Vector3.up * 1.2f;
    public virtual bool IsThreatening => false;
    public virtual float ImpactIn => float.MaxValue;
    public bool HasToken { get; set; }
    public float Yaw { get; set; }

    protected Game G => Game.Instance;
    protected Player P => Game.Instance.Player;

    // Per-frame update driven by Game

    public abstract void Tick(float dt);

    public virtual void OnParried() { }
    public virtual void OnBlocked() { }

    // Damage

    public virtual float TakeHit(in HitInfo hit)
    {
        if (!Active || !Alive) return 0;
        var dmg = Mathf.Min(Hp, hit.Damage);
        Hp -= hit.Damage;
        Rig.Flash(new Color(1, 0.9f, 0.8f) * 0.6f);
        G.Hud.Popup(Chest + Vector3.up * 0.4f, Mathf.RoundToInt(hit.Damage).ToString(), hit.Counter ? "big" : null);
        if (Hp <= 0) Die(hit.Direction);
        return dmg;
    }

    protected virtual void Die(Vector3 dir)
    {
        Active = false;
        if (HasToken) G.ReleaseToken(this);
        G.OnEnemyKilled(this);
    }

    // Helpers

    protected float DistToPlayer
    {
        get
        {
            var d = P.Position - Position;
            d.y = 0;
            return d.magnitude;
        }
    }

    protected Vector3 DirToPlayer
    {
        get
        {
            var d = P.Position - Position;
            d.y = 0;
            return d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.forward;
        }
    }

    protected Vector3 Forward => Quaternion.Euler(0, Yaw, 0) * Vector3.forward;

    protected void FacePlayer(float dt, float degPerSec)
    {
        var d = DirToPlayer;
        var target = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        Yaw = Mathf.MoveTowardsAngle(Yaw, target, degPerSec * dt);
        transform.rotation = Quaternion.Euler(0, Yaw, 0);
    }

    protected float AngleToPlayer => Vector3.Angle(Forward, DirToPlayer);

    // Moves with separation from other enemies and the player, clamped to the walkable area.
    protected void Move(Vector3 delta)
    {
        var p = Position + delta;
        foreach (var e in G.Enemies)
        {
            if (e == this || !e.Alive) continue;
            var d = p - e.Position;
            d.y = 0;
            var min = Radius + e.Radius;
            if (d.sqrMagnitude < min * min && d.sqrMagnitude > 1e-5f) p += d.normalized * (min - d.magnitude) * 0.5f;
        }
        var toPlayer = p - P.Position;
        toPlayer.y = 0;
        var pmin = Radius + 0.45f;
        if (toPlayer.sqrMagnitude < pmin * pmin && toPlayer.sqrMagnitude > 1e-5f) p += toPlayer.normalized * (pmin - toPlayer.magnitude);
        var hw = G.Dungeon.HalfWidthAt(p.z) + (IsBoss ? -0.6f : 0.2f);
        p.x = Mathf.Clamp(p.x, -hw, hw);
        if (G.Dungeon.GateClosed || IsBoss) p.z = Mathf.Max(p.z, G.Dungeon.ArenaStart + 1.2f);
        p.z = Mathf.Min(p.z, G.Encounters.BarrierZ - 0.7f);
        p.y = 0;
        transform.position = p;
    }

    // Telegraph: glint, shrinking timing ring that closes at the moment of impact, and a sound cue.
    protected void Telegraph(float duration, Vector3 glintPos, float ringSize = 3.2f)
    {
        var col = new Color(1, 0.45f, 0.15f) * 2.5f;
        Fx.Instance.Glint(glintPos, col, 1.2f);
        Fx.Instance.Sprite(Fx.Instance.RingMaterial, Chest, null, ringSize, ringSize * 0.25f, col, duration,
                           fadeIn: 0.15f, follow: transform, ease: Ease.Linear, hold: true);
        Sfx.Play("enemy_telegraph", 0.7f);
    }
}

} // namespace Cryptbound
