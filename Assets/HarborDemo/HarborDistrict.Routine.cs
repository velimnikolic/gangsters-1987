using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace HarborDemo
{
    // The port's day, as against its geography: everything that is only true for a
    // while. The shift takes its break at the burn barrel and goes back to work; the
    // gate arms lift for a lorry and drop behind her; a grab throws dust while a bulk
    // ship is worked and none when she is gone; somebody welds on the forecourt; and
    // after dark the gantries and the gates light up.
    //
    // One tick drives all of it, out of the district's own Tick. Nothing here is a
    // MonoBehaviour and nothing here is per-frame expensive: the breaks are picked
    // once every half minute, the gates read a handful of lorries, and the lights are
    // only written when the hour has moved.
    public partial class HarborDistrict
    {
        /// <summary>A crate or a pallet a man may sit on: the surface he sits on, which
        /// way he faces, and whether somebody is on it.</summary>
        sealed class HarborSeat
        {
            public Vector3 Top;
            public float Yaw;
            public bool Taken;
        }

        readonly List<HarborSeat> _seats = new List<HarborSeat>();
        readonly List<(HarborWorker man, HarborSeat seat)> _resting = new List<(HarborWorker, HarborSeat)>();
        float _breakIn = 12f;

        readonly List<Light> _floods = new List<Light>();
        LivingCity.Ambient.CityClock _clock;
        float _litHour = -99f;

        ParticleSystem _welderSparks;
        Light _welderFlash;
        float _welderTimer, _welderOn;

        Vector3 _customsPost, _customsDoor;

        // ------------------------------------------------------------ building

        void BuildRoutine()
        {
            _clock = Object.FindAnyObjectByType<LivingCity.Ambient.CityClock>();
            SmokeTheBarrels();
            BuildWelder();
            PostTheGate();
            LightThePort();
        }

        /// <summary>A drum with a fire in it makes smoke. It also says, from the far side
        /// of the yard, that this is where the men stand - which is what makes the break
        /// read as a break rather than as three dockers wandering off.</summary>
        void SmokeTheBarrels()
        {
            var smoke = HarborKit.TryLoad(HarborKit.FxSmokeWhite);
            var flame = HarborKit.TryLoad(LivingCity.Ambient.FireSmokeFx.FlamesTiny);
            if (smoke == null && flame == null) return;
            for (int i = 0; i < berths; i++)
            {
                var at = new Vector3(BerthX(i) - 30f, TileTop + 1.05f, QuayLaneZ + 4.4f);
                if (smoke != null)
                {
                    var go = Instantiate(smoke, _liveRoot);
                    go.name = "BarrelSmoke";
                    go.transform.localPosition = at + Vector3.up * 0.35f;
                    go.transform.localRotation = Quaternion.identity;
                    go.transform.localScale = Vector3.one * 0.32f;
                    LivingCity.Ambient.FireSmokeFx.TintSmoke(
                        go.GetComponentInChildren<ParticleSystem>(),
                        LivingCity.Ambient.FireSmokeFx.FireSmoke);
                }
                if (flame != null)
                {
                    var go = Instantiate(flame, _liveRoot);
                    go.name = "BarrelFire";
                    go.transform.localPosition = at;
                    go.transform.localRotation = Quaternion.identity;
                    go.transform.localScale = Vector3.one * 0.42f;
                }
            }
        }

        /// <summary>Somebody cutting on the forecourt of the last shed: a bench, a board
        /// of tools, and the flash. The flash is the point - a yard with a welder in it
        /// blinks, and a still yard reads as a photograph however much is in it.</summary>
        void BuildWelder()
        {
            if (_shedDoors.Count == 0) return;
            var door = _shedDoors[_shedDoors.Count - 1];
            var at = new Vector3(door.x - 9f, TileTop, door.z + 0.6f);
            if (InGateLane(at.x, 3f)) return;

            var bench = HarborKit.TryLoad(HarborKit.Workbench);
            var pallet = HarborKit.TryLoad(HarborKit.Pallet);
            var drum = HarborKit.TryLoad(HarborKit.BarrelMetal);
            if (bench != null) HarborKit.Sit(bench, at, 180f, _yardRoot, "WelderBench");
            if (pallet != null) HarborKit.Sit(pallet, at + new Vector3(2.2f, 0f, 0.4f), 20f, _yardRoot, "Pallet");
            if (drum != null) HarborKit.Sit(drum, at + new Vector3(-1.6f, 0f, 0.6f), 0f, _yardRoot, "Drum");

            var sparks = HarborKit.TryLoad(HarborKit.FxSparks);
            if (sparks != null)
            {
                var go = Instantiate(sparks, _liveRoot);
                go.name = "WelderSparks";
                go.transform.localPosition = at + new Vector3(0f, 0.95f, -0.3f);
                _welderSparks = go.GetComponentInChildren<ParticleSystem>();
            }
            var flash = new GameObject("WelderFlash");
            flash.transform.SetParent(_liveRoot, false);
            flash.transform.localPosition = at + new Vector3(0f, 1.1f, -0.3f);
            _welderFlash = flash.AddComponent<Light>();
            _welderFlash.type = LightType.Point;
            _welderFlash.color = new Color(0.78f, 0.88f, 1f);
            _welderFlash.range = 9f;
            _welderFlash.intensity = 0f;
            _welderFlash.shadows = LightShadows.None;
            _welderTimer = HarborKit.Range(_rng, 1f, 4f);
        }

        /// <summary>The men the works want standing in them: the customs officer at the
        /// weighbridge, and a fitter at the pump if the tank farm got one. Both walk a
        /// short round rather than standing to attention - a man rooted to a spot is a
        /// statue of a man.</summary>
        void PostTheGate()
        {
            LoadBodies();
            if (_workerBodies.Count == 0) return;
            if (gateWorks && _customsPost != Vector3.zero)
            {
                var round = new List<Vector3>
                {
                    _customsDoor,
                    _customsPost,
                    _customsPost + new Vector3(-2.5f, 0f, 4f),
                };
                var man = Man(HarborKit.Pick(_rng, _workerBodies), _liveRoot, _customsDoor, 1.15f, WorldPoints(round), null);
                if (man != null) man.DwellRange = new Vector2(6f, 16f);
            }
            if (_dieselPump != Vector3.zero)
            {
                var round = new List<Vector3>
                {
                    _dieselPump + new Vector3(1.6f, 0f, -1.4f),
                    _dieselPump + new Vector3(-3f, 0f, -2.5f),
                    _dieselPump + new Vector3(0.5f, 0f, -5f),
                };
                Man(HarborKit.Pick(_rng, _workerBodies), _liveRoot, round[0], 1.05f, WorldPoints(round), null);
            }
        }

        /// <summary>The port after dark: a pair of floods under each gantry's boom -
        /// which travel with the gantry, because a crane lights the box it is working -
        /// and one over each gate. The quay's own pier lamps are lit by the city's
        /// DemoStreetLamps, which now knows that lamp by name.
        ///
        /// Kept to a handful: URP's forward renderer will take a few hundred lights in a
        /// frame and the headlights and the street want their share.</summary>
        void LightThePort()
        {
            Light Flood(Transform parent, Vector3 local, float range, float angle, float intensity)
            {
                var go = new GameObject("Flood");
                go.transform.SetParent(parent, false);
                go.transform.localPosition = local;
                go.transform.localRotation = Quaternion.Euler(70f, 0f, 0f);
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot;
                l.color = new Color(1f, 0.94f, 0.82f);
                l.range = range;
                l.spotAngle = angle;
                l.innerSpotAngle = angle * 0.5f;
                l.intensity = 0f;
                l.shadows = LightShadows.None;
                _floods.Add(l);
                return l;
            }

            foreach (var crane in _cranes)
            {
                if (crane.Root == null) continue;
                foreach (float side in new[] { -1f, 1f })
                    Flood(crane.Root, new Vector3(side * HarborCrane.LegHalfX, HarborCrane.BoomY - 1.6f, -6f), 60f, 90f, 12f);
            }
            if (gateWorks)
                foreach (float gx in new[] { _gateWestX, _gateEastX })
                    Flood(_liveRoot, new Vector3(gx + 7.5f, TileTop + 7f, _fenceZ - 3f), 26f, 100f, 8f);
        }

        // ------------------------------------------------------------ the tick

        void TickRoutine(float dt)
        {
            TickBreaks(dt);
            TickGates(dt);
            TickBerthWorks();
            TickWelder(dt);
            TickLights();
        }

        /// <summary>The shift's break. Every half minute or so one man who is not already
        /// off is sent to a free seat by the nearest burn barrel for a few minutes; when
        /// he gets up the seat goes back in the pool. A rota, not a scheduler: the port
        /// never has more than a few men sitting at once and nobody keeps a timetable.</summary>
        void TickBreaks(float dt)
        {
            for (int i = _resting.Count - 1; i >= 0; i--)
            {
                var (man, seat) = _resting[i];
                if (man.Tf != null && man.Resting) continue;
                seat.Taken = false;
                _resting.RemoveAt(i);
            }
            _breakIn -= dt;
            if (_breakIn > 0f || _seats.Count == 0 || _workers.Count == 0) return;
            _breakIn = HarborKit.Range(_rng, 20f, 45f);
            if (_resting.Count >= Mathf.Max(1, _seats.Count / 2)) return;

            var free = _seats[_rng.Next(_seats.Count)];
            if (free.Taken) return;
            // the nearest man off his round who is not already sitting - a docker walks
            // to the drum nearest him, not across the whole port
            HarborWorker best = null;
            float bestD = 70f * 70f;
            for (int i = 0; i < _workers.Count; i++)
            {
                var w = _workers[i];
                if (w.Tf == null || w.Static || w.Resting || w.Frame != null) continue;
                float d = (w.Tf.position - free.Top).sqrMagnitude;
                if (d < bestD) { bestD = d; best = w; }
            }
            if (best != null && best.TakeBreak(free.Top, free.Yaw, HarborKit.Range(_rng, 25f, 65f)))
            {
                free.Taken = true;
                _resting.Add((best, free));
            }
        }

        /// <summary>The gate arms. Each reads the lorries: one coming up to it inside its
        /// notice lifts it, and it drops a few seconds after the last of them is past.
        /// A handful of booms against a handful of lorries - cheap enough to do every
        /// frame and simpler than telling every lorry which gate she is at.</summary>
        void TickGates(float dt)
        {
            if (_booms.Count == 0) return;
            for (int i = 0; i < _booms.Count; i++)
            {
                var boom = _booms[i];
                var at = W(boom.At);
                for (int k = 0; k < _trucks.Count; k++)
                {
                    var tf = _trucks[k].Tf;
                    if (tf == null) continue;
                    if ((tf.position - at).sqrMagnitude < HarborBoom.Notice * HarborBoom.Notice) { boom.Ask(); break; }
                }
                boom.Tick(dt);
            }
        }

        /// <summary>The dust off a grab: while a bulk berth has a ship alongside being
        /// worked, and not otherwise.</summary>
        void TickBerthWorks()
        {
            if (_bulkQuays.Count == 0 || _shipping == null) return;
            for (int i = 0; i < _bulkQuays.Count; i++)
            {
                var (berth, fx) = _bulkQuays[i];
                if (fx == null) continue;
                bool working = berth < _shipping.Berths.Count && _shipping.Berths[berth].Working;
                if (fx.gameObject.activeSelf != working) fx.gameObject.SetActive(working);
            }
        }

        /// <summary>The welder: a run of a few seconds with the arc struck, then a rest
        /// while he moves the work. The light is the arc - it swamps the sparks, so it is
        /// what the eye actually catches from across the yard.</summary>
        void TickWelder(float dt)
        {
            if (_welderFlash == null) return;
            _welderTimer -= dt;
            if (_welderTimer <= 0f)
            {
                _welderOn = _welderOn > 0f ? 0f : HarborKit.Range(_rng, 1.2f, 3.5f);
                _welderTimer = _welderOn > 0f ? _welderOn : HarborKit.Range(_rng, 2.5f, 7f);
                if (_welderSparks != null)
                {
                    if (_welderOn > 0f) _welderSparks.Play(true);
                    else _welderSparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }
            // the arc is not a steady lamp: it stutters
            _welderFlash.intensity = _welderOn > 0f
                ? 3f + Mathf.PerlinNoise(Time.time * 34f, 0.3f) * 9f
                : 0f;
        }

        /// <summary>The floods, by the hour. Only written when the hour has actually
        /// moved: a dozen lights re-set every frame for nothing is a dozen lights.</summary>
        void TickLights()
        {
            if (_floods.Count == 0) return;
            float hour = _clock != null ? _clock.Hour : 12f;
            if (Mathf.Abs(hour - _litHour) < 0.05f) return;
            _litHour = hour;
            float night = DemoSky.Nightness(hour);
            for (int i = 0; i < _floods.Count; i++)
            {
                var l = _floods[i];
                if (l == null) continue;
                float full = l.range > 40f ? 12f : 8f;
                l.intensity = full * night;
                if (l.enabled != night > 0.02f) l.enabled = night > 0.02f;
            }
        }

        // ------------------------------------------------------------ seats

        /// <summary>A thing a man may sit on, offered to the break rota. Called as the
        /// yard is dressed, so the seats ARE the crates that were put down rather than a
        /// second set of points that happen to be near them.</summary>
        void OfferSeat(GameObject prop, Vector3 at, float yaw, Vector3 facing)
        {
            if (prop == null) return;
            var b = HarborKit.PrefabBounds(prop);
            var turn = Quaternion.Euler(0f, yaw, 0f);
            // b.max.y, not the piece's height: DressYard sets these crates down with
            // Prop, which puts the PIVOT on the point, so the seat surface is max.y
            // above it. (Sit would put min.y there and the two must not disagree.)
            var top = at + turn * new Vector3(b.center.x, b.max.y, b.center.z);
            var look = facing - top;
            look.y = 0f;
            _seats.Add(new HarborSeat
            {
                Top = _inner.ToWorld(top),
                Yaw = _inner.yaw + (look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look, Vector3.up).eulerAngles.y : 0f),
            });
        }
    }
}
