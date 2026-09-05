// Street furniture keeps its palette and uses a material-specific weathering finish.
// Both lighting paths share the finish, as they do for the pavement.
Shader "LivingCity/Street Prop Surface"
{
    Properties
    {
        [MainTexture] _BaseMap("Existing palette", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)
        _Smoothness("Smoothness", Range(0,1)) = 0.18
        _Metallic("Metallic", Range(0,1)) = 0
        [KeywordEnum(Paint, Wood, Stone, Plastic)] _Prop("Surface", Float) = 0
        [HideInInspector] _Cutoff("Cutoff", Float) = 0.5
        [HideInInspector] _BumpScale("Bump scale", Float) = 1
        [HideInInspector] _OcclusionStrength("Occlusion", Float) = 1
        [HideInInspector] _EmissionColor("Emission", Color) = (0,0,0,0)
        [HideInInspector] _Cull("Cull", Float) = 2
        [HideInInspector] _ZWrite("ZWrite", Float) = 1
        [HideInInspector] _Surface("Surface", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "UniversalMaterialType"="Lit" }
        Pass
        {
            Name "StreetPropLit"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex LitPassVertex
            #pragma fragment StreetPropFragment
            #pragma shader_feature_local_fragment _PROP_PAINT _PROP_WOOD _PROP_STONE _PROP_PLASTIC
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #define REQUIRES_WORLD_SPACE_POS_INTERPOLATOR
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"

            #include "StreetPropSurface.hlsl"

            void StreetPropFragment(Varyings input, out half4 outColor : SV_Target0)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                SurfaceData surface;
                InitializeStandardLitSurfaceData(input.uv, surface);
                InputData data;
                InitializeInputData(input, surface.normalTS, data);

                ApplyStreetProp(input.positionWS, data.normalWS, surface);
                SETUP_DEBUG_TEXTURE_DATA(data, UNDO_TRANSFORM_TEX(input.uv, _BaseMap));
                #if defined(_DBUFFER)
                    ApplyDecalToSurfaceData(input.positionCS, surface, data);
                #endif
                InitializeBakedGIData(input, data);
                half4 color = UniversalFragmentPBR(data, surface);
                outColor = half4(MixFog(color.rgb, data.fogCoord), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "GBuffer"
            Tags { "LightMode"="UniversalGBuffer" }
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma target 4.5
            #pragma exclude_renderers gles3 glcore
            #pragma vertex LitGBufferPassVertex
            #pragma fragment StreetPropGBuffer
            #pragma shader_feature_local_fragment _PROP_PAINT _PROP_WOOD _PROP_STONE _PROP_PLASTIC
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _RENDER_PASS_ENABLED
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #define REQUIRES_WORLD_SPACE_POS_INTERPOLATOR
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitGBufferPass.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GBufferOutputFormat.hlsl"
            #include "StreetPropSurface.hlsl"

            GBufferFragOutput StreetPropGBuffer(Varyings input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                SurfaceData surface;
                InitializeStandardLitSurfaceData(input.uv, surface);
                InputData data;
                InitializeInputData(input, surface.normalTS, data);
                ApplyStreetProp(input.positionWS, data.normalWS, surface);
                SETUP_DEBUG_TEXTURE_DATA(data, UNDO_TRANSFORM_TEX(input.uv, _BaseMap));
                #if defined(_DBUFFER)
                    ApplyDecalToSurfaceData(input.positionCS, surface, data);
                #endif
                InitializeBakedGIData(input, data);
                BRDFData brdf;
                InitializeBRDFData(surface.albedo, surface.metallic, surface.specular,
                                   surface.smoothness, surface.alpha, brdf);
                Light mainLight = GetMainLight(data.shadowCoord, data.positionWS, data.shadowMask);
                MixRealtimeAndBakedGI(mainLight, data.normalWS, data.bakedGI, data.shadowMask);
                half3 gi = GlobalIllumination(brdf, (BRDFData)0, 0, data.bakedGI,
                    surface.occlusion, data.positionWS, data.normalWS,
                    data.viewDirectionWS, data.normalizedScreenSpaceUV);
                return PackGBuffersBRDFData(brdf, data, surface.smoothness,
                                           surface.emission + gi, surface.occlusion);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
        UsePass "Universal Render Pipeline/Lit/Meta"
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
