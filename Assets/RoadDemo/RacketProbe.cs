using System.Collections.Generic;
using System.Text;
using LivingCity.Outfit;
using LivingCity.Territory;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The racket, driven by nobody. It gives the orders a player gives against a
    /// shopfront - SMASH IT UP at one door, SMASH IT UP at a second, then DEMAND
    /// PROTECTION at a third - and watches what the city does about them, so a report
    /// about the doorstep chain is a measurement rather than a reading of the code.
    ///
    /// It watches the MEN as well as the orders, because the complaint that started it
    /// was about movement: every frame it measures each man's step against the step his
    /// own speed accounts for, and counts the frames he spends with his body switched
    /// off. A teleport is a step nothing accounts for; a body that vanishes and comes
    /// back is a doorway beat that lost him.
    ///
    /// Started and read through gangsters_racket_probe. It orders through the ordinary
    /// gateways - DoorJobs into the outfit's book, the territory command gateway for the
    /// demand - so what it proves is what the surfaces do, not a private path.
    /// </summary>
    public sealed class RacketProbe : MonoBehaviour
    {
        /// <summary>Sim seconds before the first order: the city has to finish standing
        /// up and the crew has to be on its feet.</summary>
        public float startAfter = 4f;

        /// <summary>How long one order is given to come off before the probe moves on.</summary>
        public float patience = 90f;

        /// <summary>A step longer than this multiple of what the man's speed accounts
        /// for is not walking. Generous: a frame hitch stretches an honest step.</summary>
        public float jumpFactor = 4f;

        /// <summary>Below this a step is noise, whatever the speed says.</summary>
        public float jumpFloor = 0.6f;

        /// <summary>Pick doors at least this far from the crew, so the run exercises the
        /// long march, the streaming that comes with it, and a doorstep beat reached
        /// after a real walk rather than from across the pavement.</summary>
        public float atLeastMetres;

        /// <summary>File the second smash WITHOUT waiting for the first to land - the
        /// sequence the complaint came from: one order given, a second door picked while
        /// the men are still at the first. Two live jobs in one lieutenant's book.</summary>
        public bool overlap;

        public bool Finished { get; private set; }
        public string Verdict { get; private set; } = "";

        readonly List<string> _log = new List<string>();
        readonly Dictionary<int, Watch> _watched = new Dictionary<int, Watch>();
        readonly List<TerritoryBusinessId> _doors = new List<TerritoryBusinessId>();

        sealed class Watch
        {
            public string Name;
            public Vector3 Last;
            public bool Had;
            public float WorstStep;
            public float WorstAllowed;
            public float WorstAt;
            public int Jumps;
            public int HiddenFrames;
            public int HiddenSpans;
            public bool WasHidden;
            public bool InVisit;
            public Vector3 HidAt;
            public float WorstReturn;
        }

        int _step;
        int _jumpsSaid;
        float _clock;
        float _stepAt;
        DemoCrews _crews;
        DemoCrews.Unit _ours;

        void Update()
        {
            _clock += Time.deltaTime;
            WatchTheMen();
            if (Finished || _clock < startAfter)
                return;

            if (_crews == null)
            {
                _crews = FindAnyObjectByType<DemoCrews>();
                if (_crews == null)
                    return;
            }

            if (_ours == null || _ours.Wiped)
            {
                _ours = null;
                for (var i = 0; i < _crews.Units.Count; i++)
                {
                    var unit = _crews.Units[i];
                    if (unit == null || unit.Wiped || unit.IsPolice || unit.Faction != 0)
                        continue;
                    _ours = unit;
                    break;
                }
                if (_ours == null)
                {
                    Give("no crew of _ours on the street - nothing to order");
                    return;
                }
                _crews.Select(_ours);
            }

            switch (_step)
            {
                case 0: Pick(); break;
                case 1:
                    Order(0, "first");
                    // The overlap run gives the second order at once, so the second job
                    // queues behind the first in the same lieutenant's book.
                    if (overlap && _step == 2)
                    {
                        Order(1, "second");
                        _step = 2;
                        Say("both orders are in the book: " + BookLine());
                    }
                    break;
                case 2: AwaitWreck(0, "first"); break;
                case 3:
                    if (overlap)
                        Advance();
                    else
                        Order(1, "second");
                    break;
                case 4: AwaitWreck(1, "second"); break;
                case 5: Demand(); break;
                case 6: AwaitAnswer(); break;
                case 7: Burn(); break;
                case 8: AwaitBurn(); break;
                case 9: Demand(); break;
                case 10: AwaitAnswer(); break;
                default: Give(""); break;
            }
        }

        // ------------------------------------------------------------------ the doors

        void Pick()
        {
            var runtime = TerritoryRuntime.Instance;
            if (runtime == null)
            {
                Give("no territory runtime in this scene");
                return;
            }

            var rows = LivingCity.Business.CityBusinesses.All;
            var here = _ours.Position;
            var best = new List<(float D, TerritoryBusinessId Id)>();
            for (var i = 0; i < rows.Count; i++)
            {
                var id = rows[i].Id;
                if (!runtime.IsRacketable(id) || ShopDamage.IsBusinessDamaged(id))
                    continue;
                // Our own paper is not a door the racket has anything to put to - and
                // the nearest shop to a crew standing at the headquarters IS the
                // headquarters, which is how the first run of this probe ended.
                if (LivingCity.Gameplay.DoorHolder.Read(id) == DoorTenure.Ours)
                    continue;
                if (!runtime.TryGetBusinessApproach(id, out var door))
                    continue;
                var away = (door - here).sqrMagnitude;
                if (atLeastMetres > 0f && away < atLeastMetres * atLeastMetres)
                    continue;
                best.Add((away, id));
            }

            best.Sort((a, b) => a.D.CompareTo(b.D));
            _doors.Clear();
            for (var i = 0; i < best.Count && _doors.Count < 3; i++)
                _doors.Add(best[i].Id);

            if (_doors.Count < 3)
            {
                Give("fewer than three shops within reach - nothing to run against");
                return;
            }

            for (var i = 0; i < _doors.Count; i++)
                Say("door " + (i + 1) + ": " + NameOf(_doors[i]) + " at " +
                    Mathf.Sqrt(best[i].D).ToString("0") + " m");
            Advance();
        }

        // ----------------------------------------------------------------- the orders

        void Order(int which, string word)
        {
            var id = _doors[which];
            if (!LivingCity.Gameplay.DoorJobs.TryBuild(
                    id, OrderType.SmashUp, _ours.CrewId, Mathf.Max(1, _ours.Standing()),
                    out var job, out var refusal))
            {
                Say("the " + word + " smash was refused: " + refusal);
                Give("an order the card offers was refused when filed");
                return;
            }

            var outfit = LivingCity.Gameplay.OutfitDirector.Instance;
            if (outfit == null)
            {
                Give("no outfit director - the order book is not open");
                return;
            }

            var result = outfit.IssueOrder(job);
            Say("SMASH IT UP filed on the " + word + " door (" + NameOf(id) + "): " +
                (result.Ok ? "issued" : "REFUSED - " + result.Reason));
            if (!result.Ok)
            {
                Give("the order book refused a smash the card offered");
                return;
            }
            Advance();
        }

        void AwaitWreck(int which, string word)
        {
            var id = _doors[which];
            if (ShopDamage.IsBusinessDamaged(id))
            {
                Say("the " + word + " front went in after " +
                    (_clock - _stepAt).ToString("0.0") + " s · " + StandingOf(id));
                Advance();
                return;
            }

            if (_clock - _stepAt < patience)
                return;

            Say("THE " + word.ToUpperInvariant() + " FRONT NEVER WENT IN - " +
                patience.ToString("0") + " s and the shop is untouched · " +
                BookLine() + " · " + StandingOf(id));
            Give("a filed smash never landed on the shop");
        }

        void Demand()
        {
            var runtime = TerritoryRuntime.Instance;
            if (runtime?.Commands == null)
            {
                Give("no territory command gateway");
                return;
            }

            // The demand goes to the door we WRECKED. That is the whole chain the game
            // is about - ask, lean, wreck, ask again - and the run is worthless if it
            // never puts the last question to the man it spent the violence on.
            var sent = runtime.Commands.Submit(new ApproachBusinessCommand(
                TerritoryCommandNodeId.Crew(_ours.CrewId), _doors[0],
                TerritoryRacketIntent.Demand));
            // What the ledger has ALREADY been asked, so the wait can tell a fresh answer
            // from the standing one. Reading the state alone said "answered" the instant
            // the order was given, because a wrecked shop is Intimidated before anybody
            // opens his mouth.
            _demandsBefore = Asked(_doors[0]);
            Say("DEMAND PROTECTION filed on the WRECKED door (" + NameOf(_doors[0]) + "): " +
                sent.Status + (string.IsNullOrEmpty(sent.Reason) ? "" : " - " + sent.Reason));
            if (sent.Status == TerritoryCommandStatus.Rejected)
            {
                Give("the demand was rejected");
                return;
            }
            Advance();
        }

        void AwaitAnswer()
        {
            var runtime = TerritoryRuntime.Instance;
            var us = new TerritoryGangId(LivingCity.Gangs.GangCatalog.PlayerGangId);
            var state = runtime?.Racket != null
                ? runtime.Racket.StateOf(_doors[0], us)
                : TerritoryProtectionState.Unaffiliated;

            // Nothing counts until the question is actually PUT to him.
            if (Asked(_doors[0]) == _demandsBefore)
            {
                if (_clock - _stepAt < patience)
                    return;
                Say("THE OWNER WAS NEVER ASKED - " + patience.ToString("0") +
                    " s and the men never got the question to him · " +
                    StandingOf(_doors[0]));
                Give("a filed demand never reached the door");
                return;
            }

            if (state == TerritoryProtectionState.Compliant)
            {
                Say("HE PAYS, after " + (_clock - _stepAt).ToString("0.0") + " s · " +
                    StandingOf(_doors[0]));
                Give("");
                return;
            }

            if (state == TerritoryProtectionState.Defiant ||
                state == TerritoryProtectionState.Hesitant ||
                state == TerritoryProtectionState.Intimidated)
            {
                Say("the wrecked owner answered after " + (_clock - _stepAt).ToString("0.0") +
                    " s · " + StandingOf(_doors[0]));

                // A man who is short after ONE act is not a fault - that is the ladder
                // having rungs. The fault is a ladder with no top, so the probe goes up
                // it: burn him out and put the question again. If he still will not pay
                // after the front is in AND alight, with our men in his doorway, there
                // is nothing left to do to a shopkeeper and the game cannot be won at
                // the thing it is about.
                if (!_burned)
                {
                    Advance();
                    return;
                }

                Give("a shop smashed AND burnt, with our men at its door, still would " +
                     "not pay - the ladder has no top");
                return;
            }

            Give("the owner answered something the probe does not have a word for");
        }

        bool _burned;
        int _demandsBefore;

        /// <summary>How many times this door has been asked by us, ever.</summary>
        static int Asked(TerritoryBusinessId id)
        {
            var racket = TerritoryRuntime.Instance?.Racket;
            var us = new TerritoryGangId(LivingCity.Gangs.GangCatalog.PlayerGangId);
            return racket != null && racket.TryGetRelationship(id, us, out var word)
                ? word.Demands
                : 0;
        }

        void Burn()
        {
            var id = _doors[0];
            if (!LivingCity.Gameplay.DoorJobs.TryBuild(
                    id, OrderType.Torch, _ours.CrewId, Mathf.Max(1, _ours.Standing()),
                    out var job, out var refusal))
            {
                Give("TORCH IT was refused on a door the card offers it against: " + refusal);
                return;
            }

            var outfit = LivingCity.Gameplay.OutfitDirector.Instance;
            var result = outfit != null ? outfit.IssueOrder(job) : default;
            Say("TORCH IT filed on the wrecked door (" + NameOf(id) + "): " +
                (result.Ok ? "issued" : "REFUSED - " + result.Reason));
            if (!result.Ok)
            {
                Give("the order book refused a torch the card offered");
                return;
            }
            _burned = true;
            Advance();
        }

        void AwaitBurn()
        {
            // The boards are already up from the smash, so the fire has no NEW visual to
            // wait on: what is waited on is the ORDER closing, which is the moment the
            // racket is told (OutfitDirector.OnJobResolved).
            var outfit = LivingCity.Gameplay.OutfitDirector.Instance;
            if (outfit != null && outfit.Book.Jobs.Count == 0)
            {
                Say("the torch came off after " + (_clock - _stepAt).ToString("0.0") +
                    " s · " + StandingOf(_doors[0]));
                Advance();
                return;
            }

            if (_clock - _stepAt < patience)
                return;

            Say("THE TORCH NEVER CAME OFF - " + BookLine());
            Give("a filed torch never closed");
        }

        // -------------------------------------------------------------------- the men

        void WatchTheMen()
        {
            if (_crews == null)
                return;

            for (var i = 0; i < _crews.Units.Count; i++)
            {
                var unit = _crews.Units[i];
                if (unit == null || unit.Faction != 0)
                    continue;

                foreach (var man in unit.All())
                {
                    if (man == null || man.Tf == null)
                        continue;

                    if (!_watched.TryGetValue(man.CharacterId, out var watch))
                    {
                        watch = new Watch { Name = man.Tf.name };
                        _watched.Add(man.CharacterId, watch);
                    }

                    // Where the doorway beat CAUGHT him: a man who starts entering from
                    // across the street is the complaint, and the number is the proof.
                    var phase = DoorBeat.PhaseOf(man);
                    if (phase != DoorBeat.VisitPhase.None && !watch.InVisit)
                    {
                        watch.InVisit = true;
                        var door = DoorBeat.DoorOf(man);
                        Say("doorway beat began on " + watch.Name + " at " +
                            Vector3.Distance(man.Tf.position, door).ToString("0.0") +
                            " m from the door");
                    }
                    else if (phase == DoorBeat.VisitPhase.None)
                    {
                        watch.InVisit = false;
                    }

                    var hidden = !man.Tf.gameObject.activeInHierarchy;
                    if (hidden)
                    {
                        watch.HiddenFrames++;
                        if (!watch.WasHidden)
                        {
                            watch.HiddenSpans++;
                            watch.HidAt = watch.Had ? watch.Last : man.Tf.position;
                        }
                        watch.WasHidden = true;
                        // A hidden man is INSIDE: where he is put down is the doorway
                        // beat's business, and the step back out is not a teleport.
                        watch.Had = false;
                        continue;
                    }

                    // A body that went off in one place and came back in another is the
                    // teleport a player actually SEES, and it is invisible to a
                    // frame-by-frame step test because there are no frames in between.
                    if (watch.WasHidden)
                    {
                        var moved = Vector3.Distance(watch.HidAt, man.Tf.position);
                        if (moved > watch.WorstReturn)
                            watch.WorstReturn = moved;
                        if (moved > 3f && _jumpsSaid < 8)
                        {
                            _jumpsSaid++;
                            Say("VANISHED AND CAME BACK " + moved.ToString("0.0") +
                                " m away: " + watch.Name + " from " + Where(watch.HidAt) +
                                " to " + Where(man.Tf.position));
                        }
                    }

                    watch.WasHidden = false;
                    var now = man.Tf.position;
                    if (watch.Had && Time.deltaTime > 0f && !man.Dead)
                    {
                        var stepped = (now - watch.Last).magnitude;
                        // What his own speed accounts for this frame, with room for a
                        // hitch. A man in a car is not walking and is not judged here.
                        var allowed = Mathf.Max(
                            jumpFloor, man.Speed * Time.deltaTime * jumpFactor);
                        if (stepped > allowed)
                        {
                            watch.Jumps++;
                            if (stepped > watch.WorstStep)
                            {
                                watch.WorstStep = stepped;
                                watch.WorstAllowed = allowed;
                                watch.WorstAt = _clock;
                            }
                            // The first few are written out in full: a teleport is only
                            // diagnosable with the phase the man was in when it happened.
                            if (_jumpsSaid < 8)
                            {
                                _jumpsSaid++;
                                Say("JUMP " + watch.Name + " " + stepped.ToString("0.0") +
                                    " m in one frame (speed accounted for " +
                                    allowed.ToString("0.00") + " m) · doorway phase " +
                                    DoorBeat.PhaseOf(man) + " · from " + Where(watch.Last) +
                                    " to " + Where(now));
                            }
                        }
                    }

                    watch.Last = now;
                    watch.Had = true;
                }
            }
        }

        // ------------------------------------------------------------------- the word

        void Advance()
        {
            _step++;
            _stepAt = _clock;
        }

        void Say(string line) => _log.Add("[" + _clock.ToString("0.0") + "s] " + line);

        void Give(string fault)
        {
            Finished = true;
            var sb = new StringBuilder();
            for (var i = 0; i < _log.Count; i++)
                sb.AppendLine(_log[i]);

            sb.AppendLine("--- the men");
            foreach (var pair in _watched)
            {
                var watch = pair.Value;
                sb.AppendLine("  " + watch.Name +
                    ": jumps=" + watch.Jumps +
                    (watch.Jumps > 0
                        ? " worst=" + watch.WorstStep.ToString("0.00") + " m in one frame " +
                          "(his speed accounted for " + watch.WorstAllowed.ToString("0.00") +
                          " m, at " + watch.WorstAt.ToString("0.0") + "s)"
                        : "") +
                    " hidden=" + watch.HiddenFrames + " frames in " + watch.HiddenSpans +
                    (watch.HiddenSpans == 1 ? " span" : " spans") +
                    (watch.WorstReturn > 0.01f
                        ? " · came back " + watch.WorstReturn.ToString("0.0") + " m off"
                        : ""));
            }

            sb.AppendLine("--- the wire");
            var racket = TerritoryRuntime.Instance?.Racket;
            if (racket != null)
            {
                var dispatches = racket.Dispatches;
                for (var i = dispatches.Count - 1; i >= 0 && i > dispatches.Count - 9; i--)
                    sb.AppendLine("  " +
                        TerritoryStandingVocabulary.Default.Describe(
                            dispatches[i].News, NameOf(dispatches[i].BusinessId)));
                if (dispatches.Count == 0)
                    sb.AppendLine("  (the wire is empty)");
            }

            sb.AppendLine("--- the book");
            sb.AppendLine("  " + BookLine());

            Verdict = (fault.Length > 0 ? "FAULT: " + fault + "\n" : "no fault\n") + sb;
            enabled = false;
        }

        string BookLine()
        {
            var outfit = LivingCity.Gameplay.OutfitDirector.Instance;
            if (outfit == null)
                return "no order book";
            if (outfit.Book.Jobs.Count == 0)
                return "the book is empty";
            var sb = new StringBuilder();
            foreach (var job in outfit.Book.Jobs)
                sb.Append("#").Append(job.Id).Append(" ").Append(job.Type)
                  .Append(" ").Append(job.Stage)
                  .Append(" work=").Append(job.WorkHoursLeft.ToString("0.0"))
                  .Append(" travel=").Append(job.TravelHoursLeft.ToString("0.0"))
                  .Append(job.StreetOutcome.HasValue
                      ? " street=" + job.StreetOutcome.Value : " street=open")
                  .Append("; ");
            return sb.ToString();
        }

        string StandingOf(TerritoryBusinessId id)
        {
            var racket = TerritoryRuntime.Instance?.Racket;
            if (racket == null)
                return "no racket";
            var us = new TerritoryGangId(LivingCity.Gangs.GangCatalog.PlayerGangId);
            var state = racket.StateOf(id, us);
            var line = NameOf(id) + " stands " +
                       TerritoryStandingVocabulary.Default.Describe(state);
            if (TerritoryRuntime.Instance.TryExplainDemand(id, us, out var terms))
                line += " · asked now: " + terms.Verdict + " at " +
                        terms.Score.ToString("0.0") + " of THIS MAN'S " +
                        terms.AcceptAt.ToString("0.0") +
                        " (table bar " + racket.Config.AcceptAt.ToString("0") + ")";
            return line;
        }

        static string Where(Vector3 point) =>
            "(" + point.x.ToString("0") + ", " + point.z.ToString("0") + ")";

        static string NameOf(TerritoryBusinessId id)
        {
            var rows = LivingCity.Business.CityBusinesses.All;
            for (var i = 0; i < rows.Count; i++)
                if (rows[i].Id == id && !string.IsNullOrWhiteSpace(rows[i].Name))
                    return rows[i].Name;
            return id.Value;
        }
    }
}
