using System;
using System.Collections.Generic;
using LivingCity.Territory;

namespace LivingCity.Tests
{
    /// <summary>
    /// The Phase-1 scenarios (GAN-135 / TEST-001..009): the whole chain run end to end
    /// over the REAL ledgers - men arrive, a street learns to fear a name, its shops start
    /// paying, and only then does the street answer to somebody. Then the men leave, a
    /// rival leans on the same street, and it all comes apart the same way it went
    /// together.
    ///
    /// Nothing here is a mock: it is the same Presence, Fear, racket, Power and control
    /// code the city runs, wired the way the runtime wires it. What it does not have is a
    /// city - the physical legs (marching, doors, gunfire) are proven in Play by the
    /// epics' own audits, and are deliberately not re-staged here.
    /// </summary>
    public static class ScenarioTests
    {
        /// <summary>Every scenario, for the capstone run and the epic's own suite.</summary>
        public static List<string> Run()
        {
            var failures = new List<string>();
            failures.AddRange(Takeover());
            failures.AddRange(Withdrawal());
            failures.AddRange(Contest());
            failures.AddRange(Loss());
            failures.AddRange(Responsibility());
            failures.AddRange(BossCapacity());
            failures.AddRange(LieutenantLoad());
            failures.AddRange(UiAuthority());
            return failures;
        }

        /// <summary>
        /// What the slice FEELS like, measured rather than argued about. These are tuning
        /// notes, not failures: a street that needs a dozen threats before it folds is a
        /// balance question for the economy epic, and the run reports it beside its
        /// verdict instead of failing over it.
        /// </summary>
        public static List<string> BalanceNotes()
        {
            var notes = new List<string>();
            var rig = new Rig();

            rig.Men(0, 5);
            rig.Tick();
            notes.Add("five men standing on a street are worth " +
                      rig.PresenceOf(0).ToString("0.0") + " Presence (" +
                      rig.Score(0).ToString("0.0") + " toward the street).");

            var threats = 0;
            while (rig.ComplianceOf(0) <= 0f && threats < 40)
            {
                rig.Threaten(0, 0);
                rig.Tick();
                rig.Demand(0, 0);
                threats++;
            }
            notes.Add(threats >= 40
                ? "a shop never came round in forty threats."
                : "a shop came round after " + threats + " threats with five men outside.");

            rig.Demand(0, 1);
            var ticks = 0;
            while (rig.State != TerritoryControlState.Controlled &&
                   rig.State != TerritoryControlState.Dominated && ticks < 40)
            {
                rig.Tick();
                ticks++;
            }
            notes.Add(ticks >= 40
                ? "the street never came to us at all."
                : "the street read as held " + (ticks * 0.25).ToString("0.00") +
                  " game hours after the shops came round.");

            rig.NoMen();
            var fade = 0;
            while (rig.PresenceOf(0) > 1f && fade < 400)
            {
                rig.Advance(1);
                fade++;
            }
            notes.Add("with the men gone, the street kept them for " + fade +
                      " game hours before Presence fell under 1.");

            return notes;
        }

        // ------------------------------------------------------------------- TEST-001

