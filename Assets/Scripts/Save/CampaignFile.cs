using System;
using LivingCity.Outfit;
using LivingCity.Territory;

namespace LivingCity.Save
{
    /// <summary>One premises and whose name is on it.</summary>
    [Serializable]
    public sealed class DeedDto
    {
        public string businessId;
        public int gangId;
        public int legacyBlockId;
    }

    /// <summary>A shop that is shut, why, and until when.</summary>
    [Serializable]
    public sealed class ShutdownDto
    {
        public string businessId;
        public int cause;
        public double startedAt;
        public double recoveryAt;
    }

    /// <summary>What one family has learnt of the city.</summary>
    [Serializable]
    public sealed class KnowledgeDto
    {
        public int gangId;
        public string[] places;
        public int[] men;
    }

    /// <summary>
    /// A CAMPAIGN, WRITTEN DOWN.
    ///
    /// JsonUtility's rules and no others: a [Serializable] class of public fields and
    /// arrays. No dictionaries, no properties, nothing reflected over. Every stateful
    /// class copies its own values by name in its own file, so a field that moves breaks
    /// the build instead of quietly disappearing from the save.
    ///
    /// WHAT IS DELIBERATELY NOT IN HERE (D19): positions. Where a man was standing is a
    /// frame, not a campaign; bodies are re-stood from the rosters on load. Nor fear,
    /// presence or derived control - all three are decaying readings of bodies that no
    /// longer exist, and the streets re-learn what they feel within a day.
    /// </summary>
    [Serializable]
    public sealed class CampaignFile
    {
        /// <summary>Bump when a field is removed or its meaning changes. A file from a
        /// LATER version is refused with a printed reason rather than half-read.
        /// </summary>
        public const int Version = 1;

        public int version = Version;
        public int citySeed;

        /// <summary>The campaign day and the hour of it. The clock is restored to these
        /// and to nothing else.</summary>
        public int day = 1;
        public float hourOfDay;

        public UnderworldDto underworld;
        public TerritoryDto territory;
        public DeedDto[] deeds;
        public ShutdownDto[] shutdowns;
        public KnowledgeDto[] knowledge;
    }
}
