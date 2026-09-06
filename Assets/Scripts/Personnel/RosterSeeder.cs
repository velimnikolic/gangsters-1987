using System.Collections.Generic;
using LivingCity.Generation;
using LivingCity.Gangs;

namespace LivingCity.Personnel
{
    /// <summary>
    /// The outfit on day one: DON SALVATORE ALONE. One man, one car, and nobody else on
    /// the books - no lieutenant, no crew, no man on the front desk. Every name after
    /// his is one the player went out and got (RECRUIT off the corner, or a name out of
    /// the morning classified), which is what makes the first hire a decision instead of
    /// a formality.
    ///
    /// <see cref="GenerateStaffed"/> is the six-man fixture the pure tests measure and
    /// the harnesses stand up; it is NOT what a campaign opens with.
    ///
    /// Deterministic for a given seed, on its own SeedOffsets band so retuning the roster
    /// can never re-lay the city. The draw order is FIXED and documented inline - insert a
    /// draw mid-sequence and every fixture's six reshuffles.
    ///
    /// GangsterNames assigns an English street name after the skills are known, on
    /// its own stream. The story Don keeps his authored identity.
    /// </summary>
    public static class RosterSeeder
    {
        /// <summary>The campaign's opening year and the length of its year, named here
        /// rather than read from Outfit.Campaign so the Personnel core stays free of
        /// that layer - the same discipline <see cref="OrderResolutionRecruitFloor"/>
        /// keeps. The two must agree, and SkillFoundationTests asserts that they do.</summary>
        public const int CalendarStartYear = 1987;

        public const int CalendarDaysPerYear = 364;

        /// <summary>Don Salvatore's age in the opening year. Scripted like the rest of
        /// him: a Don in his early fifties, old enough that the years have started
        /// taking his hands back and young enough to still be holding the outfit.</summary>
        public const int BossAge = 52;

        /// <summary>Men the STAFFED FIXTURE deals besides the Boss. The campaign opens
        /// with none of them - see <see cref="Generate"/>.</summary>
        public const int FixtureStaffCount = 6;
        public const int FixtureMemberCount = FixtureStaffCount + 1;

        /// <summary>The Don's Character id in the fixture, where he is added after the
        /// six. In the opening books he is the first and only man dealt, so there he
        /// carries id 0 - which is why this constant is named for the fixture rather
        /// than read as "the Boss is always character six".</summary>
        public const int FixtureBossCharacterId = FixtureStaffCount;

        /// <summary>None: the .38 every man carries is his own, not the outfit's
        /// stock - the armory holds what is BETTER than that. Kept as a named
        /// number because the stock test counts against it.</summary>
        public const int PistolCount = 0;

        static readonly string[] VehicleNames = { "Sedan", "Coupe", "Panel Van" };

        /// <summary>
        /// The books a campaign opens on: the Don, and the car out back. Nothing else.
        /// The vehicle is drawn off the same Personnel band so the opening stays
        /// deterministic in the city seed alone.
        /// </summary>
        public static Roster Generate(int seed)
        {
            var rng = new System.Random(seed + SeedOffsets.Personnel);
            var roster = Roster.Create(GangCatalog.PlayerGangId);
            roster.Seed = seed;

            AddBoss(roster);

            roster.Equipment.Add(new RosterEquipment
            {
                Id = roster.NextEquipmentId(),
                Kind = EquipmentKind.Vehicle,
                DisplayName = VehicleNames[rng.Next(VehicleNames.Length)],
                Value = 1500,
            });

            return roster;
        }

