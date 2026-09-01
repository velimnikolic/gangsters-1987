using System.Collections.Generic;
using LivingCity.Personnel;
using RoadDemo;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RacketeeringDemo
{
    /// <summary>
    /// A single-building viewer for the animation beats already used by the live city.
    /// This file authors no racket animation or timing: walking is CrewWalker.OrderAcross,
    /// doorway visits are DoorBeat.VisitThrough, interior actions are the city's authored
    /// takes, and the collection prop is BagCarry.Give. The dropdown only chooses which
    /// existing city call chain to replay.
    /// </summary>
    public sealed class RacketeeringAnimationDemoBuilder : MonoBehaviour
    {
        const string ShopPath =
            "Assets/Synty/PolygonTown/Prefabs/Buildings/SM_Bld_Shop_01.prefab";
        const string GangsterPath =
            "Assets/Synty/PolygonGangWarfare/Prefabs/Character/SM_Chr_Italian_Gangster_01.prefab";
        const string OwnerPath =
            "Assets/Synty/PolygonTown/Prefabs/Characters/SM_Chr_ShopKeeper_01.prefab";
        const string CounterPath =
            "Assets/Synty/PolygonTown/Prefabs/Props/SM_Prop_ShopCounter_01.prefab";
        const string ShelfPath =
            "Assets/Synty/PolygonTown/Prefabs/Props/SM_Prop_ShopShelf_Wall_01.prefab";
        const string FridgePath =
            "Assets/Synty/PolygonTown/Prefabs/Props/SM_Prop_ShopFridge_01.prefab";

        const int DemoCrewId = 1987;
        const int FeedBagCrewId = DemoCrewId + 1;
        const int InteriorFeedLayer = 29;
        const string ViewerRootName = "Live City Racket Animation Viewer";
        const float FloorY = 0.26f;
        const float SetOffAfter = 1f;
        const float CompleteHold = 1.8f;
        const float DamageHold = 4f;

        static readonly Vector3 StreetStart = new Vector3(0f, FloorY, 11f);
        // The authored front doors sit on z=4.21 and the character bounds reach about
        // 0.7 m behind their root. z=4.65 buried half the man in the facade; 5.15 keeps
        // his heels outside while still reading as standing directly at the entrance.
        static readonly Vector3 DoorPoint = new Vector3(0f, FloorY, 5.15f);
        static readonly Vector3 DoorThreshold = new Vector3(0f, FloorY, 4.18f);
        // The whole renderer has crossed the 4.21 m facade plane before DoorBeat hides it:
        // this is what prevents the live feed reading as a spawn on the other camera.
        static readonly Vector3 InsidePoint = new Vector3(0f, FloorY, 3.18f);
        static readonly Vector3 CameraPosition = new Vector3(7.8f, 4.5f, 13.8f);
        static readonly Vector3 CameraLook = new Vector3(0f, 1.35f, 6.8f);
        // Kept inside PedDetail's 60 m animation radius, but isolated from the street
        // camera by layer. At the old 1000 m stage all authored city gestures were
        // intentionally culled, leaving two bodies rotating on idle in the popup.
        static readonly Vector3 FeedOrigin = new Vector3(30f, 0f, 30f);
        static readonly Vector3 FeedVisitorPoint =
            FeedOrigin + new Vector3(0f, FloorY, 1.3f);
        static readonly Vector3 FeedOwnerPoint =
            FeedOrigin + new Vector3(0f, FloorY, -0.85f);

        static readonly float[] ReviewSpeeds = { 0.25f, 0.5f, 1f, 2f };
        static readonly string[] ActionLabels =
        {
            "DEMAND PROTECTION",
            "THREATEN THE OWNER",
            "COLLECT THE TAKE",
            "RAID THE PREMISES",
            "SMASH UP THE SHOP",
            "TORCH THE SHOP",
        };

        enum DemoAction
        {
            DemandProtection,
            ThreatenOwner,
            CollectTake,
            RaidPremises,
            SmashUpShop,
            TorchShop,
        }

        readonly List<Material> _materials = new List<Material>();

        Transform _runtime;
        Transform _actor;
        Transform _shop;
        Transform _feedRoot;
        Transform _feedVisitorActor;
        CrewWalker _walker;
        CrewWalker _feedVisitor;
        CrewWalker _feedOwner;
        Camera _camera;
        Camera _feedCamera;
        RenderTexture _feedTexture;
        Material _roadMaterial;
        Material _sidewalkMaterial;
        Material _shopPadMaterial;

        GUIStyle _titleStyle;
        GUIStyle _bodyStyle;
        GUIStyle _statusStyle;
        GUIStyle _menuButtonStyle;

        DemoAction _selected = DemoAction.DemandProtection;
        DemoAction _queuedAction = DemoAction.DemandProtection;
        float _elapsed;
        float _arrivedAt;
        float _visitStartedAt;
        float _originalTimeScale;
        float _reviewSpeed = 1f;
        string _phase = "READY";
        bool _dropdownOpen;
        bool _setOff;
        bool _visitCalled;
        bool _wasInside;
        bool _returned;
        bool _insideFeed;
        bool _restartQueued;
        bool _changedTimeScale;
        int _swingCount;
        float _nextSwingAt;
        bool _damageApplied;
        Transform _damageVisual;
        Transform _molotovVisual;

        void Awake()
        {
            EnsureBuilt();
        }

        void OnEnable()
        {
            // Unity preserves scene-object references across a hot reload, but the
            // plain C# CrewWalker is intentionally not serialized. Rebuild the thin
            // viewer instead of leaving a perfectly rendered actor that can no longer
            // tick any of the shared city actions.
            if (Application.isPlaying && (_runtime == null || _walker == null))
                EnsureBuilt();
        }

        void EnsureBuilt()
        {
            if (_runtime != null && _walker != null)
                return;

            var staleRuntime = _runtime != null
                ? _runtime
                : transform.Find(ViewerRootName);
            if (staleRuntime != null)
            {
                staleRuntime.gameObject.SetActive(false);
                Destroy(staleRuntime.gameObject);
            }

            BagCarry.Drop(DemoCrewId, banked: true);
            BagCarry.Drop(FeedBagCrewId, banked: true);
            _walker?.Dispose();
            _feedVisitor?.Dispose();
            _feedOwner?.Dispose();
            if (_feedTexture != null)
            {
                _feedTexture.Release();
                Destroy(_feedTexture);
            }
            if (_damageVisual != null)
                Destroy(_damageVisual.gameObject);
            if (_molotovVisual != null)
                Destroy(_molotovVisual.gameObject);
            TestBench.DestroyAll(_materials);

            _runtime = null;
            _actor = null;
            _shop = null;
            _feedRoot = null;
            _feedVisitorActor = null;
            _walker = null;
            _feedVisitor = null;
            _feedOwner = null;
            _camera = null;
            _feedCamera = null;
            _feedTexture = null;
            _damageVisual = null;
            _molotovVisual = null;

            _originalTimeScale = Time.timeScale;
            Build();
        }

        void Build()
        {
            _runtime = new GameObject(ViewerRootName).transform;
            _runtime.SetParent(transform, false);

            MakeMaterials();
            BuildStreetAndShop();
            BuildActor();
            BuildInteriorFeed();
            BuildLighting();
            BuildCamera();
            RestartNow(_selected);
        }

        void BuildStreetAndShop()
        {
            Cube("road", _runtime, new Vector3(0f, -0.08f, 12f),
                new Vector3(24f, 0.16f, 8f), _roadMaterial);
            Cube("sidewalk", _runtime, new Vector3(0f, 0f, 7f),
                new Vector3(24f, 0.22f, 2.2f), _sidewalkMaterial);
            Cube("shop pad", _runtime, new Vector3(0f, 0.10f, 0f),
                new Vector3(8f, 0.2f, 8f), _shopPadMaterial);

            var shopPrefab = Load<GameObject>(ShopPath);
            if (shopPrefab != null)
            {
                var shop = Instantiate(shopPrefab, _runtime);
                shop.name = "City shop used as the doorstep";
                shop.transform.localPosition = Vector3.zero;
                shop.transform.localRotation = Quaternion.identity;
                StripPhysics(shop);
                _shop = shop.transform;
            }

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.40f, 0.41f, 0.43f);
            RenderSettings.fog = false;
        }

        void BuildActor()
        {
            var prefab = Load<GameObject>(GangsterPath);
            if (prefab == null)
                return;

            var body = Instantiate(prefab, _runtime);
            body.name = "Live city crew walker";
            body.SetActive(true);
            StripPhysics(body);
            foreach (var script in body.GetComponentsInChildren<MonoBehaviour>(true))
                Destroy(script);

            _actor = body.transform;
            _walker = new CrewWalker
            {
                CharacterId = DemoCrewId,
                DisplayName = "RACKET DEMO",
                Speed = 2f,
                SourcePrefab = prefab,
            };

            // The same wardrobe draw and deterministic variety seed used by DemoCrews.
            var clips = CrewKit.Draw(CrewKit.Clips(), new System.Random(4242));
            _walker.InitAt(_actor, clips, StreetStart,
                Quaternion.LookRotation(Vector3.back, Vector3.up));
            _walker.RoamsAlone = false;
            _walker.SetJog(3f);
            _walker.Arm(CrewKit.Weapon(CrewArms.DefaultSidearm), EquipmentKind.Pistol);
        }

        void BuildInteriorFeed()
        {
            _feedRoot = new GameObject("Interior live feed stage").transform;
            _feedRoot.SetParent(_runtime, false);
            _feedRoot.position = FeedOrigin;

            // One small reusable room and one reusable interior double. The live street
            // body remains genuinely hidden under DoorBeat; the double lets the feed use
            // the city's authored conversation takes without two systems fighting over
            // the same transform.
            Cube("feed floor", _feedRoot, new Vector3(0f, 0.10f, 0f),
                new Vector3(7.5f, 0.2f, 7f), _shopPadMaterial);
            Cube("feed rear wall", _feedRoot, new Vector3(0f, 1.8f, -3.45f),
                new Vector3(7.5f, 3.6f, 0.18f), _sidewalkMaterial);
            Cube("feed left wall", _feedRoot, new Vector3(-3.65f, 1.8f, 0f),
                new Vector3(0.18f, 3.6f, 7f), _sidewalkMaterial);
            Cube("feed right wall", _feedRoot, new Vector3(3.65f, 1.8f, 0f),
                new Vector3(0.18f, 3.6f, 7f), _sidewalkMaterial);
            PlaceFeedPrefab(CounterPath, "feed owner counter", new Vector3(0f, FloorY, 0f), 180f);
            PlaceFeedPrefab(ShelfPath, "feed stock shelf", new Vector3(-2.45f, FloorY, -2.7f), 0f);
            PlaceFeedPrefab(FridgePath, "feed fridge", new Vector3(2.45f, FloorY, -2.55f), 180f);

            var ownerPrefab = Load<GameObject>(OwnerPath);
            if (ownerPrefab != null)
            {
                var body = Instantiate(ownerPrefab, _feedRoot);
                body.name = "Live feed shop owner";
                StripPhysics(body);
                foreach (var script in body.GetComponentsInChildren<MonoBehaviour>(true))
                    Destroy(script);
                body.transform.SetPositionAndRotation(FeedOwnerPoint,
                    Quaternion.LookRotation(Vector3.forward, Vector3.up));

                _feedOwner = new CrewWalker
                {
                    CharacterId = -DemoCrewId,
                    DisplayName = "SHOP OWNER",
                    Speed = 2f,
                    SourcePrefab = ownerPrefab,
                };
                var clips = CrewKit.Draw(CrewKit.Clips(), new System.Random(1987));
                _feedOwner.InitAt(body.transform, clips, FeedOwnerPoint,
                    Quaternion.LookRotation(Vector3.forward, Vector3.up));
                _feedOwner.RoamsAlone = false;
            }

            var visitorPrefab = Load<GameObject>(GangsterPath);
            if (visitorPrefab != null)
            {
                var body = Instantiate(visitorPrefab, _feedRoot);
                body.name = "Interior feed crew walker";
                StripPhysics(body);
                foreach (var script in body.GetComponentsInChildren<MonoBehaviour>(true))
                    Destroy(script);

                _feedVisitorActor = body.transform;
                _feedVisitor = new CrewWalker
                {
                    CharacterId = FeedBagCrewId,
                    DisplayName = "RACKET DEMO · INTERIOR FEED",
                    Speed = 2f,
                    SourcePrefab = visitorPrefab,
                };
                var clips = CrewKit.Draw(CrewKit.Clips(), new System.Random(4242));
                _feedVisitor.InitAt(_feedVisitorActor, clips, FeedVisitorPoint,
                    Quaternion.LookRotation(Vector3.back, Vector3.up));
                _feedVisitor.RoamsAlone = false;
                _feedVisitor.Arm(
                    CrewKit.Weapon(CrewArms.DefaultSidearm), EquipmentKind.Pistol);
            }

            _feedTexture = new RenderTexture(768, 432, 16, RenderTextureFormat.ARGB32)
            {
                name = "Racket shop live feed",
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false,
            };
            _feedTexture.Create();

            var cameraObject = new GameObject("Interior live feed camera");
            cameraObject.transform.SetParent(_feedRoot, false);
            _feedCamera = cameraObject.AddComponent<Camera>();
            _feedCamera.targetTexture = _feedTexture;
            _feedCamera.cullingMask = 1 << InteriorFeedLayer;
            _feedCamera.fieldOfView = 47f;
            _feedCamera.nearClipPlane = 0.08f;
            _feedCamera.farClipPlane = 30f;
            _feedCamera.clearFlags = CameraClearFlags.SolidColor;
            _feedCamera.backgroundColor = new Color(0.055f, 0.045f, 0.035f);
            _feedCamera.GetUniversalAdditionalCameraData().antialiasing =
                AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var cameraPosition = FeedOrigin + new Vector3(3.35f, 2.45f, 3.15f);
            var cameraLook = FeedOrigin + new Vector3(0f, 1.25f, 0.05f);
            cameraObject.transform.SetPositionAndRotation(cameraPosition,
                Quaternion.LookRotation(cameraLook - cameraPosition, Vector3.up));

            var light = new GameObject("Interior live feed light").AddComponent<Light>();
            light.transform.SetParent(_feedRoot, false);
            light.type = LightType.Point;
            light.range = 10f;
            light.intensity = 2f;
            light.color = new Color(1f, 0.76f, 0.54f);
            light.shadows = LightShadows.Soft;
            light.cullingMask = 1 << InteriorFeedLayer;
            light.transform.localPosition = new Vector3(0.8f, 3f, 1.6f);

            SetLayerRecursively(_feedRoot.gameObject, InteriorFeedLayer);
            _feedRoot.gameObject.SetActive(false);
        }

        void BuildLighting()
        {
            var key = new GameObject("street key light").AddComponent<Light>();
            key.transform.SetParent(_runtime, false);
            key.type = LightType.Directional;
            key.intensity = 1.15f;
            key.color = new Color(1f, 0.91f, 0.78f);
            key.shadows = LightShadows.Soft;
            key.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

            var fill = new GameObject("door fill light").AddComponent<Light>();
            fill.transform.SetParent(_runtime, false);
            fill.type = LightType.Point;
            fill.range = 12f;
            fill.intensity = 1.65f;
            fill.color = new Color(1f, 0.76f, 0.54f);
            fill.shadows = LightShadows.Soft;
            fill.transform.localPosition = new Vector3(1.8f, 3.2f, 5.1f);
        }

        void BuildCamera()
        {
            var cameraObject = new GameObject("Live City Beat Camera") { tag = "MainCamera" };
            cameraObject.transform.SetParent(_runtime, false);
            _camera = cameraObject.AddComponent<Camera>();
            _camera.cullingMask &= ~(1 << InteriorFeedLayer);
            _camera.fieldOfView = 47f;
            _camera.nearClipPlane = 0.08f;
            _camera.farClipPlane = 120f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.11f, 0.14f, 0.19f);
            _camera.GetUniversalAdditionalCameraData().antialiasing =
                AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.transform.position = CameraPosition;
            cameraObject.transform.rotation = Quaternion.LookRotation(
                CameraLook - CameraPosition, Vector3.up);
            DemoCamera.ClaimMainCamera(_camera);
        }

        void Update()
        {
            HandleKeyboard();
            if (_walker == null || _actor == null)
                return;

            var dt = Time.deltaTime;
            _elapsed += dt;
            _walker.TickCrew(dt);
            if (_insideFeed)
            {
                _feedVisitor?.TickCrew(dt);
                _feedOwner?.TickCrew(dt);
            }

            if (!_setOff && _elapsed >= SetOffAfter)
            {
                _setOff = true;
                _phase = "CREWWALKER.ORDERACROSS · WALK TO THE DOOR";
                _walker.OrderAcross(DoorPoint);
            }

            if (_setOff && !_visitCalled && !_returned && !_walker.HasOrder &&
                HorizontalDistance(_actor.position, DoorPoint) <= 0.55f)
                ArrivedAtDoor();

            if (_visitCalled && !_returned)
            {
                if (IsWrecking(_selected))
                    TickWreckingAction();
                else
                    ReadSharedDoorBeat();
            }

            var hold = IsWrecking(_selected) ? DamageHold : CompleteHold;
            if (_returned && Time.time - _arrivedAt >= hold)
            {
                var next = _restartQueued ? _queuedAction : _selected;
                RestartNow(next);
            }
        }

        void ArrivedAtDoor()
        {
            _arrivedAt = Time.time;
            if (IsWrecking(_selected))
            {
                BeginWreckingAction();
                return;
            }

            switch (_selected)
            {
                case DemoAction.CollectTake:
                    // The collection bag belongs to the job, not to this shop owner:
                    // the collector arrived carrying it and takes it through the door.
                    _phase = "DOORBEAT.VISITTHROUGH · ENTERING WITH COLLECTION BAG";
                    break;
                case DemoAction.ThreatenOwner:
                    _phase = "DOORBEAT.VISITTHROUGH · THREATEN THE OWNER";
                    break;
                case DemoAction.RaidPremises:
                    _phase = "CREWJOBS.RAID · ENTER THE PREMISES";
                    break;
                default:
                    _phase = "DOORBEAT.VISITTHROUGH · DEMAND PROTECTION";
                    break;
            }

            _visitCalled = true;
            _visitStartedAt = Time.time;
            DoorBeat.VisitThrough(
                _walker, DoorPoint, DoorThreshold, InsidePoint, _shop);
        }

        void ReadSharedDoorBeat()
        {
            var sharedPhase = DoorBeat.PhaseOf(_walker);
            if (sharedPhase == DoorBeat.VisitPhase.Inside)
            {
                _wasInside = true;
                StartInteriorFeed();
                switch (_selected)
                {
                    case DemoAction.CollectTake:
                        _phase = "LIVE FEED · OWNER PAYS INTO COLLECTION BAG";
                        break;
                    case DemoAction.ThreatenOwner:
                        _phase = "LIVE FEED · AUTHORED AGGRESSIVE / PLEAD TAKES";
                        break;
                    case DemoAction.RaidPremises:
                        _phase = "LIVE FEED · RAID INSIDE THE PREMISES";
                        break;
                    default:
                        _phase = "LIVE FEED · AUTHORED CITY CONVERSATION";
                        break;
                }
                return;
            }

            // The street camera comes back before DoorBeat reveals the real body inside
            // the open doorway, so the exit is as physical as the entry rather than a
            // second pop at the pavement.
            if (_insideFeed)
                StopInteriorFeed();

            switch (sharedPhase)
            {
                case DoorBeat.VisitPhase.Approaching:
                    _phase = "CREWWALKER.ORDERTOPOINT · WALKING TO THE DOORSTEP";
                    return;
                case DoorBeat.VisitPhase.OpeningEntry:
                    _phase = "DOORBEAT · OPENING THE SHOP DOORS";
                    return;
                case DoorBeat.VisitPhase.Entering:
                    _phase = "CREWWALKER.ORDERTOPOINT · CROSSING THE THRESHOLD";
                    return;
                case DoorBeat.VisitPhase.OpeningExit:
                    _phase = "DOORBEAT · OPENING FOR THE RETURN";
                    return;
                case DoorBeat.VisitPhase.Exiting:
                    _phase = _selected == DemoAction.CollectTake
                        ? "WALKING OUT · CARRYING THE TAKE"
                        : "WALKING OUT OF THE SHOP";
                    return;
                case DoorBeat.VisitPhase.Closing:
                    _phase = "DOORBEAT · CLOSING THE SHOP DOORS";
                    return;
                case DoorBeat.VisitPhase.Talking:
                    _phase = "DOORBEAT · AUTHORED DOORSTEP TALK";
                    return;
            }

            if (_wasInside && sharedPhase == DoorBeat.VisitPhase.None)
            {
                _visitCalled = false;
                _returned = true;
                _arrivedAt = Time.time;
                _phase = _selected == DemoAction.CollectTake
                    ? "RETURNED FROM THE SHOP · CARRYING THE TAKE"
                    : "RETURNED FROM THE SHOP · ACTION COMPLETE";
                return;
            }

            // Defensive backstop if the shared visit was refused by a live-city guard
            // such as combat. A real passage owns its own timing while it is active.
            if (sharedPhase == DoorBeat.VisitPhase.None &&
                Time.time - _visitStartedAt > 1f)
            {
                _visitCalled = false;
                _returned = true;
                _arrivedAt = Time.time;
                _phase = "SHARED DOOR BEAT REFUSED · COMPLETE";
            }
        }

        void StartInteriorFeed()
        {
            if (_insideFeed || _actor == null || _feedRoot == null)
                return;

            _insideFeed = true;
            _feedRoot.gameObject.SetActive(true);

            if (_feedVisitorActor == null || _feedVisitor == null || _feedOwner == null)
                return;

            _feedVisitorActor.gameObject.SetActive(true);
            _feedVisitorActor.SetPositionAndRotation(FeedVisitorPoint,
                Quaternion.LookRotation(Vector3.back, Vector3.up));
            _feedOwner.Tf.gameObject.SetActive(true);
            _feedOwner.Tf.SetPositionAndRotation(FeedOwnerPoint,
                Quaternion.LookRotation(Vector3.forward, Vector3.up));

            // The city's own chat ownership keeps both roots facing one another; the
            // old feed left them loitering, so SpendLook turned them round and round.
            var conversationFor = DoorBeat.InsideSeconds + 0.5f;
            _feedVisitor.BeginChat(_feedOwner, conversationFor, speaksFirst: true);
            _feedOwner.BeginChat(_feedVisitor, conversationFor, speaksFirst: false);

            switch (_selected)
            {
                case DemoAction.ThreatenOwner:
                    _feedVisitor.PlayTake(
                        CrewKit.AggressiveLoop, loop: true, speed: 1f, at: 0f);
                    _feedOwner.PlayTake(CrewKit.Plead, loop: false, speed: 1f, at: 0f);
                    break;
                case DemoAction.CollectTake:
                    _feedOwner.PlayTake(
                        CrewKit.DoorTalk, loop: true, speed: 1f, at: 0f);
                    BagCarry.Give(FeedBagCrewId, _feedVisitor);
                    break;
                case DemoAction.RaidPremises:
                    _feedVisitor.PlayTake(
                        CrewKit.AggressiveLoop, loop: true, speed: 1f, at: 0f);
                    _feedOwner.PlayTake(
                        CrewKit.Plead, loop: false, speed: 1f, at: 0f);
                    break;
                default:
                    _feedVisitor.PlayTake(
                        CrewKit.DoorTalk, loop: true, speed: 1f, at: 0f);
                    break;
            }
        }

        void StopInteriorFeed()
        {
            BagCarry.Drop(FeedBagCrewId, banked: true);
            _feedVisitor?.EndTake();
            _feedOwner?.EndTake();
            _feedVisitor?.EndChat();
            _feedOwner?.EndChat();
            _insideFeed = false;
            if (_feedRoot != null)
                _feedRoot.gameObject.SetActive(false);
        }

        static bool IsWrecking(DemoAction action) =>
            action == DemoAction.SmashUpShop || action == DemoAction.TorchShop;

        void BeginWreckingAction()
        {
            _visitCalled = true;
            _visitStartedAt = Time.time;
            _damageApplied = false;

            if (_selected == DemoAction.TorchShop)
            {
                _phase = "MOLOTOVPROJECTILE · LIGHTING AND THROWING";
                var projectile = MolotovProjectile.ThrowAt(
                    _walker,
                    DoorThreshold + Vector3.up * 0.85f,
                    DoorThreshold,
                    Vector3.forward,
                    "RACKET DEMO",
                    FloorY,
                    OnTorchIgnited);
                if (projectile != null)
                {
                    _molotovVisual = projectile.transform;
                    return;
                }

                _phase = "MOLOTOV THROW REFUSED · COMPLETE";
                _visitCalled = false;
                _returned = true;
                _arrivedAt = Time.time;
                return;
            }

            _swingCount = 0;
            _nextSwingAt = Time.time;
            _phase = "CREWJOBS.SMASHUP · TWO QUICK BLOWS AT THE SHOPFRONT";
            TickWreckingAction();
        }

        void TickWreckingAction()
        {
            if (_selected == DemoAction.TorchShop)
            {
                // The projectile normally finishes through OnTorchIgnited. This is only
                // a defensive escape if its scene object was removed mid-flight.
                if (!_damageApplied && _molotovVisual == null &&
                    Time.time - _visitStartedAt > 2.5f)
                {
                    _damageVisual = ShopDamage.ScorchAt(
                        DoorThreshold, Vector3.forward, "RACKET DEMO", FloorY);
                    CompleteDamageAction("SHOPDAMAGE.SCORCHAT · PREMISES BURNING");
                }
                return;
            }

            if (_swingCount < CrewJobs.PremisesSmashRounds)
            {
                if (Time.time < _nextSwingAt || ArmBeat.Acting(_walker))
                    return;

                if (!ArmBeat.Swing(
                    _walker, DoorThreshold, CrewJobs.PremisesSmashFor))
                {
                    _phase = "ARMBEAT REFUSED THE EXISTING CITY SWING";
                    return;
                }

                _swingCount++;
                _nextSwingAt = Time.time + CrewJobs.PremisesSmashEvery;
                _phase = "ARMBEAT.SWING · SMASH BLOW " +
                    _swingCount + "/" + CrewJobs.PremisesSmashRounds;
                return;
            }

            if (ArmBeat.Acting(_walker) || _damageApplied)
                return;

            _damageVisual = ShopDamage.SmashAt(
                DoorThreshold, Vector3.forward, "RACKET DEMO", FloorY);
            CompleteDamageAction("SHOPDAMAGE.SMASHAT · EXTERIOR BOARDS UP");
        }

        void OnTorchIgnited(Transform damage)
        {
            _molotovVisual = null;
            _damageVisual = damage;
            CompleteDamageAction("SHOPDAMAGE.SCORCHAT · PREMISES BURNING NOW");
        }

        void CompleteDamageAction(string phase)
        {
            _damageApplied = true;
            _phase = phase;
            _visitCalled = false;
            _returned = true;
            _arrivedAt = Time.time;
        }

        void RestartNow(DemoAction action)
        {
            if (_walker == null || _actor == null)
                return;

            StopInteriorFeed();
            BagCarry.Drop(DemoCrewId, banked: true);
            if (_damageVisual != null)
            {
                Destroy(_damageVisual.gameObject);
                _damageVisual = null;
            }
            if (_molotovVisual != null)
            {
                Destroy(_molotovVisual.gameObject);
                _molotovVisual = null;
            }
            _selected = action;
            _queuedAction = action;
            _actor.gameObject.SetActive(true);
            _actor.SetPositionAndRotation(StreetStart,
                Quaternion.LookRotation(Vector3.back, Vector3.up));
            if (_selected == DemoAction.CollectTake)
            {
                // CollectProtection dresses its collector for the whole round. The
                // owner pays into this bag; the owner does not hand the bag over.
                BagCarry.Give(DemoCrewId, _walker);
            }

            _elapsed = 0f;
            _arrivedAt = 0f;
            _visitStartedAt = 0f;
            _phase = "READY · " + ActionLabels[(int)_selected];
            _dropdownOpen = false;
            _setOff = false;
            _visitCalled = false;
            _wasInside = false;
            _returned = false;
            _restartQueued = false;
            _swingCount = 0;
            _nextSwingAt = 0f;
            _damageApplied = false;
        }

        void RequestRestart(DemoAction action)
        {
            _queuedAction = action;
            if (_visitCalled && !_returned)
            {
                // DoorBeat owns the body until it returns it. Cutting it off would make
                // the viewer differ from the live city and could strand a hidden man.
                _restartQueued = true;
                _dropdownOpen = false;
                _phase = "NEXT ACTION QUEUED · WAITING FOR SHARED DOORBEAT";
                return;
            }
            RestartNow(action);
        }

        void HandleKeyboard()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.rKey.wasPressedThisFrame)
                RequestRestart(_selected);
            if (keyboard.leftArrowKey.wasPressedThisFrame)
                SelectRelative(-1);
            if (keyboard.rightArrowKey.wasPressedThisFrame)
                SelectRelative(1);

            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                if (Time.timeScale <= 0.001f)
                    SetReviewSpeed(_reviewSpeed);
                else
                {
                    _reviewSpeed = Time.timeScale;
                    SetReviewSpeed(0f);
                }
            }

            if (keyboard.equalsKey.wasPressedThisFrame ||
                keyboard.numpadPlusKey.wasPressedThisFrame)
                StepSpeed(1);
            if (keyboard.minusKey.wasPressedThisFrame ||
                keyboard.numpadMinusKey.wasPressedThisFrame)
                StepSpeed(-1);
        }

        void SelectRelative(int direction)
        {
            var count = ActionLabels.Length;
            var index = ((int)_selected + direction + count) % count;
            RequestRestart((DemoAction)index);
        }

        void StepSpeed(int direction)
        {
            var current = Time.timeScale <= 0.001f ? _reviewSpeed : Time.timeScale;
            var nearest = 0;
            var gap = float.MaxValue;
            for (var i = 0; i < ReviewSpeeds.Length; i++)
            {
                var distance = Mathf.Abs(ReviewSpeeds[i] - current);
                if (distance >= gap)
                    continue;
                gap = distance;
                nearest = i;
            }
            nearest = Mathf.Clamp(nearest + direction, 0, ReviewSpeeds.Length - 1);
            _reviewSpeed = ReviewSpeeds[nearest];
            SetReviewSpeed(_reviewSpeed);
        }

        void SetReviewSpeed(float speed)
        {
            _changedTimeScale = true;
            Time.timeScale = speed;
        }

        void OnGUI()
        {
            if (_camera == null)
                return;
            EnsureStyles();

            var width = Mathf.Min(930f, Screen.width - 28f);
            var panel = new Rect(14f, 12f, width, 174f);
            GUI.Box(panel, GUIContent.none);
            GUI.Label(new Rect(panel.x + 16f, panel.y + 9f, panel.width - 32f, 28f),
                "LIVE CITY ANIMATIONS · REKETIRANJE LOKALA", _titleStyle);
            GUI.Label(new Rect(panel.x + 16f, panel.y + 37f, panel.width - 32f, 20f),
                "Bez demo animacija: viewer poziva iste CrewWalker / DoorBeat / ArmBeat / BagCarry sisteme.",
                _bodyStyle);

            var actionRect = new Rect(panel.x + 16f, panel.y + 62f, 272f, 30f);
            if (GUI.Button(actionRect, ActionLabels[(int)_selected] + "   ▼",
                    _menuButtonStyle))
                _dropdownOpen = !_dropdownOpen;

            if (GUI.Button(new Rect(actionRect.xMax + 10f, actionRect.y, 92f, 30f), "REPLAY"))
                RequestRestart(_selected);

            var paused = Time.timeScale <= 0.001f;
            if (GUI.Button(new Rect(actionRect.xMax + 108f, actionRect.y, 92f, 30f),
                    paused ? "RESUME" : "PAUSE"))
            {
                if (paused)
                    SetReviewSpeed(_reviewSpeed);
                else
                {
                    _reviewSpeed = Time.timeScale;
                    SetReviewSpeed(0f);
                }
            }

            GUI.Label(new Rect(panel.x + 16f, panel.y + 98f, panel.width - 32f, 23f),
                _phase, _statusStyle);
            GUI.Label(new Rect(panel.x + 16f, panel.y + 123f, panel.width - 32f, 20f),
                SourceLine(_selected), _bodyStyle);
            GUI.Label(new Rect(panel.x + 16f, panel.y + 146f, panel.width - 32f, 18f),
                "←/→ akcija   R replay   SPACE pauza   +/− brzina   " +
                (paused ? "PAUSED" : "SPEED " + Time.timeScale.ToString("0.00") + "x"),
                _bodyStyle);

            if (_insideFeed)
                DrawInteriorFeed();
            if (_dropdownOpen)
                DrawDropdown(actionRect);
        }

        void DrawInteriorFeed()
        {
            if (_feedTexture == null)
                return;

            // This is the only view of the hidden interior beat, so keep the people
            // large enough to read their authored takes in an ordinary docked Game view.
            var width = Mathf.Min(720f, Screen.width - 36f);
            var height = width * 9f / 16f;
            var frame = new Rect(Screen.width - width - 18f, 202f, width, height + 56f);
            GUI.Box(frame, GUIContent.none);

            var old = GUI.color;
            GUI.color = new Color(0.92f, 0.12f, 0.10f, 1f);
            GUI.DrawTexture(new Rect(frame.x + 12f, frame.y + 11f, 9f, 9f),
                Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(new Rect(frame.x + 28f, frame.y + 5f, frame.width - 40f, 22f),
                "LIVE · INSIDE SHOP", _statusStyle);

            var picture = new Rect(frame.x + 10f, frame.y + 30f,
                frame.width - 20f, height);
            GUI.DrawTexture(picture, _feedTexture, ScaleMode.ScaleToFit, false);
            GUI.Label(new Rect(frame.x + 12f, picture.yMax + 4f, frame.width - 24f, 18f),
                _selected == DemoAction.CollectTake
                    ? "DoorBeat.Inside · owner pays · full-size BagCarry duffel stays with hood"
                    : _selected == DemoAction.RaidPremises
                        ? "CrewJobs Raid · DoorBeat passage · existing AggressiveLoop / Plead takes"
                        : "DoorBeat.Inside · isti gradski authored takes · jedan reusable feed double",
                _bodyStyle);
        }

        void DrawDropdown(Rect anchor)
        {
            var menu = new Rect(anchor.x, anchor.yMax + 3f, anchor.width,
                ActionLabels.Length * 31f + 6f);
            GUI.Box(menu, GUIContent.none);
            for (var i = 0; i < ActionLabels.Length; i++)
            {
                var row = new Rect(menu.x + 3f, menu.y + 3f + i * 31f,
                    menu.width - 6f, 29f);
                if (GUI.Button(row, ActionLabels[i], _menuButtonStyle))
                    RequestRestart((DemoAction)i);
            }

            var currentEvent = Event.current;
            if (currentEvent.type == EventType.MouseDown &&
                !menu.Contains(currentEvent.mousePosition) &&
                !anchor.Contains(currentEvent.mousePosition))
                _dropdownOpen = false;
        }

        static string SourceLine(DemoAction action)
        {
            switch (action)
            {
                case DemoAction.DemandProtection:
                    return "CITY SOURCE: CrewWalker.OrderAcross → DoorBeat.VisitThrough → authored interior talk";
                case DemoAction.ThreatenOwner:
                    return "CITY SOURCE: DoorBeat.VisitThrough + gradski AggressiveLoop / Plead takes u inside feedu";
                case DemoAction.CollectTake:
                    return "CITY SOURCE: BagCarry.Give → CrewWalker.OrderAcross → DoorBeat.VisitThrough · isti hood nosi torbu celu rundu";
                case DemoAction.RaidPremises:
                    return "CITY SOURCE: CrewJobs Raid → DoorBeat.VisitBusiness / VisitThrough → authored inside takes";
                case DemoAction.SmashUpShop:
                    return "CITY SOURCE: CrewJobs 2× quick ArmBeat.Swing → exterior ShopDamage.SmashAt";
                case DemoAction.TorchShop:
                    return "CITY SOURCE: Synty MolotovProjectile throw → immediate ShopDamage.ScorchAt";
                default:
                    return "CITY SOURCE: shared live-city racket sequence";
            }
        }

        void EnsureStyles()
        {
            if (_titleStyle != null)
                return;
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.83f, 0.48f) },
            };
            _bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.90f, 0.92f, 0.95f) },
            };
            _statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.98f, 0.57f, 0.20f) },
            };
            _menuButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(10, 8, 2, 2),
            };
        }

        static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        void PlaceFeedPrefab(string path, string name, Vector3 localPosition, float yaw)
        {
            var prefab = Load<GameObject>(path);
            if (prefab == null || _feedRoot == null)
                return;
            var instance = Instantiate(prefab, _feedRoot);
            instance.name = name;
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            StripPhysics(instance);
        }

        static void StripPhysics(GameObject root)
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                Destroy(collider);
            foreach (var body in root.GetComponentsInChildren<Rigidbody>(true))
                Destroy(body);
        }

        static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null)
                return;
            root.layer = layer;
            for (var i = 0; i < root.transform.childCount; i++)
                SetLayerRecursively(root.transform.GetChild(i).gameObject, layer);
        }

        static T Load<T>(string path) where T : Object
        {
#if UNITY_EDITOR
            return DemoAssetLoad.Load<T>(path);
#else
            return null;
#endif
        }

        GameObject Cube(string name, Transform parent, Vector3 localPosition,
            Vector3 localScale, Material material)
        {
            var instance = GameObject.CreatePrimitive(PrimitiveType.Cube);
            instance.name = name;
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = localPosition;
            instance.transform.localScale = localScale;
            instance.GetComponent<MeshRenderer>().sharedMaterial = material;
            var collider = instance.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);
            return instance;
        }

        void MakeMaterials()
        {
            _roadMaterial = Lit("demo asphalt", new Color(0.075f, 0.08f, 0.095f), 0.05f);
            _sidewalkMaterial = Lit("demo sidewalk", new Color(0.42f, 0.40f, 0.37f), 0.12f);
            _shopPadMaterial = Lit("shop pad", new Color(0.24f, 0.18f, 0.13f), 0.18f);
        }

        Material Lit(string name, Color colour, float smoothness)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", colour);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", colour);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", smoothness);
            _materials.Add(material);
            return material;
        }

        void OnDestroy()
        {
            BagCarry.Drop(DemoCrewId, banked: true);
            BagCarry.Drop(FeedBagCrewId, banked: true);
            if (_damageVisual != null)
                Destroy(_damageVisual.gameObject);
            if (_molotovVisual != null)
                Destroy(_molotovVisual.gameObject);
            _walker?.Dispose();
            _feedVisitor?.Dispose();
            _feedOwner?.Dispose();
            if (_feedTexture != null)
            {
                _feedTexture.Release();
                Destroy(_feedTexture);
            }
            TestBench.DestroyAll(_materials);
            if (_changedTimeScale)
                Time.timeScale = _originalTimeScale;
        }
    }
}
