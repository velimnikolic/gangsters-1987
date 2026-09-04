using System;
using System.Collections.Generic;
using System.Globalization;
using LivingCity.Territory;

namespace LivingCity.Business
{
    /// <summary>
    /// Who holds the deed. An individual is one civilian proprietor with a face the ledger
    /// can draw later; a company is a firm behind a gate (a harbour operator, a works); a
    /// civic owner is the city itself, which the racket layer may refuse to touch.
    /// </summary>
    public enum BusinessOwnerKind
    {
        Individual,
        Company,
        Civic,
    }

    /// <summary>Rough age of an individual proprietor. Company and civic owners have none.</summary>
    public enum BusinessOwnerAge
    {
        None,
        Young,
        Middle,
        Old,
    }

    /// <summary>
    /// What the business is doing at all. Phase 1 only ever writes Trading - closure,
    /// burning out and seizure belong to the later racket and war layers - but the field
    /// exists now so those layers mutate a record rather than inventing a parallel one.
    /// </summary>
    public enum BusinessOperationalState
    {
        Trading,
        Shut,
        Ruined,
    }

    /// <summary>How big the premises are, as the site plan measures them.</summary>
    public enum BusinessSiteSize
    {
        Small,
        Medium,
        Large,
        Compound,
    }

    /// <summary>
    /// A canonical business site's identity: which provider published it, which plan or
    /// recipe it came out of, and which group inside that plan it is. Stable across
    /// streaming, view recycling and compose/incremental compose, because every part of it
    /// is read from persistent plan data and never from a GameObject.
    /// </summary>
    public readonly struct BusinessSiteId : IEquatable<BusinessSiteId>
    {
        public BusinessSiteId(string value) => Value = value ?? "";

        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);

        public bool Equals(BusinessSiteId other) =>
            string.Equals(Value ?? "", other.Value ?? "", StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is BusinessSiteId other && Equals(other);

        public override int GetHashCode() =>
            Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);

        public override string ToString() => Value ?? "";

        public static bool operator ==(BusinessSiteId left, BusinessSiteId right) =>
            left.Equals(right);

