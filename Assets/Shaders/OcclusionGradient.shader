// Visual-only building occlusion experiment for URP.
//
// This is true smooth transparency so the demo can answer the visual question exactly.
// It intentionally keeps ZWrite off: opaque people already drawn behind the facade remain
// visible through it. The original building materials are restored whenever the effect is
// zero, and the ordinary collider is never touched. A full opaque ShadowCaster pass remains
// so a faded building does not also make the street lighting pop.
Shader "LivingCity/Occlusion Gradient"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
        _Cutoff("Source alpha cutoff", Range(0, 1)) = 0.01
        _FadeAmount("Occlusion amount", Range(0, 1)) = 0
        _GradientMode("Vertical gradient", Float) = 1
        _OpaqueFloor("Opaque floor", Range(0, 0.45)) = 0.08
        _BoundsMinY("Bounds min Y", Float) = 0
        _BoundsInvHeight("Bounds inverse height", Float) = 1

        // Read by the borrowed URP/Lit ShadowCaster pass. Keeping this opaque is
        // deliberate: the visual shell fades, its established ground shadow does not.
        [HideInInspector] _Surface("__surface", Float) = 0
        [HideInInspector] _Cull("__cull", Float) = 2
        [HideInInspector] _AlphaClip("__clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
        }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex GradientVertex
            #pragma fragment GradientFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Cutoff;
                half _FadeAmount;
                half _GradientMode;
                half _OpaqueFloor;
                float _BoundsMinY;
                float _BoundsInvHeight;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings GradientVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 GradientFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                clip(albedo.a - _Cutoff);

                half height01 = saturate((input.positionWS.y - _BoundsMinY) * _BoundsInvHeight);
                half verticalAlpha = saturate((1.0h - height01) / max(0.001h, 1.0h - _OpaqueFloor));
                half uniformAlpha = 1.0h - _FadeAmount;
                half gradedAlpha = lerp(1.0h, verticalAlpha, _FadeAmount);
                half profileAlpha = lerp(uniformAlpha, gradedAlpha, step(0.5h, _GradientMode));
                half alpha = albedo.a * profileAlpha;
                clip(alpha - 0.001h);

                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half diffuse = saturate(dot(normalWS, mainLight.direction));
                half3 ambient = max(SampleSH(normalWS), half3(0.06h, 0.06h, 0.06h));
                half3 lighting = ambient + mainLight.color *
                    (diffuse * mainLight.distanceAttenuation * mainLight.shadowAttenuation);
                half3 colour = MixFog(albedo.rgb * lighting, input.fogFactor);
                return half4(colour, alpha);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }

    Fallback Off
}
