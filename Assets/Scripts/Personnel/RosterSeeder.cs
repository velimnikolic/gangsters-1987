using System.Collections.Generic;
using LivingCity.Entities;
using LivingCity.Generation;

namespace LivingCity.Personnel
{
    /// <summary>
    /// The outfit on day one: six men rolled from one rng stream, a lieutenant with two
    /// hoods under him, one man on the front desk, two in the pool, and a stock of three
    /// pistols and one car - nothing handed out yet, and no accountant or lawyer.
    ///
    /// Deterministic for a given seed, on its own SeedOffsets band so retuning the roster
    /// can never re-lay the city. The draw order is FIXED and documented inline - insert a
    /// draw mid-sequence and every campaign's starting six reshuffles.
    ///
    /// Roles draw NOTHING: the lieutenant is the best head (Intelligence + Organization),
    /// the front the best remaining businessman, the crew the two best remaining fighters
    /// (Firearms + Fists + Knives). Derived roles keep the stream length constant and make
    /// the starting assignment sensible for free - the player re-deals from the almanac.
    ///
    /// Names index into PedestrianIdentity's tables - already 1980s-flavoured, already
    /// length-budgeted for popups - so a gangster can share a name with some civilian
    /// across town. At 1,920 combinations, so can two civilians.
    /// </summary>
    public static class RosterSeeder
    {
        public const int MemberCount = 6;
        /// <summary>None: the .38 every man carries is his own, not the outfit's
        /// stock - the armory holds what is BETTER than that. Kept as a named
        /// number because the stock test counts against it.</summary>
        public const int PistolCount = 0;

        static readonly string[] VehicleNames = { "Sedan", "Coupe", "Panel Van" };

        public static Roster Generate(int seed)
        {
            var rng = new System.Random(seed + SeedOffsets.Personnel);
            var roster = new Roster();

            // Draws 1..N, per man in id order: first name, surname (both redrawn together
            // on a full-name collision among the six), his rap sheet (a count, then three
            // draws a line - see RapSheet.Deal), the 11 attributes in enum order, then
            // loyalty. The order is FIXED: inserting a draw mid-sequence re-deals every
            // seed's starting six, which is why the rap sheet went in beside the name
            // rather than anywhere more convenient.
            for (var i = 0; i < MemberCount; i++)
            {
                var member = new Character { Id = roster.NextCharacterId() };
                DrawName(rng, roster, member);
                RapSheet.Deal(rng, member);

                for (var a = 0; a < AttributeScale.Count; a++)
                    member.SetHalfSteps((CharacterAttribute)a,
                        rng.Next(AttributeScale.MinHalfSteps, AttributeScale.MaxHalfSteps + 1));

                member.Loyalty = rng.Next(35, 86);
                roster.Members.Add(member);
            }

            // Final draw: which car sits out back.
            var vehicleName = VehicleNames[rng.Next(VehicleNames.Length)];

            AssignStartingRoles(roster);

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
            var rng = new System.Random(seed + SeedOffsets.Personnel + 250);
            var roster = new Roster();

            for (var i = 0; i < memberCount; i++)
            {
                var member = new Character { Id = roster.NextCharacterId() };
                DrawName(rng, roster, member);
                RapSheet.Deal(rng, member);

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
                RosterOps.Promote(roster, roster.Members[k * 10].Id, out var crewId);
                for (var h = 1; h <= 4 && k * 10 + h < memberCount; h++)
                    RosterOps.AssignToCrew(roster, roster.Members[k * 10 + h].Id, crewId);
            }

            if (memberCount > 7)
                RosterOps.AssignToFront(roster, roster.Members[7].Id);

            roster.Equipment.Add(new RosterEquipment
            {
                Id = roster.NextEquipmentId(),
                Kind = EquipmentKind.Vehicle,
                DisplayName = VehicleNames[rng.Next(VehicleNames.Length)],
                Value = 1500,
            });

            return roster;
        }

        /// <summary>A raw recruit's ceiling - three stars, and most of them well under
        /// it. The founding six keep their generous rolls; everybody hired after them
        /// is a corner boy who has to be BUILT, which is what makes the improvement
        /// system the point of the roster rather than a decoration on it.</summary>
        public const int RecruitCeilingHalfSteps = 6;

        /// <summary>Extra rolls a good recruiter buys, per half-step of Intelligence
        /// over the Recruit order's own floor. A sharp man knows a promising one when
        /// he sees him; each bonus re-rolls a random trade and keeps the better.</summary>
        public const int RecruitBonusPerHalfStep = 1;

        /// <summary>One more man off the corner: a name nobody on the books has, eleven
        /// rolled attributes, middling loyalty, and put on the books unassigned. The
        /// recruiting door - the street bar's empty chip and the Recruit order both.
        ///
        /// recruiterHalfSteps is the Intelligence of whoever went looking; pass 0 for a
        /// walk-in, which is what the street bar's chip is.
        /// </summary>
        public static Character Recruit(Roster roster, System.Random rng,
            int recruiterHalfSteps = 0)
        {
            var member = new Character { Id = roster.NextCharacterId() };
            DrawName(rng, roster, member);
            RapSheet.Deal(rng, member);

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
        /// </summary>
        public static Character Deal(Roster roster, System.Random rng,
            int ceilingHalfSteps)
        {
            if (ceilingHalfSteps < AttributeScale.MinHalfSteps)
                ceilingHalfSteps = AttributeScale.MinHalfSteps;
            if (ceilingHalfSteps > AttributeScale.MaxHalfSteps)
                ceilingHalfSteps = AttributeScale.MaxHalfSteps;

            var member = new Character { Id = -1 };
            DrawName(rng, roster, member);
            RapSheet.Deal(rng, member);

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
                m => m.GetHalfSteps(CharacterAttribute.Intelligence) +
                     m.GetHalfSteps(CharacterAttribute.Organization));
            lieutenant.Rank = Rank.Lieutenant;

            var front = TakeBest(remaining,
                m => m.GetHalfSteps(CharacterAttribute.Business));
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
            m.GetHalfSteps(CharacterAttribute.Firearms) +
            m.GetHalfSteps(CharacterAttribute.Fists) +
            m.GetHalfSteps(CharacterAttribute.Knives);

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