        /// <summary>
        /// One house's opening books. House 0 is the player and is dealt exactly as it
        /// always has been - the Don and his car, nothing else, because everybody after
        /// him is somebody the player went out and got.
        ///
        /// A rival family opens on men, because it has been in business for years: its
        /// Don, one to three capos, and two or three hoods behind each of them. The
        /// shape is the one <see cref="GangSeeder"/> used to deal out of thin air; the
        /// difference is that these are CHARACTERS now, on a roster with a safe and a
        /// wage bill, dealt through the same doors the player's men go through
        /// (<see cref="RosterOps.Promote"/> and <see cref="RosterOps.AssignToCrew"/>),
        /// so a family can never stand in a shape the rules forbid.
        ///
        /// GangsterNames is the shared naming rule; creation paths assign the name
        /// after dealing skills, before publishing the person to the roster.
        /// </summary>
        public static Roster Generate(int seed, int gangId)
        {
            if (gangId == GangCatalog.PlayerGangId)
                return Generate(seed);

            // Every house on its own stream, mixed off the city's seed - deepening one
            // family can never reshuffle another, and the player's own opening is
            // untouched by any of them.
            var rng = new System.Random(Potential.Mix(seed + SeedOffsets.Personnel, gangId));
            var roster = Roster.Create(gangId);
            roster.Seed = seed;

            // Draw 1..N, in this FROZEN order: the Don's given name, then per crew the
            // capo and his hoods, then the car. Inserting a draw mid-sequence re-deals
            // every family on every seed.
            AddFamilyBoss(rng, roster, gangId);

            var crews = rng.Next(GangSeeder.MinLieutenants, GangSeeder.MaxLieutenants + 1);
            for (var c = 0; c < crews; c++)
            {
                // The span of control binds a family exactly as it binds the outfit: a
                // Don the street has never heard of holds one capo and no more, and the
                // family is simply smaller. Never bypassed.
                var capo = DealMan(rng, roster);
                if (!RosterOps.Promote(roster, capo.Id, out var crewId).Ok)
                {
                    roster.Members.Remove(capo);
                    break;
                }

                var hoods = rng.Next(GangSeeder.MinSoldiers, GangSeeder.MaxSoldiers + 1);
                var crewHoods = new List<Character>(hoods);
                for (var h = 0; h < hoods; h++)
                {
                    var hood = DealMan(rng, roster);
                    RosterOps.AssignToCrew(roster, hood.Id, crewId);
                    crewHoods.Add(hood);
                }

                DressTheCrew(gangId, c, capo, crewHoods);
            }

            roster.Equipment.Add(new RosterEquipment
            {
                Id = roster.NextEquipmentId(),
                Kind = EquipmentKind.Vehicle,
                DisplayName = VehicleNames[rng.Next(VehicleNames.Length)],
                Value = 1500,
            });

            return roster;
        }

        /// <summary>
        /// The six-man fixture: Don Salvatore plus six men rolled from one rng stream, a
        /// lieutenant with two hoods under him, one man on the front desk, two directly
        /// under the Boss, and one car. What the pure tests measure and what a harness
        /// stands up when it needs men on the street; a campaign does NOT open here.
        ///
        /// Roles draw NOTHING: the lieutenant is the best head (Awareness + Organization),
        /// the front the best remaining Streetwise man, the crew the two best remaining
        /// fighters (Combat). Derived roles keep the stream length constant and make the
        /// assignment sensible for free.
        /// </summary>
        public static Roster GenerateStaffed(int seed)
        {
            var rng = new System.Random(seed + SeedOffsets.Personnel);
            var roster = Roster.Create(GangCatalog.PlayerGangId);
            roster.Seed = seed;

            // Draws 1..N, per man in id order: two name-seed draws, his rap sheet
            // (a count, then three draws a line - see RapSheet.Deal), then the 11
            // attributes in enum order and
            // loyalty. The order is FIXED: inserting a draw mid-sequence re-deals every
            // seed's starting six, which is why the rap sheet went in beside the name
            // rather than anywhere more convenient.
            for (var i = 0; i < FixtureStaffCount; i++)
            {
                var member = new Character { Id = roster.NextCharacterId() };
                var nameSeed = GangsterNames.DrawSeed(rng);
                RapSheet.Deal(rng, member);

                // Ceilings first, off his own stream - the stats below are dealt into
                // them, so nobody starts above what he could ever reach. Consumes no
                // draw from the sequence above, which is why the starting six kept
                // their names and their numbers when this landed. His date of birth
                // rides the same stream, for the same reason.
                var stream = Potential.StreamFor(roster.Seed, member.Id);
                Potential.Roll(member, stream);
                Aging.RollBirth(member, stream, YearOf(roster), CalendarDaysPerYear);
                Personality.Roll(member, stream);

                for (var a = 0; a < AttributeScale.Count; a++)
                    member.SetHalfSteps((CharacterAttribute)a,
                        rng.Next(AttributeScale.MinHalfSteps, AttributeScale.MaxHalfSteps + 1));

                member.Loyalty = rng.Next(35, 86);
                GangsterNames.Assign(roster, member, nameSeed);
                roster.Members.Add(member);
            }

            // Final draw: which car sits out back.
            var vehicleName = VehicleNames[rng.Next(VehicleNames.Length)];

            AssignStartingRoles(roster);
            AddBossAndRootHoods(roster);

            for (var i = 0; i < PistolCount; i++)
                roster.Equipment.Add(new RosterEquipment
                {
                    Id = roster.NextEquipmentId(),
                    Kind = EquipmentKind.Pistol,
                    DisplayName = ".38 Pistol",
                    Value = 100,
                });

            roster.Equipment.Add(new RosterEquipment
            {
                Id = roster.NextEquipmentId(),
                Kind = EquipmentKind.Vehicle,
                DisplayName = vehicleName,
                Value = 1500,
            });

            return roster;
        }

