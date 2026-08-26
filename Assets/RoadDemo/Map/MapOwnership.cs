using System.Collections.Generic;

namespace RoadDemo
{
    /// <summary>
    /// Which buildings a family has taken, building by building.
    ///
    /// This is a SEAM and not a rule. The city models ownership one way only: a family
    /// operates behind one door (<see cref="GangFront"/>) and the ground around it reads
    /// as theirs. There is no per-building deed anywhere in the project, and the design
    /// sheet's map needs one, because CLAIM flips a single building's colour.
    ///
    /// So this holds the override and nothing else: no cost, no cooldown, no contest, no
    /// consequence. What it means to take a building - who may, at what price, what the
    /// family whose ground it was does about it - is a rule that has not been written,
    /// and inventing one here would put a mechanic into the game through the map. When
    /// those rules arrive they go in front of <see cref="Claim"/>; everything the map
    /// itself does (the colour, the marker, the log line, the card) already works.
    ///
    /// Nothing here is saved. A campaign that outlives a session will want it in the
    /// outfit's own books rather than in a dictionary on a HUD.
    /// </summary>
    public sealed class MapOwnership
    {
        readonly Dictionary<int, int> _held = new Dictionary<int, int>();

        /// <summary>Bumped on every change, so the turf resolve and the cached building
        /// layer both know to look again.</summary>
        public int Version { get; private set; }

        public int Count => _held.Count;

        public bool TryGet(int buildingId, out int gangId) =>
            _held.TryGetValue(buildingId, out gangId);

        /// <summary>Hand a building to a family. Returns false when it was already
        /// theirs, which is what makes the card read ALREADY YOURS instead of firing a
        /// marker at a building nothing happened to.</summary>
        public bool Claim(int buildingId, int gangId)
        {
            if (_held.TryGetValue(buildingId, out var held) && held == gangId)
                return false;
            _held[buildingId] = gangId;
            Version++;
            return true;
        }

        public void Clear()
        {
            if (_held.Count == 0)
                return;
            _held.Clear();
            Version++;
        }
    }
}
