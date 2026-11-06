// Alpha-blended vertex-color shader with an optional detail texture, used for the procedural wheel face.
Shader "Lucky/VertexColor"
{
    Properties
    {
        _MainTex ("Detail", 2D) = "white" {}
        _DetailStrength ("Detail Strength", Range(0,1)) = 0.35
        _Intensity ("Intensity", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend [_SrcBlend] [_DstBlend]
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half _DetailStrength;
                half _Intensity;
            CBUFFER_END

            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS);
                o.uv = i.uv;
                o.color = i.color;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half3 detail = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb;
                half4 c = i.color;
                c.rgb *= lerp(1.0, detail * 2.0, _DetailStrength) * _Intensity;
                return c;
            }
            ENDHLSL
        }
    }
}