        /// <summary>
        /// The scale-test roster: memberCount men organised into crews of five (one
        /// lieutenant, four hoods) per ten, one front, the rest pooled. Structure is
        /// built THROUGH RosterOps, so this fixture can never encode a state the rules
        /// forbid. Its own sub-stream inside the Personnel band (+250) so flipping the
        /// debug roster on can never reshuffle the real starting six.
        /// </summary>
        public static Roster GenerateLarge(int seed, int memberCount)
        {
            // +250 on the seed as well as on the stream: the scale fixture's ceilings
            // sit on their own band, the same way its draws do.
            var rng = new System.Random(seed + SeedOffsets.Personnel + 250);
            var roster = Roster.Create(GangCatalog.PlayerGangId);
            roster.Seed = seed + 250;

            var ordinaryCount = System.Math.Max(0, memberCount - 1);
            for (var i = 0; i < ordinaryCount; i++)
            {
                // Keep the canonical Boss identity on Character 6 in the scale fixture
                // too. Adding him consumes no draw, and index 6 is deliberately outside
                // every deterministic lieutenant/initial-Hood slot below.
                if (i == FixtureStaffCount && roster.FindBoss() == null)
                    AddBoss(roster);

                var member = new Character { Id = roster.NextCharacterId() };
                var nameSeed = GangsterNames.DrawSeed(rng);
                RapSheet.Deal(rng, member);
                var stream = Potential.StreamFor(roster.Seed, member.Id);
                Potential.Roll(member, stream);
                Aging.RollBirth(member, stream, YearOf(roster), CalendarDaysPerYear);
                Personality.Roll(member, stream);

                for (var a = 0; a < AttributeScale.Count; a++)
                    member.SetHalfSteps((CharacterAttribute)a,
                        rng.Next(AttributeScale.MinHalfSteps, AttributeScale.MaxHalfSteps + 1));

                member.Loyalty = rng.Next(35, 86);
                GangsterNames.Assign(roster, member, nameSeed);
                roster.Members.Add(member);
            }

            // The fixture wants six crews out of sixty men, and the span of control
            // (Command.LieutenantCap) says a Boss holds as many branches as his
            // Leadership and his name will carry. Don Salvatore's scripted 4-star
            // reading holds five, so the DEBUG Don is dealt the full reading - the
            // fixture's whole job is to stand sixty men up on a page, and its own doc
            // promises it can never encode a state the rules forbid. The real Don is
            // untouched: growing past five branches is work he has to do.
            var fixtureBoss = roster.FindBoss();
            if (fixtureBoss != null)
            {
                fixtureBoss.SetHalfSteps(CharacterAttribute.Leadership,
                    AttributeScale.MaxHalfSteps);
                fixtureBoss.SetHalfSteps(CharacterAttribute.StreetAuthority,
                    AttributeScale.MaxHalfSteps);
            }

            // One crew per full ten men: ids k*10 lead, k*10+1..+4 follow; the back half
            // of each ten stays pooled. Deterministic in the ids alone - no draws.
            var crews = memberCount / 10;
            for (var k = 0; k < crews; k++)
            {
                if (k * 10 >= ordinaryCount)
                    break;
                RosterOps.Promote(roster, roster.Members[k * 10].Id, out var crewId);
                for (var h = 1; h <= Crew.MaxTacticalHoods &&
                    k * 10 + h < ordinaryCount; h++)
                    RosterOps.AssignToCrew(roster, roster.Members[k * 10 + h].Id, crewId);
            }

            if (ordinaryCount > 7)
                RosterOps.AssignToFront(roster, roster.Members[7].Id);

            AddBossAndRootHoods(roster);

            roster.Equipment.Add(new RosterEquipment
            {
                Id = roster.NextEquipmentId(),
                Kind = EquipmentKind.Vehicle,
                DisplayName = VehicleNames[rng.Next(VehicleNames.Length)],
                Value = 1500,
            });

            return roster;
        }

