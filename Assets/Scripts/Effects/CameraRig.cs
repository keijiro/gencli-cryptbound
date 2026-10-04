using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Cryptbound {

// Third-person follow camera with arena framing, scripted cinematic shots and trauma shake.
public sealed class CameraRig : MonoBehaviour
{
    public static CameraRig Instance { get; private set; }

    public enum Mode { Follow, Arena, Cinematic, Orbit }

    public Mode CurrentMode { get; set; } = Mode.Follow;
    public Transform Target { get; set; }
    public Transform Focus { get; set; }
    public Camera Camera { get; private set; }
    public float MinZ { get; set; } = -1000;

    Vector3 _pos, _look;
    float _fov = 52;
    Vector3 _cinePos, _cineLook;
    float _cineFov = 40, _cineSpeed = 6;
    float _trauma;
    float _orbitAngle;
    Vector3 _orbitCenter;

    void Awake()
    {
        Instance = this;
        Camera = GetComponent<Camera>();
        if (GetComponent<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
        Camera.allowHDR = true;
        Camera.nearClipPlane = 0.1f;
        Camera.farClipPlane = 90;
        Camera.clearFlags = CameraClearFlags.SolidColor;
        Camera.backgroundColor = Color.black;
        var data = Camera.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        data.antialiasingQuality = AntialiasingQuality.High;
    }

    public void Shake(float amount) => _trauma = Mathf.Min(1, _trauma + amount);

    public void SetCinematic(Vector3 pos, Vector3 look, float fov, float speed = 6)
    {
        CurrentMode = Mode.Cinematic;
        (_cinePos, _cineLook, _cineFov, _cineSpeed) = (pos, look, fov, speed);
    }

    public void SetOrbit(Vector3 center, float angle)
    {
        CurrentMode = Mode.Orbit;
        _orbitCenter = center;
        _orbitAngle = angle;
    }

    public void Snap()
    {
        Desired(out _pos, out _look, out _fov, 0);
        Apply(0);
    }

    void Desired(out Vector3 pos, out Vector3 look, out float fov, float dt)
    {
        var p = Target != null ? Target.position : Vector3.zero;
        switch (CurrentMode)
        {
            case Mode.Arena:
            {
                var f = Focus != null ? Focus.position : p + Vector3.forward * 6;
                var mid = Vector3.Lerp(p, f, 0.3f);
                var z = p.z - 7.0f;
                var clamp = Mathf.Max(0, MinZ - z);
                pos = new Vector3(p.x * 0.45f + 0.6f, 5.0f + clamp * 0.8f, z + clamp);
                look = new Vector3(mid.x * 0.7f, 1.2f, mid.z + 0.8f);
                fov = 56;
                break;
            }
            case Mode.Cinematic:
                pos = _cinePos;
                look = _cineLook;
                fov = _cineFov;
                break;
            case Mode.Orbit:
            {
                _orbitAngle += dt * 0.12f;
                var r = 5.5f;
                pos = _orbitCenter + new Vector3(Mathf.Sin(_orbitAngle) * r * 0.5f, 2.4f, -Mathf.Cos(_orbitAngle * 0.7f) * r - 2);
                look = _orbitCenter + new Vector3(0, 1.6f, 4);
                fov = 50;
                break;
            }
            default:
                pos = new Vector3(p.x * 0.5f + 0.75f, 3.3f, Mathf.Max(p.z - 4.4f, MinZ));
                look = new Vector3(p.x * 0.75f + 0.25f, 1.05f, p.z + 4.5f);
                fov = 52;
                break;
        }
    }

    void LateUpdate()
    {
        var dt = Time.unscaledDeltaTime;
        Desired(out var pos, out var look, out var fov, dt);
        var speed = CurrentMode == Mode.Cinematic ? _cineSpeed : CurrentMode == Mode.Orbit ? 2 : 7;
        var k = Easing.Damp(speed, dt);
        _pos = Vector3.Lerp(_pos, pos, k);
        _look = Vector3.Lerp(_look, look, k);
        _fov = Mathf.Lerp(_fov, fov, k);
        _trauma = Mathf.MoveTowards(_trauma, 0, dt * 1.3f);
        Apply(dt);
    }

    void Apply(float dt)
    {
        var t = Time.unscaledTime * 22;
        var s = _trauma * _trauma;
        var offset = new Vector3(Mathf.PerlinNoise(t, 0.1f) - 0.5f, Mathf.PerlinNoise(0.3f, t) - 0.5f, 0) * s * 0.5f;
        var roll = (Mathf.PerlinNoise(t, t * 0.5f) - 0.5f) * s * 6;
        transform.position = _pos + offset;
        transform.rotation = Quaternion.LookRotation(_look - _pos) * Quaternion.Euler(0, 0, roll);
        Camera.fieldOfView = _fov;
    }
}

} // namespace Cryptbound
