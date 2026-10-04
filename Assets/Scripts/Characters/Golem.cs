using UnityEngine;

namespace Cryptbound {

// Floor guardian. Slams, sweeps, rolling shockwaves and hurled rocks, gaining moves on deeper floors.
public sealed class Golem : Enemy
{
    enum State { Dormant, Assembling, Idle, Windup, Recover, Stagger, Dead }
    enum Strike { Slam, Swipe, Shockwave, Barrage }

    State _state;
    Strike _move;
    float _timer, _cooldown, _windupTime, _staggerTime, _speedMul = 1;
    int _floor, _comboLeft, _rocksLeft;
    bool _enraged;
    Light _coreLight;
    ParticleSystem _embers;
    Vector3 _laneDir;
    readonly Vector3[] _scatter = new Vector3[3];
    readonly Quaternion[] _scatterRot = new Quaternion[3];

    public string Title { get; private set; }
    public override Vector3 Chest => Position + Vector3.up * 2.8f;
    public override bool IsThreatening => _state == State.Windup && _move != Strike.Shockwave;
    public bool IsDead => _state == State.Dead;
    public override float ImpactIn => IsThreatening && _move != Strike.Barrage ? _windupTime - _timer : float.MaxValue;
    public bool LaneActive => (_state == State.Windup && _move == Strike.Shockwave) || _waveLeft > 0;
    public Vector3 LanePosition => Position;
    public Vector3 LaneDirection => _laneDir;

    static readonly string[] Names = { "Stone Warden", "Bone-Bound Colossus", "Lamenting Monolith", "Drowned Titan", "Abyssal Sentinel" };

    public void Setup(int floor)
    {
        _floor = floor;
        Kind = EnemyKind.Golem;
        Title = floor <= Names.Length ? Names[floor - 1] : $"Nameless Guardian {floor - Names.Length}";
        Rig = PartRig.Create(CharacterDefs.Golem, "Rig", transform);
        Rig.BaseSmoothing = 6;
        MaxHp = Hp = 950 * Balance.HpMul(floor);
        Damage = 46 * Balance.DmgMul(floor);
        _speedMul = Mathf.Sqrt(Balance.SpeedMul(floor));
        ExpValue = 450 * Balance.ExpMul(floor);
        Radius = 1.6f;
        Yaw = 180;
        transform.rotation = Quaternion.Euler(0, Yaw, 0);

        var lgo = new GameObject("CoreLight");
        lgo.transform.SetParent(Rig.Body, false);
        lgo.transform.localPosition = new Vector3(0, 0, 0.9f);
        _coreLight = lgo.AddComponent<Light>();
        _coreLight.type = LightType.Point;
        _coreLight.color = new Color(1, 0.55f, 0.2f);
        _coreLight.range = 7;
        _coreLight.intensity = 0;
        _embers = Fx.Instance.Wisps(Rig.Body, new Vector3(0, -1.2f, 0), new Color(1, 0.5f, 0.2f), 0.9f, 0, 0.12f, 0.3f, "vfx_glow", 3);

        // Dormant: parts lie scattered among the rubble.
        _state = State.Dormant;
        _scatter[0] = new Vector3(0.4f, 0.9f, 0.6f);
        _scatter[1] = new Vector3(2.6f, 0.6f, -1.2f);
        _scatter[2] = new Vector3(-2.4f, 0.6f, 0.9f);
        _scatterRot[0] = Quaternion.Euler(80, 20, 10);
        _scatterRot[1] = Quaternion.Euler(-70, 40, 90);
        _scatterRot[2] = Quaternion.Euler(60, -30, -80);
        Rig.Stop();
        Rig.Bob = 0;
        PlaceScattered(0);
    }

    void PlaceScattered(float t)
    {
        var rest = Rig.Def.Rest;
        var parts = new[] { Rig.Body, Rig.Right, Rig.Left };
        var pos = new[] { rest.BodyPos, rest.RPos, rest.LPos };
        var rot = new[] { rest.BodyRot, rest.RRot, rest.LRot };
        for (var i = 0; i < 3; i++)
        {
            // Each part starts at a different moment and arcs up into place.
            var k = Easing.Apply(Ease.InOut, Mathf.Clamp01((t - i * 0.18f) / 0.7f));
            var arc = Mathf.Sin(k * Mathf.PI) * 2.2f;
            parts[i].localPosition = Vector3.Lerp(_scatter[i], pos[i], k) + Vector3.up * arc;
            parts[i].localRotation = Quaternion.Slerp(_scatterRot[i], rot[i], k);
        }
    }

