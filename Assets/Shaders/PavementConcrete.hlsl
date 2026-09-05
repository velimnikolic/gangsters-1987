// Shared finish for the forward and deferred lighting paths.
#ifndef LIVING_CITY_PAVEMENT_INCLUDED
#define LIVING_CITY_PAVEMENT_INCLUDED
#include "CitySurfaceNoise.hlsl"

// Sparse, irregular deposits: the centre and aspect ratio change per patch, and
// the boundary is broken up by noise. Each stamp fits inside its cell.
float Deposits(float2 p, float spacing, float density)
{
    float2 grid = p / spacing;
    float2 id = floor(grid);
    float pick = Hash(id + 73.1);
    float2 centre = 0.36 + float2(Hash(id + 11.7), Hash(id + 39.2)) * 0.28;
    float2 delta = frac(grid) - centre;
    float angle = Hash(id + 91.4) * 6.283185;
    float2 q = float2(delta.x * cos(angle) - delta.y * sin(angle),
                      delta.x * sin(angle) + delta.y * cos(angle));
    q *= float2(1.0, lerp(1.1, 2.0, Hash(id + 8.6)));
    float radius = lerp(0.13, 0.27, Hash(id + 24.8));
    float boundary = length(q) + (Noise(p * (5.0 / spacing)) - 0.5) * 0.12;
    return (1.0 - smoothstep(radius * 0.45, radius, boundary)) * step(pick, density);
}

void ApplyPavement(float3 positionWS, half3 normalWS, inout SurfaceData surface)
{
    float2 p = positionWS.xz;
    // Follow each mesh's existing slab joints even when a hand-placed block has
    // a fractional world offset. Noise remains world-space to avoid tile repeats.
    float2 scale = float2(length(GetObjectToWorldMatrix()._m00_m10_m20),
                          length(GetObjectToWorldMatrix()._m02_m12_m22));
    float2 panel = TransformWorldToObject(positionWS).xz * scale / 1.25;
    float2 cell = floor(panel);
    float2 edge = min(frac(panel), 1.0 - frac(panel)) * 1.25;
    float footprint = max(length(fwidth(p)), 0.001);
    float aa = max(footprint * 0.6, 0.003);
    float seams = 1.0 - smoothstep(0.007, 0.007 + aa, min(edge.x, edge.y));
    // Fade fine features before they alias into a dark grid at city zoom.
    seams *= 1.0 - smoothstep(0.07, 0.28, footprint);
    float2 origin = GetObjectToWorldMatrix()._m03_m23;
    float2 panelId = cell + floor(origin * 4.0);
    float panelPick = Hash(panelId);
    float panelTone = lerp(0.85, 1.12, panelPick);
    float mottling = Noise(p * 0.65) - 0.5;
    float stains = smoothstep(0.65, 0.87, Noise(p * 0.21 + 19.7));
    float grain = (Noise(p * 19.0) - 0.5) *
                  (1.0 - smoothstep(0.015, 0.09, footprint));
    float deposits = Deposits(p + 6.3, 3.4, 0.48);
    float specks = Deposits(p + 81.7, 0.55, 0.13) *
                   (1.0 - smoothstep(0.035, 0.13, footprint));
    // A few replaced slabs retain a slightly different concrete mix.
    float repair = step(0.955, Hash(panelId + 121.3));
    float repairTone = lerp(0.82, 1.09, Hash(panelId + 35.9));
    panelTone = lerp(panelTone, repairTone, repair);
    // One restrained hairline on occasional slabs; keep it off repairs.
    float2 slab = frac(panel);
    float crackPath = 0.25 + panelPick * 0.4 + (Noise(slab.yy * 5.0 + panelId) - 0.5) * 0.16;
    float crackDistance = abs(slab.x - crackPath) * 1.25;
    float crack = (1.0 - smoothstep(0.002, 0.002 + aa, crackDistance)) *
                  step(0.94, Hash(panelId + 51.2)) * (1.0 - repair) *
                  smoothstep(0.12, 0.3, slab.y) * (1.0 - smoothstep(0.7, 0.96, slab.y)) *
                  (1.0 - smoothstep(0.035, 0.12, footprint));
    float finish = panelTone + mottling * 0.19 + grain * 0.07 - stains * 0.22;
    finish *= 1.0 - deposits * 0.34 - specks * 0.28 - crack * 0.32;
    finish *= 1.0 - seams * 0.22;
    // Keep vertical kerb faces and the palette's dark metal grates intact.
    float concrete = smoothstep(0.12, 0.26, dot(surface.albedo, float3(0.2126,0.7152,0.0722)));
    float top = smoothstep(0.80, 0.97, normalWS.y) * concrete;
    surface.albedo *= lerp(1.0, finish, top);
    surface.albedo *= lerp(half3(1,1,1), half3(1.02,0.985,0.94), top * deposits * 0.45);
    surface.smoothness = lerp(surface.smoothness, 0.16 + deposits * 0.13, top);
}
#endif
