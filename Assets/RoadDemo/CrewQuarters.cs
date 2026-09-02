using System.Collections.Generic;
using LivingCity.Territory;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// THE MEN GO INSIDE AND STAY THERE.
    ///
    /// A crew of the outfit can be put INTO one of the family's own premises - the
    /// headquarters first of all, and any door the outfit holds the deed to. The order
    /// is one row on that door's menu ("TAKE THEM INSIDE"), the same row on all three
    /// surfaces the game offers a door's menu on, and what it does is exactly what it
    /// says: the crew walks to the doorstep, the men file through the door one at a
    /// time on their own feet (DoorBeat's passage, held open rather than timed), and
    /// they are off the street until somebody brings them out.
    ///
    /// Nothing else is claimed for it. Men inside are not a garrison, they do not
    /// answer a fight at the door and they collect nothing: they are indoors, which is
    /// the whole of what the player asked for ("udje u zgradu i to je to"). What it is
    /// FOR is the Don - he stands on his own street like every other lieutenant now, and
    /// a boss the player would rather not have standing in the open has somewhere to be.
    ///
    /// Keyed by crew id and static, like CrewJobs beside it: the crew is the unit of
    /// command, so a crew that loses its lieutenant and reforms under an heir stays
    /// where it was put. Reset at SubsystemRegistration - with domain reload off a stale
    /// crew id would billet next session's first crew in last session's building.
    /// </summary>
    public static class CrewQuarters
    {
        /// <summary>Near enough the doorstep for the men to start going through it.
        /// A crew stood off a door spreads over several metres, so this is the crew's
        /// reach and not the beat's own stride (DoorBeat.AtTheDoor) - each man walks
        /// the last stretch himself once he is told to go in.</summary>
        public const float ReachMetres = 9f;

        /// <summary>Seconds between one man going through the door and the next. They
        /// file in; they do not walk through each other.</summary>
        public const float FileSeconds = 0.7f;

        /// <summary>How long a crew may fail to reach the doorstep before the walk is
        /// sent out again - the same trick the doorstep errands keep (TendApproaches),
        /// because an order the player gave is a thing the game owes him.</summary>
        public const float MarchAgainSeconds = 25f;

        /// <summary>One crew, and the door it was put behind.</summary>
        sealed class Billet
        {
            /// <summary>The premises, as the simulation names it. Invalid on an
            /// authored scene's front, where the men hide at the doorstep instead.</summary>
            public TerritoryBusinessId Door;

            /// <summary>The pavement outside it - what they walk to, and what they come
            /// back out onto.</summary>
            public Vector3 Doorstep;

            /// <summary>The word painted outside, when the door carries one ("HQ"), so
            /// the crew's chip can say which of our places they are in.</summary>
            public string Word = "";

            public float MarchedAt;
            public float NextManAt;

            /// <summary>Everybody who could get in is in.</summary>
            public bool In;
        }

        static readonly Dictionary<int, Billet> Billets = new Dictionary<int, Billet>();
        static readonly List<int> Scratch = new List<int>();

        /// <summary>The crew whose march THIS class is issuing right now. Every other
        /// march is somebody giving the crew a different job, which ends the billet
        /// (<see cref="Retasked"/>); the walk to the door must not end the very order
        /// that sent it.</summary>
        static int _marching = -1;

        /// <summary>Is this crew off the street, inside one of our doors?</summary>
        public static bool Inside(DemoCrews.Unit unit) =>
            unit != null && Billets.TryGetValue(unit.CrewId, out var billet) && billet.In;

        /// <summary>Told to go in, and not all the way in yet - walking to the door, or
        /// filing through it.</summary>
        public static bool MovingIn(DemoCrews.Unit unit) =>
            unit != null && Billets.TryGetValue(unit.CrewId, out var billet) && !billet.In;

        /// <summary>Under a move-in order at all, however far along it is.</summary>
        public static bool Billeted(DemoCrews.Unit unit) =>
            unit != null && Billets.ContainsKey(unit.CrewId);

        /// <summary>Is this crew the one behind THIS door - the question the door's own
        /// menu asks before it offers to bring them out again.</summary>
        public static bool At(DemoCrews.Unit unit, TerritoryBusinessId door) =>
            unit != null && door.IsValid &&
            Billets.TryGetValue(unit.CrewId, out var billet) && billet.Door == door;

        /// <summary>The same question for a door the business directory cannot name -
        /// an authored scene's front, which is known only by the pavement outside it.
        /// </summary>
        public static bool AtDoorstep(DemoCrews.Unit unit, Vector3 doorstep)
        {
            if (unit == null || !Billets.TryGetValue(unit.CrewId, out var billet))
                return false;
            var gap = billet.Doorstep - doorstep;
            gap.y = 0f;
            return gap.sqrMagnitude <= SameDoorMetres * SameDoorMetres;
        }

        /// <summary>Two doorsteps this close are the same doorstep.</summary>
        const float SameDoorMetres = 3f;

        /// <summary>Where this crew stands with this door, in the words the shared order
        /// table reads. The one reading, so the street card, the paper map and the block
        /// file cannot offer TAKE THEM INSIDE against a hallway the men are already
        /// standing in.</summary>
        public static TerritoryQuartersState State(
            DemoCrews.Unit unit, TerritoryBusinessId door) =>
            At(unit, door) ? TerritoryQuartersState.Here
            : Billeted(unit) ? TerritoryQuartersState.Elsewhere
            : TerritoryQuartersState.None;

        /// <summary>The same reading for a door known only by its pavement point.</summary>
        public static TerritoryQuartersState StateAt(
            DemoCrews.Unit unit, Vector3 doorstep) =>
            AtDoorstep(unit, doorstep) ? TerritoryQuartersState.Here
            : Billeted(unit) ? TerritoryQuartersState.Elsewhere
            : TerritoryQuartersState.None;

        /// <summary>What a crew inside is doing, in the two words a chip has room for -
        /// and which of our places it is in, when the door carries a word of its own.
        /// Null for a crew that is out on the street like any other.</summary>
        public static string Word(DemoCrews.Unit unit)
        {
            if (unit == null || !Billets.TryGetValue(unit.CrewId, out var billet))
                return null;
            if (!billet.In)
                return "GOING INSIDE";
            return string.IsNullOrEmpty(billet.Word) ? "INSIDE" : "INSIDE " + billet.Word;
        }

        /// <summary>Where a billeted crew is, for the things that must not lose it while
        /// its men are switched off inside - the map's own fog, which reads a block off
        /// the men standing on it and would let the street go dark round a door the
        /// outfit is sitting in.</summary>
        public static bool TryGetDoorstep(DemoCrews.Unit unit, out Vector3 doorstep)
        {
            doorstep = Vector3.zero;
            if (unit == null || !Billets.TryGetValue(unit.CrewId, out var billet))
                return false;
            doorstep = billet.Doorstep;
            return true;
        }

        /// <summary>
        /// TAKE THEM INSIDE. The crew is sent to the premises and moves in when it gets
        /// there. The door's own pavement point and the word painted outside it are read
        /// here rather than passed in, so every surface that offers the row - the street
        /// card, the paper map, the block file - sends the men to the same place.
        /// </summary>
        public static bool Station(
            DemoCrews crews, DemoCrews.Unit unit, TerritoryBusinessId door)
        {
            if (!TryDoorstep(door, out var doorstep, out var word))
                return false;
            return Station(crews, unit, door, doorstep, word);
        }

        /// <summary>The same order against a door the business directory cannot name -
        /// an authored scene's front, known only by the pavement outside it.</summary>
        public static bool Station(
            DemoCrews crews, DemoCrews.Unit unit, Vector3 doorstep, string word) =>
            Station(crews, unit, default, doorstep, word);

        /// <summary>Where the men would go, and what the place is called on the street.
        /// False for a door with no pavement point anybody can name.</summary>
        public static bool TryDoorstep(
            TerritoryBusinessId door, out Vector3 doorstep, out string word)
        {
            doorstep = Vector3.zero;
            word = "";
            if (!door.IsValid)
                return false;

            // Our own front knows both: the exact pavement spot its door was cut for,
            // and the word painted outside it ("HQ").
            var fronts = GangFront.All;
            for (var i = 0; i < fronts.Count; i++)
            {
                var front = fronts[i];
                if (front == null || front.BusinessId != door)
                    continue;
                doorstep = front.Outside;
                word = front.Role ?? "";
                return true;
            }

            var runtime = TerritoryRuntime.Instance;
            return runtime != null && runtime.TryGetBusinessApproach(door, out doorstep);
        }

        static bool Station(
            DemoCrews crews, DemoCrews.Unit unit, TerritoryBusinessId door,
            Vector3 doorstep, string word)
        {
            if (crews == null || unit == null || unit.Faction != 0 || unit.Wiped)
                return false;

            // Another of our doors: they are not in two buildings at once. Out of that
            // one on the spot - the walk to this one is the beat the player watches.
            if (Billets.TryGetValue(unit.CrewId, out var standing))
            {
                var same = door.IsValid
                    ? standing.Door == door
                    : !standing.Door.IsValid && AtDoorstep(unit, doorstep);
                if (same)
                    return true;   // already going where he is being sent
                CallOut(unit);
            }

            Billets[unit.CrewId] = new Billet
            {
                Door = door,
                Doorstep = doorstep,
                Word = word ?? "",
                MarchedAt = Time.time,
                NextManAt = 0f,
            };

            // The walk is an ordinary march, so everything a march already settles -
            // the car left behind, the fight called off, the errand dropped - is
            // settled here too.
            March(crews, unit, doorstep);
            return true;
        }

        /// <summary>
        /// BRING THEM OUT. Out through the door they went in by, on their feet.
        /// </summary>
        public static void BringOut(DemoCrews.Unit unit) => Empty(unit, walkOut: true);

        /// <summary>
        /// Out NOW, because they were given something else to do. The reverse passage
        /// takes seconds and a man walking one cannot also be marching across the city -
        /// the beat would put him back at this door in the middle of it - so an order
        /// that retasks a billeted crew puts its men straight back on the pavement.
        /// </summary>
        public static void CallOut(DemoCrews.Unit unit) => Empty(unit, walkOut: false);

        /// <summary>
        /// The crew was given something else to do - a march, a mark, a car, a job off
        /// the book. Whatever it is, it is not standing in our hallway: the billet ends
        /// and the men are back on the pavement in the same frame, so the order that
        /// retasked them acts on men who can walk.
        /// </summary>
        public static void Retasked(DemoCrews.Unit unit)
        {
            if (unit == null || _marching == unit.CrewId)
                return;
            Empty(unit, walkOut: false);
        }

        /// <summary>Our own walk to the door, marked as ours so it does not read as the
        /// crew being retasked away from the order that issued it.</summary>
        static void March(DemoCrews crews, DemoCrews.Unit unit, Vector3 to)
        {
            _marching = unit.CrewId;
            crews.MarchTo(unit, to);
            _marching = -1;
        }

        static void Empty(DemoCrews.Unit unit, bool walkOut)
        {
            if (unit == null || !Billets.Remove(unit.CrewId))
                return;
            foreach (var man in unit.All())
            {
                if (man == null)
                    continue;
                if (walkOut)
                    DoorBeat.SendOut(man);
                else
                    DoorBeat.Evict(man);
            }
        }

        /// <summary>Whatever the books do to a crew, its billet follows the crew id.
        /// A crew that has left the street - wiped, disbanded - leaves no billet
        /// behind for the next crew to inherit that number.</summary>
        public static void Forget(int crewId) => Billets.Remove(crewId);

        public static void Tick(DemoCrews crews)
        {
            if (crews == null || Billets.Count == 0)
                return;

            Scratch.Clear();
            foreach (var crewId in Billets.Keys)
                Scratch.Add(crewId);

            for (var i = 0; i < Scratch.Count; i++)
            {
                var crewId = Scratch[i];
                if (!Billets.TryGetValue(crewId, out var billet))
                    continue;

                var unit = crews.UnitOfCrew(crewId);
                if (unit == null || unit.Wiped)
                {
                    Billets.Remove(crewId);
                    continue;
                }

                if (billet.In)
                {
                    // Held men are held by the beat, and the beat is what lets them go -
                    // a body struck off the books, a scene torn down. When the last of
                    // them is back on the street the crew is out, whatever put it out.
                    if (!AnybodyHeld(unit))
                        Billets.Remove(crewId);
                    continue;
                }

                FileIn(crews, unit, billet);
            }
        }

        /// <summary>The men going through the door, one at a time, and the walk sent
        /// out again for a crew that is not getting there.</summary>
        static void FileIn(DemoCrews crews, DemoCrews.Unit unit, Billet billet)
        {
            var waiting = false;
            var crossing = false;
            var nearest = float.MaxValue;

            foreach (var man in unit.All())
            {
                if (man == null || man.Dead || man.Tf == null)
                    continue;
                if (DoorBeat.Held(man))
                    continue;

                waiting = true;

                // A man already walking up to the door or crossing it is left to it -
                // the beat owns him until he is in, and a march sent out over the top of
                // it would pull him off the threshold.
                if (DoorBeat.Active(man))
                {
                    crossing = true;
                    continue;
                }

                var gap = man.Tf.position - billet.Doorstep;
                gap.y = 0f;
                var metres = gap.magnitude;
                if (metres < nearest)
                    nearest = metres;
                if (metres > ReachMetres || Time.time < billet.NextManAt)
                    continue;

                // In he goes. One a tick: the door is a door, not a gate.
                if (billet.Door.IsValid)
                    DoorBeat.MoveIn(man, billet.Door, billet.Doorstep);
                else
                    DoorBeat.MoveIn(man, billet.Doorstep);
                billet.NextManAt = Time.time + FileSeconds;
                billet.MarchedAt = Time.time;
                return;
            }

            if (!waiting)
            {
                billet.In = true;
                return;
            }

            // Nobody is near enough and nobody is crossing: the walk did not carry.
            // Send it out again rather than leave a crew stood in the street under an
            // order it never completed.
            if (!crossing && nearest > ReachMetres &&
                Time.time - billet.MarchedAt > MarchAgainSeconds)
            {
                billet.MarchedAt = Time.time;
                March(crews, unit, billet.Doorstep);
            }
        }

        static bool AnybodyHeld(DemoCrews.Unit unit)
        {
            foreach (var man in unit.All())
                if (man != null && !man.Dead && DoorBeat.Held(man))
                    return true;
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            Billets.Clear();
            Scratch.Clear();
            _marching = -1;
        }
    }
}
