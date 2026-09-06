using System.Collections.Generic;
using System.Linq;
using LivingCity.Territory;
using RoadDemo;
using Unity.Pipeline.Commands;
using UnityEngine;

namespace GangstersTools
{
    /// <summary>
    /// THE LAW IN THIS CITY, COUNTED (GAN-236).
    ///
    /// The precinct work had no instrument: `gangsters_police_precinct_audit` judges the
    /// PREFAB, `gangsters_police_tests` judges the pure model, and nothing at all reported
    /// the force that is actually standing in a built city. So the ticket's acceptance -
    /// "a seeded city has N stations, each answers its own ground, kill one and that end
    /// of town reads NO LAW, kill an officer and cars come off more than one roster" - was
    /// only ever provable by watching the screen.
    ///
    /// This is that instrument, and it is READ-ONLY: it founds nothing, sends nobody, and
    /// changes no state. It reports what stands, and it FAILS on the things that would be
    /// silent otherwise - a station house the map never seated, a precinct policing no
    /// ground, blocks nobody polices, a car on a roster that does not exist.
    ///
    ///   unity command gangsters_police_probe --json
    ///   unity command gangsters_police_probe --rows true --json
    /// </summary>
    public static class PoliceForceProbe
    {
        /// <summary>How many unpoliced block ids the report names before it stops - a city
        /// with a plan fault should say WHICH blocks, not print two hundred of them.</summary>
        const int NamedBlocks = 12;