        /// <summary>
        /// A neutral street, taken the only way there is: send men, be seen, lean on the
        /// shops until they pay, and let the reading follow. No capture, no claim, no
        /// button that hands anything over.
        /// </summary>
        public static List<string> Takeover()
        {
            var failures = new List<string>();
            var rig = new Rig();

            if (rig.State != TerritoryControlState.Unknown &&
                rig.State != TerritoryControlState.Uncontrolled)
                failures.Add("TEST-001: the street did not start out nobody's.");

            // 1. The men arrive and stand there.
            rig.Men(0, 5);
            rig.Tick();
            if (rig.PresenceOf(0) <= 0f)
                failures.Add("TEST-001: men standing on the street raised no Presence.");
            if (rig.State == TerritoryControlState.Controlled ||
                rig.State == TerritoryControlState.Dominated)
                failures.Add("TEST-001: men alone took the street.");

            // 2. They lean on the shops. Each threat is a fear act at that door.
            for (var i = 0; i < 6; i++)
            {
                rig.Threaten(0, 0);
                rig.Threaten(0, 1);
            }
            rig.Tick();
            if (rig.FearOf(0) <= 0f)
                failures.Add("TEST-001: leaning on the shops frightened nobody.");

            // 3. And the shops start paying.
            rig.Demand(0, 0);
            rig.Demand(0, 1);
            if (rig.ComplianceOf(0) <= 0f)
                failures.Add("TEST-001: no shop came round after all that.");

            // 4. The reading follows - on its own, on the tick.
            rig.Tick();
            rig.Tick();
            if (rig.State != TerritoryControlState.Controlled &&
                rig.State != TerritoryControlState.Dominated)
                failures.Add("TEST-001: the street never came to us: " + rig.State +
                             " (score " + rig.Score(0).ToString("0.0") + ")");
            if (rig.Leader.Value != 0)
                failures.Add("TEST-001: the street answers to somebody else.");
            if (rig.Changes < 1)
                failures.Add("TEST-001: the street changed hands without saying so.");

            return failures;
        }

        // ------------------------------------------------------------------- TEST-002

        /// <summary>
        /// Take the men away and the street does not forget overnight - but it does not
        /// stay ours for nothing either. Presence fades, and what is left is what we
        /// earned: the fear and the shops.
        /// </summary>
        public static List<string> Withdrawal()
        {
            var failures = new List<string>();
            var rig = Held();

            var beforePresence = rig.PresenceOf(0);
            rig.NoMen();
            rig.Tick();

            if (!(rig.PresenceOf(0) < beforePresence))
                failures.Add("TEST-002: the men went home and Presence did not move.");
            if (rig.PresenceOf(0) <= 0f)
                failures.Add("TEST-002: the street forgot them the moment they left.");
            if (rig.FearOf(0) <= 0f)
                failures.Add("TEST-002: withdrawing the men wiped out the fear as well.");

            // Days pass with nobody on the street: what is left is memory, and it fades.
            rig.Advance(48);
            if (rig.PresenceOf(0) > 1f)
                failures.Add("TEST-002: two days empty and the street still has our men.");
            if (rig.State == TerritoryControlState.Dominated)
                failures.Add("TEST-002: an empty street is still held outright.");

            return failures;
        }

        // ------------------------------------------------------------------- TEST-003

        /// <summary>
        /// A rival walks onto the same street with the same rules, gets close enough, and
        /// the street becomes a fight - with nobody declaring one.
        /// </summary>
        public static List<string> Contest()
        {
            var failures = new List<string>();
            var rig = new Rig();

            // Two houses working the same street the same way: their own men at the same
            // doors, their own names earned there, and neither of them yet paid by a shop.
            // Nobody declares anything - the street simply stops belonging to one of them.
            rig.Men(0, 5);
            rig.Men(7, 5);
            for (var i = 0; i < 6; i++)
            {
                rig.Threaten(0, 0);
                rig.Threaten(7, 1);
            }
            rig.Tick();
            rig.Tick();

            if (rig.State != TerritoryControlState.Contested)
                failures.Add("TEST-003: two houses on one street is not a fight: " +
                             rig.State + " (" + rig.Score(0).ToString("0.0") + " vs " +
                             rig.Score(7).ToString("0.0") + ")");
            if (rig.Contested < 1)
                failures.Add("TEST-003: the fight was never announced.");
            if (Math.Abs(rig.Score(0) - rig.Score(7)) > rig.ContestedMargin)
                failures.Add("TEST-003: the two houses are not really close.");

            // And one of them pulling ahead ends it - by being worth more, not by winning
            // anything that was declared.
            for (var i = 0; i < 10; i++)
                rig.Threaten(0, 1);
            rig.Demand(0, 0);
            rig.Demand(0, 1);
            rig.Tick();
            rig.Tick();
            if (rig.State == TerritoryControlState.Contested)
                failures.Add("TEST-003: the fight never ended even when one house won it.");
            if (rig.Leader.Value != 0)
                failures.Add("TEST-003: the house that pulled ahead does not hold the street.");

            return failures;
        }

