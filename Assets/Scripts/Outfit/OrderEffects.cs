namespace LivingCity.Outfit
{
    /// <summary>
    /// WHICH ORDERS ACTUALLY DO SOMETHING.
    ///
    /// The order table has always listed every order the design names; several of them
    /// resolve, print a line and change nothing in the world. That is survivable when a
    /// person files them - he can see nothing happened and stop - and it is not
    /// survivable when a mind does, because twenty families would spend a fortune every
    /// week on orders with no effect and the tally would read as a working economy.
    ///
    /// So: a mind may only file an order that is BUILT. The table is the one place that
    /// says so, and <c>gangsters_house_tests</c> asserts that everything marked built has
    /// a contract somewhere that exercises its effect.
    ///
    /// Turning one on is a deliberate act, done in the ticket that builds its effect.
    /// </summary>
    public static class OrderEffects
    {
        /// <summary>Whether this order has an effect a mind can count on.</summary>
        public static bool Built(OrderType type)
        {
            switch (type)
            {
                // The racket, the round and the violence: the street resolves these and
                // the racket, the fear ledger and the shop all feel them.
                case OrderType.Assault:
                case OrderType.SmashUp:
                case OrderType.Raid:
                case OrderType.Torch:
                case OrderType.Bomb:
                case OrderType.Kill:
                    return true;

                // Men on a door do something now (D10).
                case OrderType.Guard:
                    return true;

                // A signing never fails and the man lands in the crew.
                case OrderType.Recruit:
                    return true;

                // The deed moves to the house that ordered it.
                case OrderType.BuyPremises:
                    return true;

                // The block's doors and fronts are learnt for that house.
                case OrderType.Explore:
                    return true;

                // One printed incident per skimming man, and his skim ends.
                case OrderType.Audit:
                    return true;

                // A man off their books for three days, and a ransom in both books.
                // EPIC 10 owns the rest of it; this much is real.
                case OrderType.Kidnap:
                    return true;

                // A quarter of what the shop takes, for men minding it - and NOT a
                // second NetPerDay over a deed that already pays one (RIVAL-009 step 5).
                case OrderType.RunBusiness:
                    return true;

                // ---------------------------------------------------------------- not yet

                // RIVAL-009 step 4: refused outright until the city has vacant premises
                // to open a business in. There are none, so nobody may file it.
                case OrderType.SetUpBusiness:

                // The police plan (Docs/police-behaviour-plan.md, Phase 4) owns what
                // these buy. Any house may file one and the money moves; nothing else
                // happens yet, so no mind spends on them.
                case OrderType.Bribe:
                case OrderType.EmployPolice:
                case OrderType.Donate:

                // The older extortion verbs, superseded by the racket's own doorstep
                // chain. Nothing reads their outcome.
                case OrderType.Extort:
                case OrderType.Intimidate:
                case OrderType.CollectProtection:
                case OrderType.AdjustProtection:

                // A watch that nothing reads. Patrol is OperateInBlock's job (D11) and
                // Ambush has no street behaviour at all.
                case OrderType.Patrol:
                case OrderType.Ambush:
                    return false;
            }
            return false;
        }
    }
}
