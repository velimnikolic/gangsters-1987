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
        _FadeAmount("Occlusion amount", Range(0, 2)) = 0
        _GradientMode("Vertical gradient", Float) = 1
        _GradientStartHeight("Gradient start height", Float) = 5
        _BoundsMinY("Bounds min Y", Float) = 0
        _BoundsHeight("Bounds height", Float) = 1
        _BoundsCenter("Bounds center", Vector) = (0, 0, 0, 1)

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
            #pragma target 3.5
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
                float _GradientStartHeight;
                float _BoundsMinY;
                float _BoundsHeight;
                float4 _BoundsCenter;
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
                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);

                // Keep the lower building fully opaque, then grade from the requested
                // world-space height to the roof. This makes the base consistent across
                // buildings with different total heights.
                float fadeHeight = max(0.01, _BoundsHeight - _GradientStartHeight);
                half height01 = saturate(
                    (input.positionWS.y - (_BoundsMinY + _GradientStartHeight)) / fadeHeight);
                half linearVerticalAlpha = 1.0h - height01;
                // Ease the height profile: preserve more body near the 5 m start, but
                // drive the upper storeys and roof toward zero faster than a linear fade.
                half verticalAlpha = smoothstep(0.0h, 1.0h, linearVerticalAlpha);
                half firstHundred = saturate(_FadeAmount);
                half beyondHundred = max(0.0h, _FadeAmount - 1.0h);

                // 0..100% establishes the requested start-to-roof gradient. 100..200%
                // carries that same edge below the pavement until no shell remains.
                half gradedAlpha = _FadeAmount <= 1.0h
                    ? lerp(1.0h, verticalAlpha, firstHundred)
                    : saturate(verticalAlpha - beyondHundred);
                half uniformAlpha = 1.0h - saturate(_FadeAmount * 0.5h);
                half profileAlpha = lerp(uniformAlpha, gradedAlpha, step(0.5h, _GradientMode));

                // Above the ground floor the rear half is fully clear from 100% onward,
                // so it cannot ghost through the front facade; the front half takes the
                // normal height cut.
                float2 cameraPlanar = _WorldSpaceCameraPos.xz - _BoundsCenter.xz;
                float cameraPlanarInvLength = rsqrt(max(dot(cameraPlanar, cameraPlanar), 0.0001));
                half cameraSide = step(0.0, dot(input.positionWS.xz - _BoundsCenter.xz,
                    cameraPlanar * cameraPlanarInvLength));
                half rearKeep = lerp(1.0h, cameraSide, firstHundred);
                profileAlpha *= rearKeep;

                // Above 100%, remove upward-facing roof, balcony and modular floor slabs
                // independently of their renderer bounds. A roof authored as a separate
                // storey otherwise computes its own local gradient and remains visible.
                half upwardSurface = smoothstep(0.35h, 0.75h, normalWS.y);
                half horizontalCut = upwardSurface *
                    saturate((_FadeAmount - 1.0h) * 2.0h);
                profileAlpha *= 1.0h - horizontalCut;

                // Apply this last so neither the height gradient, the rear clear nor the
                // roof/slab cut can touch the ground floor: the whole of it stays exactly
                // as authored. Back faces are culled, so every ground-floor fragment that
                // reaches here is a wall turned towards the camera - the one the player
                // is owed whole - and the ground floor of the far side never draws.
                // Everything above the ground-floor line uses the ordinary profile.
                half groundFloor = 1.0h - step(
                    _BoundsMinY + _GradientStartHeight, input.positionWS.y);
                profileAlpha = lerp(profileAlpha, 1.0h, groundFloor);

                half alpha = albedo.a * profileAlpha;
                clip(alpha - 0.001h);

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
