using System.Collections.Generic;

namespace LivingCity.Personnel
{
    /// <summary>
    /// What the book has to say about a man at a glance. A set rather than a single
    /// verdict: the same man can be a gun worth keeping and a man worth watching, and
    /// the ledger has to be able to print both against his name.
    /// </summary>
    [System.Flags]
    public enum ManFlag
    {
        None = 0,

        /// <summary>He could run a crew - the three command trades are all there.</summary>
        LieutenantMaterial = 1,

        /// <summary>He can shoot, he will go, and he does what he was told.</summary>
        HitmanMaterial = 2,

        /// <summary>He wants more than he has, and he no longer thinks it is coming
        /// from us.</summary>
        RedFlag = 4,
    }

    /// <summary>
    /// The three marks the ledger puts against a name, so the player never has to read
    /// eleven numbers per man to find the one worth promoting or the one worth watching.
    ///
    /// Evaluated ON DEMAND, every time, off the live man. Nothing here is cached on the
    /// character except the record of what has already been ANNOUNCED - a mark that went
    /// stale between a promotion and a repaint would be worse than no mark at all.
    ///
    /// The thresholds are written on the design's 0-100 scale. Two of the three mix
    /// sources: the skills are half-steps read through <see cref="AttributeScale"/>, the
    /// character traits are already 0-100. The conversion helper is only ever applied to
    /// the skill side, and never inline.
    ///
    /// Rounding, stated once: half-star granularity means a skill can only sit on a
    /// multiple of ten, so a threshold of 55 is met at 60 and not at 50 -
    /// <see cref="AttributeScale.HalfStepsFor"/> rounds the threshold UP at the midpoint
    /// and every comparison here is made in half-steps. In practice "Organization >= 55"
    /// and "Organization >= 60" are the same rule; the spec's figure is kept because the
    /// intent is what the table records.
    ///
    /// Flags NEVER act. They inform the player, and nothing else: the defection
    /// arithmetic reads the same underlying numbers rather than the mark drawn from
    /// them, so hiding the mark would not change what happens to the outfit.
    ///
    /// Pure and free of UnityEngine like the rest of Personnel.
    /// </summary>
    public static class ManFlags
    {
        // ---- Lieutenant material: he could run a crew.
        public const int LeadershipForCrew = 60;
        public const int OrganizationForCrew = 55;
        public const int StreetAuthorityForCrew = 50;

        // ---- Hitman material: he can shoot, he will go, he does as he is told.
        public const int CombatForGun = 70;
        public const int CourageForGun = 60;
        public const int DisciplineForGun = 50;

        // ---- Red flag: hungry, and no longer ours.
        public const int AmbitionForRedFlag = 70;

        /// <summary>Not a figure of its own: the red flag goes up exactly where the
        /// loyalty layer already says a man bears watching, so the mark beside his name
        /// and the line in the paper can never disagree about him.</summary>
        public const int LoyaltyForRedFlag = Loyalty.WatchBand;

        /// <summary>
        /// What the book says about him right now. A man off the books carries no
        /// marks - the record keeps his line, but nothing is going to be done about
        /// him either way.
        /// </summary>
        public static ManFlag Of(Character man)
        {
            if (man == null || man.Gone)
                return ManFlag.None;

            var flags = ManFlag.None;

            // A specialist is never promoted and never sent anywhere: marking him as
            // lieutenant material would be an offer the ledger cannot honour.
            if (man.Specialty == Specialty.None &&
                Has(man, CharacterAttribute.Leadership, LeadershipForCrew) &&
                Has(man, CharacterAttribute.Organization, OrganizationForCrew) &&
                Has(man, CharacterAttribute.StreetAuthority, StreetAuthorityForCrew))
                flags |= ManFlag.LieutenantMaterial;

            if (man.Specialty == Specialty.None &&
                Has(man, CharacterAttribute.Combat, CombatForGun) &&
                Personality.Get(man, PersonalityTrait.Courage) >= CourageForGun &&
                Personality.Get(man, PersonalityTrait.Discipline) >= DisciplineForGun)
                flags |= ManFlag.HitmanMaterial;

            if (Personality.Get(man, PersonalityTrait.Ambition) >= AmbitionForRedFlag &&
                Personality.Get(man, PersonalityTrait.Loyalty) <= LoyaltyForRedFlag)
                flags |= ManFlag.RedFlag;

            return flags;
        }

