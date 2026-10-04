#if UNITY_EDITOR
using UnityEngine;

namespace Cryptbound {

// Editor-only autopilot used to soak-test pacing and difficulty. Plays like an
// imperfect human: parries a share of attacks, blocks some, eats the rest.
public static class Bot
{
    public static bool Enabled;
    public static float ParrySkill = 0.55f;
    public static float BlockSkill = 0.5f;

    public static Vector2 Move;
    public static bool Attack, GuardHeld, GuardPressed, Confirm;
    public static int Horizontal;

    static float _guardUntil, _nextAttack, _nextConfirm, _counterDelay = -1;
    static readonly System.Collections.Generic.Dictionary<Object, (int plan, bool acted)> _plans = new();

    public static void Think()
    {
        Attack = GuardPressed = Confirm = false;
        Horizontal = 0;
        if (!Enabled) return;
        var g = Game.Instance;
        var p = g.Player;
        var now = Time.unscaledTime;

        if (g.State == Game.Mode.LevelUp || g.State == Game.Mode.GameOver || g.State == Game.Mode.Title)
        {
            if (now > _nextConfirm) { Confirm = true; Attack = true; _nextConfirm = now + 0.6f; }
            Move = Vector2.zero;
            GuardHeld = false;
            return;
        }

        // Counter prompt: react after a human-ish delay.
        if (g.CounterPromptActive)
        {
            if (_counterDelay < 0) _counterDelay = now + Random.Range(0.25f, 0.6f);
            if (now > _counterDelay) Attack = true;
            Move = Vector2.zero;
            return;
        }
        _counterDelay = -1;

        // Incoming threats
        Enemy nearest = null;
        var nearestDist = float.MaxValue;
        foreach (var e in g.Enemies)
        {
            if (!e.Active || !e.Alive) continue;
            var d = Vector3.Distance(e.Position, p.Position) - e.Radius;
            if (d < nearestDist) { nearestDist = d; nearest = e; }
            if (e.IsThreatening && d < 5.5f) React(e, e.ImpactIn, p);
        }
        foreach (var pr in g.Projectiles)
        {
            if (pr.Dead || !pr.Hostile) continue;
            var to = p.Chest - pr.transform.position;
            if (Vector3.Dot(to, pr.Velocity) <= 0) continue;
            React(pr, Mathf.Max(0, to.magnitude - 0.9f) / pr.Velocity.magnitude, p);
        }
        GuardHeld = now < _guardUntil;

        // Movement
        var target = p.Position + Vector3.forward * 5;
        if (g.Dungeon.PortalOpen) target = g.Dungeon.PortalPosition;
        else if (nearest != null) target = nearest.Position;
        var dir = target - p.Position;
        dir.y = 0;
        var want = Vector2.zero;
        var keep = nearest != null && !g.Dungeon.PortalOpen ? 1.9f : 0.3f;
        if (dir.magnitude > keep) want = new Vector2(dir.x, dir.z).normalized;

        // Sidestep a golem shockwave lane.
        if (g.Boss != null && g.Boss.LaneActive)
        {
            var rel = p.Position - g.Boss.LanePosition;
            var side = Vector3.Cross(Vector3.up, g.Boss.LaneDirection);
            var lateral = Vector3.Dot(rel, side);
            if (Mathf.Abs(lateral) < 2.2f)
            {
                var s = lateral >= 0 ? 1 : -1;
                if (Mathf.Abs(p.Position.x) > g.Dungeon.HalfWidthAt(p.Position.z) - 1.2f) s = -s;
                var sv = side * s;
                want = new Vector2(sv.x, sv.z);
            }
        }
        Move = want;

        // Attack when in reach and not guarding
        if (nearest != null && nearestDist < 2.4f && !GuardHeld && now > _nextAttack)
        {
            Attack = true;
            _nextAttack = now + Random.Range(0.12f, 0.3f);
        }
    }

    static void React(Object source, float impactIn, Player p)
    {
        if (impactIn > 0.7f) return;
        if (_plans.Count > 64) _plans.Clear();
        if (!_plans.TryGetValue(source, out var st))
        {
            var r = Random.value;
            st.plan = r < ParrySkill ? 1 : r < ParrySkill + (1 - ParrySkill) * BlockSkill ? 2 : 0;
            st.acted = false;
        }
        if (!st.acted)
        {
            if (st.plan == 1 && impactIn < p.Stats.ParryWindow * 0.6f) { GuardPressed = true; _guardUntil = Time.unscaledTime + 0.3f; st.acted = true; }
            else if (st.plan == 2 && impactIn < 0.45f) { GuardPressed = true; _guardUntil = Time.unscaledTime + 0.45f; st.acted = true; }
        }
        _plans[source] = st;
    }
}

} // namespace Cryptbound
#endif