        // ------------------------------------------------------------------- TEST-004

        /// <summary>
        /// Ground goes the way it came. The men leave, the shops change hands, and the
        /// street walks back down the ladder - and the house that held it is told.
        /// </summary>
        public static List<string> Loss()
        {
            var failures = new List<string>();
            var rig = Held();

            rig.NoMen();
            // The rival takes the shops off us and earns his own name here.
            rig.Men(7, 6);
            for (var i = 0; i < 8; i++)
            {
                rig.Threaten(7, 0);
                rig.Threaten(7, 1);
            }
            rig.Tick();
            rig.Demand(7, 0);
            rig.Demand(7, 1);
            rig.Advance(72);
            rig.Tick();
            rig.Tick();

            if (rig.ComplianceOf(0) > 0f)
                failures.Add("TEST-004: the shops are still paying a house that left.");
            if (rig.Leader.Value == 0)
                failures.Add("TEST-004: the street still answers to us: " + rig.State);
            if (rig.Lost < 1)
                failures.Add("TEST-004: losing the street was never announced.");

            return failures;
        }

        // ------------------------------------------------------------------- TEST-005/006

        /// <summary>
        /// Paperwork is not ground. A block can be somebody's to answer for and still be
        /// nobody's street: responsibility writes no Presence, no fear and no compliance,
        /// and moves no reading.
        /// </summary>
        public static List<string> Responsibility()
        {
            var failures = new List<string>();
            var rig = new Rig();

            var definition = new TerritoryBlockDefinition(
                Rig.Block, 12, TerritoryIdentity.CoreNeighborhood(1987, 1), "Downtown",
                "Downtown Block 01", new TerritoryBounds(0f, 0f, 80f, 60f),
                "CoreTerritoryPlan.StableId");
            var state = new TerritorySimulationState(new[] { definition });
            var version = state.Version;

            state.AssignResponsibility(Rig.Block, new TerritoryResponsibility(
                new TerritoryGangId(0), new TerritoryCharacterId(1), default,
                TerritoryCommandNodeId.Boss(1)));

            if (state.Version == version)
                failures.Add("TEST-005: the assignment was not recorded at all.");
            if (state.SignalsOf(Rig.Block).Control != TerritoryControlState.Unknown)
                failures.Add("TEST-005: paperwork moved the block's control.");

            rig.Tick();
            if (rig.PresenceOf(0) != 0f || rig.FearOf(0) != 0f)
                failures.Add("TEST-005: a block on paper produced Presence or fear.");
            if (rig.State != TerritoryControlState.Uncontrolled &&
                rig.State != TerritoryControlState.Unknown)
                failures.Add("TEST-006: a block nobody stands on reads as somebody's.");

            return failures;
        }