        /// <summary>
        /// Adds the story Don without consuming the personnel RNG, so every pre-existing
        /// starting Hood/Lieutenant keeps the same ID, name and stats for a given seed.
        /// Every ordinary Hood outside a lieutenant branch answers directly to him.
        /// </summary>
        static void AddBossAndRootHoods(Roster roster)
        {
            if (roster.FindBoss() == null)
                AddBoss(roster);

            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                if (member.Rank == Rank.Hood && member.Specialty == Specialty.None &&
                    !member.Gone && roster.CrewOf(member.Id) == null &&
                    !roster.Organization.BossHoodIds.Contains(member.Id))
                    roster.Organization.BossHoodIds.Add(member.Id);
            }
        }

        /// <summary>The campaign year a man dealt right now is dealt INTO. Zero on the
        /// roster means the books have not turned yet, which is the opening year.</summary>
        static int YearOf(Roster roster) =>
            roster != null && roster.Year > 0 ? roster.Year : CalendarStartYear;

        static void AddBoss(Roster roster)
        {
            var boss = new Character
            {
                Id = roster.NextCharacterId(),
                FirstName = "Don Salvatore",
                Surname = "Ricci",
                Rank = Rank.Boss,
                Look = GangCatalog.BossModel,
                Loyalty = 100,
                BirthYear = CalendarStartYear - BossAge,
                BirthDayOfYear = 0,
            };
            // The Don is the one man with no ceiling: his numbers are scripted rather
            // than dealt, and a rolled cap would quietly cut the story character the
            // seeder exists to keep stable.
            for (var a = 0; a < AttributeScale.Count; a++)
                boss.SetPotential((CharacterAttribute)a, 100);
            for (var a = 0; a < AttributeScale.Count; a++)
                boss.SetHalfSteps((CharacterAttribute)a, 8);
            boss.SetHalfSteps(CharacterAttribute.Awareness, AttributeScale.MaxHalfSteps);
            boss.SetHalfSteps(CharacterAttribute.Organization, AttributeScale.MaxHalfSteps);

            roster.Members.Add(boss);
            roster.Organization.BossId = boss.Id;
        }

        /// <summary>
        /// A rival family's Don: the family name over the door is his surname, his
        /// given name is dealt like anybody else's, and his numbers are DEALT, not
        /// scripted. Only Don Salvatore is written by hand; every other house's head is
        /// a man the city rolled, which is why one family holds three capos and the
        /// next holds one - the span of control decides it, not a table.
        /// </summary>
        static void AddFamilyBoss(System.Random rng, Roster roster, int gangId)
        {
            var nameSeed = rng.Next();
            var boss = new Character
            {
                Id = roster.NextCharacterId(),
                Surname = GangCatalog.Names[gangId],
                Rank = Rank.Boss,
                // The boss-only suit, the same one Don Salvatore wears. A Don is a Don
                // whichever family he heads, and the street reads the coat before it
                // reads the name over the door.
                Look = GangCatalog.BossModel,
                Loyalty = 100,
            };
            DealInto(rng, roster, boss);
            GangsterNames.Assign(roster, boss, nameSeed, GangCatalog.Names[gangId]);
            roster.Members.Add(boss);
            roster.Organization.BossId = boss.Id;
        }

        /// <summary>
        /// The coats one crew of a family wears. A family's colour is its BODIES - id 12
        /// is Greco's coat and nobody else's - so the catalog's staple is dealt onto the
        /// men here, at the one place a family is dealt, rather than being picked again
        /// wherever somebody happens to stand them up.
        ///
        /// The capo wears his family's lieutenant coat; his men wear four different ones
        /// off the hood table, none of them his, and a family's SECOND crew starts its
        /// walk further along the stock so two corners are not the same three coats
        /// twice over (the rule <see cref="GangLooks.HoodsFor"/> was written for).
        /// </summary>
        static void DressTheCrew(int gangId, int crewIndex, Character capo,
            List<Character> hoods)
        {
            capo.Look = GangCatalog.LieutenantModels[gangId];

            var table = GangLooks.Hoods;
            var from = table[(GangLooks.IndexOf(GangCatalog.SoldierModels[gangId]) +
                              3 * crewIndex) % table.Length];
            var looks = GangLooks.HoodsFor(capo.Look, from, hoods.Count);
            for (var i = 0; i < hoods.Count && i < looks.Count; i++)
                hoods[i].Look = looks[i];
        }

