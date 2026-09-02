using LivingCity.Personnel;

namespace LivingCity.Outfit
{
    /// <summary>
    /// One family's book. The player's outfit is house 0 and every rival is a house
    /// exactly like it: the same <see cref="Roster"/> with the same rules, its own
    /// <see cref="CampaignRunner"/> with its own safe, its own order book and its own
    /// wage bill, and one front door in the city.
    ///
    /// There is no second class of family. The only thing that tells the player's house
    /// apart from the twenty others is who files its orders - the ledger, or a mind -
    /// and that lives outside this object entirely.
    ///
    /// Pure and free of UnityEngine, like the rest of the Outfit layer.
    /// </summary>
    public sealed class House
    {
        public House(int gangId, Roster roster, CampaignRunner runner)
        {
            GangId = gangId;
            Roster = roster;
            Runner = runner;
        }

        /// <summary>Which family this is, in <see cref="Gangs.GangCatalog"/>'s
        /// numbering. Never decoded from anything; it is written here once.</summary>
        public int GangId { get; }

        public bool IsPlayer => GangId == Gangs.GangCatalog.PlayerGangId;

        public Roster Roster { get; private set; }

        /// <summary>
        /// Swaps the book under the house. The one caller today is the ledger's F2
        /// scale roster, which deals sixty men under a director that is already
        /// standing; the save file will be the second (RIVAL-010). Everything derived
        /// off the old book - the house's own draw - is dropped with it.
        /// </summary>
        public void Restock(Roster roster)
        {
            if (roster == null)
                return;
            Roster = roster;
            draw = null;
            Touch();
        }

        public CampaignRunner Runner { get; }

        /// <summary>The premises over which the family's name hangs. Invalid until the
        /// street seats it - a house exists whether or not the city found it a door,
        /// which is what keeps the books and the pavement independent.</summary>
        public Territory.TerritoryBusinessId Front;

        /// <summary>Moves whenever the house's men or money do, so a page can tell in
        /// one integer whether it has anything to repaint.</summary>
        public int Version { get; private set; }

        System.Random draw;

        /// <summary>
        /// The house's own stream for a man signed off a corner. House 0 keeps the very
        /// number the ledger's recruiting door has always used, so the player signs the
        /// same men on the same seed as before; every other house mixes its own id in,
        /// so two families never draw the same corner boy.
        /// </summary>
        public System.Random Draw => draw ??= new System.Random(
            IsPlayer
                ? (Roster != null ? Roster.Seed : 0) * 31 + 7
                : Potential.Mix((Roster != null ? Roster.Seed : 0) * 31 + 7, GangId));

        public void Touch() => Version++;

        /// <summary>
        /// WHEN THIS HOUSE NEXT THINKS (D7). Every four game hours, staggered by the
        /// family's own number so twenty-one minds never land on one frame. Trouble
        /// against it brings the hour forward (RIVAL-006).
        /// </summary>
        public double NextThinkHour;

        /// <summary>Think NOW - something happened that will not wait four hours.
        /// </summary>
        public void WakeNow(double gameHour) => NextThinkHour = gameHour;

        /// <summary>How many thinks running have found nothing louder to do than spend
        /// money. A family buys cars when the street is quiet (D22).</summary>
        public int QuietThinks { get; private set; }

        /// <summary>One turn of mind, remembered: a think that acted on a tier below
        /// the buying one breaks the quiet.</summary>
        public void NoteThink(bool busy) => QuietThinks = busy ? 0 : QuietThinks + 1;

        /// <summary>The family's own place in the four-hour rota, so the twenty-one are
        /// spread evenly across it rather than all thinking at nine o'clock.</summary>
        public void OpenTheRota(double gameHour, float everyHours, int houses)
        {
            if (houses < 1)
                houses = 1;
            NextThinkHour = gameHour + everyHours * (GangId % houses) / houses;
        }

        /// <summary>
        /// Nobody left on the books - every name struck through, dead or gone over.
        /// Derived, never stored: a family is finished when its last man is, and no
        /// pass anywhere writes a flag saying so.
        ///
        /// Nothing runs for an extinct house: no day tick, no wages, no orders.
        /// </summary>
        public bool Extinct
        {
            get
            {
                if (Roster == null)
                    return true;
                for (var i = 0; i < Roster.Members.Count; i++)
                    if (!Roster.Members[i].Gone)
                        return false;
                return true;
            }
        }

        /// <summary>How many men still answer the family's telephone.</summary>
        public int Standing
        {
            get
            {
                if (Roster == null)
                    return 0;
                var standing = 0;
                for (var i = 0; i < Roster.Members.Count; i++)
                    if (!Roster.Members[i].Gone)
                        standing++;
                return standing;
            }
        }
    }
}
