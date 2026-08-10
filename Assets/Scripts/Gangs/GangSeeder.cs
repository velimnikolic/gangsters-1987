using LivingCity.Entities;
using LivingCity.Generation;

namespace LivingCity.Gangs
{
    /// <summary>
    /// Deals the city its gangs, deterministically: one stream off
    /// seed + SeedOffsets.Gangs, drawn in one FROZEN order (RosterSeeder's discipline -
    /// inserting a draw mid-sequence reshuffles every campaign):
    ///
    ///   1. the player front's pick roll,
    ///   2. per gang in id order, a child seed (the extension point: future per-gang
    ///      detail draws from the child stream, never from this one),
    ///   3. per AI gang in id order, crew size then names, lieutenant first.
    ///
    /// The player's gang draws NOTHING for its members - it mirrors the personnel
    /// roster, so the men outside the front are the same men the P-key ledger lists,
    /// bound by Character.Id. Names come from PedestrianIdentity's shared tables: a
    /// gangster can share a name with some civilian across town, exactly as two
    /// civilians already can.
    /// </summary>
    public static class GangSeeder
    {
        /// <summary>AI soldiers besides the lieutenant: 2 or 3.</summary>
        public const int MinSoldiers = 2;
        public const int MaxSoldiers = 3;

        public static Gang[] Generate(int seed, Personnel.Roster playerRoster)
        {
            var rng = new System.Random(seed + SeedOffsets.Gangs);
            var gangs = new Gang[GangCatalog.GangCount];
            for (var i = 0; i < gangs.Length; i++)
                gangs[i] = new Gang
                {
                    Id = i,
                    Name = GangCatalog.Names[i],
                    IsPlayer = i == GangCatalog.PlayerGangId,
                };

            // Draw 1: the player front's pick.
            gangs[GangCatalog.PlayerGangId].FrontRoll = rng.Next();

            // Draw 2: child seeds, every gang, in id order.
            foreach (var gang in gangs)
                gang.MemberSeed = rng.Next();

            // Draw 3: the AI crews.
            for (var i = 0; i < gangs.Length; i++)
            {
                if (gangs[i].IsPlayer)
                    continue;

                var soldiers = rng.Next(MinSoldiers, MaxSoldiers + 1);
                for (var member = 0; member <= soldiers; member++)
                    gangs[i].Members.Add(DrawMember(rng, gangs[i], lieutenant: member == 0));
            }

            MirrorRoster(gangs[GangCatalog.PlayerGangId], playerRoster);
            return gangs;
        }

        /// <summary>Redraws the first+surname PAIR on a full-name collision within the
        /// gang - two Frankies in one crew of four reads as a bug. Fifty tries, then a
        /// duplicate beats an unbounded loop (RosterSeeder's guard).</summary>
        static GangMemberIdentity DrawMember(System.Random rng, Gang gang, bool lieutenant)
        {
            var first = "";
            var surname = "";
            for (var guard = 0; guard < 50; guard++)
            {
                first = PedestrianIdentity.AllMaleNames[
                    rng.Next(PedestrianIdentity.AllMaleNames.Count)];
                surname = PedestrianIdentity.AllSurnames[
                    rng.Next(PedestrianIdentity.AllSurnames.Count)];

                if (!HasName(gang, first + " " + surname))
                    break;
            }

            return new GangMemberIdentity
            {
                FirstName = first,
                Surname = surname,
                Lieutenant = lieutenant,
            };
        }

        static bool HasName(Gang gang, string fullName)
        {
            foreach (var member in gang.Members)
                if (member.FullName == fullName)
                    return true;

            return false;
        }

        /// <summary>The ledger made flesh: every roster member becomes a street presence,
        /// lieutenant first so he takes the door slot. A null or empty roster leaves the
        /// gang installed but nobody outside - the director logs, never throws.</summary>
        static void MirrorRoster(Gang gang, Personnel.Roster roster)
        {
            if (roster == null)
                return;

            // Lieutenant(s) first, then the rest in Members order - a stable ordering
            // derived from data the roster already owns, no draws.
            for (var pass = 0; pass < 2; pass++)
                foreach (var member in roster.Members)
                {
                    var isLieutenant = member.Rank == Personnel.Rank.Lieutenant;
                    if (isLieutenant != (pass == 0))
                        continue;

                    gang.Members.Add(new GangMemberIdentity
                    {
                        FirstName = member.FirstName,
                        Surname = member.Surname,
                        Lieutenant = isLieutenant,
                        PersonnelId = member.Id,
                    });
                }
        }
    }
}
