using System.Collections.Generic;

namespace LivingCity.Outfit
{
    /// <summary>
    /// One house's standing claim on the outfit: what it is owed, and the campaign day
    /// it falls due. Pure data - the amount is re-assessed from live turf every day, so
    /// a levy is never a stored number that drifts from the city it was struck against.
    /// </summary>
    public sealed class Levy
    {
        public int GangId;

        /// <summary>What falls due on <see cref="DueDay"/>. Re-assessed daily off turf:
        /// take a block off the house that levies you and the envelope gets thinner.</summary>
        public int Amount;

        /// <summary>The campaign day the money is expected. Re-set a cycle forward each
        /// time it is met, so the claim is standing rather than one-off.</summary>
        public int DueDay;

        /// <summary>The safe could not cover it on the day. It stays due, and the house
        /// remembers - Tribute.Settle sours the stance the first time it happens.</summary>
        public bool Overdue;
    }

    /// <summary>
    /// The tribute book: what the outfit kicks up to the houses above it.
    ///
    /// A smaller outfit pays the bigger ones to be left alone, which is the period's own
    /// arrangement and the one pressure in the game that is not payroll. Who levies is
    /// DERIVED, never authored: any house the outfit is not at war with, that holds more
    /// of the city than the outfit does, has a claim. Take enough blocks off a house and
    /// its claim disappears on its own; go to war and you stop paying and start bleeding.
    ///
    /// Pure and free of UnityEngine like the rest of the outfit layer, so the headless
    /// suite can assess a book and settle it without an editor.
    /// </summary>
    public sealed class Tribute
    {
        /// <summary>Days between one envelope and the next. Five, not seven: the books
        /// keep days, and a cycle that happened to be a week would put a week back into
        /// a game that has none.</summary>
        public const int CycleDays = 5;

        /// <summary>What a house asks per block it holds over the outfit's own count -
        /// the levy is the GAP, so closing it is the way out from under.</summary>
        public const int PerBlockAhead = 500;

        /// <summary>No house sends a man across town for less than this.</summary>
        public const int Floor = 1_000;

        /// <summary>Every standing claim, one per house that has one.</summary>
        public readonly List<Levy> Levies = new List<Levy>();

        public Levy For(int gangId)
        {
            for (var i = 0; i < Levies.Count; i++)
                if (Levies[i].GangId == gangId)
                    return Levies[i];
            return null;
        }

        /// <summary>What one house is owed right now, or 0 if it has no claim.</summary>
        public int OwedTo(int gangId) => For(gangId)?.Amount ?? 0;

        /// <summary>The claim falling due soonest - what the blotter's TRIBUTE cell
        /// reads. Null when nobody has a claim, which is the answer when the outfit is
        /// the biggest thing in the city or at war with everything that is not.</summary>
        public Levy Nearest()
        {
            Levy soonest = null;
            for (var i = 0; i < Levies.Count; i++)
            {
                var levy = Levies[i];
                if (levy.Amount <= 0)
                    continue;
                if (soonest == null || levy.DueDay < soonest.DueDay)
                    soonest = levy;
            }
            return soonest;
        }

        /// <summary>Everything owed across every house, for the finances page's foot.</summary>
        public int TotalOwed()
        {
            var total = 0;
            for (var i = 0; i < Levies.Count; i++)
                total += Levies[i].Amount;
            return total;
        }

        /// <summary>
        /// Re-reads the city and re-prices every claim. Called at the day tick BEFORE
        /// settling, so what comes out of the safe is priced against the city as it
        /// stands this morning and not as it stood when the levy was first struck.
        ///
        /// A house at war levies nothing - that is what the war is instead of. A house
        /// the outfit has caught up with levies nothing either: the claim is the gap.
        /// An existing claim keeps its due day; a new one starts a cycle out.
        /// </summary>
        public void Assess(System.Func<int, bool> atWarWith,
            IReadOnlyList<Turf.Holding> holdings, int playerGangId, int day)
        {
            var mine = Turf.CountOf(holdings, playerGangId);

            for (var i = 0; i < holdings.Count; i++)
            {
                var gangId = holdings[i].GangId;
                if (gangId == playerGangId)
                    continue;
                // A holding stamped with an id the catalog does not name is a bug
                // somewhere upstream, but the ledger must not be where it crashes:
                // every reader of a levy looks its house up by name.
                if (gangId < 0 || gangId >= Gangs.GangCatalog.GangCount)
                    continue;
                if (For(gangId) != null)
                    continue;
                Levies.Add(new Levy { GangId = gangId, DueDay = day + CycleDays });
            }

            for (var i = Levies.Count - 1; i >= 0; i--)
            {
                var levy = Levies[i];
                var theirs = Turf.CountOf(holdings, levy.GangId);
                var atWar = atWarWith != null && atWarWith(levy.GangId);
                var ahead = theirs - mine;

                if (atWar || ahead <= 0)
                {
                    // The claim lapses. An overdue one lapses too: a house you have
                    // caught up with or gone to war with is not collecting old debts
                    // through the same quiet channel it used to.
                    Levies.RemoveAt(i);
                    continue;
                }

                var asked = ahead * PerBlockAhead;
                levy.Amount = asked < Floor ? Floor : asked;
            }
        }

        /// <summary>
        /// Pays whatever has fallen due out of the safe, booking it to the open sheet.
        /// Returns what was handed over. What cannot be covered stays due and marks the
        /// house - the caller sours the stance, because souring one is a Relations
        /// change and this class does not reach into that.
        /// </summary>
        /// <param name="soured">Filled with the gang ids that went unpaid this tick.</param>
        public int Settle(Accounts accounts, int day, List<int> soured)
        {
            soured?.Clear();
            if (accounts == null)
                return 0;

            var handed = 0;
            for (var i = 0; i < Levies.Count; i++)
            {
                var levy = Levies[i];
                if (levy.Amount <= 0 || levy.DueDay > day)
                    continue;

                if (accounts.Safe >= levy.Amount)
                {
                    BalanceMath.Pay(accounts, levy.Amount, out _);
                    if (accounts.Current != null)
                        accounts.Current.OtherCosts += levy.Amount;
                    handed += levy.Amount;
                    levy.Overdue = false;
                    levy.DueDay = day + CycleDays;
                    continue;
                }

                // Short. The envelope stays owed and the house is told about it once -
                // Overdue latches so a long broke stretch does not sour the same house
                // every midnight until it is at war with a man who never moved.
                if (!levy.Overdue)
                {
                    levy.Overdue = true;
                    soured?.Add(levy.GangId);
                }
            }
            return handed;
        }
    }
}
