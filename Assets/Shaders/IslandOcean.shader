Shader "LivingCity/Island Ocean"
{
    Properties
    {
        _DeepColor("Deep water", Color) = (.19,.35,.40,1)
        _ShallowColor("Shallows", Color) = (.34,.52,.46,1)
        _WaveStrength("Wind ripple strength", Range(0,2)) = 1
        _FlowSpeed("Wave speed", Range(0,2)) = 1
        _Roughness("Surface roughness", Range(.15,.65)) = .28
        _FoamStrength("Shore foam", Range(0,1)) = .24
    }
    SubShader
    {
        // Opaque depth-writing water remains visible below hulls and does not rely
        // on a camera depth texture, a scaled prefab UV or transparent sorting.
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+5" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _DeepColor, _ShallowColor;
            float _WaveStrength, _FlowSpeed, _Roughness, _FoamStrength;
        CBUFFER_END

        struct Attributes { float4 positionOS:POSITION; half4 color:COLOR; };
        // Mesh colour is data: R = depth / 18 m, G = depth / 2 m, B = sheltered water.
        struct Varyings { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; half4 water:COLOR; half fog:TEXCOORD1; };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.world = TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = TransformWorldToHClip(output.world);
            output.water = input.color;
            output.fog = ComputeFogFactor(output.positionCS.z);
            return output;
        }

        float WaterNoise(float2 p)
        {
            float2 cell = floor(p), f = frac(p);
            f = f * f * (3 - 2 * f);
            float4 h = frac(sin(float4(dot(cell, float2(127.1,311.7)),
                dot(cell + float2(1,0), float2(127.1,311.7)),
                dot(cell + float2(0,1), float2(127.1,311.7)),
                dot(cell + 1, float2(127.1,311.7)))) * 43758.5453);
            return lerp(lerp(h.x,h.y,f.x), lerp(h.z,h.w,f.x), f.y);
        }

        float2 WaveSlope(float2 p, float2 direction, float frequency, float slope, float speed, float phase)
        {
            float angle = dot(p, direction) * frequency - _Time.y * _FlowSpeed * speed + phase;
            // Filter waves smaller than a screen pixel; map-height water must not sparkle
            // or turn into a moire pattern. World-space phases also join every ocean tile.
            float filter = 1 - smoothstep(.65, 2.6, fwidth(angle));
            return direction * (cos(angle) * slope * filter);
        }

        half3 WaterNormal(float2 p, half sheltered)
        {
            // The 60 m mesh stays at the hulls' shared waterline. Sub-metre ripples
            // belong in the normal, not vertex displacement on that coarse grid.
            float2 slope = WaveSlope(p, float2(.94,.342), .19,.10,.72,0);
            slope += WaveSlope(p, float2(-.6,.8), .31,.065,.93,1.7);
            slope += WaveSlope(p, float2(.8,.6), .83,.048,1.43,3.1);
            slope += WaveSlope(p, float2(-.28,.96), 1.57,.033,1.96,.8);
            slope += WaveSlope(p, float2(.98,-.199), 3.9,.022,2.91,2.3);
            slope += WaveSlope(p, float2(-.8,-.6), 7.1,.014,3.72,4.6);
            slope *= _WaveStrength * lerp(1,.38,sheltered);
            return normalize(float3(-slope.x, 1, -slope.y));
        }

        half WaterFresnel(half facing)
        {
            // Air/water IOR 1.333: about 2% reflection face-on, approaching a mirror
            // at the horizon. Reflection is radiance, never darkened by the body tint.
            half grazing = 1 - saturate(facing);
            return .0204 + .9796 * Pow4(grazing) * grazing;
        }

        half3 WaterReflection(half3 reflected, float3 world, float2 screenUV, half roughness, half3 ambient, Light sun)
        {
            half3 sky = max(SampleSH(reflected), ambient * .65);
            // Live sky lighting supplies a reflection when the runtime sky has no baked
            // cubemap. This follows day/night and also keeps bridge shadows readable.
            sky = max(sky, sun.color * saturate(sun.direction.y) * half3(.055,.085,.12));
            sky *= lerp(1.55, 1.05, saturate(reflected.y));
            half3 probe = GlossyEnvironmentReflection(reflected, world, roughness, 1, screenUV);
            // A cached daytime probe must not illuminate the water all night. Preserve
            // its detail but bound its energy by the current sky before blending it in.
            half probeLuma = dot(probe, half3(.2126,.7152,.0722));
            half skyLuma = dot(sky, half3(.2126,.7152,.0722));
            probe *= min(1, skyLuma * 2 / max(.0001, probeLuma));
            return lerp(sky, max(probe, sky * .65), .55);
        }

        float SunReflection(half3 normal, half3 view, half3 light, float roughness)
        {
            float3 halfway = SafeNormalize(view + light);
            float nv = max(.001, saturate(dot(normal, view)));
            float nl = saturate(dot(normal, light));
            float nh = saturate(dot(normal, halfway));
            // GGX with a small footprint-based roughness increase suppresses hot,
            // isolated pixels in the distant sun trail. Keep its math at float precision.
            float variance = dot(ddx(normal),ddx(normal)) + dot(ddy(normal),ddy(normal));
            float a2 = max(.0005, Pow4(roughness) + min(.08, variance * .25));
            float d = nh * nh * (a2 - 1) + 1;
            float distribution = a2 / (PI * d * d);
            float visibility = .5 / max(.001,
                nl * sqrt(nv * nv * (1 - a2) + a2) + nv * sqrt(nl * nl * (1 - a2) + a2));
            return distribution * visibility * WaterFresnel(dot(view, halfway)) * nl;
        }
        ENDHLSL

        Pass
        {
            Name "Ocean"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION

            half4 Frag(Varyings input):SV_Target
            {
                float2 p = input.world.xz;
                half sheltered = saturate(input.water.b);
                half3 normal = WaterNormal(p, sheltered);
                half3 view = GetWorldSpaceNormalizeViewDir(input.world);
                Light sun = GetMainLight(TransformWorldToShadowCoord(input.world));
                half3 ambient = SampleSH(half3(0,1,0));
                half3 daylight = sun.color * sun.distanceAttenuation;
                half3 illumination = ambient + daylight * saturate(dot(normal,sun.direction)) * sun.shadowAttenuation;
                half depth = 1 - exp2(-saturate(input.water.r) * 4.5);
                half3 body = lerp(_ShallowColor.rgb, _DeepColor.rgb, depth);
                // Suspended sediment makes the river a little greener than open water.
                body *= lerp(half3(1,1,1), half3(.94,1,.90), sheltered);
                half swell = WaterNoise(p * .035 + float2(-.025,.012) * _Time.y * _FlowSpeed);
                body *= lerp(.94,1.06,swell);
                half fresnel = WaterFresnel(dot(normal,view));
                half roughness = clamp(_Roughness * lerp(1,.85,sheltered), .15,.65);
                half3 reflection = WaterReflection(reflect(-view,normal), input.world,
                    GetNormalizedScreenSpaceUV(input.positionCS), roughness, ambient, sun);
                half3 color = body * illumination * (1 - fresnel) + reflection * fresnel;
                color += SunReflection(normal,view,sun.direction,roughness) * daylight * sun.shadowAttenuation;

                float foamNoise = WaterNoise(p * .42 + float2(.10,-.06) * _Time.y * _FlowSpeed);
                float foamWave = .5 + .5 * sin(dot(p,float2(.22,.13)) - _Time.y * _FlowSpeed * .65 + swell * 5);
                half foam = (1 - saturate(input.water.g)) * smoothstep(.48,.8,foamNoise)
                    * smoothstep(.35,.85,foamWave) * _FoamStrength * lerp(1,.22,sheltered);
                color = lerp(color, half3(.65,.72,.68) * illumination, foam);
                return half4(MixFog(color,input.fog),1);
            }
            ENDHLSL
        }

        // CoreDemo uses deferred rendering with SSAO. Supply the same water surface
        // to depth/normal prepasses so submerged ground is not mistaken for the surface.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            Cull Off
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment DepthFrag
            half DepthFrag(Varyings input):SV_Target { return input.positionCS.z; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment NormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 NormalsFrag(Varyings input):SV_Target
            {
                half3 normal = WaterNormal(input.world.xz, saturate(input.water.b));
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = saturate(PackNormalOctQuadEncode(normal) * .5 + .5);
                    return half4(PackFloat2To888(oct),0);
                #else
                    return half4(normal,0);
                #endif
            }
            ENDHLSL
        }
    }
    Fallback Off
}