        /// <summary>One more man on a family's books, dealt exactly as the founding six
        /// are dealt: a name nobody in the house carries, a rap sheet, his hidden
        /// ceilings off his own stream, and eleven numbers rolled into them.</summary>
        static Character DealMan(System.Random rng, Roster roster)
        {
            var member = new Character { Id = roster.NextCharacterId(), Rank = Rank.Hood };
            var nameSeed = GangsterNames.DrawSeed(rng);
            DealInto(rng, roster, member);
            GangsterNames.Assign(roster, member, nameSeed);
            roster.Members.Add(member);
            return member;
        }

        /// <summary>The part of a deal that is the same for every man: the rap sheet,
        /// the ceilings, the date of birth, the temper, the eleven trades and how much
        /// he cares. Named separately so the Don of a family and the men under him read
        /// off one sequence.</summary>
        static void DealInto(System.Random rng, Roster roster, Character member)
        {
            RapSheet.Deal(rng, member);
            var stream = Potential.StreamFor(roster.Seed, member.Id);
            Potential.Roll(member, stream);
            Aging.RollBirth(member, stream, YearOf(roster), CalendarDaysPerYear);
            Personality.Roll(member, stream);

            for (var a = 0; a < AttributeScale.Count; a++)
                member.SetHalfSteps((CharacterAttribute)a,
                    rng.Next(AttributeScale.MinHalfSteps, AttributeScale.MaxHalfSteps + 1));

            if (member.Rank != Rank.Boss)
                member.Loyalty = rng.Next(35, 86);
        }

        /// <summary>A raw recruit's ceiling - three stars, and most of them well under
        /// it. The founding six keep their generous rolls; everybody hired after them
        /// is a corner boy who has to be BUILT, which is what makes the improvement
        /// system the point of the roster rather than a decoration on it.</summary>
        public const int RecruitCeilingHalfSteps = 6;

        /// <summary>Extra rolls a good recruiter buys, per half-step of Awareness
        /// over the Recruit order's own floor. A sharp man knows a promising one when
        /// he sees him; each bonus re-rolls a random trade and keeps the better.</summary>
        public const int RecruitBonusPerHalfStep = 1;

        /// <summary>One more man off the corner: a name nobody on the books has, eleven
        /// rolled attributes, middling loyalty, and put in the operational pool while
        /// answering directly to the Boss. The
        /// recruiting door - the Organization Ledger intent and the Recruit order both.
        ///
        /// recruiterHalfSteps is the Awareness of whoever went looking; pass 0 for a
        /// walk-in, which is what the street bar's chip is.
        /// </summary>
        public static Character Recruit(Roster roster, System.Random rng,
            int recruiterHalfSteps = 0, string broughtBy = "")
        {
            var member = new Character
            {
                Id = roster.NextCharacterId(),
                Rank = Rank.Hood,
            };
            var nameSeed = GangsterNames.DrawSeed(rng);
            RapSheet.Deal(rng, member);
            var stream = Potential.StreamFor(roster.Seed, member.Id);
            Potential.Roll(member, stream);
            Aging.RollBirth(member, stream, YearOf(roster), CalendarDaysPerYear);
            Personality.Roll(member, stream);

            for (var a = 0; a < AttributeScale.Count; a++)
                member.SetHalfSteps((CharacterAttribute)a,
                    rng.Next(AttributeScale.MinHalfSteps, RecruitCeilingHalfSteps + 1));

            // What the recruiter's eye is worth: a handful of second looks at random
            // trades, each kept only if it is better. Never a floor raise - he finds a
            // better man, he does not train the one he found.
            var floor = OrderResolutionRecruitFloor;
            var bonus = (recruiterHalfSteps - floor) * RecruitBonusPerHalfStep;
            for (var i = 0; i < bonus; i++)
            {
                var attribute = (CharacterAttribute)rng.Next(AttributeScale.Count);
                var roll = rng.Next(AttributeScale.MinHalfSteps,
                    AttributeScale.MaxHalfSteps + 1);
                if (roll > member.GetHalfSteps(attribute))
                    member.SetHalfSteps(attribute, roll);
            }

            member.Loyalty = rng.Next(35, 86);
            GangsterNames.Assign(roster, member, nameSeed);
            roster.Members.Add(member);
            if (roster.FindBoss() != null)
                roster.Organization.BossHoodIds.Add(member.Id);
            // Identity includes the body he will wear in both dossier and street. Cast
            // here, inside the personnel authority, rather than letting whichever UI or
            // physical projection sees him first become the writer of appearance state.
            GangLooks.LookFor(member, roster);
            // The first line of his file with us: the day he came on, and who found
            // him. Everything else on it he has to earn.
            Career.Joined(member, roster.Day, broughtBy);
            return member;
        }

