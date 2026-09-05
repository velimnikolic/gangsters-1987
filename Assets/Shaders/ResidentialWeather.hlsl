#ifndef RESIDENTIAL_WEATHER_INCLUDED
#define RESIDENTIAL_WEATHER_INCLUDED
float WearHash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
float WearNoise(float2 p)
{
    float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(WearHash(i), WearHash(i + float2(1,0)), f.x),
                lerp(WearHash(i + float2(0,1)), WearHash(i + 1), f.x), f.y);
}
half3 ResidentialWeather(half3 albedo, half3 normal, float3 p)
{
    float wall = 1.0 - smoothstep(0.25, 0.7, abs(normal.y));
    float along = abs(normal.x) > abs(normal.z) ? p.z : p.x;
    float patch = WearNoise(float2(along * 1.7, p.y * 1.1));
    // Narrow, faint runoff and low damp. Preserve the brick/paint texture rather
    // than covering every module with a high-contrast, cloudy noise layer.
    float streak = smoothstep(0.72, 0.88, WearNoise(float2(along * 3.3, p.y * 0.16)));
    float damp = exp(-max(p.y, 0.0) * 0.9) * (0.3 + patch * 0.4);
    // Most concrete stays clean enough to read. Roofs receive only a trace of wear.
    float ground = smoothstep(0.64, 0.86, WearNoise(p.xz * 0.38)) * 0.16;
    ground *= lerp(1.0, 0.25, smoothstep(1.0, 3.0, p.y));
    float grime = lerp(ground, damp * 0.24 + streak * 0.13, wall);
    float grey = dot(albedo, half3(0.299, 0.587, 0.114));
    half3 faded = lerp(albedo, grey.xxx * half3(1.03, 1.01, 0.96), 0.18);
    faded *= 1.0 - grime;
    float salt = smoothstep(0.76, 0.9, patch) * wall * 0.055;
    return lerp(faded, half3(0.42, 0.39, 0.32), salt);
}
#endif
