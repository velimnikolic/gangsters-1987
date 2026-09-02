using System.Collections.Generic;

namespace LivingCity.Police
{
    /// <summary>What a station can lose. Two things, because a station is two things:
    /// men and cars.</summary>
    public enum PoliceLoss
    {
        Officer,
        Car,
    }

    /// <summary>One hole in a precinct's strength: what went, the day it went, and the
    /// day the department fills it. Absolute campaign days on both, never countdowns -
    /// a counter drifts over a long soak and a stored day cannot.</summary>
    public sealed class PoliceLossRecord
    {
        public PoliceLoss Kind;
        public int LostOnDay;
        public int BackOnDay;
    }

    /// <summary>
    /// A PRECINCT'S STRENGTH, as a fact about the campaign rather than a count of
    /// GameObjects.
    ///
    /// Before this the police were spawned once at scene build and never again: kill
    /// every officer and every car crew near you and the city had no law for the rest
    /// of the session, because Send found no unit and quietly did nothing. That is not
    /// a police force, it is a set of props with a finite number of lives.
    ///
    /// So a station has AUTHORISED strength - what the city pays for - and a list of
    /// the holes in it. The bodies on the street are VIEWS of this: a dead officer
    /// takes a man off the roster (through StreetAlarm, like every other death), and
    /// the department posts a replacement who arrives on a stated day. Strength never
    /// climbs above what the city authorised, and nothing is ever conjured mid-fight.
    ///
    /// Pure and free of UnityEngine like the rest of the sim's arithmetic.
    /// </summary>
    public sealed class PoliceRoster
    {
        public int StationId;

        /// <summary>What the plaque says - "PRECINCT 2".</summary>
        public string Name = "";

        /// <summary>What the city pays for. The ceiling on everything below.</summary>
        public int AuthorisedCars;
        public int AuthorisedOfficers;

        /// <summary>The holes, oldest first.</summary>
        public readonly List<PoliceLossRecord> Losses = new List<PoliceLossRecord>();

        /// <summary>The next shield number this station issues. Kept on the roster so a
        /// replacement's identity is dealt from (station, badge) and comes out the same
        /// on a second run of the same seed.</summary>
        public int NextBadge = 1;

        public PoliceRoster() { }

        public PoliceRoster(int stationId, string name, int cars, int officers)
        {
            StationId = stationId;
            Name = name ?? "";
            AuthorisedCars = cars < 0 ? 0 : cars;
            AuthorisedOfficers = officers < 0 ? 0 : officers;
        }

        /// <summary>Cars the station actually has.</summary>
        public int Cars => AuthorisedCars - Missing(PoliceLoss.Car);

        /// <summary>Men the station actually has.</summary>
        public int Officers => AuthorisedOfficers - Missing(PoliceLoss.Officer);

        /// <summary>Nothing left to send. The map says NO LAW over a precinct in this
        /// state, because the silence the player would otherwise get is indistinguishable
        /// from a bug.</summary>
        public bool Empty => Cars <= 0 && Officers <= 0;

        public int Missing(PoliceLoss kind)
        {
            var n = 0;
            for (var i = 0; i < Losses.Count; i++)
                if (Losses[i].Kind == kind)
                    n++;
            return n;
        }

        /// <summary>
        /// One down. Returns the hole it made, or null when the station had nothing of
        /// that kind left to lose - a roster cannot go negative, and a body counted
        /// twice (a death heard on two channels) must not cost the precinct two men.
        /// </summary>
        public PoliceLossRecord Lose(PoliceLoss kind, int today, PoliceRosterConfig config)
        {
            var held = kind == PoliceLoss.Car ? Cars : Officers;
            if (held <= 0)
                return null;

            var wait = config != null ? config.ReplacementDays(kind) : 0;
            var loss = new PoliceLossRecord
            {
                Kind = kind,
                LostOnDay = today,
                // A day that is not a day (a scene with no campaign behind it) can have
                // no replacement date either: the hole stands until a real calendar
                // turns up, rather than being filled on day zero by arithmetic.
                BackOnDay = today > 0 ? today + wait : 0,
            };
            Losses.Add(loss);
            return loss;
        }

        /// <summary>
        /// The day turned: every hole whose day has come is filled. Returns what was
        /// filled, so the scene can walk the new men out of the door and park the new
        /// car in its stall - the roster itself has no opinion about bodies.
        /// </summary>
        public int Replace(int today, List<PoliceLossRecord> filled)
        {
            filled?.Clear();
            var n = 0;
            for (var i = Losses.Count - 1; i >= 0; i--)
            {
                var loss = Losses[i];
                if (loss.BackOnDay <= 0 || loss.BackOnDay > today)
                    continue;
                Losses.RemoveAt(i);
                filled?.Add(loss);
                n++;
            }
            return n;
        }

        /// <summary>The shield number the next man off the bus wears.</summary>
        public int IssueBadge() => NextBadge++;

        /// <summary>What the plaque reads. One line, and it says the thing the player
        /// needs: how much law this end of town has, and when the rest of it is back.</summary>
        public string Plaque()
        {
            if (Empty)
                return (Name.Length > 0 ? Name + " — " : "") + "NO LAW — precinct empty";

            var line = (Name.Length > 0 ? Name + " — " : "") +
                       Cars + (Cars == 1 ? " car, " : " cars, ") +
                       Officers + (Officers == 1 ? " man on duty" : " men on duty");
            if (Losses.Count == 0)
                return line;

            var back = int.MaxValue;
            for (var i = 0; i < Losses.Count; i++)
                if (Losses[i].BackOnDay > 0 && Losses[i].BackOnDay < back)
                    back = Losses[i].BackOnDay;

            line += ", " + Losses.Count + " lost";
            if (back != int.MaxValue)
                line += ", back day " + back;
            return line;
        }
    }
}