        [CliCommand("gangsters_police_probe",
                    "Report the live police force and judge it: the station houses this " +
                    "city founded, each one's roster against what actually stands, its " +
                    "plaque, the blocks it polices off the precinct map, the blocks nobody " +
                    "polices, and - during a hunt - how many cars are out and off whose " +
                    "rosters. Read-only: it founds nothing and sends nobody.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "police", "precinct", "audit" })]
        public static object Probe(
            [CliArg("rows", "List the blocks each station house polices.")] bool rows = false,
            [CliArg("blocks", "List every block with the house that polices it.")]
            bool blocks = false)
        {
            var force = PoliceForce.Instance != null
                ? PoliceForce.Instance
                : Object.FindAnyObjectByType<PoliceForce>();
            if (force == null)
                return new
                {
                    passed = false,
                    playing = Application.isPlaying,
                    reason = Application.isPlaying
                        ? "no police force in this scene - the city stood no station house, " +
                          "or it has not been built yet"
                        : "Play Mode is not running; there is no city to report",
                };

            var faults = new List<string>();
            var territory = TerritoryRuntime.Instance;
            var geography = territory != null ? territory.Geography : null;
            var full = geography as TerritoryGeography;

            // ------------------------------------------------------- the map's own word
            //
            // Counted off the geography rather than off the force, because that is the
            // half of the answer the force does not have: which GROUND each house holds.
            var held = new Dictionary<int, int>();
            var unpoliced = new List<string>();
            var everyBlock = new List<object>();
            var cityBlocks = geography != null ? geography.BlockIds.Count : 0;
            if (geography != null)
                for (var i = 0; i < geography.BlockIds.Count; i++)
                {
                    var blockId = geography.BlockIds[i];
                    var owner = geography.PrecinctOf(blockId);
                    if (owner == TerritoryPrecinctMap.NoPrecinct)
                    {
                        if (unpoliced.Count < NamedBlocks) unpoliced.Add(blockId.Value);
                        continue;
                    }

                    held.TryGetValue(owner, out var count);
                    held[owner] = count + 1;
                    if (blocks)
                        everyBlock.Add(new
                        {
                            block = blockId.Value,
                            precinct = owner,
                            hops = full != null ? full.Precincts.HopsToStation(blockId) : -1,
                        });
                }

            var unpolicedCount = cityBlocks - held.Values.Sum();

            // --------------------------------------------------------------- the houses
            var seen = new HashSet<int>();
            var report = new List<object>();
            var carsStanding = 0;
            var menStanding = 0;
            var silent = 0;
            foreach (var precinct in force.Precincts)
            {
                var roster = precinct?.Roster;
                if (roster == null)
                {
                    faults.Add("PROBE: a precinct was founded with no roster.");
                    continue;
                }

                if (!seen.Add(roster.StationId))
                    faults.Add("PROBE: two precincts both call themselves station " +
                               roster.StationId + ".");

                var seat = geography != null
                    ? geography.PrecinctSeats.FirstOrDefault(
                        house => house.StationId == roster.StationId)
                    : default;
                var seated = seat.Block.IsValid;
                if (geography != null && !seated)
                    faults.Add("PROBE: station " + roster.StationId + " (" + roster.Name +
                               ") stands in the city but sits on no block of the precinct " +
                               "map; it answers calls the map cannot route to it.");

                held.TryGetValue(roster.StationId, out var ground);
                if (geography != null && seated && ground == 0)
                    faults.Add("PROBE: station " + roster.StationId + " (" + roster.Name +
                               ") polices no block at all.");

                // What is on the ground against what the books say it may have. More cars
                // standing than the roster allows means a loss was never booked, which is
                // exactly the fault a replacement day would otherwise hide.
                var bodies = 0;
                var fleetworthy = 0;
                var free = 0;
                for (var i = 0; i < precinct.Cars.Count; i++)
                {
                    var car = precinct.Cars[i];
                    if (car == null) continue;
                    bodies++;
                    if (car.Fleetworthy) fleetworthy++;
                    if (car.Available) free++;
                    if (car.Precinct != roster.StationId)
                        faults.Add("PROBE: a car in " + roster.Name + "'s yard is booked to " +
                                   "station " + car.Precinct + ".");
                }

                var men = 0;
                for (var i = 0; i < precinct.Leads.Count; i++)
                {
                    var lead = precinct.Leads[i];
                    if (lead?.Unit == null) continue;
                    men += lead.Unit.Size();
                    if (lead.Available) free++;
                    if (lead.Precinct != roster.StationId)
                        faults.Add("PROBE: a beat on " + roster.Name + "'s watch is booked to " +
                                   "station " + lead.Precinct + ".");
                }

                if (fleetworthy > roster.Cars)
                    faults.Add("PROBE: " + roster.Name + " has " + fleetworthy + " cars on the " +
                               "street and " + roster.Cars + " on its books.");

                carsStanding += fleetworthy;
                menStanding += men;
                // A HOUSE WITH NOTHING FREE ANSWERS NOTHING. Not a fault - it is the state
                // the ticket asks to be able to SEE, and the plaque says so in words - but
                // it is counted, because "that end of town went quiet" has to be a number
                // before a report is evidence of it.
                if (free == 0) silent++;

                var farthest = -1;
                if (full != null && rows)
                    foreach (var blockId in full.Precincts.GroundOf(roster.StationId))
                    {
                        var hops = full.Precincts.HopsToStation(blockId);
                        if (hops > farthest) farthest = hops;
                    }

                report.Add(new
                {
                    id = roster.StationId,
                    name = roster.Name,
                    plaque = roster.Plaque(),
                    empty = roster.Empty,
                    seat = seated ? seat.Block.Value : "",
                    blocks = ground,
                    farthestBlockInStreets = farthest,
                    carsAuthorised = roster.AuthorisedCars,
                    carsOnTheBooks = roster.Cars,
                    carsStanding = fleetworthy,
                    carBodies = bodies,
                    menAuthorised = roster.AuthorisedOfficers,
                    menOnTheBooks = roster.Officers,
                    menStanding = men,
                    beats = precinct.Leads.Count,
                    unitsFreeToSend = free,
                    answersItsGround = free > 0,
                    at = Round(precinct.At),
                    countyLine = Round(precinct.CountyLine),
                    ground = rows && full != null
                        ? full.Precincts.GroundOf(roster.StationId).Select(b => b.Value).ToArray()
                        : null,
                });
            }

            if (force.Precincts.Count == 0)
                faults.Add("PROBE: this city founded no precinct at all - no house had both " +
                           "a forecourt and a car.");
            else if (geography != null && unpolicedCount > 0)
                faults.Add("PROBE: " + unpolicedCount + " of " + cityBlocks + " blocks are " +
                           "policed by nobody while the city has " + force.Precincts.Count +
                           " station house(s).");

            // A station house the geography could not seat is already a geography fault;
            // carry it through so one call is the whole verdict.
            if (geography != null)
                foreach (var line in geography.Report.Faults)
                    if (line.Contains("station house"))
                        faults.Add("PROBE: " + line);

            // ---------------------------------------------------------------- the hunt
            var dispatch = Object.FindAnyObjectByType<PoliceDispatch>();
            var answering = new List<int>();
            var swarmCars = dispatch != null ? dispatch.SwarmAnswer(answering) : 0;

            return new
            {
                passed = faults.Count == 0,
                playing = Application.isPlaying,
                stations = force.Precincts.Count,
                stationsThatCanAnswer = force.Precincts.Count - silent,
                stationsGoneQuiet = silent,
                watch = force.WatchKnown ? force.Watch.ToString() : "no clock yet",
                carsStanding,
                menStanding,
                cityBlocks,
                policedBlocks = cityBlocks - unpolicedCount,
                unpolicedBlocks = unpolicedCount,
                unpolicedNamed = unpoliced.ToArray(),
                swarming = dispatch != null && dispatch.Swarming,
                swarmCars,
                swarmCap = PoliceDispatch.SwarmCarCap,
                swarmPrecincts = answering.ToArray(),
                precincts = report.ToArray(),
                everyBlock = blocks ? everyBlock.ToArray() : null,
                faults = faults.ToArray(),
            };
        }

        /// <summary>A world point at metre resolution: a report is read by a person, and
        /// six decimals of float noise is not evidence of anything.</summary>
        static string Round(Vector3 where) =>
            Mathf.RoundToInt(where.x) + "," + Mathf.RoundToInt(where.z);
    }
}
