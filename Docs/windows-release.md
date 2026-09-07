# CoreDemo Windows release

With Unity access authorized for the task, stop Play, finish compilation, then run
**Tools > Build > CoreDemo Windows Release with FPS**. The active target must be
Windows x64. The same entry point is `GangstersTools.CoreDemoReleaseBuilder.Build`.

The output is `Builds/CoreDemo-Release/Gangsters.exe`. Distribute the whole
`CoreDemo-Release` directory, including `Gangsters_Data/StreamingAssets`.
`build-info.json` records the result, scene, release flag and packaged asset count.
During a build, `Temp/CoreDemoRelease/build-result.json` records its current stage;
only a completed result of `Succeeded` is a successful build.

The builder packages path-addressed game content into an LZ4 AssetBundle and writes
its path index from Unity's imported assets. It includes regional content and lazy
loads such as animations, effects and vehicle models. Both files are generated;
change their source assets or `CoreDemoReleaseBuilder`, then rebuild. The bundle
cache lives in `Builds/CoreDemoContent`.

The builder also generates `Assets/Resources/CoreDemoShaders.asset` from literal
`Shader.Find` calls in runtime source. The Player loads these shader references
before scene startup, including shaders used only by procedural materials.

`DemoAssetLoad` uses AssetDatabase in the Editor and `PlayerAssetBundle` in the
Player. Content loads on demand; the path index does not hold object references.
This preserves the shared simulation and composition rules across both hosts.

The release build enables `GANGSTERS_FPS_COUNTER`, showing FPS and average frame
time in the bottom-right corner. Samples use real time and update twice per second.
Development build, debugger and profiler attachment are disabled. Detailed frame
profiling remains available in Editor and development builds.

An ordinary Unity Player build alone does not package this content. Use the menu
above to produce a complete release.
