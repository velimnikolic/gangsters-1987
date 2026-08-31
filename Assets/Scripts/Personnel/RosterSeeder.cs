using System.Collections.Generic;
using LivingCity.Entities;
using LivingCity.Generation;
using LivingCity.Gangs;

namespace LivingCity.Personnel
{
    /// <summary>
    /// The outfit on day one: Don Salvatore as a stable, real Boss Character plus the
    /// same six men rolled from one rng stream, a lieutenant with two hoods under him,
    /// one man on the front desk, two directly under the Boss, and one car.
    ///
    /// Deterministic for a given seed, on its own SeedOffsets band so retuning the roster
    /// can never re-lay the city. The draw order is FIXED and documented inline - insert a
    /// draw mid-sequence and every campaign's starting six reshuffles.
    ///
    /// Roles draw NOTHING: the lieutenant is the best head (Awareness + Organization),
    /// the front the best remaining Streetwise man, the crew the two best remaining
    /// fighters (Combat). Derived roles keep the stream length constant and make the
    /// starting assignment sensible for free - the player re-deals from the almanac.
    ///
    /// Names index into PedestrianIdentity's tables - already 1980s-flavoured, already
    /// length-budgeted for popups - so a gangster can share a name with some civilian
    /// across town. At 1,920 combinations, so can two civilians.
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

        public const int StartingStaffCount = 6;
        public const int MemberCount = StartingStaffCount + 1;
        public const int BossCharacterId = StartingStaffCount;
        /// <summary>None: the .38 every man carries is his own, not the outfit's
        /// stock - the armory holds what is BETTER than that. Kept as a named
        /// number because the stock test counts against it.</summary>
        public const int PistolCount = 0;

        static readonly string[] VehicleNames = { "Sedan", "Coupe", "Panel Van" };

        public static Roster Generate(int seed)
        {
            var rng = new System.Random(seed + SeedOffsets.Personnel);
            var roster = new Roster { Seed = seed };

            // Draws 1..N, per man in id order: first name, surname (both redrawn together
            // on a full-name collision among the six), his rap sheet (a count, then three
            // draws a line - see RapSheet.Deal), the 11 attributes in enum order, then
            // loyalty. The order is FIXED: inserting a draw mid-sequence re-deals every
            // seed's starting six, which is why the rap sheet went in beside the name
            // rather than anywhere more convenient.
            for (var i = 0; i < StartingStaffCount; i++)
            {
                var member = new Character { Id = roster.NextCharacterId() };
                DrawName(rng, roster, member);
                RapSheet.Deal(rng, member);

                // Ceilings first, off his own stream - the stats below are dealt into
                // them, so nobody starts above what he could ever reach. Consumes no
                // draw from the sequence above, which is why the starting six kept
                // their names and their numbers when this landed. His date of birth
                // rides the same stream, for the same reason.
                var stream = Potential.StreamFor(roster.Seed, member.Id);
                Potential.Roll(member, stream);
                Aging.RollBirth(member, stream, YearOf(roster), CalendarDaysPerYear);

                for (var a = 0; a < AttributeScale.Count; a++)
                    member.SetHalfSteps((CharacterAttribute)a,
                        rng.Next(AttributeScale.MinHalfSteps, AttributeScale.MaxHalfSteps + 1));

                member.Loyalty = rng.Next(35, 86);
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
            var roster = new Roster { Seed = seed + 250 };

            var ordinaryCount = System.Math.Max(0, memberCount - 1);
            for (var i = 0; i < ordinaryCount; i++)
            {
                // Keep the canonical Boss identity on Character 6 in the scale fixture
                // too. Adding him consumes no draw, and index 6 is deliberately outside
                // every deterministic lieutenant/initial-Hood slot below.
                if (i == StartingStaffCount && roster.FindBoss() == null)
                    AddBoss(roster);

                var member = new Character { Id = roster.NextCharacterId() };
                DrawName(rng, roster, member);
                RapSheet.Deal(rng, member);
                var stream = Potential.StreamFor(roster.Seed, member.Id);
                Potential.Roll(member, stream);
                Aging.RollBirth(member, stream, YearOf(roster), CalendarDaysPerYear);

                for (var a = 0; a < AttributeScale.Count; a++)
                    member.SetHalfSteps((CharacterAttribute)a,
                        rng.Next(AttributeScale.MinHalfSteps, AttributeScale.MaxHalfSteps + 1));

                member.Loyalty = rng.Next(35, 86);
                roster.Members.Add(member);
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
            int recruiterHalfSteps = 0)
        {
            var member = new Character
            {
                Id = roster.NextCharacterId(),
                Rank = Rank.Hood,
            };
            DrawName(rng, roster, member);
            RapSheet.Deal(rng, member);
            var stream = Potential.StreamFor(roster.Seed, member.Id);
            Potential.Roll(member, stream);
            Aging.RollBirth(member, stream, YearOf(roster), CalendarDaysPerYear);

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
            roster.Members.Add(member);
            if (roster.FindBoss() != null)
                roster.Organization.BossHoodIds.Add(member.Id);
            // Identity includes the body he will wear in both dossier and street. Cast
            // here, inside the personnel authority, rather than letting whichever UI or
            // physical projection sees him first become the writer of appearance state.
            GangLooks.LookFor(member, roster);
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
            DrawName(rng, roster, member);
            RapSheet.Deal(rng, member);
            Potential.Roll(member, potentialStream);
            Aging.RollBirth(member, potentialStream, YearOf(roster), CalendarDaysPerYear);

            for (var a = 0; a < AttributeScale.Count; a++)
                member.SetHalfSteps((CharacterAttribute)a,
                    rng.Next(AttributeScale.MinHalfSteps, ceilingHalfSteps + 1));
            member.Loyalty = rng.Next(35, 86);
            return member;
        }

        static void DrawName(System.Random rng, Roster roster, Character member)
        {
            var firsts = PedestrianIdentity.AllMaleNames;
            var surnames = PedestrianIdentity.AllSurnames;

            // The guard cannot plausibly trip (6 names out of 1,920 pairs), but an rng
            // loop without one is a hang waiting on a shrunk name table.
            for (var guard = 0; guard < 50; guard++)
            {
                member.FirstName = firsts[rng.Next(firsts.Count)];
                member.Surname = surnames[rng.Next(surnames.Count)];

                var taken = false;
                for (var i = 0; i < roster.Members.Count; i++)
                    if (roster.Members[i].FullName == member.FullName)
                    {
                        taken = true;
                        break;
                    }
                if (!taken)
                    return;
            }
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
