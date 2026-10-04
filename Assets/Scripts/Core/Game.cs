using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Cryptbound {

// Top-level state machine: title, play loop, level-ups, cinematic sequences and game over.
public sealed class Game : MonoBehaviour
{
    public static Game Instance { get; private set; }
    public static bool SkipTitle;
    public static float TimeBase = 1;   // editor soak tests run faster than real time

    // Play mode starts without a domain reload: statics must be reset by hand.
    // (SkipTitle still survives the in-session scene reload used for retries.)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        SkipTitle = false;
        TimeBase = 1;
#if UNITY_EDITOR
        Bot.Enabled = false;
#endif
    }

    public enum Mode { Title, Playing, LevelUp, Cinematic, GameOver, Paused }

    // Public members

    public Mode State { get; private set; } = Mode.Title;
    public int Floor { get; private set; } = 1;
    public Player Player { get; private set; }
    public Dungeon Dungeon { get; private set; }
    public Encounters Encounters { get; private set; }
    public Hud Hud { get; private set; }
    public readonly List<Enemy> Enemies = new();
    public readonly List<Projectile> Projectiles = new();
    public bool CounterPromptActive { get; private set; }
    public Golem Boss => _boss;

    // Internal state

    readonly List<Orb> _orbs = new();
    readonly HashSet<Enemy> _tokens = new(), _castTokens = new();
    CameraRig _cam;
    Golem _boss;
    bool _bossStarted, _bossActive, _bossDefeatPending, _inSequence;
    int _kills, _pendingLevelUps, _cardIndex;
    float _playTime, _hitStop, _menuTime, _playerDtScale = 1;
    SkillDef[] _offers;
    ReflectionProbe _probe;
    float _probeTimer;

    // Setup

    void Awake()
    {
        Instance = this;
        Time.timeScale = 1 * TimeBase;
        gameObject.AddComponent<Fx>();
        gameObject.AddComponent<Sfx>();
        gameObject.AddComponent<PostFx>();
        Dungeon = new GameObject("Dungeon").AddComponent<Dungeon>();
        Encounters = new Encounters();
        Hud = FindAnyObjectByType<Hud>();
        var cam = Camera.main;
        var rig = cam.GetComponent<CameraRig>();
        _cam = rig != null ? rig : cam.gameObject.AddComponent<CameraRig>();
        Player = new GameObject("Player").AddComponent<Player>();
        Player.Init();
        _cam.Target = Player.transform;

        // Dungeon reflections come from a realtime probe that follows the player.
        RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = null;
        _probe = new GameObject("ReflectionProbe").AddComponent<ReflectionProbe>();
        _probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
        _probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;
        _probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.IndividualFaces;
        _probe.resolution = 64;
        _probe.size = new Vector3(20, 10, 40);
        _probe.intensity = 1.2f;
        _probe.clearFlags = UnityEngine.Rendering.ReflectionProbeClearFlags.SolidColor;
        _probe.backgroundColor = Color.black;
    }

    IEnumerator Start()
    {
        Dungeon.Build(Floor);
        Encounters.Setup(Floor, Dungeon.Length);
        Player.transform.position = new Vector3(0, 0, SkipTitle ? -2 : 1);
        while (!Hud.Ready) yield return null;
        Hud.SetHudVisible(false);
        if (SkipTitle)
        {
            _cam.CurrentMode = CameraRig.Mode.Follow;
            _cam.Snap();
            yield return FloorIntro();
        }
        else
        {
            State = Mode.Title;
            _cam.SetOrbit(new Vector3(0, 0, 2), 0);
            _cam.Snap();
            Hud.FadeTo(0, 2.5f);
            var best = PlayerPrefs.GetInt("BestFloor", 0);
            Hud.ShowTitleScreen(best > 0 ? $"Deepest descent: B{best}" : "");
            Sfx.Instance.Music("bgm_title", 2);
            _menuTime = Time.unscaledTime;
        }
    }

    // Main loop

    void Update()
    {
#if UNITY_EDITOR
        Bot.Think();
#endif
        var udt = Time.unscaledDeltaTime;
        if (_hitStop > 0)
        {
            _hitStop -= udt;
            if (_hitStop <= 0 && !_inSequence) Time.timeScale = 1 * TimeBase;
        }

        switch (State)
        {
            case Mode.Title:
                Player.Tick(Time.deltaTime, false);
                Dungeon.Tick(Player.Position, Time.deltaTime);
                if (Controls.ConfirmPressed && Time.unscaledTime - _menuTime > 0.8f) StartCoroutine(BeginRun());
                break;
            case Mode.LevelUp:
                TickLevelUp();
                break;
            case Mode.Paused:
                if (Controls.PausePressed)
                {
                    State = Mode.Playing;
                    Time.timeScale = 1 * TimeBase;
                    Hud.HideCenter();
                    Sfx.Instance.Muffle(0);
                }
                break;
            case Mode.GameOver:
                Dungeon.Tick(Player.Position, Time.deltaTime);
                if (Controls.ConfirmPressed && Time.unscaledTime - _menuTime > 1.5f)
                {
                    SkipTitle = true;
                    Time.timeScale = 1 * TimeBase;
                    SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                }
                break;
            default:
                Simulate(Time.deltaTime);
                break;
        }

        if (State == Mode.Playing && !_inSequence && Controls.PausePressed && _hitStop <= 0)
        {
            State = Mode.Paused;
            Time.timeScale = 0;
            Hud.ShowCenter("PAUSED", "Press Esc to resume", 0);
            Sfx.Instance.Muffle(0.8f);
        }
        if (State == Mode.Playing && !_inSequence)
        {
            if (_bossDefeatPending) { _bossDefeatPending = false; StartCoroutine(BossOutro()); }
            else if (_pendingLevelUps > 0) OpenLevelUp();
        }
        UpdateHud();

        _probeTimer -= udt;
        if (_probeTimer <= 0)
        {
            _probeTimer = 0.4f;
            _probe.transform.position = Player.Position + Vector3.up * 1.6f;
            _probe.RenderProbe();
        }
    }

    void Simulate(float dt)
    {
        if (State == Mode.Playing) _playTime += dt;

        // Walkable range: behind the gate once it drops, short of any active barrier.
        Player.MinZ = Dungeon.GateClosed ? Dungeon.ArenaStart + 0.8f : -6;
        var maxZ = Dungeon.PortalOpen ? Dungeon.ArenaEnd - 0.3f : Dungeon.ArenaEnd - 2.5f;
        Player.MaxZ = Mathf.Min(maxZ, Encounters.BarrierZ - 0.8f);
        Player.Tick(dt * _playerDtScale, State == Mode.Playing && !_inSequence);

        for (var i = 0; i < Enemies.Count; i++)
            if (Enemies[i] != null && Enemies[i].Alive) Enemies[i].Tick(dt);
        if (_boss != null && _boss.IsDead && _boss.Rig != null) _boss.Rig.Tick(dt);
        Enemies.RemoveAll(e => e == null || !e.Alive);

        for (var i = 0; i < Projectiles.Count; i++) Projectiles[i].Tick(dt);
        Projectiles.RemoveAll(p => p == null || p.Dead);

        for (var i = _orbs.Count - 1; i >= 0; i--)
        {
            var o = _orbs[i];
            if (Player.Dead) break;
            if (!o.Tick(dt, Player.Chest)) continue;
            Collect(o);
            _orbs.RemoveAt(i);
            Destroy(o.gameObject);
        }

        if (State == Mode.Playing && !_inSequence && !_bossStarted) Encounters.Tick(dt);
        Dungeon.Tick(Player.Position, dt);

        if (State == Mode.Playing && !_inSequence)
        {
            if (!_bossStarted && Player.Position.z > Dungeon.ArenaStart + 3.0f) StartCoroutine(BossIntro());
            var toPortal = Player.Position - Dungeon.PortalPosition;
            toPortal.y = 0;
            if (Dungeon.PortalOpen && toPortal.magnitude < 1.9f) StartCoroutine(Descend());
        }
    }

    void UpdateHud()
    {
        if (!Hud.Ready) return;
        var s = Player.Stats;
        Hud.SetStats(Player.Hp, s.MaxHp, Player.Stamina, s.MaxStamina, s.Exp, s.ExpToNext, s.Level);
        var z = Player.Position.z;
        var inArena = _bossStarted || z > Dungeon.ArenaStart;
        var t = inArena ? 1 : Mathf.Clamp01(z / Dungeon.ArenaStart);
        Hud.SetProgress(Dungeon.ArenaStart - z, t, Encounters.Completed, inArena && !Dungeon.PortalOpen);
    }

    // Spawning

    public Enemy SpawnEnemy(EnemyKind kind, Vector3 pos)
    {
        var go = new GameObject(kind.ToString());
        go.transform.position = pos;
        Enemy e;
        switch (kind)
        {
            case EnemyKind.Ghost:
            case EnemyKind.GhostLord:
            {
                var g = go.AddComponent<Ghost>();
                g.Setup(kind == EnemyKind.GhostLord, Floor);
                e = g;
                break;
            }
            default:
            {
                var s = go.AddComponent<Skeleton>();
                s.Setup(kind == EnemyKind.SkeletonLord, Floor);
                e = s;
                break;
            }
        }
        Enemies.Add(e);
        return e;
    }

    public void SpawnBolt(Vector3 pos, Vector3 velocity, float damage, Enemy source, Color color)
      => Projectiles.Add(Projectile.Spawn(Projectile.Kind.Bolt, pos, velocity, damage, true, source, color));

    public void SpawnRock(Vector3 pos, Vector3 velocity, float damage, Enemy source)
      => Projectiles.Add(Projectile.Spawn(Projectile.Kind.Rock, pos, velocity, damage, true, source, new Color(1, 0.6f, 0.3f)));

    public void SpawnWave(Vector3 pos, Vector3 velocity, float damage)
    {
        Projectiles.Add(Projectile.Spawn(Projectile.Kind.Wave, pos, velocity, damage, false, null, new Color(0.5f, 0.8f, 1)));
        Sfx.Play("sword_swing_heavy", 0.6f, 1.4f);
    }

    void SpawnOrbs(Vector3 pos, float exp, bool heal)
    {
        var n = Mathf.Clamp(Mathf.RoundToInt(exp / 12), 3, 24);
        for (var i = 0; i < n; i++) _orbs.Add(Orb.Spawn(pos, exp / n, false));
        if (heal) _orbs.Add(Orb.Spawn(pos, Player.Stats.MaxHp * 0.15f, true));
    }

    void Collect(Orb o)
    {
        if (o.Heal)
        {
            Player.Heal(o.Value);
            Sfx.Play("heal", 0.7f);
            return;
        }
        var before = Player.Stats.MaxHp;
        var ups = Player.Stats.AddExp(o.Value);
        Sfx.Play("exp_orb", 0.25f, 1 + Random.Range(-0.1f, 0.25f));
        if (ups <= 0) return;
        _pendingLevelUps += ups;
        Player.Hp += Player.Stats.MaxHp - before;
    }

    // Combat hooks

    public DefenseResult ResolveAttack(in AttackInfo a)
    {
        var result = Player.Defend(a);
        if (result == DefenseResult.Parried)
        {
            if (a.Projectile != null) StartCoroutine(ReflectSequence());
            else if (a.Source != null && !_inSequence) StartCoroutine(ParrySequence(a.Source));
        }
        return result;
    }

    public bool TryToken(Enemy e)
    {
        _tokens.RemoveWhere(x => x == null || !x.Alive);
        if (_tokens.Count >= (Floor >= 4 ? 3 : 2)) return false;
        _tokens.Add(e);
        e.HasToken = true;
        return true;
    }

    public void ReleaseToken(Enemy e)
    {
        _tokens.Remove(e);
        if (e != null) e.HasToken = false;
    }

    public bool TryCastToken(Enemy e)
    {
        _castTokens.RemoveWhere(x => x == null || !x.Alive);
        if (_castTokens.Count >= (Floor == 1 ? 1 : 2)) return false;
        _castTokens.Add(e);
        return true;
    }

    public void ReleaseCastToken(Enemy e) => _castTokens.Remove(e);

    public void HitStop(float duration)
    {
        if (_inSequence) return;
        _hitStop = Mathf.Max(_hitStop, duration);
        Time.timeScale = 0.05f * TimeBase;
    }

    public void PlayerDealtDamage(float amount) => Player.Heal(amount * Player.Stats.LifeSteal, false);

    public void OnEnemyKilled(Enemy e)
    {
        _kills++;
        ReleaseCastToken(e);
        SpawnOrbs(e.Chest, e.ExpValue, Random.value < 0.2f);
    }

    public void OnBossKilled(Golem g)
    {
        _kills++;
        _bossDefeatPending = true;
    }

    public void OnPlayerDeath() => StartCoroutine(GameOverSequence());

    public void OnWaveCleared()
    {
        var heal = Player.Stats.MaxHp * 0.1f;
        Player.Heal(heal);
        Sfx.Play("heal", 0.5f);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    void RunLog(string message)
      => Debug.Log($"[RUN] {message} | t={_playTime:F0}s level={Player.Stats.Level} hp={Player.Hp:F0}/{Player.Stats.MaxHp:F0} kills={_kills}");

#if UNITY_EDITOR
    // Test hooks for driving the game from the editor.
    public void DebugParry()
    {
        foreach (var e in Enemies)
            if (e.Active && e.Alive) { StartCoroutine(ParrySequence(e)); return; }
    }

    public void DebugWarp(float z) => Player.transform.position = new Vector3(0, 0, z);
    public void DebugToBoss() { Encounters.SkipAll(); DebugWarp(Dungeon.ArenaStart - 1); }
    public void DebugKillBoss() { if (_boss != null) _boss.TakeHit(new HitInfo { Damage = 1e6f }); }
    public void DebugLevelUp() => _pendingLevelUps++;
    public void DebugInvulnerable(bool on) => Player.Invulnerable = on;

    public void DebugFloor(int floor)
    {
        Floor = floor - 1;
        _bossStarted = true;
        StartCoroutine(Descend());
    }
#endif

    // Level up

    void OpenLevelUp()
    {
        State = Mode.LevelUp;
        Time.timeScale = 0;
        _offers = Player.Stats.Offer(3);
        var ranks = new int[_offers.Length];
        for (var i = 0; i < _offers.Length; i++) ranks[i] = Player.Stats.Rank(_offers[i].Id);
        _cardIndex = _offers.Length / 2;
        Hud.ShowLevelUp(Player.Stats.Level - _pendingLevelUps + 1, _offers, ranks);
        Hud.SelectCard(_cardIndex);
        Sfx.Play("level_up", 0.9f);
        Fx.Instance.LevelUp(Player.transform);
        _menuTime = Time.unscaledTime;
    }

    void TickLevelUp()
    {
        if (Time.unscaledTime - _menuTime < 0.45f) return;
        var h = Controls.HorizontalPressed;
        if (h != 0)
        {
            _cardIndex = Mathf.Clamp(_cardIndex + h, 0, _offers.Length - 1);
            Hud.SelectCard(_cardIndex);
            Sfx.Play("ui_select", 0.7f);
        }
        if (!Controls.ConfirmPressed) return;
        var pick = _offers[_cardIndex];
        var maxHp = Player.Stats.MaxHp;
        var maxSt = Player.Stats.MaxStamina;
        Player.Stats.Learn(pick.Id);
        Player.Hp += Player.Stats.MaxHp - maxHp;
        Player.Stamina += Player.Stats.MaxStamina - maxSt;
        if (pick.Id == SkillId.Vitality) Player.Hp = Player.Stats.MaxHp;
        Player.Heal(Player.Stats.MaxHp * 0.2f, false);
        Sfx.Play("ui_confirm", 0.8f);
        _pendingLevelUps--;
        Hud.HideLevelUp();
        if (_pendingLevelUps > 0) { OpenLevelUp(); return; }
        Hud.Toast($"Learned {pick.Name}", 2);
        State = Mode.Playing;
        Time.timeScale = 1 * TimeBase;
    }

    // Sequences

    IEnumerator BeginRun()
    {
        _inSequence = true;
        State = Mode.Cinematic;
        Hud.HideTitleScreen();
        Sfx.Play("ui_confirm", 0.9f);
        Sfx.Instance.Music(null, 1.5f);
        _cam.CurrentMode = CameraRig.Mode.Follow;
        yield return new WaitForSecondsRealtime(1.2f);
        yield return FloorIntro();
    }

    IEnumerator FloorIntro()
    {
        _inSequence = true;
        State = Mode.Cinematic;
        Hud.SetFloor(Floor, Dungeon.Theme.Name, Encounters.Nodes);
        Hud.SetHudVisible(true);
        Hud.FadeTo(0, 1.6f);
        Hud.ShowCenter($"B{Floor}", Dungeon.Theme.Name, 3.2f);
        RunLog($"floor {Floor} start");
        Sfx.Play("boss_intro", 0.45f, 0.8f);
        Sfx.Instance.Music("bgm_explore", 2.5f);
        yield return new WaitForSecondsRealtime(1.4f);
        State = Mode.Playing;
        _inSequence = false;
        if (Floor == 1) Hud.Toast("Arrow Keys: Move  •  X: Attack  •  Z: Guard", 5);
        if (Floor == 6) Hud.Toast("Beyond here, the crypt grows ever more merciless...", 5);
    }

    IEnumerator ParrySequence(Enemy enemy)
    {
        _inSequence = true;
        State = Mode.Cinematic;
        Player.Invulnerable = true;
        _hitStop = 0;
        enemy.OnParried();
        Player.FaceTowards(enemy.Position);
        Player.PlayParryPose();

        var contact = Vector3.Lerp(Player.ShieldPoint, enemy.Chest, enemy.IsBoss ? 0.15f : 0.35f);
        Fx.Instance.Parry(contact);
        var keyLight = new GameObject("ClashLight").AddComponent<Light>();
        keyLight.type = LightType.Point;
        keyLight.color = new Color(1, 0.85f, 0.65f);
        keyLight.range = 8;
        keyLight.intensity = 30;
        keyLight.transform.position = contact + Vector3.up * 0.6f;
        Sfx.Play("parry", 1, 1, 0);
        Sfx.Play("slowmo", 0.8f, 1, 0);
        PostFx.Instance.Pulse(0.9f, -0.35f, 1.2f);
        Hud.Flash(0.45f);
        _cam.Shake(0.5f);
        Time.timeScale = 0.03f * TimeBase;
        Sfx.Instance.Muffle(1);

        // Side-on cinematic framing of the clash
        var axis = enemy.Position - Player.Position;
        axis.y = 0;
        var gap = axis.magnitude;
        axis = gap > 0.01f ? axis / gap : Vector3.forward;
        var mid = (Player.Position + enemy.Position) * 0.5f;
        var side = Vector3.Cross(Vector3.up, axis);
        if (mid.x * side.x > 0) side = -side;
        var height = enemy.IsBoss ? 2.2f : 1.3f;
        var dist = gap * 0.9f + (enemy.IsBoss ? 6.5f : 3.4f);
        var camPos = mid + side * dist + Vector3.up * height - axis * (enemy.IsBoss ? 2.5f : 1.0f);
        var limit = Dungeon.HalfWidthAt(mid.z) + 0.6f;
        if (Mathf.Abs(camPos.x) > limit)
        {
            var over = Mathf.Abs(camPos.x) - limit;
            camPos.x = Mathf.Sign(camPos.x) * limit;
            camPos -= axis * over * 0.8f;
        }
        var look = mid + Vector3.up * (enemy.IsBoss ? 2.0f : 1.2f);
        _cam.SetCinematic(camPos, look, 36, 9);
        PostFx.Instance.FocusDistance = Vector3.Distance(camPos, look);
        PostFx.Instance.Cinematic(1);
        Hud.Letterbox(true);
        yield return new WaitForSecondsRealtime(0.22f);

        // Prompt the counter
        Hud.ShowCounter(true);
        CounterPromptActive = true;
        var t = 0f;
        var pressed = false;
        while (t < Balance.CounterWindow)
        {
            if (Controls.AttackPressed) { pressed = true; break; }
            t += Time.unscaledDeltaTime;
            keyLight.intensity = Mathf.Lerp(30, 12, t / Balance.CounterWindow);
            Hud.SetCounterTimer(1 - t / Balance.CounterWindow);
            _cam.SetCinematic(camPos + axis * (t * 0.5f), look, 36 - t * 3, 9);
            yield return null;
        }
        Hud.ShowCounter(false);
        CounterPromptActive = false;

        if (pressed && enemy.Alive)
        {
            Sfx.Play("counter_slash", 1, 1, 0);
            Hud.ShowCenter("COUNTER", "", 1.1f, "glory");
            Time.timeScale = 0.25f * TimeBase;
            _playerDtScale = 4;
            Player.PlayCounterPose(1);
            var start = Player.Position;
            var target = enemy.Position - axis * (enemy.Radius + 1.0f);
            _cam.SetCinematic(mid + side * (dist * 0.6f) + Vector3.up * (height * 0.8f) - axis * 0.5f, look, 44, 14);
            var dash = 0f;
            while (dash < 0.12f)
            {
                dash += Time.unscaledDeltaTime;
                Player.transform.position = Vector3.Lerp(start, target, Easing.Apply(Ease.OutCubic, dash / 0.12f));
                Player.FaceTowards(enemy.Position);
                Fx.Instance.Magic(Player.Chest, 3, new Color(0.5f, 0.75f, 1), 1, 0.2f);
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.05f);

            var dmg = Player.Stats.Attack * Player.Stats.CounterMul * Random.Range(0.95f, 1.05f);
            var gold = new Color(1, 0.8f, 0.45f) * 3;
            var hitPos = enemy.Chest;
            PlayerDealtDamage(enemy.TakeHit(new HitInfo { Damage = dmg, Direction = axis, Knockback = 1.6f, Heavy = true, Counter = true }));
            var slashRot = Quaternion.LookRotation(side, Vector3.up);
            Fx.Instance.Slash(hitPos, slashRot * Quaternion.Euler(0, 0, 40), enemy.IsBoss ? 7 : 4.5f, gold, false, 0.45f);
            Fx.Instance.Slash(hitPos, slashRot * Quaternion.Euler(0, 0, -40), enemy.IsBoss ? 7 : 4.5f, gold * 0.8f, true, 0.5f);
            Fx.Instance.Hit(hitPos, new Color(1, 0.85f, 0.5f), 3);
            Fx.Instance.Sprite("vfx_glow", hitPos, null, 2, 9, gold, 0.5f, true);
            keyLight.transform.position = hitPos + Vector3.up * 0.5f;
            keyLight.intensity = 60;
            Sfx.Play(enemy.IsBoss ? "hit_stone" : enemy.Kind == EnemyKind.Ghost || enemy.Kind == EnemyKind.GhostLord ? "hit_ghost" : "hit_bone", 1, 0.8f, 0);
            _cam.Shake(1.0f);
            PostFx.Instance.Pulse(0.8f, -0.45f, 1.6f);
            Hud.Flash(0.6f);
            var restore = Player.Stats.MaxStamina * Balance.CounterStamina;
            Player.Stamina = Mathf.Min(Player.Stats.MaxStamina, Player.Stamina + restore);
            Hud.Popup(Player.Chest + Vector3.up * 0.8f, $"+{Mathf.RoundToInt(restore)} STAMINA", "heal");

            if (Player.Stats.Rank(SkillId.CounterNova) > 0)
            {
                Fx.Instance.Shockwave(Player.Position, new Color(0.5f, 0.8f, 1) * 2, 5, true);
                Sfx.Play("shockwave", 0.9f);
                foreach (var e in Enemies.ToArray())
                {
                    if (e == enemy || !e.Active || !e.Alive) continue;
                    var d = e.Position - Player.Position;
                    d.y = 0;
                    if (d.magnitude < 5 + e.Radius)
                        PlayerDealtDamage(e.TakeHit(new HitInfo { Damage = Player.Stats.Attack * 2, Direction = d.normalized, Knockback = 1.5f, Heavy = true }));
                }
            }

            Time.timeScale = 0;
            yield return new WaitForSecondsRealtime(0.1f);
            Time.timeScale = 0.2f * TimeBase;
            yield return new WaitForSecondsRealtime(0.4f);
        }
        else if (enemy.Alive)
        {
            Hud.Popup(enemy.Chest + Vector3.up * 0.5f, "PARRIED", "info");
        }

        // Return to play
        Destroy(keyLight.gameObject);
        _cam.CurrentMode = _bossActive ? CameraRig.Mode.Arena : CameraRig.Mode.Follow;
        PostFx.Instance.Cinematic(0);
        Hud.Letterbox(false);
        Sfx.Instance.Muffle(0);
        _playerDtScale = 1;
        var k = 0f;
        var from = Time.timeScale / TimeBase;
        while (k < 0.3f)
        {
            k += Time.unscaledDeltaTime;
            Time.timeScale = Mathf.Lerp(from, 1, k / 0.3f) * TimeBase;
            yield return null;
        }
        Time.timeScale = 1 * TimeBase;
        State = Mode.Playing;
        _inSequence = false;
        yield return new WaitForSecondsRealtime(0.3f);
        Player.Invulnerable = false;
    }

    IEnumerator ReflectSequence()
    {
        Sfx.Play("reflect", 1);
        Sfx.Play("parry", 0.5f, 1.3f);
        Fx.Instance.Parry(Player.ShieldPoint);
        Hud.Popup(Player.Chest + Vector3.up * 0.8f, "REFLECT", "info");
        Player.PlayParryPose();
        _cam.Shake(0.3f);
        PostFx.Instance.Pulse(0.5f, -0.15f, 0.6f);
        if (_inSequence) yield break;
        Time.timeScale = 0.15f * TimeBase;
        yield return new WaitForSecondsRealtime(0.2f);
        if (!_inSequence && State == Mode.Playing) Time.timeScale = 1 * TimeBase;
    }

    IEnumerator BossIntro()
    {
        _inSequence = true;
        _bossStarted = true;
        State = Mode.Cinematic;
        Player.Invulnerable = true;
        Encounters.ClearPending();
        Sfx.Instance.Music(null, 1.5f);
        Hud.Letterbox(true);
        PostFx.Instance.Cinematic(0.4f);

        // The gate slams shut behind the player.
        Dungeon.CloseGate();
        _cam.SetCinematic(new Vector3(2.5f, 2.2f, Dungeon.ArenaStart + 6), new Vector3(0, 2.2f, Dungeon.ArenaStart), 50, 5);
        yield return new WaitForSecondsRealtime(0.35f);
        Sfx.Play("gate_close", 1);
        _cam.Shake(0.45f);
        Fx.Instance.Dust(new Vector3(0, 0.2f, Dungeon.ArenaStart), 30, 1.5f);
        yield return new WaitForSecondsRealtime(1.0f);

        // The guardian assembles from the rubble.
        var go = new GameObject("Golem");
        go.transform.position = new Vector3(0, 0, Dungeon.ArenaCenter + 3.5f);
        _boss = go.AddComponent<Golem>();
        _boss.Setup(Floor);
        Enemies.Add(_boss);
        _cam.Focus = _boss.transform;
        var bp = _boss.Position;
        _cam.SetCinematic(bp + new Vector3(-4.5f, 1.2f, -8), bp + Vector3.up * 1.2f, 46, 4);
        yield return new WaitForSecondsRealtime(0.8f);
        _boss.Assemble();
        _cam.SetCinematic(bp + new Vector3(-3.5f, 2.6f, -7.5f), bp + Vector3.up * 2.6f, 44, 0.8f);
        while (!_boss.Assembled) yield return null;

        // Ignition and roar
        _cam.SetCinematic(bp + new Vector3(1.8f, 2.2f, -5.5f), _boss.Chest, 38, 6);
        yield return new WaitForSecondsRealtime(0.5f);
        _boss.Ignite();
        Sfx.Play("golem_roar", 1, 1, 0);
        Sfx.Play("boss_intro", 0.9f, 1, 0);
        _cam.Shake(0.8f);
        PostFx.Instance.Pulse(0.6f, -0.25f, 0.9f);
        Hud.Flash(0.25f);
        Fx.Instance.Embers(_boss.Chest, 50, new Color(1, 0.5f, 0.2f), 4);
        Fx.Instance.Dust(_boss.Position, 40, 2);
        Hud.ShowCenter("GUARDIAN", _boss.Title, 2.8f, "danger");
        Player.Heal(Player.Stats.MaxHp * 0.3f);
        Player.Stamina = Player.Stats.MaxStamina;
        yield return new WaitForSecondsRealtime(1.6f);

        Sfx.Instance.Music("bgm_boss", 0.4f);
        Hud.ShowBoss(_boss.Title);
        _cam.MinZ = Dungeon.ArenaStart + 0.6f;
        _cam.CurrentMode = CameraRig.Mode.Arena;
        Hud.Letterbox(false);
        PostFx.Instance.Cinematic(0);
        yield return new WaitForSecondsRealtime(0.5f);
        _bossActive = true;
        State = Mode.Playing;
        _inSequence = false;
        Player.Invulnerable = false;
    }

    IEnumerator BossOutro()
    {
        _inSequence = true;
        _bossActive = false;
        State = Mode.Cinematic;
        Player.Invulnerable = true;
        Hud.HideBoss();
        Sfx.Instance.Music(null, 2);
        foreach (var p in Projectiles) p.Kill(false);
        foreach (var e in Enemies.ToArray()) if (e != _boss && e.Alive) e.TakeHit(new HitInfo { Damage = 99999 });

        // Slow-motion collapse
        Time.timeScale = 0.2f * TimeBase;
        Sfx.Play("slowmo", 0.9f, 0.8f, 0);
        Hud.Letterbox(true);
        PostFx.Instance.Cinematic(0.7f);
        var bp = _boss.Position;
        var t = 0f;
        while (t < 1.6f)
        {
            t += Time.unscaledDeltaTime;
            var a = t * 0.5f;
            _cam.SetCinematic(bp + new Vector3(Mathf.Sin(a) * 7, 2.4f, -Mathf.Cos(a) * 7), bp + Vector3.up * 2.4f, 44, 5);
            PostFx.Instance.FocusDistance = 7;
            if (Random.value < 0.25f)
            {
                var part = new[] { _boss.Rig.Body, _boss.Rig.Right, _boss.Rig.Left }[Random.Range(0, 3)];
                Fx.Instance.Embers(part.position, 8, new Color(1, 0.5f, 0.2f), 3);
                Fx.Instance.Bits(part.position, 4, new Color(0.6f, 0.5f, 0.4f));
                _boss.Rig.Flash(new Color(1, 0.5f, 0.2f) * 1.2f, 0.15f);
                _cam.Shake(0.1f);
            }
            yield return null;
        }
        Time.timeScale = 1 * TimeBase;
        var chest = _boss.Chest;
        var exp = _boss.ExpValue;
        _boss.Collapse();
        _boss = null;
        Sfx.Play("golem_death", 1, 1, 0);
        _cam.Shake(1.0f);
        PostFx.Instance.Pulse(0.7f, -0.4f, 1.5f);
        Hud.Flash(0.7f);
        SpawnOrbs(chest, exp, true);
        yield return new WaitForSecondsRealtime(1.6f);

        // Floor cleared
        Sfx.Play("floor_clear", 1, 1, 0);
        RunLog($"floor {Floor} cleared");
        Hud.ShowCenter("FLOOR CLEARED", $"B{Floor}  —  {Dungeon.Theme.Name}", 3.5f, "glory");
        Player.Heal(Player.Stats.MaxHp * 0.5f);
        Player.Stamina = Player.Stats.MaxStamina;
        _cam.CurrentMode = CameraRig.Mode.Follow;
        PostFx.Instance.Cinematic(0);
        yield return new WaitForSecondsRealtime(2.0f);

        // The way down opens.
        Dungeon.OpenPortal();
        Sfx.Play("portal", 0.9f);
        var portal = Dungeon.PortalPosition + Vector3.up * 2;
        _cam.SetCinematic(portal + new Vector3(2.5f, 0.6f, -7), portal, 45, 2.5f);
        yield return new WaitForSecondsRealtime(2.0f);
        Hud.Letterbox(false);
        _cam.CurrentMode = CameraRig.Mode.Follow;
        Hud.Toast("The way down lies open. Step into the portal.", 4);
        State = Mode.Playing;
        _inSequence = false;
        Player.Invulnerable = false;
    }

    IEnumerator Descend()
    {
        _inSequence = true;
        State = Mode.Cinematic;
        Player.Invulnerable = true;
        Sfx.Play("portal", 1);
        Fx.Instance.Magic(Dungeon.PortalPosition + Vector3.up * 1.5f, 80, new Color(0.5f, 0.75f, 1), 4, 0.3f);
        Hud.FadeTo(1, 1.1f);
        Sfx.Instance.Music(null, 1);
        var start = Player.Position;
        var t = 0f;
        while (t < 1.3f)
        {
            t += Time.unscaledDeltaTime;
            Player.transform.position = Vector3.Lerp(start, Dungeon.PortalPosition + Vector3.forward * 0.6f, t / 1.3f);
            yield return null;
        }

        // Build the next floor in darkness.
        Floor++;
        foreach (var e in Enemies) if (e != null) Destroy(e.gameObject);
        Enemies.Clear();
        foreach (var p in Projectiles) if (p != null) Destroy(p.gameObject);
        Projectiles.Clear();
        foreach (var o in _orbs) { Collect(o); Destroy(o.gameObject); }
        _orbs.Clear();
        _tokens.Clear();
        _castTokens.Clear();
        _bossStarted = false;
        Player.UndyingUsed = false;
        Dungeon.Build(Floor);
        Encounters.Setup(Floor, Dungeon.Length);
        Player.transform.position = new Vector3(0, 0, -2);
        Player.Yaw = 0;
        _cam.MinZ = -1000;
        _cam.CurrentMode = CameraRig.Mode.Follow;
        _cam.Snap();
        PlayerPrefs.SetInt("BestFloor", Mathf.Max(PlayerPrefs.GetInt("BestFloor", 0), Floor));
        yield return new WaitForSecondsRealtime(0.4f);
        Player.Invulnerable = false;
        yield return FloorIntro();
    }

    IEnumerator GameOverSequence()
    {
        _inSequence = true;
        State = Mode.Cinematic;
        Time.timeScale = 0.3f * TimeBase;
        RunLog($"died on floor {Floor} ({(_bossStarted ? "guardian" : $"wave {Encounters.Completed + 1}")})");
        Sfx.Instance.Music(null, 1);
        Sfx.Instance.Muffle(0.6f);
        PostFx.Instance.Death(1);
        Hud.Letterbox(true);
        var p = Player.Position;
        _cam.SetCinematic(p + new Vector3(1.8f, 2.4f, -3.2f), p + Vector3.up * 0.6f, 40, 1.5f);
        yield return new WaitForSecondsRealtime(2.4f);
        Time.timeScale = 1 * TimeBase;
        Sfx.Instance.Muffle(0);
        PlayerPrefs.SetInt("BestFloor", Mathf.Max(PlayerPrefs.GetInt("BestFloor", 0), Floor));
        var time = Mathf.FloorToInt(_playTime);
        Hud.SetHudVisible(false);
        Hud.HideBoss();
        Hud.ShowGameOver($"Fell on B{Floor}  —  {Dungeon.Theme.Name}\nLevel {Player.Stats.Level}  •  Foes slain {_kills}  •  Time {time / 60:00}:{time % 60:00}");
        Sfx.Instance.Music("bgm_title", 3);
        State = Mode.GameOver;
        _menuTime = Time.unscaledTime;
    }
}

} // namespace Cryptbound
