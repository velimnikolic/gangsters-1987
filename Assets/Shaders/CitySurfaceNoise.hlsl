// Deterministic noise shared by pavement and street furniture.
#ifndef LIVING_CITY_SURFACE_NOISE_INCLUDED
#define LIVING_CITY_SURFACE_NOISE_INCLUDED
float Hash(float2 p)
{
    float3 q = frac(float3(p.xyx) * 0.1031);
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

float Noise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(Hash(i), Hash(i + float2(1,0)), f.x),
                lerp(Hash(i + float2(0,1)), Hash(i + 1), f.x), f.y);
}

#endif