        /// <summary>
        /// TEST-005 as the ticket writes it: a Boss with men reporting to him directly
        /// and ground on his paper, taking blocks up to the ceiling his own Leadership
        /// sets - and the NEXT one refused, with a reason that names him.
        ///
        /// Separate from <see cref="Responsibility"/> on purpose. That one asserts that
        /// paperwork is not ground; this one asserts what the paperwork itself will and
        /// will not take, which is a different promise and was covered only inside the
        /// organization suite.
        /// </summary>
        public static List<string> BossCapacity()
        {
            var failures = new List<string>();
            var roster = Personnel.RosterSeeder.Generate(4);
            var boss = roster.FindBoss();
            if (boss == null)
            {
                failures.Add("TEST-005: the seed dealt no Boss.");
                return failures;
            }

            var rng = new Random(4);
            var query = new Personnel.OrganizationQuery(roster);

            // Men who answer to him and nobody else. A new man LANDS on the Boss's own
            // branch - that is where an unposted man lives - so the assertion is that
            // he is there and on the Boss's count, not that he can be moved there.
            var before = query.CapacityOf(boss.Id).Manpower.Current;
            for (var i = 0; i < 3; i++)
            {
                var hood = Personnel.RosterSeeder.Recruit(roster, rng);
                if (roster.AssignmentOf(hood.Id).Kind != Personnel.AssignmentKind.Pool)
                    failures.Add("TEST-005: a new man did not land under the Boss.");
            }

            var direct = query.CapacityOf(boss.Id).Manpower.Current - before;
            if (direct != 3)
                failures.Add("TEST-005: three men reporting to him directly count as " +
                             direct + " on his books.");

            // Ground on his paper, up to the ceiling and not one block past it.
            var cap = Personnel.Command.BlockCap(boss, roster.Organization.Limits);
            if (cap <= 0)
            {
                failures.Add("TEST-005: the Boss can carry no ground at all.");
                return failures;
            }

            for (var i = 0; i < cap; i++)
            {
                var blockId = new TerritoryBlockId("core:test:boss:block:" + i);
                var taken = Personnel.RosterOps.AssignBlockResponsibility(
                    roster, blockId, boss.Id, true);
                if (!taken.Ok)
                    failures.Add("TEST-005: block " + (i + 1) + " of " + cap +
                                 " was refused below the cap: " + taken.Reason);
            }

            var over = new TerritoryBlockId("core:test:boss:block:over");
            var refused = Personnel.RosterOps.AssignBlockResponsibility(
                roster, over, boss.Id, true);
            if (refused.Ok)
                failures.Add("TEST-005: the Boss took block " + (cap + 1) +
                             " past his own ceiling of " + cap + ".");
            else if (string.IsNullOrEmpty(refused.Reason) ||
                     refused.Reason.IndexOf(boss.FullName, StringComparison.Ordinal) < 0)
                failures.Add("TEST-005: the refusal does not name the man refusing: " +
                             "\"" + refused.Reason + "\"" + ".");

            var blocks = query.CapacityOf(boss.Id).Blocks;
            if (blocks.Current != cap || blocks.Maximum != cap || blocks.IsOverCapacity)
                failures.Add("TEST-005: his paper reads " + blocks.Current + " / " +
                             blocks.Maximum + " after the refusal, not " + cap + " / " +
                             cap + ".");

            return failures;
        }

