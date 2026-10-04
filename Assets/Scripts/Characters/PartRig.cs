using UnityEngine;

namespace Cryptbound {

// Transform-only pose of a three-part character (body + two floating hands),
// expressed in the character's local space (+Z forward).
public struct Pose
{
    public Vector3 BodyPos;
    public Quaternion BodyRot;
    public Vector3 RPos;
    public Quaternion RRot;
    public Vector3 LPos;
    public Quaternion LRot;

    public static Pose Lerp(in Pose a, in Pose b, float t) => new Pose
    {
        BodyPos = Vector3.LerpUnclamped(a.BodyPos, b.BodyPos, t),
        BodyRot = Quaternion.SlerpUnclamped(a.BodyRot, b.BodyRot, t),
        RPos = Vector3.LerpUnclamped(a.RPos, b.RPos, t),
        RRot = Quaternion.SlerpUnclamped(a.RRot, b.RRot, t),
        LPos = Vector3.LerpUnclamped(a.LPos, b.LPos, t),
        LRot = Quaternion.SlerpUnclamped(a.LRot, b.LRot, t)
    };

    // Rotation that points a part's +Y axis (blade / arm axis) along dir.
    public static Quaternion Aim(Vector3 dir, Vector3 upHint)
    {
        dir.Normalize();
        if (Mathf.Abs(Vector3.Dot(dir, upHint.normalized)) > 0.97f) upHint = Vector3.forward;
        return Quaternion.LookRotation(dir, upHint) * Quaternion.Euler(90, 0, 0);
    }

    public static Quaternion Aim(Vector3 dir) => Aim(dir, Mathf.Abs(dir.normalized.y) > 0.7f ? Vector3.back : Vector3.up);

    public Pose WithBody(Vector3 pos, Quaternion rot) { var p = this; p.BodyPos = pos; p.BodyRot = rot; return p; }
    public Pose WithRight(Vector3 pos, Quaternion rot) { var p = this; p.RPos = pos; p.RRot = rot; return p; }
    public Pose WithLeft(Vector3 pos, Quaternion rot) { var p = this; p.LPos = pos; p.LRot = rot; return p; }
}

public readonly struct PoseKey
{
    public readonly Pose Pose;
    public readonly float Duration;
    public readonly Ease Ease;
    public PoseKey(in Pose pose, float duration, Ease ease = Ease.InOut)
      => (Pose, Duration, Ease) = (pose, duration, ease);
}

// Mesh part placement relative to its animated pivot.
public sealed class PartDef
{
    public string Model;
    public float Scale = 1;
    public Vector3 Pivot;        // point in normalized model space that sits on the pivot
    public Vector3 Euler;        // extra rotation applied to the mesh
    public bool Mirror;
}

public sealed class RigDef
{
    public PartDef Body, Right, Left;
    public Pose Rest;
    public float BobAmplitude = 0.05f;
    public float BobFrequency = 1.5f;
}

// Builds and animates a character purely through part positions and rotations.
public sealed class PartRig : MonoBehaviour
{
    // Public members

    public Transform Body { get; private set; }
    public Transform Right { get; private set; }
    public Transform Left { get; private set; }
    public RigDef Def { get; private set; }
    public Pose Base { get; set; }
    public float BaseSmoothing { get; set; } = 14;
    public bool IsPlaying => _keys != null;
    public float Bob { get; set; } = 1;
    public Pose Current => _current;

    // Rig construction

    public static PartRig Create(RigDef def, string name, Transform parent = null)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        var rig = go.AddComponent<PartRig>();
        rig.Def = def;
        rig.Body = MakePart(def.Body, "Body", go.transform);
        rig.Right = MakePart(def.Right, "Right", go.transform);
        rig.Left = MakePart(def.Left, "Left", go.transform);
        rig.Base = def.Rest;
        rig._current = def.Rest;
        rig._phase = Random.value * 10;
        var mats = new System.Collections.Generic.List<Material>();
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
        {
            var instances = r.materials;
            foreach (var m in instances)
            {
                m.EnableKeyword("_EMISSIVE");
                m.SetColor(EmissiveID, Color.black);
            }
            mats.AddRange(instances);
        }
        rig._materials = mats.ToArray();
        rig.Apply();
        return rig;
    }

    static Transform MakePart(PartDef def, string name, Transform parent)
    {
        if (def == null) return null;
        var pivot = new GameObject(name).transform;
        pivot.SetParent(parent, false);
        var src = GameAssets.Instance.Model(def.Model);
        if (src == null) return pivot;
        var mesh = Instantiate(src, pivot).transform;
        mesh.name = def.Model;
        // Tripo meshes face -X; rotate them to face +Z, then apply the part's own rotation.
        // Mirroring flips native Z, which is the character's left-right axis after the turn.
        var rot = Quaternion.Euler(def.Euler) * Quaternion.Euler(0, 90, 0);
        mesh.localRotation = rot;
        mesh.localScale = new Vector3(def.Scale, def.Scale, def.Mirror ? -def.Scale : def.Scale);
        var offset = def.Pivot * def.Scale;
        if (def.Mirror) offset.x = -offset.x;
        mesh.localPosition = -(Quaternion.Euler(def.Euler) * offset);
        foreach (var r in mesh.GetComponentsInChildren<Renderer>())
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        return pivot;
    }

