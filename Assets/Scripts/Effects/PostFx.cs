using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Cryptbound {

// Runtime-built post-processing volume with cinematic and impact controls.
public sealed class PostFx : MonoBehaviour
{
    public static PostFx Instance { get; private set; }

    Bloom _bloom;
    Vignette _vignette;
    ColorAdjustments _color;
    ChromaticAberration _chroma;
    LensDistortion _lens;
    DepthOfField _dof;
    FilmGrain _grain;

    float _cinematic, _cinematicTarget;
    float _chromaPulse, _lensPulse, _exposurePulse, _hurt;
    float _deathFade;

    public float FocusDistance { get; set; } = 4;

    void Awake()
    {
        Instance = this;
        var volume = gameObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 10;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        volume.profile = profile;

        var tone = profile.Add<Tonemapping>(true);
        tone.mode.Override(TonemappingMode.ACES);

        _bloom = profile.Add<Bloom>(true);
        _bloom.threshold.Override(0.95f);
        _bloom.intensity.Override(1.1f);
        _bloom.scatter.Override(0.72f);

        _vignette = profile.Add<Vignette>(true);
        _vignette.intensity.Override(0.36f);
        _vignette.smoothness.Override(0.45f);
        _vignette.color.Override(Color.black);

        _color = profile.Add<ColorAdjustments>(true);
        _color.postExposure.Override(0.6f);
        _color.contrast.Override(14);
        _color.saturation.Override(-4);
        _color.colorFilter.Override(Color.white);

        _chroma = profile.Add<ChromaticAberration>(true);
        _chroma.intensity.Override(0.06f);

        _lens = profile.Add<LensDistortion>(true);
        _lens.intensity.Override(0);

        _dof = profile.Add<DepthOfField>(true);
        _dof.mode.Override(DepthOfFieldMode.Off);
        _dof.focusDistance.Override(4);
        _dof.aperture.Override(2.2f);
        _dof.focalLength.Override(70);

        _grain = profile.Add<FilmGrain>(true);
        _grain.type.Override(FilmGrainLookup.Thin1);
        _grain.intensity.Override(0.18f);
    }

    public void Cinematic(float target) => _cinematicTarget = target;

    public void Pulse(float chroma, float lens, float exposure)
    {
        _chromaPulse = Mathf.Max(_chromaPulse, chroma);
        _lensPulse = Mathf.Min(_lensPulse, lens);
        _exposurePulse = Mathf.Max(_exposurePulse, exposure);
    }

    public void Hurt(float amount) => _hurt = Mathf.Max(_hurt, amount);

    public void Death(float amount) => _deathFade = amount;

    void Update()
    {
        var dt = Time.unscaledDeltaTime;
        _cinematic = Mathf.MoveTowards(_cinematic, _cinematicTarget, dt * 3);
        _chromaPulse = Mathf.MoveTowards(_chromaPulse, 0, dt * 2.2f);
        _lensPulse = Mathf.MoveTowards(_lensPulse, 0, dt * 1.4f);
        _exposurePulse = Mathf.MoveTowards(_exposurePulse, 0, dt * 4);
        _hurt = Mathf.MoveTowards(_hurt, 0, dt * 1.6f);

        var c = _cinematic;
        _vignette.intensity.value = Mathf.Lerp(0.36f, 0.55f, c) + _hurt * 0.25f;
        _vignette.color.value = Color.Lerp(Color.black, new Color(0.6f, 0, 0), _hurt);
        _color.saturation.value = Mathf.Lerp(-4, -45, Mathf.Max(c, _deathFade)) ;
        _color.contrast.value = Mathf.Lerp(14, 28, c);
        _color.postExposure.value = 0.6f + _exposurePulse - _deathFade * 0.8f;
        _color.colorFilter.value = Color.Lerp(Color.white, new Color(1, 0.92f, 0.85f), c);
        _chroma.intensity.value = 0.06f + 0.25f * c + _chromaPulse;
        _lens.intensity.value = _lensPulse;
        _dof.mode.value = c > 0.05f ? DepthOfFieldMode.Bokeh : DepthOfFieldMode.Off;
        _dof.focusDistance.value = FocusDistance;
        _dof.aperture.value = Mathf.Lerp(16, 2.4f, c);
    }
}

} // namespace Cryptbound
