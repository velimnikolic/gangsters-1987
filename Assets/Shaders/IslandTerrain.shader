Shader "LivingCity/Island Terrain"
{
    Properties
    {
        [MainTexture] _BaseMap("Base", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1,1,1,1)
        _Smoothness("Smoothness", Range(0,1)) = 0.12
        _Metallic("Metallic", Range(0,1)) = 0
        [HideInInspector] _SpecColor("Specular", Color) = (0.2,0.2,0.2,1)
        [HideInInspector] _EmissionColor("Emission", Color) = (0,0,0,0)
        [HideInInspector] _Cutoff("Cutoff", Float) = 0.5
        [HideInInspector] _BumpScale("Bump scale", Float) = 1
        [HideInInspector] _Parallax("Parallax", Float) = 0.005
        [HideInInspector] _OcclusionStrength("Occlusion", Float) = 1
        [HideInInspector] _ClearCoatMask("Clear coat", Float) = 0
        [HideInInspector] _ClearCoatSmoothness("Clear coat smoothness", Float) = 0
        [HideInInspector] _DetailAlbedoMapScale("Detail albedo", Float) = 1
        [HideInInspector] _DetailAlbedoMap("Detail albedo map", 2D) = "linearGrey" {}
        [HideInInspector] _DetailNormalMapScale("Detail normal", Float) = 1
        [HideInInspector] _Cull("Cull", Float) = 2
        [HideInInspector] _ZWrite("ZWrite", Float) = 1
        [HideInInspector] _Surface("Surface", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" "UniversalMaterialType"="Lit" }
        Pass
        {
            Name "IslandTerrain"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            // Match the UnityPerMaterial layout used by the Lit depth/shadow passes below.
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; half3 normal:TEXCOORD1; half4 color:COLOR; half fog:TEXCOORD2; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.world=TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS=TransformWorldToHClip(output.world);
                output.normal=TransformObjectToWorldNormal(input.normalOS);
                output.color=input.color;
                output.fog=ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                half3 normal=normalize(input.normal);
                Light sun=GetMainLight(TransformWorldToShadowCoord(input.world));
                float grain=frac(sin(dot(floor(input.world.xz*1.7),float2(12.9898,78.233)))*43758.5453);
                float strata=sin(input.world.y*1.1+sin(input.world.x*.08))*saturate(1-normal.y)*.05;
                float patch=.5+.25*sin(input.world.x*.009+sin(input.world.z*.006))
                    +.25*sin(input.world.z*.011-input.world.x*.004);
                half3 meadow=lerp(half3(.26,.33,.15),half3(.39,.43,.22),patch);
                half3 albedo=lerp(meadow,input.color.rgb,input.color.a*_BaseColor.a)*(.96+grain*.08+strata);
                half3 lighting=SampleSH(normal)+sun.color*(saturate(dot(normal,sun.direction))*sun.shadowAttenuation);
                return half4(MixFog(albedo*max(lighting,half3(.04,.045,.055)),input.fog),1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