    // Called by the boss intro sequence.
    public void Assemble()
    {
        _state = State.Assembling;
        _timer = 0;
        Sfx.Play("golem_rise", 1);
    }

    public bool Assembled => _state != State.Dormant && _state != State.Assembling;

    public void Ignite()
    {
        Active = true;
        _state = State.Idle;
        _cooldown = 1.2f;
        Rig.Bob = 1;
        Rig.Snap(Rig.Def.Rest);
        Rig.Flash(new Color(1, 0.6f, 0.3f) * 1.5f, 0.5f);
        var em = _embers.emission;
        em.rateOverTime = 10;
    }

    public override void Tick(float dt)
    {
        _timer += dt;
        var glow = _enraged ? 2.2f : 1.0f;
        if (Active) _coreLight.intensity = Mathf.Lerp(_coreLight.intensity, (3 + Mathf.Sin(Time.time * 4) * 0.6f) * glow, dt * 4);
        switch (_state)
        {
            case State.Dormant:
                return;
            case State.Assembling:
            {
                var t = _timer / 2.6f;
                PlaceScattered(t);
                _coreLight.intensity = Mathf.Clamp01(t) * 2;
                if (Random.value < 0.5f) Fx.Instance.Dust(Position + Random.insideUnitSphere * 2 + Vector3.up * 0.3f, 1, 1.2f);
                if (Random.value < 0.15f) CameraRig.Instance.Shake(0.05f);
                if (t >= 1.25f) { Rig.Snap(Rig.Def.Rest); _state = State.Idle; Rig.Bob = 1; Active = false; }
                return;
            }
            case State.Idle:
            {
                if (!Active) break;
                FacePlayer(dt, 70);
                var dist = DistToPlayer;
                if (dist > 3.8f) Move(DirToPlayer * (1.3f * _speedMul * dt));
                _cooldown -= dt;
                if (_cooldown <= 0) ChooseMove(dist);
                break;
            }
            case State.Windup:
                TickWindup(dt);
                break;
            case State.Recover:
                if (_timer > (_move == Strike.Slam ? 0.9f : 0.6f) / _speedMul)
                {
                    if (_comboLeft > 0) { _comboLeft--; StartMove(Strike.Slam, 0.7f); }
                    else
                    {
                        _state = State.Idle;
                        _cooldown = Random.Range(1.1f, 2.0f) * (_enraged ? 0.65f : 1) / _speedMul;
                    }
                }
                break;
            case State.Stagger:
                if (_timer > _staggerTime)
                {
                    _state = State.Idle;
                    _cooldown = 0.8f;
                    Rig.SetGlow(Color.black);
                }
                break;
        }
        TickShockwave(dt);
        Rig.Tick(dt);
    }

    // Move selection

    void ChooseMove(float dist)
    {
        var options = new System.Collections.Generic.List<(Strike, float)>();
        if (dist < 5.5f) { options.Add((Strike.Slam, 3)); options.Add((Strike.Swipe, 3)); }
        if (_floor >= 2 || _enraged) options.Add((Strike.Shockwave, dist > 4.5f ? 3 : 1.2f));
        if (_floor >= 3) options.Add((Strike.Barrage, dist > 5.5f ? 3 : 0.8f));
        if (options.Count == 0) { options.Add((Strike.Slam, 1)); }
        var total = 0f;
        foreach (var o in options) total += o.Item2;
        var r = Random.value * total;
        var pick = options[0].Item1;
        foreach (var o in options) { if ((r -= o.Item2) <= 0) { pick = o.Item1; break; } }
        if (pick != Strike.Shockwave && pick != Strike.Barrage && dist > 5.5f) return; // keep walking in
        _comboLeft = pick == Strike.Swipe && (_floor >= 4 || _enraged) && Random.value < 0.5f ? 1 : 0;
        StartMove(pick, 1);
    }

    float WindupFor(Strike m) => m switch
    {
        Strike.Slam => 1.15f,
        Strike.Swipe => 0.95f,
        Strike.Shockwave => 1.25f,
        _ => 0.7f
    } * (_enraged ? 0.8f : 1) / _speedMul;