        /// <summary>
        /// TEST-006 as the ticket writes it: a lieutenant loaded to the config's own
        /// numbers - fifty men and three blocks - and the overload VISIBLE rather than
        /// paid for by a magic penalty. Nothing here asserts a modifier, because the
        /// design has none: a branch simply refuses the next man, and the reading says
        /// so plainly.
        /// </summary>
        public static List<string> LieutenantLoad()
        {
            var failures = new List<string>();
            var roster = Personnel.RosterSeeder.Generate(9);
            Personnel.RosterOps.ConfigureOrganization(
                roster, new Personnel.OrganizationLimits(70, 4, 50, 3));
            if (roster.Crews.Count == 0)
            {
                failures.Add("TEST-006: the seed dealt no branch to load.");
                return failures;
            }

            var crew = roster.Crews[0];
            var lieutenant = roster.Find(crew.LieutenantId);
            var query = new Personnel.OrganizationQuery(roster);
            var rng = new Random(9);

            var manpower = query.CapacityOf(crew.LieutenantId).Manpower;
            if (manpower.Current > manpower.Maximum)
            {
                failures.Add("TEST-006: the seed dealt a branch already past its cap.");
                return failures;
            }

            // Fill him to the cap his own Leadership allows of the config's fifty. Every
            // loop is guarded: a cap that became hard under a rule change must fail the
            // run, never hang it.
            var guard = manpower.Maximum - manpower.Current + 4;
            while (guard-- > 0 &&
                   query.CapacityOf(crew.LieutenantId).Manpower.Current <
                   query.CapacityOf(crew.LieutenantId).Manpower.Maximum)
            {
                var hood = Personnel.RosterSeeder.Recruit(roster, rng);
                if (!Personnel.RosterOps.AssignToCrew(roster, hood.Id, crew.Id).Ok)
                {
                    failures.Add("TEST-006: the branch refused a man below its cap.");
                    return failures;
                }
            }

            var full = query.CapacityOf(crew.LieutenantId).Manpower;
            if (full.Current != full.Maximum)
                failures.Add("TEST-006: the branch never reached its own cap (" +
                             full.Current + " / " + full.Maximum + ").");

            var extra = Personnel.RosterSeeder.Recruit(roster, rng);
            var refusedMan = Personnel.RosterOps.AssignToCrew(roster, extra.Id, crew.Id);
            if (refusedMan.Ok)
                failures.Add("TEST-006: he took a man past his manpower cap.");
            else if (lieutenant != null &&
                     (string.IsNullOrEmpty(refusedMan.Reason) ||
                      refusedMan.Reason.IndexOf(
                          lieutenant.FullName, StringComparison.Ordinal) < 0))
                failures.Add("TEST-006: the refusal does not name the lieutenant: " +
                             "\"" + refusedMan.Reason + "\"" + ".");

            // Ground the same way: to the ceiling, then no further.
            var blockCap = Personnel.Command.BlockCap(
                lieutenant, roster.Organization.Limits);
            for (var i = 0; i < blockCap; i++)
                if (!Personnel.RosterOps.AssignBlockResponsibility(
                        roster, new TerritoryBlockId("core:test:lt:block:" + i),
                        crew.LieutenantId, true).Ok)
                    failures.Add("TEST-006: block " + (i + 1) + " of " + blockCap +
                                 " was refused below the cap.");
            if (Personnel.RosterOps.AssignBlockResponsibility(
                    roster, new TerritoryBlockId("core:test:lt:block:over"),
                    crew.LieutenantId, true).Ok)
                failures.Add("TEST-006: he took ground past his block cap of " +
                             blockCap + ".");

            // The load is READ, not paid for: at the cap the reading is full and not
            // over, and nothing of his has been quietly moved to make it so.
            var reading = query.CapacityOf(crew.LieutenantId);
            if (reading.Manpower.IsOverCapacity || reading.Blocks.IsOverCapacity)
                failures.Add("TEST-006: a branch at its cap reads as OVER it.");
            if (reading.Blocks.Current != blockCap || reading.Blocks.Maximum != blockCap)
                failures.Add("TEST-006: his paper reads " + reading.Blocks.Current +
                             " / " + reading.Blocks.Maximum + ", not " + blockCap +
                             " / " + blockCap + ".");

            return failures;
        }

        // ------------------------------------------------------------------- TEST-007

        /// <summary>
        /// Nothing the player can see is anything the player can write. Every page the
        /// game shows is words with no setters behind them, and reading them changes
        /// nothing.
        /// </summary>
        public static List<string> UiAuthority()
        {
            var failures = new List<string>();

            foreach (var type in new[]
                     {
                         typeof(TerritoryBlockPresentation),
                         typeof(TerritoryBusinessPresentation),
                     })
            {
                foreach (var property in type.GetProperties())
                    if (property.CanWrite)
                        failures.Add("TEST-007: " + type.Name + "." + property.Name +
                                     " can be written by a view.");
                foreach (var field in type.GetFields())
                    if (!field.IsInitOnly && !field.IsLiteral)
                        failures.Add("TEST-007: " + type.Name + "." + field.Name +
                                     " is a public mutable field.");
            }

            // Reading the player's page cannot move the store behind it.
            var definition = new TerritoryBlockDefinition(
                Rig.Block, 12, TerritoryIdentity.CoreNeighborhood(1987, 1), "Downtown",
                "Downtown Block 01", new TerritoryBounds(0f, 0f, 80f, 60f),
                "CoreTerritoryPlan.StableId");
            var state = new TerritorySimulationState(new[] { definition });
            state.SetSignals(Rig.Block, new TerritoryBlockSignals(
                localFear: 40f, control: TerritoryControlState.Controlled,
                gangs: new[] { new TerritoryGangSignals(new TerritoryGangId(0), 50f, 20f, 40f) }));

            var truth = new TerritoryTruthQuery(state);
            var player = new TerritoryPlayerQuery(
                truth, new TerritoryGangId(0), TerritoryPresentationProfile.Default);
            var before = state.Version;
            for (var i = 0; i < 5; i++)
            {
                player.TryGetBlock(Rig.Block, out _);
                truth.TryGetBlock(Rig.Block, out _);
            }

            if (state.Version != before)
                failures.Add("TEST-007: reading the block moved the simulation.");

            return failures;
        }

