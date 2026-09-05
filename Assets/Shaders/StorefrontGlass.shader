// Stylised shop glass for the live storefront facade.
//
// The room and display props behind the pane already carry the interior read. This layer
// stays deliberately clear face-on, then gains a cool reflection at grazing angles and one
// broad, soft reflected band. It is procedural so every streamed storefront can share one
// material without adding textures or per-instance allocations.
Shader "LivingCity/Storefront Glass"
{
    Properties
    {
        [MainColor] _BaseColor("Glass tint", Color) = (0.055, 0.105, 0.12, 0.15)
        _HorizonColor("Horizon reflection", Color) = (0.24, 0.31, 0.33, 1)
        _SkyColor("Sky reflection", Color) = (0.46, 0.61, 0.65, 1)
        _FresnelPower("Fresnel power", Range(1, 8)) = 3.5
        _FresnelOpacity("Fresnel opacity", Range(0, 1)) = 0.28
        _BandOpacity("Reflection band opacity", Range(0, 0.5)) = 0.09
        _BandWidth("Reflection band width", Range(0.02, 0.3)) = 0.13
        _DirtColor("Edge dirt", Color) = (0.16, 0.15, 0.12, 1)
        _DirtOpacity("Edge dirt opacity", Range(0, 0.3)) = 0.045
        _DirtHeight("Bottom dirt height", Range(0.02, 0.35)) = 0.13
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+20"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Unlit"
        }
        LOD 100

        Pass
        {
            Name "StorefrontGlass"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex GlassVertex
            #pragma fragment GlassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _HorizonColor;
                half4 _SkyColor;
                half4 _DirtColor;
                half _FresnelPower;
                half _FresnelOpacity;
                half _BandOpacity;
                half _BandWidth;
                half _DirtOpacity;
                half _DirtHeight;
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

            Varyings GlassVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 GlassFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 viewDirection = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half3 normal = NormalizeNormalPerPixel(input.normalWS);
                // Open doors expose the back of the same pane. Face the normal towards the
                // camera so the reflection response remains identical on both sides.
                normal *= dot(normal, viewDirection) < 0.0h ? -1.0h : 1.0h;

                half facing = saturate(dot(normal, viewDirection));
                half fresnel = pow(1.0h - facing, _FresnelPower);
                half3 reflected = reflect(-viewDirection, normal);
                half skyAmount = smoothstep(0.18h, 0.82h,
                    saturate(reflected.y * 0.5h + 0.5h));
                half3 reflectionColor = lerp(_HorizonColor.rgb, _SkyColor.rgb, skyAmount);

                // World position offsets the otherwise shared pane UVs. Neighbouring panes
                // therefore form one loose reflection rather than obvious repeated stickers.
                half bandPhase = frac(input.uv.x * 0.62h + input.uv.y * 0.38h +
                    dot(input.positionWS.xz, half2(0.047h, 0.071h)));
                half bandDistance = abs(bandPhase - 0.5h);
                half band = 1.0h - smoothstep(
                    _BandWidth, _BandWidth + 0.08h, bandDistance);

                // Keep grime restrained to the sill and the narrow frame contact line. It is
                // intentionally weaker than the reflected band at the game's normal camera.
                half2 paneUv = saturate(input.uv);
                half bottomDirt = 1.0h - smoothstep(0.0h, _DirtHeight, paneUv.y);
                half sideDistance = min(paneUv.x, 1.0h - paneUv.x);
                half sideDirt = 1.0h - smoothstep(0.0h, 0.035h, sideDistance);
                half dirt = saturate(bottomDirt + sideDirt * 0.35h);

                half reflectionMix = saturate(fresnel * 0.78h + band * 0.42h);
                half3 color = lerp(_BaseColor.rgb, reflectionColor, reflectionMix);
                color = lerp(color, _DirtColor.rgb, dirt * 0.22h);
                color = MixFog(color, input.fogFactor);

                half alpha = saturate(_BaseColor.a +
                    fresnel * _FresnelOpacity +
                    band * _BandOpacity +
                    dirt * _DirtOpacity);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