    void StartMove(Strike m, float timeScale)
    {
        _move = m;
        _state = State.Windup;
        _timer = 0;
        _windupTime = WindupFor(m) * timeScale;
        var rest = Rig.Def.Rest;
        switch (m)
        {
            case Strike.Slam:
            {
                var p = rest.WithBody(rest.BodyPos + Vector3.up * 0.35f, Quaternion.Euler(-12, 0, 0))
                            .WithRight(new Vector3(1.0f, 5.4f, -0.3f), CharacterDefs.Fist(new Vector3(0.1f, 1, -0.3f)))
                            .WithLeft(new Vector3(-1.0f, 5.4f, -0.3f), CharacterDefs.Fist(new Vector3(-0.1f, 1, -0.3f)));
                Rig.Play(new PoseKey(p, _windupTime * 0.8f, Ease.Out), new PoseKey(p, _windupTime * 0.2f, Ease.Linear));
                Telegraph(_windupTime, Chest + Vector3.up * 2.5f, 5.5f);
                break;
            }
            case Strike.Swipe:
            {
                var p = rest.WithBody(rest.BodyPos, Quaternion.Euler(0, 35, 0))
                            .WithRight(new Vector3(2.6f, 3.5f, -1.3f), CharacterDefs.Fist(new Vector3(1, -0.3f, -0.5f)));
                Rig.Play(new PoseKey(p, _windupTime * 0.8f, Ease.Out), new PoseKey(p, _windupTime * 0.2f, Ease.Linear));
                Telegraph(_windupTime, transform.TransformPoint(new Vector3(2.8f, 3, -1.3f)), 5.5f);
                break;
            }
            case Strike.Shockwave:
            {
                var p = rest.WithBody(rest.BodyPos + Vector3.up * 0.25f, Quaternion.Euler(-8, 0, 0))
                            .WithRight(new Vector3(1.4f, 4.6f, 0.4f), CharacterDefs.Fist(new Vector3(0.2f, 1, 0.2f)))
                            .WithLeft(new Vector3(-1.4f, 4.6f, 0.4f), CharacterDefs.Fist(new Vector3(-0.2f, 1, 0.2f)));
                Rig.Play(new PoseKey(p, _windupTime * 0.8f, Ease.Out), new PoseKey(p, _windupTime * 0.2f, Ease.Linear));
                _laneDir = DirToPlayer;
                ShowLane();
                Sfx.Play("golem_roar", 0.45f, 1.3f);
                break;
            }
            case Strike.Barrage:
            {
                _rocksLeft = 3;
                ThrowWindup();
                break;
            }
        }
        Rig.SetGlow(new Color(1, 0.3f, 0.08f) * 0.07f);
    }

    void ShowLane()
    {
        // A burning strip on the floor marks the shockwave's path.
        var yaw = Mathf.Atan2(_laneDir.x, _laneDir.z) * Mathf.Rad2Deg;
        var center = Position + _laneDir * 9 + Vector3.up * 0.04f;
        var rot = Quaternion.Euler(90, yaw, 0);
        Fx.Instance.Sprite("vfx_glow", center, rot, 1, 1, new Color(1, 0.3f, 0.1f) * 2.2f, _windupTime + 0.3f,
                           fadeIn: 0.2f, aspect: new Vector2(2.6f, 18), hold: true);
    }

    void ThrowWindup()
    {
        _timer = 0;
        _windupTime = WindupFor(Strike.Barrage);
        var rest = Rig.Def.Rest;
        var p = rest.WithBody(rest.BodyPos, Quaternion.Euler(-6, 20, 0))
                    .WithRight(new Vector3(1.9f, 4.8f, -1.1f), CharacterDefs.Fist(new Vector3(0.3f, 1, -0.5f)));
        Rig.Play(new PoseKey(p, _windupTime, Ease.Out));
        Fx.Instance.Glint(transform.TransformPoint(new Vector3(2.2f, 6.2f, -1.6f)), new Color(1, 0.5f, 0.2f) * 2.5f, 1.4f);
        Sfx.Play("enemy_telegraph", 0.7f, 0.8f);
    }

