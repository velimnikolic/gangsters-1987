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
            // on a full-name collision among the six), the 11 attributes in enum order,
            // then loyalty.
            for (var i = 0; i < MemberCount; i++)
            {
                var member = new Character { Id = roster.NextCharacterId() };
                DrawName(rng, roster, member);

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

        /// <summary>One more man off the corner, dealt the way the starting six were -
        /// a name nobody on the books has, eleven rolled attributes, middling loyalty -
        /// and put on the books unassigned. The recruiting door: the street bar's
        /// empty chip, later the ledger's own hire page.</summary>
        public static Character Recruit(Roster roster, System.Random rng)
        {
            var member = new Character { Id = roster.NextCharacterId() };
            DrawName(rng, roster, member);
            for (var a = 0; a < AttributeScale.Count; a++)
                member.SetHalfSteps((CharacterAttribute)a,
                    rng.Next(AttributeScale.MinHalfSteps, AttributeScale.MaxHalfSteps + 1));
            member.Loyalty = rng.Next(35, 86);
            roster.Members.Add(member);
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
