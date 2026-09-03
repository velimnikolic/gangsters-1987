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
    /// Ordinary crews inside are not a garrison and do not answer a fight at the door:
    /// they are indoors, which is the whole of the player's order. The one deliberate
    /// exception is GAN-273's bag detail; its own runtime AI may bring it out to defend
    /// the headquarters block, then file it back in when that block is clear.
    ///
    /// Keyed by the physical unit token: separate families may reuse a paper crew id,
    /// while a line, its bag detail, and every police pair need independent rooms.
    /// </summary>
    public static class CrewQuarters
    {
        readonly struct UnitKey : System.IEquatable<UnitKey>
        {
            public readonly int CrewId;
            public readonly bool Detachment;
            public readonly int RuntimeId;
            public UnitKey(DemoCrews.Unit unit)
            {
                CrewId = unit != null ? unit.CrewId : 0;
                Detachment = unit != null && unit.IsDetachment;
                // Crew numbers belong to organization books and overlap across
                // families; every police detail dealt by hand has CrewId zero.  The
                // physical unit token is the only identity that is unique across all
                // of them, and a Sync keeps the same Unit object/token while rebuilding
                // its members.
                RuntimeId = unit != null ? unit.CrowdGroupId : 0;
            }
            public bool Equals(UnitKey other) =>
                RuntimeId == other.RuntimeId;
            public override bool Equals(object obj) => obj is UnitKey other && Equals(other);
            public override int GetHashCode() => RuntimeId;
        }

        static UnitKey Key(DemoCrews.Unit unit) =>
            new UnitKey(unit);
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
            public DemoCrews.Unit Unit;
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

            /// <summary>The law, not the player, is walking surrendered men through
            /// the precinct door. Every other direct march still respects custody.</summary>
            public bool AllowCustody;

            /// <summary>Everybody who could get in is in.</summary>
            public bool In;
        }

        static readonly Dictionary<UnitKey, Billet> Billets =
            new Dictionary<UnitKey, Billet>();
        static readonly List<UnitKey> Scratch = new List<UnitKey>();

        /// <summary>The crew whose march THIS class is issuing right now. Every other
        /// march is somebody giving the crew a different job, which ends the billet
        /// (<see cref="Retasked"/>); the walk to the door must not end the very order
        /// that sent it.</summary>
        static UnitKey? _marching;

        /// <summary>Is this crew off the street, inside one of our doors?</summary>
        public static bool Inside(DemoCrews.Unit unit) =>
            unit != null && Billets.TryGetValue(Key(unit), out var billet) && billet.In;

        public static bool InsideHeadquarters(DemoCrews.Unit unit)
        {
            if (unit == null || !Billets.TryGetValue(Key(unit), out var billet) || !billet.In)
                return false;

            // "HQ" is painted outside every family's front. Headquarters is therefore
            // an identity - our actual door - never a role string a purchased rival
            // premises can keep carrying after its deed changes hands.
            var front = DemoCrews.PlayerFront();
            if (front != null)
            {
                if (front.BusinessId.IsValid && billet.Door == front.BusinessId)
                    return true;
                return SameDoorstep(billet.Doorstep, front.Outside);
            }

            var outfit = LivingCity.Gameplay.OutfitDirector.Instance;
            return outfit != null && outfit.TryGetHeadquarters(out var doorstep, out _) &&
                   SameDoorstep(billet.Doorstep, doorstep);
        }

        /// <summary>Told to go in, and not all the way in yet - walking to the door, or
        /// filing through it.</summary>
        public static bool MovingIn(DemoCrews.Unit unit) =>
            unit != null && Billets.TryGetValue(Key(unit), out var billet) && !billet.In;

        /// <summary>Under a move-in order at all, however far along it is.</summary>
        public static bool Billeted(DemoCrews.Unit unit) =>
            unit != null && Billets.ContainsKey(Key(unit));

        /// <summary>Is this crew the one behind THIS door - the question the door's own
        /// menu asks before it offers to bring them out again.</summary>
        public static bool At(DemoCrews.Unit unit, TerritoryBusinessId door) =>
            unit != null && door.IsValid &&
            Billets.TryGetValue(Key(unit), out var billet) && billet.Door == door;

        /// <summary>Is anybody of ours actually behind THIS door, all the way in? The
        /// map asks it of the hideout, so the plaque can say the men are in it rather
        /// than only that it exists.</summary>
        public static bool AnyoneInside(TerritoryBusinessId door)
        {
            if (!door.IsValid)
                return false;
            foreach (var billet in Billets.Values)
                if (billet.In && billet.Door == door)
                    return true;
            return false;
        }

        /// <summary>The same question for a door the business directory cannot name -
        /// an authored scene's front, which is known only by the pavement outside it.
        /// </summary>
        public static bool AtDoorstep(DemoCrews.Unit unit, Vector3 doorstep)
        {
            if (unit == null || !Billets.TryGetValue(Key(unit), out var billet))
                return false;
            return SameDoorstep(billet.Doorstep, doorstep);
        }

        static bool SameDoorstep(Vector3 one, Vector3 other)
        {
            var gap = one - other;
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
            if (unit == null || !Billets.TryGetValue(Key(unit), out var billet))
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
            if (unit == null || !Billets.TryGetValue(Key(unit), out var billet))
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
            DemoCrews crews, DemoCrews.Unit unit, TerritoryBusinessId door,
            bool speak = false, bool allowCustody = false)
        {
            if (!TryDoorstep(door, out var doorstep, out var word))
                return false;
            return Station(crews, unit, door, doorstep, word, speak, allowCustody);
        }

        /// <summary>The same order against a door the business directory cannot name -
        /// an authored scene's front, known only by the pavement outside it.</summary>
        public static bool Station(
            DemoCrews crews, DemoCrews.Unit unit, Vector3 doorstep, string word,
            bool speak = false, bool allowCustody = false) =>
            Station(crews, unit, default, doorstep, word, speak, allowCustody);

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

        /// <summary>
        /// <paramref name="speak"/> is the crew's answer, and it is OFF by default. Men are
        /// put indoors by the game as often as by the player - a wanted man going to ground,
        /// a crew standing up inside its own headquarters at the start of a run - and a
        /// lieutenant announcing that they are off the street each time is chatter nobody
        /// asked for. The rows the PLAYER clicks turn it on.
        /// </summary>
        static bool Station(
            DemoCrews crews, DemoCrews.Unit unit, TerritoryBusinessId door,
            Vector3 doorstep, string word, bool speak, bool allowCustody)
        {
            // ANY house's crew may be taken indoors - a family's Don keeps to his own
            // premises (D4) by exactly the call the player's TAKE THEM INSIDE row makes.
            // A permanent police beat also uses this passage at its station door; the
            // direct Unit on the billet keeps it distinct from an underworld crew id.
            if (crews == null || unit == null || unit.Wiped)
                return false;
            if (!allowCustody && !crews.AcceptsPlayerOrder(unit))
                return false;

            // Another of our doors: they are not in two buildings at once. Out of that
            // one on the spot - the walk to this one is the beat the player watches.
            var key = Key(unit);
            if (Billets.TryGetValue(key, out var standing))
            {
                var same = door.IsValid
                    ? standing.Door == door
                    : !standing.Door.IsValid && AtDoorstep(unit, doorstep);
                if (same)
                    return true;   // already going where he is being sent
                CallOut(unit);
            }

            Billets[key] = new Billet
            {
                Unit = unit,
                Door = door,
                Doorstep = doorstep,
                Word = word ?? "",
                MarchedAt = Time.time,
                NextManAt = 0f,
                AllowCustody = allowCustody,
            };

            // The walk is an ordinary march, so everything a march already settles -
            // the car left behind, the fight called off, the errand dropped - is
            // settled here too.
            March(crews, unit, doorstep, allowCustody);
            if (speak)
                CrewSpeech.Say(unit, LivingCity.Data.VoiceLines.OrdInside);
            return true;
        }

        /// <summary>
        /// BRING THEM OUT. Out through the door they went in by, on their feet.
        /// </summary>
        public static void BringOut(DemoCrews.Unit unit, bool speak = false)
        {
            // Only a crew that WAS inside answers, and only when the player asked: the
            // collection round brings its own bag man out every day, and the street calls
            // this defensively on every retask.
            var housed = speak && unit != null && Billets.ContainsKey(Key(unit));
            Empty(unit, walkOut: true);
            if (housed)
                CrewSpeech.Say(unit, LivingCity.Data.VoiceLines.OrdOutside);
        }

        /// <summary>True once every living man has completed the reverse doorway beat
        /// and is visibly back on the pavement.</summary>
        public static bool AllOutside(DemoCrews.Unit unit)
        {
            if (unit == null)
                return false;
            var found = false;
            foreach (var man in unit.All())
            {
                if (man == null || man.Dead || man.Tf == null)
                    continue;
                found = true;
                if (DoorBeat.Active(man) || !man.Tf.gameObject.activeInHierarchy)
                    return false;
            }
            return found;
        }

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
            if (unit == null || (_marching.HasValue && _marching.Value.Equals(Key(unit))))
                return;
            Empty(unit, walkOut: false);
        }

        /// <summary>Our own walk to the door, marked as ours so it does not read as the
        /// crew being retasked away from the order that issued it.</summary>
        static void March(DemoCrews crews, DemoCrews.Unit unit, Vector3 to,
            bool allowCustody = false)
        {
            _marching = Key(unit);
            crews.MarchTo(unit, to, allowCustody: allowCustody);
            _marching = null;
        }

        static void Empty(DemoCrews.Unit unit, bool walkOut)
        {
            if (unit == null || !Billets.Remove(Key(unit)))
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
        public static void Forget(int crewId)
        {
            Scratch.Clear();
            foreach (var key in Billets.Keys)
                if (key.CrewId == crewId) Scratch.Add(key);
            for (var i = 0; i < Scratch.Count; i++) Billets.Remove(Scratch[i]);
        }

        public static void Forget(DemoCrews.Unit unit)
        {
            if (unit != null)
                Billets.Remove(Key(unit));
        }

        public static void Tick(DemoCrews crews)
        {
            if (crews == null || Billets.Count == 0)
                return;

            Scratch.Clear();
            foreach (var key in Billets.Keys)
                Scratch.Add(key);

            for (var i = 0; i < Scratch.Count; i++)
            {
                var key = Scratch[i];
                if (!Billets.TryGetValue(key, out var billet))
                    continue;

                var unit = billet.Unit != null
                    ? billet.Unit
                    : key.Detachment
                        ? crews.BagUnitOf(key.CrewId)
                        : crews.UnitOfCrew(key.CrewId);
                if (unit == null || unit.Wiped)
                {
                    Billets.Remove(key);
                    continue;
                }

                if (billet.In)
                {
                    // Held men are held by the beat, and the beat is what lets them go -
                    // a body struck off the books, a scene torn down. When the last of
                    // them is back on the street the crew is out, whatever put it out.
                    if (!AnybodyHeld(unit))
                        Billets.Remove(key);
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
                March(crews, unit, billet.Doorstep, billet.AllowCustody);
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
            _marching = null;
        }
    }
}
