# Live residential storefronts (GAN-294)

Every doored shop bay harvested from a residential unit owns one `Storefront` view. The
business directory remains authoritative for identity, trading state and damage; the component
owns only the independently live facade: replacement panes, swinging Synty leaves, broken glass,
fire, boards and the night/shutdown shutter.

Named kit venues (`pizzapub`, `radnja`, diners) are intentionally outside this layer and keep
the legacy `ShopDamage`/door path.

## Source measurements and baked assets

`StorefrontDoorCatalog` is the single measurement table used by the harvest, mesh baker and live
view. Positions are in the unturned source-module frame.

| module | door centre X/Z | opening W/H | leaves | local yaw |
|---|---:|---:|---:|---:|
| `SM_Bld_Shop_01` | -2.50 / -0.02 | 1.70 / 2.22 | 2 | 0° |
| `SM_Bld_Shop_02` | -4.03 / -0.21 | 1.25 / 2.40 | 2 | 0° |
| `SM_Bld_Shop_03` | -5.00 / -0.05 | 1.90 / 2.40 | 2 | 0° |
| `SM_Bld_Shop_04` | -2.50 / 0.61 | 1.40 / 2.62 | 1 | 0° |
| `SM_Bld_Shop_05` | — | — | 0 | 0° |
| `SM_Bld_Shop_06` | -4.34 / 0.12 | 1.10 / 2.50 | 1 | 0° |
| `SM_Bld_Shop_Corner_01` | -0.84 / -0.04 | 1.30 / 2.05 | 2 | 0° |
| `SM_Bld_Shop_Corner_02` | -0.77 / -0.77 | 1.27 / 2.62 | 1 | 45° |

`StorefrontLeafBaker` clips every triangle against the measured doorway prism and interpolates
position, normal, tangent, UV and colour at a cut. It writes stable-GUID mesh assets to
`Assets/CityKit/Storefront`: one `_Doorless` facade per module and `_Leaf_L`/`_Leaf_R` assets where
the profile has leaves. Leaf submesh 0 is the original wall/frame material and submesh 1 is the
original glass material. Shop 06 deliberately cuts a solid panel; Shop 05 deliberately has no
door.

## Runtime contract

- The original wall renderer remains in place but receives its baked `_Doorless` mesh. Only the
  source child renderer whose name ends in `_Glass` is disabled.
- `StorefrontLive` marks panes, leaves, shutter, boards and damage so `ScenePerf` never folds them
  into static chunks. The prefab pool restores both renderer state and the original `MeshFilter`
  mesh before reuse.
- One measured doored bay publishes one business site and one live `Storefront`. A doorless
  Shop 05 or the display half of a 10 m Shop 03 joins the adjacent premise. Binding bounds are
  explicit 5×5 m or 10×5 m pieces; no renderer heuristic decides ownership.
- `ShopEntrance` and `BuildingDoor` share the exact measured threshold. `DoorBeat`, full
  pedestrians and `ResidentialBlockLife` ambient door routines all request the same 0.55-second,
  78-degree swing.
- State is `Intact`, `Open`, `Smashed`, `Burning`, `Boarded` or `Shuttered`. Smash/fire/repair
  still enter through `ShopDamage`; a streamed view rebinds from the persistent business sets.
  Shutters follow shutdown/non-trading state and the 22:00–08:00 clock window. There is no visual
  closure dice.

## Reproduction and review

With the project open in Unity:

    unity command gangsters_storefront --what all --seed 1987 --json

`--what bake` only regenerates the mesh assets, `bench` rebuilds
`Assets/Scenes/StorefrontBench.unity`, `demo` rebuilds the playable
`Assets/Scenes/StorefrontDemo.unity`, and `audit` checks the 8 profiles, 19 baked assets, all
eligible harvested prefabs and five deterministic residential seeds. Each prefab is built twice,
including inactive-child reuse, and the resulting live hierarchy must match. The bench has all eight
untouched source modules, all eight live modules open at 78°, and a separate six-state Shop 01
row. `--unit residential-06` narrows the live construction verdict; `--draw` stands that unit in
the open scene without saving.

The traffic demo lines up all eight source shops and staggers one animated visitor per shop through
a continuous enter/open/close/exit cycle. It calls the production `Storefront.Open` and `Close`
path; it does not animate duplicate demo leaves. Shop 05 remains its authored doorless display bay
and is shown with the adjacent shared Shop 01 entrance used by the binding contract. In Play Mode,
WASD/arrows pan, Q/E or right-drag orbit, and the mouse wheel zooms.

The normal regression pair is:

    unity command gangsters_business_tests --json
    unity command gangsters_storefront --what audit --json

In Play Mode, `gangsters_door_audit` checks every physical business in an active streamed
residential view. Missing approaches, bindings, doors and leaves fail; expected off-screen sites
are excluded.