    void TickWindup(float dt)
    {
        if (_move != Strike.Shockwave && _timer < _windupTime - 0.2f) FacePlayer(dt, _move == Strike.Barrage ? 140 : 90);
        if (_timer < _windupTime) return;
        _state = State.Recover;
        _timer = 0;
        Rig.SetGlow(Color.black);
        var rest = Rig.Def.Rest;
        switch (_move)
        {
            case Strike.Slam:
            {
                var p = rest.WithBody(rest.BodyPos + new Vector3(0, -0.3f, 0.3f), Quaternion.Euler(20, 0, 0))
                            .WithRight(new Vector3(0.85f, 2.4f, 2.2f), CharacterDefs.Fist(new Vector3(0, -1, 0.5f)))
                            .WithLeft(new Vector3(-0.85f, 2.4f, 2.2f), CharacterDefs.Fist(new Vector3(0, -1, 0.5f)));
                Rig.Play(new PoseKey(p, 0.1f, Ease.In), new PoseKey(p, 0.55f, Ease.Linear));
                var impact = Position + Forward * 3.0f;
                Fx.Instance.Shockwave(impact, new Color(1, 0.55f, 0.25f) * 1.5f, 3);
                Fx.Instance.Bits(impact + Vector3.up * 0.3f, 24, new Color(0.6f, 0.5f, 0.4f));
                Sfx.Play("golem_slam", 1);
                CameraRig.Instance.Shake(0.6f);
                var d = P.Position - impact;
                d.y = 0;
                if (d.magnitude < 3.0f) Hit(Damage * 1.3f, true);
                break;
            }
            case Strike.Swipe:
            {
                var mid = rest.WithBody(rest.BodyPos, Quaternion.identity)
                              .WithRight(new Vector3(0.6f, 2.8f, 3.0f), CharacterDefs.Fist(new Vector3(0, -0.3f, 1)));
                var end = rest.WithBody(rest.BodyPos, Quaternion.Euler(0, -35, 0))
                              .WithRight(new Vector3(-1.6f, 2.8f, 2.0f), CharacterDefs.Fist(new Vector3(-1, -0.2f, 0.7f)));
                Rig.Play(new PoseKey(mid, 0.07f, Ease.In), new PoseKey(end, 0.08f, Ease.Out), new PoseKey(end, 0.4f, Ease.Linear));
                Sfx.Play("golem_swing", 1);
                Fx.Instance.Slash(Position + Vector3.up * 2.2f + Forward * 2.4f, Quaternion.LookRotation(Vector3.down, -Forward), 7, new Color(1, 0.5f, 0.2f) * 1.2f, false, 0.3f);
                if (DistToPlayer < 4.9f && AngleToPlayer < 80) Hit(Damage, true);
                break;
            }
            case Strike.Shockwave:
            {
                var p = rest.WithBody(rest.BodyPos + new Vector3(0, -0.3f, 0.2f), Quaternion.Euler(18, 0, 0))
                            .WithRight(new Vector3(1.1f, 2.0f, 1.6f), CharacterDefs.Fist(new Vector3(0, -1, 0.3f)))
                            .WithLeft(new Vector3(-1.1f, 2.0f, 1.6f), CharacterDefs.Fist(new Vector3(0, -1, 0.3f)));
                Rig.Play(new PoseKey(p, 0.1f, Ease.In), new PoseKey(p, 0.6f, Ease.Linear));
                Sfx.Play("golem_slam", 1, 0.85f);
                Sfx.Play("shockwave", 1);
                CameraRig.Instance.Shake(0.5f);
                _wavePos = Position + _laneDir * 2.5f;
                _waveLeft = 22;
                _waveHit = false;
                break;
            }
            case Strike.Barrage:
            {
                var p = rest.WithBody(rest.BodyPos, Quaternion.Euler(8, -15, 0))
                            .WithRight(new Vector3(0.9f, 3.4f, 2.0f), CharacterDefs.Fist(new Vector3(0, 0.1f, 1)));
                Rig.Play(new PoseKey(p, 0.08f, Ease.In), new PoseKey(p, 0.2f, Ease.Linear));
                var origin = transform.TransformPoint(new Vector3(0.9f, 3.6f, 2.4f));
                var target = P.Chest + Vector3.down * 0.2f;
                G.SpawnRock(origin, (target - origin).normalized * 13, Damage * 0.7f, this);
                Sfx.Play("golem_swing", 0.8f, 1.2f);
                if (--_rocksLeft > 0) { _state = State.Windup; ThrowWindup(); }
                break;
            }
        }
    }