        // ------------------------------------------------------------------- fixtures

        /// <summary>A street we have taken the long way, for the scenarios that start held.</summary>
        static Rig Held()
        {
            var rig = new Rig();
            rig.Men(0, 5);
            for (var i = 0; i < 6; i++)
            {
                rig.Threaten(0, 0);
                rig.Threaten(0, 1);
            }
            rig.Tick();
            rig.Demand(0, 0);
            rig.Demand(0, 1);
            rig.Tick();
            rig.Tick();
            return rig;
        }

        /// <summary>
        /// One street, its two shops, and the four ledgers the city runs - wired the way
        /// TerritoryRuntime wires them, so a scenario exercises the real thing.
        /// </summary>
        sealed class Rig
        {
            public static readonly TerritoryBlockId Block =
                new TerritoryBlockId("core:1987:1:2:3:4:5:res");

            static readonly TerritoryBusinessId[] Shops =
            {
                new TerritoryBusinessId("biz:corner-shop"),
                new TerritoryBusinessId("biz:bar"),
            };

            readonly TerritoryPresenceLedger presence = new TerritoryPresenceLedger();
            readonly TerritoryFearLedger fear = new TerritoryFearLedger();
            readonly TerritoryRacketLedger racket = new TerritoryRacketLedger();
            readonly TerritoryControlLedger control = new TerritoryControlLedger();
            readonly TerritoryPowerLedger power = new TerritoryPowerLedger();
            readonly List<TerritoryBusinessId> shops = new List<TerritoryBusinessId>(Shops);
            readonly List<TerritoryControlScore> scores = new List<TerritoryControlScore>();
            readonly List<TerritoryGangId> gangs = new List<TerritoryGangId>();
            readonly Dictionary<int, int> standing = new Dictionary<int, int>();

            double hour;

            public int Changes { get; private set; }
            public int Contested { get; private set; }
            public int Lost { get; private set; }

            public TerritoryControlState State => control.StateOf(Block);
            public float ContestedMargin => control.Config.ContestedExitMargin;
            public TerritoryGangId Leader => control.LeaderOf(Block);

            public float PresenceOf(int gang) => presence.TotalOf(Block, Gang(gang));
            public float FearOf(int gang) => fear.FearOf(Block, Gang(gang), hour);
            public float ComplianceOf(int gang) => racket.ComplianceOf(shops, Gang(gang));
            public float Score(int gang) => control.Config.Score(Inputs(gang)).Total;

            /// <summary>This family now has this many men standing on the street.</summary>
            public void Men(int gang, int count)
            {
                standing[gang] = count;
                Sample();
            }

            public void NoMen()
            {
                standing.Clear();
                Sample();
            }

            /// <summary>A threat at a shop's door: a fear act, filed the way the runtime
            /// files one, and the shop is marked as leant on.</summary>
            public void Threaten(int gang, int shop)
            {
                fear.Record(new TerritoryFearEvent(
                    Gang(gang), Block, TerritoryFearCategory.Threat, 1f,
                    TerritoryFearVisibility.Seen, hour, Shops[shop]));
                racket.Threaten(Shops[shop], Gang(gang), hour);
            }

