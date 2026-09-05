# Runtime ownership

CoreDemo is the game. This map supersedes legacy bootstrap/UI advice in the
2026-08-30 PROJECT_ARCHITECTURE_SNAPSHOT.md.

| Responsibility | Authority / entry |
|---|---|
| Game composition | CoreDemo.unity, CoreDemoBuilder, RoadDemoBuilder |
| Small integration scene | MiniCoreDemo.unity |
| Service installation | GameplayBootstrap: personnel, outfit, Ledger |
| Campaign / families | Underworld, House, CampaignRunner, HouseMind |
| Persistent businesses | BusinessSiteCatalog, BusinessDirectory, BusinessDeeds |
| Business views | BusinessRuntime, CityBusinesses, Storefront |
| Territory | TerritoryState and commands; TerritoryRuntime adapter |
| Physical crews | DemoCrews, CrewJobs, CrewWalker, DoorBeat |
| Police response / cases | PoliceDispatch / PrisonPipeline |
| City input | DemoCamera, PointerGesture, BuildingCardPicker veto chain |
| Orders | CrewOverlay, TurfMapHud, DoorJobs |
| Current UI | PersonnelAlmanac, StreetHud, DemoClockHud, TurfMap, ModalGate |
| UI art | LedgerStyle, UiSkin, TurfGlyphs |
| Save/load | CampaignFile |

The historical RoadDemo namespace contains production code. LivingCity also contains
shared production models: neither namespace is a deletion boundary.

GameplayBootstrap no longer installs the legacy CityBuilder gameplay stack or its
StrategicMapHud, CityOverlayHud, BlockOverlayHud and CityClockHud. Those four UI classes
are removed. Remaining legacy generation/domain adapters require individual dependency
audits: several still feed shared city catalogs.

Keep freeway/expressway, harbor, airport and their assets for CoreDemo integration.
Keep focused rigs that validate shared behavior. Check regression and generator users,
including commands that load paths, before deleting a rig.

Generator changes need asset freshness checks and regeneration when Unity access is
authorized. An already-open scene may still display an old composition after compilation.