    void Hit(float damage, bool parryable)
      => G.ResolveAttack(new AttackInfo { Source = this, Damage = damage, Parryable = parryable, Blockable = true, Origin = Position, Knockback = 1.2f });

    // Rolling shockwave along the telegraphed lane

    Vector3 _wavePos;
    float _waveLeft;
    bool _waveHit;

    void TickShockwave(float dt)
    {
        if (_waveLeft <= 0) return;
        var step = 11 * dt;
        _wavePos += _laneDir * step;
        _waveLeft -= step;
        Fx.Instance.Dust(_wavePos, 2, 1.1f);
        if (Random.value < 0.5f) Fx.Instance.Bits(_wavePos + Vector3.up * 0.2f, 2, new Color(1, 0.5f, 0.2f));
        if (Random.value < 0.35f) Fx.Instance.Ring(_wavePos, new Color(1, 0.45f, 0.15f) * 1.5f, 0.5f, 2.6f, 0.35f);
        if (_waveHit) return;
        var d = P.Position - _wavePos;
        d.y = 0;
        if (d.magnitude < 1.25f)
        {
            _waveHit = true;
            // Unblockable: only a sidestep avoids it.
            G.ResolveAttack(new AttackInfo { Source = this, Damage = Damage * 0.9f, Parryable = false, Blockable = false, Origin = _wavePos - _laneDir, Knockback = 1.5f });
        }
    }

    // Reactions

    public override void OnParried()
    {
        _comboLeft = 0;
        _rocksLeft = 0;
        _state = State.Stagger;
        _timer = 0;
        _staggerTime = 2.0f;
        var rest = Rig.Def.Rest;
        var p = rest.WithBody(rest.BodyPos + Vector3.up * 0.2f, Quaternion.Euler(-20, 0, 8))
                    .WithRight(new Vector3(2.1f, 2.4f, -0.6f), CharacterDefs.Fist(new Vector3(0.3f, -1, -0.4f)))
                    .WithLeft(new Vector3(-2.1f, 2.4f, -0.6f), CharacterDefs.Fist(new Vector3(-0.3f, -1, -0.4f)));
        Rig.Play(new PoseKey(p, 0.15f, Ease.Out), new PoseKey(p, 1.6f, Ease.Linear));
        Rig.SetGlow(new Color(0.4f, 0.6f, 1) * 0.05f);
    }

    public override float TakeHit(in HitInfo hit)
    {
        if (!Active) return 0;
        var h = hit;
        h.Damage *= P.Stats.GuardianMul * (_state == State.Stagger ? 1.25f : 1);
        var dealt = base.TakeHit(h);
        G.Hud.SetBoss(Hp / MaxHp);
        if (Alive && !_enraged && Hp < MaxHp * 0.5f)
        {
            _enraged = true;
            Sfx.Play("golem_roar", 1);
            CameraRig.Instance.Shake(0.5f);
            Rig.Flash(new Color(1, 0.4f, 0.1f) * 1.5f, 0.6f);
            var em = _embers.emission;
            em.rateOverTime = 35;
            G.Hud.Toast("The Guardian is enraged!", 2.5f);
        }
        return dealt;
    }

    protected override void Die(Vector3 dir)
    {
        Active = false;
        _state = State.Dead;
        _waveLeft = 0;
        G.OnBossKilled(this);
    }

    // Final collapse, driven by the outro sequence.
    public void Collapse()
    {
        Fx.Instance.Shockwave(Position, new Color(1, 0.5f, 0.2f) * 2, 5);
        Fx.Instance.Bits(Chest, 60, new Color(0.65f, 0.55f, 0.45f));
        Fx.Instance.Dust(Chest, 50, 2.2f);
        Fx.Instance.Embers(Chest, 80, new Color(1, 0.5f, 0.2f), 4);
        Fx.Instance.Sprite("vfx_glow", Chest, null, 2, 14, new Color(1, 0.6f, 0.3f) * 3, 0.7f);
        Rig.Shatter(Vector3.up * 2 + Forward * -3, 2.5f);
        Rig = null;
        Destroy(gameObject, 0.1f);
    }
}

} // namespace Cryptbound
