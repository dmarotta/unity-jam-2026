// Lit quad for animated (video) backgrounds in the URP 2D Renderer. Same lighting as Sprite-Lit-Default,
// but tinted by _Color only (MeshRenderers carry no sprite color) and fading in with _Alpha.
Shader "Lucky/VideoLit"
{
    Properties
    {
        _MainTex ("Video", 2D) = "black" {}
        _MaskTex ("Mask", 2D) = "white" {}
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Alpha ("Alpha", Range(0,1)) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #pragma vertex LitVertex
            #pragma fragment LitFragment
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightShared.hlsl"

            struct Attributes { COMMON_2D_INPUTS };
            struct Varyings { COMMON_2D_LIT_OUTPUTS };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Lit2DCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Alpha;
            CBUFFER_END

            Varyings LitVertex(Attributes input) { return CommonLitVertex(input); }

            half4 LitFragment(Varyings input) : SV_Target
            {
                half4 c = CommonLitFragment(input, _Color);
                c.a = _Alpha;
                return c;
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "NormalsRendering" }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #pragma vertex NormalsVertex
            #pragma fragment NormalsFragment

            struct Attributes { COMMON_2D_NORMALS_INPUTS };
            struct Varyings { COMMON_2D_NORMALS_OUTPUTS };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Normals2DCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Alpha;
            CBUFFER_END

            Varyings NormalsVertex(Attributes input) { return CommonNormalsVertex(input); }
            half4 NormalsFragment(Varyings input) : SV_Target { return CommonNormalsFragment(input, half4(1, 1, 1, _Alpha)); }
            ENDHLSL
        }
    }
}
