Shader "Cryptbound/Effect"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        [HDR] _Color("Color", Color) = (1, 1, 1, 1)
        [Enum(Additive, 0, Smoke, 1)] _Mode("Mode", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 1
    }

HLSLINCLUDE

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D(_MainTex);
SAMPLER(sampler_MainTex);

CBUFFER_START(UnityPerMaterial)
float4 _MainTex_ST;
float4 _Color;
float _Mode;
CBUFFER_END

void Vert(float4 position : POSITION,
          float2 texCoord : TEXCOORD0,
          float4 color : COLOR,
          out float4 outPosition : SV_Position,
          out float2 outTexCoord : TEXCOORD0,
          out float4 outColor : COLOR,
          out float outFog : TEXCOORD1)
{
    outPosition = TransformObjectToHClip(position.xyz);
    outTexCoord = TRANSFORM_TEX(texCoord, _MainTex);
    outColor = color * _Color;
    outFog = ComputeFogFactor(outPosition.z);
}

float4 Frag(float4 position : SV_Position,
            float2 texCoord : TEXCOORD0,
            float4 color : COLOR,
            float fog : TEXCOORD1) : SV_Target
{
    float3 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, texCoord).rgb;

    // Additive: black background textures, alpha scales intensity
    float3 add = MixFogColor(tex * color.rgb * color.a, 0, fog);

    // Smoke: luminance drives coverage, tint drives color
    float a = saturate(max(tex.r, max(tex.g, tex.b)) * color.a);
    float3 smoke = MixFog(color.rgb, fog);

    return _Mode < 0.5 ? float4(add, 1) : float4(smoke, a);
}

ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "EffectPass"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            ENDHLSL
        }
    }
}