        /// <summary>The Recruit order's stated floor, 3.5 stars. Named here rather than
        /// read from OrderTable so the Personnel core stays free of the Outfit layer -
        /// the two must agree, and LedgerTests asserts that they do.</summary>
        const int OrderResolutionRecruitFloor = 7;

        /// <summary>
        /// A man dealt but NOT put on the books - the newspaper's classified column,
        /// where a few of them advertise every morning and most are never hired
        /// (Outfit.HireMarket). He carries id -1 until somebody signs him, because ids
        /// come off the roster's own counter and the paper is set long before the
        /// outfit knows whether it will take anybody on.
        ///
        /// ceilingHalfSteps is the band he rolls in - a corner recruit's ceiling is
        /// <see cref="RecruitCeilingHalfSteps"/>; a man who advertises rolls higher,
        /// and charges for it. His name is still drawn against the roster, so the
        /// column never offers the outfit a man it already employs.
        ///
        /// potentialStream is the stream his hidden ceilings roll off. He carries no
        /// id to mix with until somebody signs him, so the caller supplies it - the
        /// column derives one from (seed, day, slot), which is what makes this
        /// morning's paper the same paper on a reload.
        /// </summary>
        public static Character Deal(Roster roster, System.Random rng,
            int ceilingHalfSteps, int potentialStream)
        {
            if (ceilingHalfSteps < AttributeScale.MinHalfSteps)
                ceilingHalfSteps = AttributeScale.MinHalfSteps;
            if (ceilingHalfSteps > AttributeScale.MaxHalfSteps)
                ceilingHalfSteps = AttributeScale.MaxHalfSteps;

            var member = new Character { Id = -1 };
            var nameSeed = GangsterNames.DrawSeed(rng);
            RapSheet.Deal(rng, member);
            Potential.Roll(member, potentialStream);
            Aging.RollBirth(member, potentialStream, YearOf(roster), CalendarDaysPerYear);
            Personality.Roll(member, potentialStream);

            for (var a = 0; a < AttributeScale.Count; a++)
                member.SetHalfSteps((CharacterAttribute)a,
                    rng.Next(AttributeScale.MinHalfSteps, ceilingHalfSteps + 1));
            member.Loyalty = rng.Next(35, 86);
            GangsterNames.Assign(roster, member, nameSeed);
            return member;
        }

        static void AssignStartingRoles(Roster roster)
        {
            var remaining = new List<Character>(roster.Members);

            var lieutenant = TakeBest(remaining,
                m => m.GetHalfSteps(CharacterAttribute.Awareness) +
                     m.GetHalfSteps(CharacterAttribute.Organization));
            lieutenant.Rank = Rank.Lieutenant;

            var front = TakeBest(remaining,
                m => m.GetHalfSteps(CharacterAttribute.Streetwise));
            roster.FrontId = front.Id;

            var first = TakeBest(remaining, FightScore);
            var second = TakeBest(remaining, FightScore);

            var crew = new Crew { Id = roster.NextCrewId(), LieutenantId = lieutenant.Id };
            // Ascending id, so the ledger's default order is stable whatever the scores.
            if (first.Id < second.Id)
            {
                crew.HoodIds.Add(first.Id);
                crew.HoodIds.Add(second.Id);
            }
            else
            {
                crew.HoodIds.Add(second.Id);
                crew.HoodIds.Add(first.Id);
            }
            roster.Crews.Add(crew);
            // The last two stand where they are - the pool is derived.
        }

        static int FightScore(Character m) =>
            m.GetHalfSteps(CharacterAttribute.Combat);

        /// <summary>Removes and returns the highest scorer; ties go to the lower id, so
        /// the outcome never depends on list order.</summary>
        static Character TakeBest(List<Character> pool, System.Func<Character, int> score)
        {
            Character best = null;
            var bestScore = int.MinValue;
            for (var i = 0; i < pool.Count; i++)
            {
                var candidate = pool[i];
                var s = score(candidate);
                if (s > bestScore || (s == bestScore && best != null && candidate.Id < best.Id))
                {
                    best = candidate;
                    bestScore = s;
                }
            }

            pool.Remove(best);
            return best;
        }
    }
}