        public static bool operator !=(BusinessSiteId left, BusinessSiteId right) =>
            !left.Equals(right);
    }

    /// <summary>
    /// An owner's identity. Deliberately its own type rather than an index into a list:
    /// a later ticket may sell a business on, and the new deed must not depend on where
    /// in some pool the old owner happened to sit.
    /// </summary>
    public readonly struct BusinessOwnerId : IEquatable<BusinessOwnerId>
    {
        public BusinessOwnerId(string value) => Value = value ?? "";

        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);

        public bool Equals(BusinessOwnerId other) =>
            string.Equals(Value ?? "", other.Value ?? "", StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is BusinessOwnerId other && Equals(other);

        public override int GetHashCode() =>
            Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);

        public override string ToString() => Value ?? "";

        public static bool operator ==(BusinessOwnerId left, BusinessOwnerId right) =>
            left.Equals(right);

        public static bool operator !=(BusinessOwnerId left, BusinessOwnerId right) =>
            !left.Equals(right);
    }

    /// <summary>
    /// The only adapters that mint business identities. The business ID reuses the existing
    /// <see cref="TerritoryBusinessId"/> - <c>BusinessMarker.BusinessId</c> already carries
    /// that type, and a second business number would mean two answers to "which shop is
    /// this". Every ID below is derived from the site, so the same seed and the same site
    /// give the same business whatever order discovery or streaming happened to run in.
    /// </summary>
    public static class BusinessIdentity
    {
        /// <summary>
        /// A site's ID: provider, the plan or recipe it was read out of, and the grouping
        /// key inside that plan. The three parts are separated by '|' rather than ':'
        /// because plan IDs (Core's StableId) already contain colons, and an ambiguous
        /// separator would let two different sites collapse onto one string.
        /// </summary>
        public static BusinessSiteId Site(string providerId, string planId, string groupKey) =>
            new BusinessSiteId(string.Format(
                CultureInfo.InvariantCulture, "{0}|{1}|{2}",
                providerId ?? "", planId ?? "", groupKey ?? ""));

        /// <summary>The business standing on a site. One site, one business - the collision
        /// ordinal exists only for a provider that publishes genuinely independent
        /// establishments under one plan key.</summary>
        public static TerritoryBusinessId Business(BusinessSiteId siteId) =>
            new TerritoryBusinessId("biz|" + siteId.Value);

        /// <summary>The deed on a site. Ownership can move later without this changing,
        /// because a transfer rewrites the business's OwnerId rather than the owner.</summary>
        public static BusinessOwnerId Owner(BusinessSiteId siteId) =>
            new BusinessOwnerId("own|" + siteId.Value);

        /// <summary>The proprietor generation on a site. Generation zero deliberately
        /// mints the original ID byte-for-byte; successors get a stable suffix here,
        /// at the one identity mint, never at a call site.</summary>
        public static BusinessOwnerId Owner(BusinessSiteId siteId, int generation) =>
            generation <= 0
                ? Owner(siteId)
                : new BusinessOwnerId(string.Format(
                    CultureInfo.InvariantCulture, "own|{0}|generation:{1}",
                    siteId.Value, generation));

        /// <summary>A shared owner - the harbour company behind several sheds, the city
        /// behind its civic premises. Keyed by name so that two sites naming the same firm
        /// resolve to one deed.</summary>
        public static BusinessOwnerId SharedOwner(string key) =>
            new BusinessOwnerId("own|shared|" + (key ?? ""));

        /// <summary>
        /// Avalanches (citySeed, siteId) before it reaches System.Random, whose nearby
        /// seeds produce visibly correlated first draws. OrderResolution.Mix's fingerprint
        /// mix, applied here for the reason the epic states: every site draws from its OWN
        /// stream, so adding or removing one site cannot reroll any other.
        /// </summary>
        public static int MixSeed(int citySeed, BusinessSiteId siteId) =>
            MixSeed(citySeed, StableHash(siteId.Value));

        /// <summary>
        /// One site's stream number <paramref name="stream"/>. The archetype roll and the
        /// owner's draw must not share a stream: seeded from the same number they would
        /// answer in lockstep, so every company-owned site would land on the same first
        /// template. The stream tag goes through the hash, not through the seed, so two
        /// streams of one site are as far apart as two different sites.
        /// </summary>
        public static int MixSeed(int citySeed, BusinessSiteId siteId, int stream) =>
            MixSeed(citySeed, unchecked(StableHash(siteId.Value) * 31 + stream));

        public static int MixSeed(int citySeed, int siteHash)
        {
            unchecked
            {
                var h = (uint)citySeed * 2654435761u + (uint)siteHash * 2246822519u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                h *= 3266489917u;
                h ^= h >> 16;
                return (int)h;
            }
        }

        /// <summary>
        /// FNV-1a over the ID text. string.GetHashCode is randomised per process in modern
        /// runtimes, so it cannot be the basis of anything a save file or a second session
        /// has to agree with.
        /// </summary>
        public static int StableHash(string text)
        {
            unchecked
            {
                var h = 2166136261u;
                if (text != null)
                    for (var i = 0; i < text.Length; i++)
                    {
                        h ^= text[i];
                        h *= 16777619u;
                    }

                return (int)h;
            }
        }
    }

    /// <summary>
    /// One simulated business. Everything here is simulation truth: no GameObject,
    /// Transform, MonoBehaviour, renderer, collider or streamed view may be reachable from
    /// this record, because the business has to exist while its street is off camera.
    /// <c>BusinessMarker</c> and every map/UI row are projections of it.
    ///
    /// Identity (Id, SiteId, Archetype) is readonly. Owner and operational state are the
    /// two mutable facts, because the later layers sell premises on and shut them down;
    /// both move through <see cref="BusinessDirectory"/> so the version stamp and the
    /// change signal cannot be bypassed.
    /// </summary>
    public sealed class BusinessRecord
    {
        internal BusinessRecord(
            TerritoryBusinessId id,
            BusinessSiteId siteId,
            BusinessArchetypeId archetype,
            string displayName,
            BusinessOwnerId ownerId,
            BusinessSiteSize size,
            int estimatedWeeklyTurnover,
            string providerId)
        {
            if (!id.IsValid)
                throw new ArgumentException("A business needs a canonical ID.", nameof(id));
            if (!siteId.IsValid)
                throw new ArgumentException("A business needs a site.", nameof(siteId));

            Id = id;
            SiteId = siteId;
            Archetype = archetype;
            DisplayName = displayName ?? id.Value;
            OwnerId = ownerId;
            Size = size;
            EstimatedWeeklyTurnover = Math.Max(0, estimatedWeeklyTurnover);
            ProviderId = providerId ?? "";
            State = BusinessOperationalState.Trading;
        }

        public TerritoryBusinessId Id { get; }
        public BusinessSiteId SiteId { get; }
        public BusinessArchetypeId Archetype { get; }
        public string DisplayName { get; }
        public BusinessSiteSize Size { get; }

        /// <summary>A fact, not a cash flow: this ticket sizes the premises, it does not
        /// pay anybody. The economy tickets read it as their starting figure.</summary>
        public int EstimatedWeeklyTurnover { get; }

        /// <summary>Which provider published the site. Kept for diagnostics: "why does this
        /// shop exist" must be answerable without re-running the sweep.</summary>
        public string ProviderId { get; }

        public BusinessOwnerId OwnerId { get; internal set; }
        public BusinessOperationalState State { get; internal set; }
    }

    /// <summary>
    /// A gazda as simulation truth. No personality, courage, fear or compliance here - those
    /// are the later economy and racket layers; this record only says who the deed names and
    /// gives the ledger's portrait rig a seed to draw from when it is asked to.
    /// </summary>
    public sealed class BusinessOwnerRecord
    {
        internal BusinessOwnerRecord(
            BusinessOwnerId id,
            BusinessOwnerKind kind,
            string displayName,
            BusinessOwnerAge age,
            int portraitSeed)
        {
            if (!id.IsValid)
                throw new ArgumentException("An owner needs a canonical ID.", nameof(id));

            Id = id;
            Kind = kind;
            DisplayName = displayName ?? id.Value;
            Age = age;
            PortraitSeed = portraitSeed;
        }

        public BusinessOwnerId Id { get; }
        public BusinessOwnerKind Kind { get; }
        public string DisplayName { get; }
        public BusinessOwnerAge Age { get; }

        /// <summary>Stored now, consumed later by the ledger's portrait rig. Nothing is
        /// spawned for an owner: a deed is a name, not an actor.</summary>
        public int PortraitSeed { get; }
    }

    public enum BusinessChangeKind
    {
        BusinessRegistered,
        OwnerRegistered,
        OwnerChanged,
        StateChanged,
    }

    public readonly struct BusinessChange
    {
        public BusinessChange(
            BusinessChangeKind kind, TerritoryBusinessId businessId, BusinessOwnerId ownerId)
        {
            Kind = kind;
            BusinessId = businessId;
            OwnerId = ownerId;
        }

        public BusinessChangeKind Kind { get; }
        public TerritoryBusinessId BusinessId { get; }
        public BusinessOwnerId OwnerId { get; }
    }

    /// <summary>Read-only view handed to consumers. A view can rebuild off this; it cannot
    /// establish anything through it.</summary>
    public interface IBusinessQuery
    {
        int Version { get; }
        IReadOnlyList<TerritoryBusinessId> BusinessIds { get; }
        bool TryGet(TerritoryBusinessId id, out BusinessRecord business);
        bool TryGetBySite(BusinessSiteId siteId, out BusinessRecord business);
        bool TryGetOwner(BusinessOwnerId id, out BusinessOwnerRecord owner);
    }

    /// <summary>
    /// The authoritative owner of every simulated city business and gazda -
    /// <c>TerritorySimulationState</c>'s pattern exactly: one store, a Version bumped on
    /// every mutation, a query interface for everyone else, and no public setter that would
    /// let a view establish truth.
    ///
    /// Registration is the only way in, duplicates are refused with both sources named, and
    /// enumeration follows registration order - which the population pass takes from the
    /// site catalogue's sorted order, never from whatever provider or streamed view spoke
    /// first.
    /// </summary>
    public sealed class BusinessDirectory : IBusinessQuery
    {
        readonly Dictionary<TerritoryBusinessId, BusinessRecord> businesses =
            new Dictionary<TerritoryBusinessId, BusinessRecord>();
        readonly Dictionary<BusinessSiteId, TerritoryBusinessId> bySite =
            new Dictionary<BusinessSiteId, TerritoryBusinessId>();
        readonly Dictionary<BusinessOwnerId, BusinessOwnerRecord> owners =
            new Dictionary<BusinessOwnerId, BusinessOwnerRecord>();
        readonly List<TerritoryBusinessId> order = new List<TerritoryBusinessId>();
        readonly List<BusinessOwnerId> ownerOrder = new List<BusinessOwnerId>();
        readonly List<string> problems = new List<string>();

        public int Version { get; private set; }
        public IReadOnlyList<TerritoryBusinessId> BusinessIds => order;
        public IReadOnlyList<BusinessOwnerId> OwnerIds => ownerOrder;

        /// <summary>Duplicate registrations and other refusals, each naming the offending
        /// ID and both sources. The audit prints these; nothing repairs them silently.</summary>
        public IReadOnlyList<string> Problems => problems;

        public event Action<BusinessChange> Changed;

        public bool TryGet(TerritoryBusinessId id, out BusinessRecord business) =>
            businesses.TryGetValue(id, out business);

        public bool TryGetBySite(BusinessSiteId siteId, out BusinessRecord business)
        {
            business = null;
            return bySite.TryGetValue(siteId, out var id) && businesses.TryGetValue(id, out business);
        }

        public bool TryGetOwner(BusinessOwnerId id, out BusinessOwnerRecord owner) =>
            owners.TryGetValue(id, out owner);

        public BusinessRecord Register(
            BusinessSiteId siteId,
            BusinessArchetypeId archetype,
            string displayName,
            BusinessOwnerId ownerId,
            BusinessSiteSize size,
            int estimatedWeeklyTurnover,
            string providerId)
        {
            var id = BusinessIdentity.Business(siteId);
            if (businesses.TryGetValue(id, out var existing))
            {
                problems.Add(
                    $"BIZ: duplicate business '{id}' - already published by provider " +
                    $"'{existing.ProviderId}' for site '{existing.SiteId}', now again by " +
                    $"provider '{providerId}' for site '{siteId}'.");
                return null;
            }

            if (bySite.TryGetValue(siteId, out var other))
            {
                problems.Add(
                    $"BIZ: site '{siteId}' was populated twice - business '{other}' already " +
                    $"stands there, provider '{providerId}' tried to add another.");
                return null;
            }

            var record = new BusinessRecord(
                id, siteId, archetype, displayName, ownerId, size,
                estimatedWeeklyTurnover, providerId);
            businesses.Add(id, record);
            bySite.Add(siteId, id);
            order.Add(id);
            Version++;
            Changed?.Invoke(new BusinessChange(
                BusinessChangeKind.BusinessRegistered, id, ownerId));
            return record;
        }

        public BusinessOwnerRecord RegisterOwner(
            BusinessOwnerId id, BusinessOwnerKind kind, string displayName,
            BusinessOwnerAge age, int portraitSeed)
        {
            if (owners.TryGetValue(id, out var existing))
            {
                // A shared deed (the harbour company, City Hall) is asked for once per
                // site by design - the second ask is the same firm, not a clash.
                if (!string.Equals(existing.DisplayName, displayName, StringComparison.Ordinal))
                    problems.Add(
                        $"BIZ: duplicate owner '{id}' - '{existing.DisplayName}' already holds " +
                        $"it, '{displayName}' was offered second.");
                return existing;
            }

            var record = new BusinessOwnerRecord(id, kind, displayName, age, portraitSeed);
            owners.Add(id, record);
            ownerOrder.Add(id);
            Version++;
            Changed?.Invoke(new BusinessChange(
                BusinessChangeKind.OwnerRegistered, default, id));
            return record;
        }

        /// <summary>Sell the premises on. The business keeps its ID and its site: only the
        /// deed moves, which is the whole reason OwnerId is not part of identity.</summary>
        public bool SetOwner(TerritoryBusinessId businessId, BusinessOwnerId ownerId)
        {
            if (!businesses.TryGetValue(businessId, out var record) || !owners.ContainsKey(ownerId))
                return false;
            if (record.OwnerId == ownerId)
                return true;

            record.OwnerId = ownerId;
            Version++;
            Changed?.Invoke(new BusinessChange(
                BusinessChangeKind.OwnerChanged, businessId, ownerId));
            return true;
        }

        public bool SetState(TerritoryBusinessId businessId, BusinessOperationalState state)
        {
            if (!businesses.TryGetValue(businessId, out var record))
                return false;
            if (record.State == state)
                return true;

            record.State = state;
            Version++;
            Changed?.Invoke(new BusinessChange(
                BusinessChangeKind.StateChanged, businessId, record.OwnerId));
            return true;
        }

        internal void Report(string problem)
        {
            if (!string.IsNullOrEmpty(problem))
                problems.Add(problem);
        }
    }
}