    // Playback

    Pose _current, _from;
    PoseKey[] _keys;
    int _keyIndex;
    float _keyTime;
    float _phase;

    public void Play(params PoseKey[] keys)
    {
        _from = _current;
        _keys = keys;
        _keyIndex = 0;
        _keyTime = 0;
    }

    public void Stop() => _keys = null;

    public void Snap(in Pose pose)
    {
        _keys = null;
        _current = pose;
        Apply();
    }

    public void Tick(float dt)
    {
        _phase += dt;
        if (_keys != null)
        {
            _keyTime += dt;
            while (_keys != null)
            {
                var key = _keys[_keyIndex];
                if (_keyTime < key.Duration)
                {
                    _current = Pose.Lerp(_from, key.Pose, Easing.Apply(key.Ease, _keyTime / key.Duration));
                    break;
                }
                _keyTime -= key.Duration;
                _from = key.Pose;
                _current = key.Pose;
                if (++_keyIndex >= _keys.Length) _keys = null;
            }
        }
        else
        {
            _current = Pose.Lerp(_current, Base, Easing.Damp(BaseSmoothing, dt));
        }
        Apply();
        UpdateFlash(dt);
    }

    void Apply()
    {
        var t = _phase * Def.BobFrequency * Mathf.PI * 2;
        var bob = Mathf.Sin(t) * Def.BobAmplitude * Bob;
        var bobR = Mathf.Sin(t - 0.9f) * Def.BobAmplitude * 0.8f * Bob;
        var bobL = Mathf.Sin(t - 1.7f) * Def.BobAmplitude * 0.8f * Bob;
        var sway = Mathf.Sin(t * 0.5f) * 0.02f * Bob;
        if (Body != null)
        {
            Body.localPosition = _current.BodyPos + new Vector3(0, bob, 0);
            Body.localRotation = _current.BodyRot * Quaternion.Euler(0, 0, sway * 40);
        }
        if (Right != null)
        {
            Right.localPosition = _current.RPos + new Vector3(sway, bobR, 0);
            Right.localRotation = _current.RRot;
        }
        if (Left != null)
        {
            Left.localPosition = _current.LPos + new Vector3(-sway, bobL, 0);
            Left.localRotation = _current.LRot;
        }
    }

    // Emissive flash (hit feedback, telegraph glow)

    static readonly int EmissiveID = Shader.PropertyToID("emissiveFactor");
    Material[] _materials;
    Color _flash, _glow, _applied = Color.clear;
    float _flashTime;

    public void Flash(Color color, float duration = 0.12f)
    {
        _flash = color;
        _flashTime = duration;
    }

    public void SetGlow(Color color) => _glow = color;

    void UpdateFlash(float dt)
    {
        _flashTime = Mathf.Max(0, _flashTime - dt);
        var c = _glow + _flash * Mathf.Clamp01(_flashTime / 0.12f);
        if (c == _applied) return;
        _applied = c;
        foreach (var m in _materials) if (m != null) m.SetColor(EmissiveID, c);
    }

    // Death: the parts come apart and tumble to the floor.

    bool _shattered;

    void OnDestroy()
    {
        if (_shattered || _materials == null) return;
        foreach (var m in _materials) if (m != null) Destroy(m);
    }

    public void Shatter(Vector3 push, float fadeDelay = 1.4f)
    {
        _shattered = true;
        foreach (var m in _materials) if (m != null) m.SetColor(EmissiveID, Color.black);
        foreach (var part in new[] { Body, Right, Left })
        {
            if (part == null) continue;
            part.SetParent(null, true);
            var d = part.gameObject.AddComponent<Debris>();
            var v = push * Random.Range(0.6f, 1.2f) + Random.insideUnitSphere * 1.5f + Vector3.up * Random.Range(1.5f, 3.5f);
            d.Launch(v, Random.insideUnitSphere * 540, fadeDelay);
        }
        Destroy(gameObject);
    }
}

// Simple ballistic tumble with floor bounce, then shrink away.
public sealed class Debris : MonoBehaviour
{
    Vector3 _velocity, _spin;
    float _time, _fadeDelay;
    Vector3 _scale;

    public void Launch(Vector3 velocity, Vector3 spin, float fadeDelay)
      => (_velocity, _spin, _fadeDelay, _scale) = (velocity, spin, fadeDelay, transform.localScale);

    void OnDestroy()
    {
        foreach (var r in GetComponentsInChildren<MeshRenderer>())
            foreach (var m in r.sharedMaterials) if (m != null) Destroy(m);
    }

    void Update()
    {
        var dt = Time.deltaTime;
        _time += dt;
        _velocity += Physics.gravity * dt;
        var p = transform.position + _velocity * dt;
        if (p.y < 0.2f)
        {
            p.y = 0.2f;
            _velocity = new Vector3(_velocity.x * 0.5f, Mathf.Abs(_velocity.y) * 0.3f, _velocity.z * 0.5f);
            _spin *= 0.5f;
        }
        transform.position = p;
        transform.rotation = Quaternion.Euler(_spin * dt) * transform.rotation;
        var f = Mathf.Clamp01((_time - _fadeDelay) / 0.5f);
        transform.localScale = _scale * (1 - f);
        if (f >= 1) Destroy(gameObject);
    }
}

} // namespace Cryptbound
