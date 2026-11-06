// Lit character sprite shader for the URP 2D Renderer: normal-mapped 2D lighting plus the SpriteFX
// effects (idle sway/breathing, hit flash, dissolve, HDR glow). Flash and dissolve edges apply after lighting
// so hits always read bright, even in dark arenas.
Shader "Lucky/SpriteFXLit"
{
    Properties
    {
        _MainTex ("Diffuse", 2D) = "white" {}
        _MaskTex ("Mask", 2D) = "white" {}
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _FlashColor ("Flash Color", Color) = (1,1,1,1)
        _FlashAmount ("Flash Amount", Range(0,1)) = 0
        _Dissolve ("Dissolve", Range(0,1)) = 0
        _DissolveEdge ("Dissolve Edge Color", Color) = (4,2.6,0.8,1)
        _Sway ("Sway", Float) = 0
        _Breath ("Breath", Float) = 0
        _SwaySpeed ("Sway Speed", Float) = 1.6
        _Phase ("Phase", Float) = 0
        _Glow ("Glow", Float) = 1
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
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
            #pragma multi_compile_instancing

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
            };

            struct Varyings
            {
                COMMON_2D_LIT_OUTPUTS
                half4 color : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Lit2DCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _FlashColor;
                half _FlashAmount;
                half _Dissolve;
                half4 _DissolveEdge;
                float _Sway;
                float _Breath;
                float _SwaySpeed;
                float _Phase;
                half _Glow;
            CBUFFER_END

            #include "Assets/Shaders/SpriteFXCommon.hlsl"

            Varyings LitVertex(Attributes input)
            {
                SetUpSpriteInstanceProperties();
                input.positionOS = SwayPosition(UnityFlipSprite(input.positionOS, unity_SpriteProps.xy), input.uv);
                Varyings o = CommonLitVertex(input);
                o.color = input.color * _Color * unity_SpriteColor;
                return o;
            }

            half4 LitFragment(Varyings input) : SV_Target
            {
                half4 main = input.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half edge = DissolveEdge(input.uv);
                half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, input.uv);
                half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv));

                SurfaceData2D surfaceData;
                InputData2D inputData;
                InitializeSurfaceData(main.rgb, main.a, mask, normalTS, surfaceData);
                InitializeInputData(input.uv, input.lightingUV, inputData);
                half4 c = CombinedShapeLightShared(surfaceData, inputData);

                c.rgb = lerp(c.rgb, _FlashColor.rgb, _FlashAmount * _FlashColor.a);
                c.rgb += _DissolveEdge.rgb * edge;
                c.rgb *= _Glow;
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
            #pragma multi_compile_instancing

            struct Attributes
            {
                COMMON_2D_NORMALS_INPUTS
                float4 color : COLOR;
            };

            struct Varyings
            {
                COMMON_2D_NORMALS_OUTPUTS
                half4 color : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Normals2DCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _FlashColor;
                half _FlashAmount;
                half _Dissolve;
                half4 _DissolveEdge;
                float _Sway;
                float _Breath;
                float _SwaySpeed;
                float _Phase;
                half _Glow;
            CBUFFER_END

            #include "Assets/Shaders/SpriteFXCommon.hlsl"

            Varyings NormalsVertex(Attributes input)
            {
                SetUpSpriteInstanceProperties();
                input.positionOS = SwayPosition(UnityFlipSprite(input.positionOS, unity_SpriteProps.xy), input.uv);
                Varyings o = CommonNormalsVertex(input);
                o.color = input.color * _Color * unity_SpriteColor;
                return o;
            }

            half4 NormalsFragment(Varyings input) : SV_Target
            {
                SetUpSpriteInstanceProperties();
                DissolveEdge(input.uv);
                return CommonNormalsFragment(input, input.color);
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment
            #pragma multi_compile_instancing

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
            };

            struct Varyings
            {
                COMMON_2D_OUTPUTS
                half4 color : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _FlashColor;
                half _FlashAmount;
                half _Dissolve;
                half4 _DissolveEdge;
                float _Sway;
                float _Breath;
                float _SwaySpeed;
                float _Phase;
                half _Glow;
            CBUFFER_END

            #include "Assets/Shaders/SpriteFXCommon.hlsl"

            Varyings UnlitVertex(Attributes input)
            {
                SetUpSpriteInstanceProperties();
                input.positionOS = SwayPosition(UnityFlipSprite(input.positionOS, unity_SpriteProps.xy), input.uv);
                Varyings o = CommonUnlitVertex(input);
                o.color = input.color * _Color * unity_SpriteColor;
                return o;
            }

            half4 UnlitFragment(Varyings input) : SV_Target
            {
                half edge = DissolveEdge(input.uv);
                half4 c = CommonUnlitFragment(input, input.color);
                c.rgb = lerp(c.rgb, _FlashColor.rgb, _FlashAmount * _FlashColor.a);
                c.rgb += _DissolveEdge.rgb * edge;
                c.rgb *= _Glow;
                return c;
            }
            ENDHLSL
        }
    }
}
