namespace LivingCity.Personnel
{
    /// <summary>
    /// THE MAN ON RETAINER. One lawyer, hired out of the newspaper's classified column
    /// like anybody else, and counsel on every case the outfit has for as long as he is
    /// on the books - there is no assigning him to a case, because an outfit with a
    /// lawyer does not shop for one on the morning of a trial.
    ///
    /// His skill is ONE function of two stats and lives here, so the ad's number, the
    /// trial's number and his own file's stars can never disagree: a lawyer who reads
    /// four stars in the paper argues like four stars in court.
    ///
    /// Pure, free of UnityEngine, like the rest of Personnel.
    /// </summary>
    public static class Lawyer
    {
        public const int MinSkill = 1;
        public const int MaxSkill = 5;

        /// <summary>The two things a lawyer is: what he notices, and what he keeps
        /// straight. Neither of them is Persuasion, deliberately - a court is paperwork
        /// and preparation, and the man who is merely charming loses.</summary>
        public static int Skill(Character man)
        {
            if (man == null)
                return 0;

            var head = man.GetHalfSteps(CharacterAttribute.Awareness) +
                       man.GetHalfSteps(CharacterAttribute.Organization);
            var floor = AttributeScale.MinHalfSteps * 2;
            var span = AttributeScale.MaxHalfSteps * 2 - floor;

            var above = head - floor;
            if (above < 0) above = 0;

            // Five bands over the span, so an all-floor man is one star and a man at
            // the ceiling of both is five - and nothing in between rounds to nought.
            var skill = MinSkill + above * (MaxSkill - MinSkill + 1) / (span + 1);
            return skill < MinSkill ? MinSkill : skill > MaxSkill ? MaxSkill : skill;
        }

        /// <summary>The outfit's counsel: the one lawyer on the books who is standing
        /// up. A man in a hospital bed or a cell is not arguing anybody's case.</summary>
        public static Character Counsel(Roster roster)
        {
            if (roster == null)
                return null;
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var man = roster.Members[i];
                if (man.Specialty == Specialty.Lawyer && !man.Gone &&
                    man.Status == CharacterStatus.Active)
                    return man;
            }
            return null;
        }

        /// <summary>The lawyer the outfit is PAYING, standing up or not. A man on
        /// retainer who is in a cell or a hospital bed cannot argue a case
        /// (<see cref="Counsel"/> is what asks that), but he is still on the books and
        /// still drawing his money - so the column must not print a second offer of
        /// counsel over him. Read this to ask "have we got a lawyer", and Counsel to ask
        /// "is he in court this morning".</summary>
        public static Character OnBooks(Roster roster)
        {
            if (roster == null)
                return null;
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var man = roster.Members[i];
                if (man.Specialty == Specialty.Lawyer && !man.Gone)
                    return man;
            }
            return null;
        }

        /// <summary>What the outfit's counsel is worth in court, or 0 with nobody on
        /// retainer - which is what a case runs at when the player never hired one.</summary>
        public static int SkillOf(Roster roster)
        {
            var counsel = Counsel(roster);
            return counsel == null ? 0 : Skill(counsel);
        }

        /// <summary>Whether he is good enough to get a man out on bail at all. A
        /// storefront lawyer cannot get a remand hearing listed.</summary>
        public const int BailSkill = 2;

        /// <summary>Stars, for his own file. The skill IS the star count.</summary>
        public static int Stars(Character man) => Skill(man);
    }
}