            /// <summary>The demand, answered by the same evaluation the city uses.</summary>
            public void Demand(int gang, int shop)
            {
                var asker = Gang(gang);
                var rival = 0f;
                for (var i = 0; i < gangs.Count; i++)
                {
                    if (gangs[i] == asker)
                        continue;
                    rival = Math.Max(rival, Standing(gangs[i]));
                }

                var protectorStanding = 0f;
                var mine = false;
                if (racket.TryGetProtector(Shops[shop], out var protector))
                {
                    mine = protector == asker;
                    if (!mine)
                        protectorStanding = Standing(protector);
                }

                racket.Demand(Shops[shop], asker, new TerritoryComplianceInputs(
                    fear.BusinessFear(Block, Shops[shop], asker, hour),
                    presence.TotalOf(Block, asker),
                    fear.BlockFear(Block, hour),
                    rival, protectorStanding, mine), hour, out _);
            }

            /// <summary>One turn of every wheel, in the order the runtime turns them.</summary>
            public void Tick(double hours = 0.25)
            {
                hour += hours;
                Sample();
                fear.Evaluate(hour);
                Read();
            }

            /// <summary>Time with nobody doing anything - the fade the design plan wants
            /// measured in days, not frames.</summary>
            public void Advance(double hours)
            {
                var step = 0.25;
                for (var t = 0.0; t < hours; t += step)
                {
                    hour += step;
                    Sample();
                    presence.DecayResidual(step);
                    fear.Evaluate(hour);
                    Read();
                }
            }

            void Sample()
            {
                presence.BeginSample();
                var character = 1;
                foreach (var pair in standing)
                    for (var i = 0; i < pair.Value; i++)
                        presence.Contribute(Block, new TerritoryActorObservation(
                            new TerritoryCharacterId(character++),
                            Gang(pair.Key),
                            TerritoryCommandNodeId.Crew(pair.Key),
                            "man", "gang", false,
                            TerritoryRank.Hood, TerritoryActorActivity.Stationed));
                presence.CommitSample(1.0 / 60.0);
            }

            void Read()
            {
                CollectGangs();
                scores.Clear();
                for (var i = 0; i < gangs.Count; i++)
                    scores.Add(control.Config.Score(Inputs(gangs[i].Value)));

                if (!control.Read(Block, scores, hour, out var change))
                    return;

                Changes++;
                if (change.BecameContested)
                    Contested++;
                if (change.LostControl)
                    Lost++;
            }

            void CollectGangs()
            {
                gangs.Clear();
                var seenPresence = new List<TerritoryGangPresence>();
                presence.CollectGangs(Block, seenPresence);
                for (var i = 0; i < seenPresence.Count; i++)
                    if (!gangs.Contains(seenPresence[i].GangId))
                        gangs.Add(seenPresence[i].GangId);

                var seenFear = new List<TerritoryGangValue>();
                fear.CollectGangs(Block, hour, seenFear);
                for (var i = 0; i < seenFear.Count; i++)
                    if (!gangs.Contains(seenFear[i].GangId))
                        gangs.Add(seenFear[i].GangId);

                var seenRacket = new List<TerritoryGangId>();
                racket.CollectGangsOn(shops, seenRacket);
                for (var i = 0; i < seenRacket.Count; i++)
                    if (!gangs.Contains(seenRacket[i]))
                        gangs.Add(seenRacket[i]);
            }

            TerritoryControlInputs Inputs(int gang) => Inputs(Gang(gang));

            TerritoryControlInputs Inputs(TerritoryGangId gang) =>
                new TerritoryControlInputs(
                    gang,
                    presence.TotalOf(Block, gang),
                    fear.FearOf(Block, gang, hour),
                    racket.ComplianceOf(shops, gang),
                    power.Coefficient(Block, gang, hour));

            float Standing(TerritoryGangId gang) =>
                0.5f * fear.FearOf(Block, gang, hour) + 0.5f * presence.TotalOf(Block, gang);

            static TerritoryGangId Gang(int id) => new TerritoryGangId(id);
        }
    }
}
