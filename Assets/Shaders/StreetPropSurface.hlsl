#ifndef LIVING_CITY_STREET_PROP_INCLUDED
#define LIVING_CITY_STREET_PROP_INCLUDED
#include "CitySurfaceNoise.hlsl"

void ApplyStreetProp(float3 positionWS, half3 normalWS, inout SurfaceData surface)
{
    // Grain follows the object when it turns; instance tone is fixed by placement.
    float3 p = TransformWorldToObject(positionWS);
    float2 origin = GetObjectToWorldMatrix()._m03_m23;
    float tone = lerp(0.94, 1.035, Hash(floor(origin * 8.0)));
    // Horizontal surfaces need two varying coordinates; a vertical projection
    // collapses to stripes on rubber mats, concrete decks and bench tops.
    float2 plane = lerp(float2(p.x + p.z * 0.73, p.y), p.xz,
                        smoothstep(0.5, 0.85, abs(normalWS.y)));
    float mottling = Noise(plane * 7.0) - 0.5;
    float footprint = max(length(fwidth(positionWS)), 0.0001);
    float fineFade = 1.0 - smoothstep(0.005, 0.03, footprint);
    float fine = (Noise(plane * 95.0) - 0.5) * fineFade;
    float up = saturate(normalWS.y);
    float baseDirt = (1.0 - smoothstep(0.03, 0.40, abs(p.y))) *
                     (0.4 + Noise(plane * 11.0) * 0.6);
    half luma = dot(surface.albedo, half3(0.2126, 0.7152, 0.0722));
    half3 original = surface.albedo;

#if defined(_PROP_WOOD)
    // Brown slats receive grain; bolts and the dark cast-iron legs keep metal.
    float wood = smoothstep(0.01, 0.06, original.r - original.g) *
                 smoothstep(0.008, 0.045, original.g - original.b);
    float grain = Noise(float2(p.x * 2.1, (p.y + p.z) * 115.0 +
                              Noise(plane * 3.0) * 2.0)) - 0.5;
    float pores = smoothstep(0.70, 0.89, Noise(float2(p.x * 5.0, (p.y + p.z) * 170.0))) * fineFade;
    float worn = up * (0.04 + Noise(plane * 2.0) * 0.10);
    half3 timber = original * (tone + mottling * 0.13 + grain * 0.32 * fineFade - pores * 0.16);
    timber = lerp(timber, half3(luma, luma * 0.98, luma * 0.92), worn);
    surface.albedo = lerp(original * (tone + mottling * 0.10), timber, wood);
    surface.smoothness = lerp(0.27, 0.15, wood);
    surface.metallic = (1.0 - wood) * 0.16;
#elif defined(_PROP_STONE)
    float pores = smoothstep(0.66, 0.86, Noise(plane * 140.0)) * fineFade;
    surface.albedo *= tone + mottling * 0.19 + fine * 0.13 - pores * 0.13;
    surface.smoothness = 0.12;
    surface.metallic = 0;
#elif defined(_PROP_PLASTIC)
    float scuffs = smoothstep(0.70, 0.89, Noise(plane * float2(3.0, 75.0))) * fineFade;
    surface.albedo *= tone + mottling * 0.12 + fine * 0.05;
    surface.albedo = lerp(surface.albedo, original * 1.17 + 0.008, scuffs * 0.24);
    surface.smoothness = 0.22 - scuffs * 0.09;
    surface.metallic = 0;
#else
    float rain = smoothstep(0.61, 0.86, Noise(plane * float2(24.0, 2.0)));
    float bevel = 4.0 * abs(normalWS.y) * (1.0 - abs(normalWS.y));
    float chip = smoothstep(0.84, 0.96, Noise(plane * 62.0)) * fineFade *
                 (0.045 + bevel * 0.25 + baseDirt * 0.16);
    // Light lettering and labels stay legible; exposed metal is only a small part
    // of the paint finish, rather than treating the whole object as bare steel.
    chip *= 1.0 - smoothstep(0.55, 0.85, luma);
    surface.albedo *= tone + mottling * 0.12 + fine * 0.06 - rain * 0.065;
    surface.albedo = lerp(surface.albedo, lerp(original * 0.65, half3(0.17,0.18,0.19), 0.4), chip);
    float rust = smoothstep(0.68, 0.88, Noise(plane * 20.0)) * baseDirt * 0.22;
    surface.albedo = lerp(surface.albedo, half3(0.13,0.055,0.025), rust);
    surface.smoothness = 0.26 + chip * 0.16 - rain * 0.06;
    surface.metallic = chip * 0.6;
#endif
    surface.albedo *= 1.0 - baseDirt * 0.18;
}
#endif
