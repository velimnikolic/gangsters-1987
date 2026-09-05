# Owner beating scene

Open `Assets/Scenes/OwnerBeatingDemo.unity` and press Play. The enforcer walks
inside, takes the owner outside, delivers four nonfatal punches, and leaves while
the bloodied owner recovers onto his hands and knees and crawls back through the door.

- R: replay; Space: pause/resume; 1: normal speed; 2: slow motion.
- WASD/arrows: move camera; Q/E or right-drag: orbit; wheel: zoom.
- Rebuild assets with Tools > City > Build Owner Beating Scene while outside Play.

`OwnerBeatingSet.prefab` contains the set, actors, doorway anchors, serialized
animation/audio references and replay driver. The scene adds lighting and the
shared `RoadDemo.DemoCamera`.

`RoadDemo.OwnerBeatingSequence` is the reusable presentation. Call `Begin` with
two existing CrewWalkers, the shop transform and inside/outside/street positions.
The caller ticks each walker once per frame and reserves both actors for the
sequence. `FirstImpact` and `Finished` are presentation callbacks; `Cancel`
releases held poses and doorway movement. Authoritative business/police effects
remain with the existing order system. The scene does not yet replace the live
BEAT order's presentation.

Punches and the fall use the project's existing humanoid clips. Extraction uses
an arm reach over shared walking. Recovery and hands-and-knees crawling use
`OwnerCrawlPose`, a procedural pose over the same walker's doorway movement.
The owner stays alive; `CrewGore.BeatingHit` adds progressive facial/clothing
blood without modifying simulation health. Review contact and crawl proportions
when assigning a different character rig or doorway.
