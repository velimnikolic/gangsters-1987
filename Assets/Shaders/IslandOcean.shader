Shader "LivingCity/Island Ocean"
{
    Properties
    {
        _DeepColor("Deep water", Color) = (.055,.105,.13,1)
        _ShallowColor("Shallows", Color) = (.17,.225,.21,1)
    }
    SubShader
    {
        // Opaque depth-writing water remains visible below hulls and does not rely
        // on a camera depth texture, a scaled prefab UV or transparent sorting.
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+5" }
        Pass
        {
            Name "Ocean"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor, _ShallowColor;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; half4 water:COLOR; half fog:TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.world=TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS=TransformWorldToHClip(output.world);
                output.water=input.color;
                output.fog=ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float2 p=input.world.xz;
                float calm=lerp(1,.38,input.water.b);
                float a=sin(p.x*.23+p.y*.11+_Time.y*.8);
                float b=cos(p.y*.19-p.x*.07+_Time.y*.57);
                half3 normal=normalize(half3(a*.11*calm,1,b*.09*calm));
                half3 view=GetWorldSpaceNormalizeViewDir(input.world);
                Light sun=GetMainLight(TransformWorldToShadowCoord(input.world));
                half fresnel=pow(1-saturate(dot(normal,view)),4);
                half3 color=lerp(_ShallowColor.rgb,_DeepColor.rgb,smoothstep(0,1,input.water.r));
                color=lerp(color,half3(.31,.36,.39),fresnel*.45);
                float wave=sin(p.x*.15+p.y*.10-_Time.y*1.1)*.5+.5;
                half foam=(1-input.water.g)*smoothstep(.7,.97,wave)*lerp(.18,.035,input.water.b);
                color=lerp(color,half3(.74,.81,.76),foam);
                half3 light=SampleSH(normal)+sun.color*(.25+.75*saturate(dot(normal,sun.direction)))*sun.shadowAttenuation;
                half sparkle=pow(saturate(dot(normal,normalize(view+sun.direction))),150)*.65;
                color=color*max(light,half3(.035,.045,.06))+sparkle*sun.color*sun.shadowAttenuation;
                return half4(MixFog(color,input.fog),1);
            }
            ENDHLSL
        }
    }
}