        /// <summary>One skill against one 0-100 threshold, both in half-steps. THE
        /// conversion, in the one place it happens.</summary>
        static bool Has(Character man, CharacterAttribute skill, int threshold) =>
            man.GetHalfSteps(skill) >= AttributeScale.HalfStepsFor(threshold);

        /// <summary>
        /// Says out loud what has just become true of him, and only once. Crossing INTO
        /// a mark is news; standing inside one for a month is not, and a feed that
        /// reprinted every standing flag every midnight would bury the day's real
        /// events under it.
        ///
        /// Crossing back OUT is silent and clears the latch, so a man who slips under a
        /// threshold and climbs back over it is announced again - the second crossing
        /// is a real event, and the player has had a month of other pages since the
        /// first.
        /// </summary>
        public static ManFlag Announce(Character man, int day, List<Incident> into)
        {
            if (man == null)
                return ManFlag.None;

            var now = Of(man);
            var fresh = now & ~man.FlagsAnnounced;
            man.FlagsAnnounced = now;

            if (into == null || fresh == ManFlag.None)
                return fresh;

            if ((fresh & ManFlag.LieutenantMaterial) != 0)
                Say(man, IncidentKind.ReadyForACrew, day, into);
            if ((fresh & ManFlag.HitmanMaterial) != 0)
                Say(man, IncidentKind.AGunForHire, day, into);
            if ((fresh & ManFlag.RedFlag) != 0)
                Say(man, IncidentKind.NotToBeTrusted, day, into);

            return fresh;
        }

        static void Say(Character man, IncidentKind kind, int day, List<Incident> into) =>
            into.Add(new Incident(man.Id, man.FullName, kind, day, "", 0,
                IncidentText.Line(kind, man.FullName, "")));

        /// <summary>The mark as the roster row prints it - three characters at most,
        /// because it stands in a column beside sixty names.</summary>
        public static string Mark(ManFlag flag) => flag switch
        {
            ManFlag.LieutenantMaterial => "LT",
            ManFlag.HitmanMaterial => "GUN",
            ManFlag.RedFlag => "!",
            _ => "",
        };

        /// <summary>The mark as the personal file prints it, in the clerk's own
        /// words.</summary>
        public static string Label(ManFlag flag) => flag switch
        {
            ManFlag.LieutenantMaterial => "Lieutenant material",
            ManFlag.HitmanMaterial => "Hitman material",
            ManFlag.RedFlag => "Bears watching",
            _ => "",
        };

        /// <summary>The three, in the order the book prints them: what he could be
        /// first, what he is a danger of second.</summary>
        public static readonly ManFlag[] All =
        {
            ManFlag.LieutenantMaterial,
            ManFlag.HitmanMaterial,
            ManFlag.RedFlag,
        };

        /// <summary>Every mark at once. Not a state any man reaches often, but it is
        /// the widest line the marks can make, which is what a column has to be cut
        /// to.</summary>
        public const ManFlag Every =
            ManFlag.LieutenantMaterial | ManFlag.HitmanMaterial | ManFlag.RedFlag;

        /// <summary>The whole set as one line - "Lieutenant material · Bears watching",
        /// or an empty string when the book has nothing to say about him.</summary>
        public static string Line(ManFlag flags)
        {
            var line = "";
            for (var i = 0; i < All.Length; i++)
            {
                if ((flags & All[i]) == 0)
                    continue;
                if (line.Length > 0)
                    line += " · ";
                line += Label(All[i]);
            }
            return line;
        }

        /// <summary>The set as the roster ROW prints it - "LT · GUN · !" - the short
        /// marks rather than the clerk's words. One writer for the line the row sets
        /// and the width that room is measured from, so the two cannot drift.</summary>
        public static string MarkLine(ManFlag flags)
        {
            var line = "";
            for (var i = 0; i < All.Length; i++)
            {
                if ((flags & All[i]) == 0)
                    continue;
                if (line.Length > 0)
                    line += " · ";
                line += Mark(All[i]);
            }
            return line;
        }
    }
}
