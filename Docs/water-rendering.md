# River and sea rendering — 2026-09-06

CoreDemo's river, open sea and integrated harbour share
`Assets/Shaders/IslandOcean.shader`. `RegionalIslandView.Build` creates its material;
`Ocean` supplies depth and shelter in mesh colours (R: depth / 18 m, G: depth / 2 m,
B: sheltered water). The integrated harbour uses this surface through ProvidesGround.
Standalone harbour prefab water remains a separate existing rendering path.

The old shader combined very dark colour properties with diffuse lighting and
multiplied its weak sky reflection by that body lighting. This is a source-level
explanation for the reported black water; no before/after render was captured.

The shader now uses blue-green body colours with a gradual depth transition,
slightly greener sheltered water, and independent Fresnel reflection. URP reflection
probes contribute detail, while current sky lighting supplies a fallback when the
runtime sky has no reflection capture. Cached reflection intensity is bounded by
current sky lighting to avoid a daytime reflection persisting through night.
Directional GGX highlights, six world-space wave bands, screen-footprint filtering
and broken, restrained shoreline foam supply surface detail. Sheltered channels
have smaller ripples and less foam. Depth and normal passes support the existing
deferred renderer and SSAO.

The surface still writes opaque depth at the existing waterline. It adds no camera,
texture, scene-colour copy or runtime allocation. Ripples change shading normals;
the coarse 60 m mesh is not displaced. Reflections use probes and a sky approximation,
not a new planar or screen-space reflection. Shore depth still comes from the
existing mesh data; fine contact foam and underwater refraction are not implemented.
Shader property names and the asset GUID are preserved. New defaults take effect
when RegionalIslandView constructs its material; an already-running material may
retain the old depth colours. No scene or generated catalog was rebuilt.

Verification:

- Offline runtime/editor C# compile passed on source fingerprint
  `d835c5f51e1adfce74abf139a74f748dd8a22328d8e5deb02560460508cad451`:
  898 runtime and 136 editor sources, with 99/32 warnings. This task changes no C#.
  The project fingerprint excludes shader files, so it does not identify this shader.
- Shader SHA256:
  `a9de6704d85e935dcde4b71b879889d28a03a57d8a4da7e7f5c644d0771d2382`.
  All 14 offline D3D11 HLSL stage/variant checks passed using Windows
  `d3dcompiler_47.dll` and the installed URP 17.5.0 includes: vertex/fragment,
  cascade/screen shadows, soft-shadow quality levels, reflection probe blending and
  box projection, fog, gamma, depth and both normal encodings.
  The temporary harness removes Unity pragmas, enables FXC half compatibility and
  adapts the unused package PopMarker macro's empty argument list in memory.
  No package files are changed. Harness, logs and bytecode are in
  `%TEMP%/gangsters-water-20260906/`.
- Deleted-asset audit passed: zero deleted GUIDs. No asset paths or loading strings
  changed. Whitespace check passed. Size check reports existing unrelated C# budget
  overruns; this shader-only change adds no C# lines or size baseline exceptions.

Unverified: Unity import/ShaderLab processing, actual GPU rendering, Player builds,
frame time and manual visual acceptance. No Unity Editor commands were sent.
Visual review should cover river and open sea at close and city-camera distances,
noon/sunset/night, bridge shade, quay edges and boat waterlines. The offline compiler
results do not establish that the reported visual issue is resolved in Play.

Adversarial review: invoked Claude through
`Tools/review/adversarial-claude/review.sh --input` with the complete frozen shader
diff, integration context and validation evidence. Input SHA256:
`88d485a908b598babb03df2861dea75e510ef94f93654ed6bb0302ac9b5b5fa4`.
The review produced no output for more than 12 minutes; only this task's review
process was then stopped. No findings or approval were returned. Adversarial review
remains incomplete; it is not counted as a successful check. The frozen input and
timeout record remain alongside the HLSL logs for a later retry.
