# Street right-click input

`RightPointerGesture` classifies the physical button once per frame. Every
`DemoCamera` samples it before input gates, including scenes without a crew overlay.
`CrewOverlay` uses the same result to admit an order.

- A stationary press becomes a click on release regardless of duration. The old
  0.45-second ceiling silently dropped deliberate slower clicks. Pointer travel
  now separates clicks from orbit: 8 reference pixels, scaled above 1080p and kept
  at 8 physical pixels in smaller windows. An excursion beyond this threshold
  remains a drag even if the pointer returns to its starting point.
- Press then release within one frame is a click. When both edges are reported
  but the button ends down, the new hold remains active. The earlier release is
  coalesced: polling cannot recover its position separately from the new press,
  so it cannot safely issue an order there. The new hold's own release can click.
- A press beginning on UI belongs to UI. Crossing ordinary HUD during an orbit
  that began on the street keeps orbit active. Crossing UI cancels plain-click admission;
  a paper modal or the turf map takes both click and orbit until release.
- Cover aiming retains its directional preview and separate release order.
  Its active sweep continues across ordinary HUD; returning to the street before
  release completes that aim. Releasing over HUD cancels it. This differs from
  plain-click admission because the press has already acquired a directional aim.
  Missing device, lost button release, focus loss, cancellation or a replacement
  press retires the old preview and its camera ownership.

Offline checks: `python3 Tools/PointerSim/run.py`. The harness compiles the actual
gesture, camera pointer block, order-admission prefix and cover cancellation/
cleanup prefixes against scripted device/UI boundaries. It does not validate
Unity device delivery, shop raycasts, rendered UI or physical job completion.
