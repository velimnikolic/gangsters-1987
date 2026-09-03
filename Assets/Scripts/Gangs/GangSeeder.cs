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
    ///      detail draws from the child stream, never from this one).
    ///
    /// IT DEALS NO MEN. It used to invent a rival family's names out of this stream,
    /// which meant a family was a list of strings with nothing behind it - no stats, no
    /// wages, nobody who could be shot. Every house has a ROSTER now
    /// (Outfit.Underworld), dealt by RosterSeeder like the player's, and this class
    /// mirrors those books onto the street's view of them.
    ///
    /// A FAMILY IS NOT ONE CREW. A mob that fields two or three capos is a mob with two
    /// or three corners, and the street lays it out that way - one knot of men per
    /// lieutenant, in different quarters (RoadDemoBuilder.SpawnRivals). Members are
    /// stored flat, in CREW order: every Lieutenant entry OPENS a crew and the soldiers
    /// behind him are his, until the next one. Members[0] is therefore a lieutenant
    /// whenever the house runs a crew at all, which is the contract the door slot at
    /// every front is written to.
    ///
    /// Names come from PedestrianIdentity's shared tables, through RosterSeeder: a
    /// gangster can share a name with some civilian across town, exactly as two
    /// civilians already can.
    /// </summary>
    public static class GangSeeder
    {
        /// <summary>Hoods behind each lieutenant in a family's opening books: 2 or 3.
        /// Read by <see cref="Personnel.RosterSeeder"/>, which deals them.</summary>
        public const int MinSoldiers = 2;
        public const int MaxSoldiers = 3;

        /// <summary>Crews an AI family runs - one capo apiece. One is a corner, three is
        /// a family with a quarter of its own; raising the top of this range puts men on
        /// the street everywhere the city has pavement for them, so it is the weight
        /// dial for the whole underworld. The Boss's own span of control can still hold
        /// a family under the bottom of it - never past the top.</summary>
        public const int MinLieutenants = 1;
        public const int MaxLieutenants = 3;

        /// <summary>
        /// The city's gangs, with each one's street view mirrored off that house's own
        /// book. <paramref name="gangCount"/> is the number the city actually dealt,
        /// INCLUDING the player's house; it deliberately limits the returned array so the
        /// registry and its FAMILIES page can never see undealt catalogue slots.
        /// <paramref name="rosterOf"/> answers with a house's roster by gang id - the
        /// underworld supplies it. A null roster is tolerated for isolated/headless views,
        /// but production callers pass the live books for every id inside gangCount.
        /// </summary>
        public static Gang[] Generate(int seed, int gangCount,
            System.Func<int, Personnel.Roster> rosterOf)
        {
            var rng = new System.Random(seed + SeedOffsets.Gangs);
            var count = gangCount < 1 ? 1
                : gangCount > GangCatalog.GangCount ? GangCatalog.GangCount
                : gangCount;
            var gangs = new Gang[count];
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

            // No draws below this line: the men are already dealt, in their houses.
            for (var i = 0; i < gangs.Length; i++)
                MirrorRoster(gangs[i], rosterOf != null ? rosterOf(i) : null);

            return gangs;
        }

        /// <summary>
        /// The ledger made flesh, for every house: crew by crew, the capo and then his
        /// men, so the flat list still reads "a Lieutenant opens a crew" - which is how
        /// the street cuts it back into knots of men.
        ///
        /// The Boss is skipped: he is authoritative personnel, not one more guard on a
        /// corner, and his own projection is a story actor at his front. Everybody the
        /// crews do not hold - the pool, the man on the front desk, the Don's detail -
        /// comes after the last crew, so a family's corners are never miscounted by a
        /// man who is not standing on one.
        /// </summary>
        static void MirrorRoster(Gang gang, Personnel.Roster roster)
        {
            if (roster == null)
                return;

            for (var i = 0; i < roster.Crews.Count; i++)
            {
                var crew = roster.Crews[i];
                var capo = roster.Find(crew.LieutenantId);
                if (capo == null || capo.Rank != Personnel.Rank.Lieutenant)
                    continue;

                Add(gang, capo, lieutenant: true);
                for (var h = 0; h < crew.HoodIds.Count; h++)
                    Add(gang, roster.Find(crew.HoodIds[h]), lieutenant: false);
            }

            // The rest of the house, in the book's own order.
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                if (member.Rank == Personnel.Rank.Boss || Holds(gang, member.Id))
                    continue;
                Add(gang, member, lieutenant: member.Rank == Personnel.Rank.Lieutenant);
            }
        }

        static void Add(Gang gang, Personnel.Character member, bool lieutenant)
        {
            if (member == null)
                return;
            gang.Members.Add(new GangMemberIdentity
            {
                FirstName = member.FirstName,
                Surname = member.Surname,
                Lieutenant = lieutenant,
                PersonnelId = member.Id,
            });
        }

        static bool Holds(Gang gang, int personnelId)
        {
            for (var i = 0; i < gang.Members.Count; i++)
                if (gang.Members[i].PersonnelId == personnelId)
                    return true;
            return false;
        }
    }
}
