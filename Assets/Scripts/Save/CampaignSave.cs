using System.Collections.Generic;
using System.IO;
using LivingCity.Business;
using LivingCity.Outfit;
using LivingCity.Territory;
using UnityEngine;

namespace LivingCity.Save
{
    /// <summary>
    /// SAVE AND LOAD, from nothing.
    ///
    /// <see cref="Compose"/> gathers the whole campaign out of the live ledgers and
    /// <see cref="Apply"/> puts it back; <see cref="Write"/> and <see cref="Read"/> are
    /// those two plus a file. The pair is kept apart on purpose: the headless suite round
    /// trips a campaign with no scene at all, which is the only way to know a save is
    /// complete rather than merely written.
    ///
    /// LOADING RELOADS THE SCENE. The city is generated from a seed, so the honest way
    /// to put a saved city back is to build it again from that seed and then restore the
    /// books over it - the same restart the user does by hand. <see cref="Pending"/> is
    /// where the file waits while that happens.
    /// </summary>
    public static class CampaignSave
    {
        /// <summary>Where the autosave goes (D19).</summary>
        public const string AutosaveName = "autosave.json";

        /// <summary>A file waiting for the scene to come back up. Static, and cleared on
        /// Play like every other static in this project.</summary>
        public static CampaignFile Pending;

        public static string Folder =>
            Path.Combine(Application.persistentDataPath, "gangsters");

        public static string AutosavePath => Path.Combine(Folder, AutosaveName);

        // ------------------------------------------------------------------ the file

        /// <summary>Writes the campaign to a file. Answers the refusal, or empty.
        /// </summary>
        public static string Write(string path)
        {
            return Write(Compose(), path);
        }

        /// <summary>The actual file writer, split from Compose so the save suite can put
        /// a deliberately exact campaign file through the same JSON/IO boundary.</summary>
        internal static string Write(CampaignFile file, string path)
        {
            if (file == null)
                return "there is no campaign to save";

            try
            {
                var folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder))
                    Directory.CreateDirectory(folder);
                File.WriteAllText(path, JsonUtility.ToJson(file, true));
                return "";
            }
            catch (System.Exception error)
            {
                return error.Message;
            }
        }

        /// <summary>Reads a file. Answers null and a refusal when it cannot be used.
        /// </summary>
        public static CampaignFile Read(string path, out string refusal)
        {
            refusal = "";
            if (!File.Exists(path))
            {
                refusal = "no save at " + path;
                return null;
            }

            CampaignFile file;
            try
            {
                file = JsonUtility.FromJson<CampaignFile>(File.ReadAllText(path));
            }
            catch (System.Exception error)
            {
                refusal = error.Message;
                return null;
            }

            if (file == null)
            {
                refusal = "the file is not a campaign";
                return null;
            }

            // A FILE FROM A LATER GAME IS REFUSED, not half-read. Half a campaign is
            // worse than none: the player would be playing a city missing whatever the
            // newer version added, with no way to tell.
            if (file.version > CampaignFile.Version)
            {
                refusal = "that save was written by a newer game (version " +
                          file.version + "; this one reads " + CampaignFile.Version + ")";
                return null;
            }

            return file;
        }

        /// <summary>
        /// LOAD. The city is generated from a seed, so the honest way to put a saved one
        /// back is to build it again from that seed and restore the books over it - the
        /// same restart the user does by hand. The file waits in Pending while the scene
        /// comes up; the territory runtime applies it on its first business tick, by
        /// which time the businesses are populated and the racket is running.
        ///
        /// Answers the refusal, or empty.
        /// </summary>
        public static string LoadFromFile(string path)
        {
            var file = Read(path, out var refusal);
            if (file == null)
                return refusal;

            Pending = file;
            Outfit.Underworld.ResetForPlay();
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
            return "";
        }

        // --------------------------------------------------------------- the gather

        /// <summary>Everything the campaign is, out of the live ledgers.</summary>
        public static CampaignFile Compose()
        {
            var underworld = Underworld.Current;
            if (underworld == null)
                return null;

            // A save is a public cut-off too: retain the incident as it stands and let
            // any later shots open a continuation after the snapshot.
            RoadDemo.PressDesk.Instance?.FlushOpenIncident();

            var runtime = RoadDemo.TerritoryRuntime.Instance;
            var business = BusinessRuntime.Instance;
            var clock = Object.FindFirstObjectByType<Ambient.CityClock>();

            var file = new CampaignFile
            {
                citySeed = underworld.CitySeed,
                day = underworld.Player != null ? underworld.Player.Runner.Campaign.Day : 1,
                hourOfDay = clock != null ? clock.Hour : 0f,
                underworld = OutfitSnapshot.Snapshot(underworld),
                territory = runtime != null
                    ? TerritorySnapshot.Snapshot(runtime.Racket, runtime.Dues, runtime.Rounds)
                    : new TerritoryDto(),
                deeds = Deeds(),
                prisoners = Save.PrisonSnapshot.Prisoners(Pipe()),
                escaped = Escapes(),
                prisonRosterSeed = Pipe() != null ? Pipe().RosterSeed : 0,
                cases = Save.PrisonSnapshot.Cases(Pipe()),
                nextCaseId = Pipe() != null ? Pipe().NextCaseId : 1,
                shutdowns = Shutdowns(business),
                knowledge = Knowledge(underworld),
                press = PressSnapshot.Snapshot(underworld.Press),
                lastEditionDay = underworld.Press.LastEditionDay,
            };
            // The physical first leg is intentionally not serialized. Its dispatcher
            // writes only the accepted recovery rule into this snapshot: unbooked men
            // return to the street wanted rather than becoming quietly free on load.
            Object.FindFirstObjectByType<RoadDemo.PoliceDispatch>()?
                .WriteCustodySaveFallback(file);
            return file;
        }

        /// <summary>Everything the campaign is, put back over a city already built from
        /// the same seed.</summary>
        public static void Apply(CampaignFile file)
        {
            if (file == null)
                return;

            var police = Object.FindFirstObjectByType<RoadDemo.PoliceForce>();
            Apply(
                file,
                Object.FindFirstObjectByType<Ambient.CityClock>(),
                RoadDemo.TerritoryRuntime.Instance,
                BusinessRuntime.Instance,
                police != null ? police.Pipeline : null);
        }

        /// <summary>The production load wiring with its scene dependencies resolved.
        /// Keeping the wiring in one callable seam lets the save suite exercise the same
        /// clock-before-deadline ordering without requiring or disturbing a loaded city.</summary>
        internal static void Apply(
            CampaignFile file,
            Ambient.CityClock clock,
            RoadDemo.TerritoryRuntime runtime,
            BusinessRuntime business,
            Police.PrisonPipeline prison)
        {
            if (file == null)
                return;

            // Put the central clock back first. Shutdown deadlines and every other
            // absolute-hour ledger must read the saved moment, not the new scene's
            // configured start hour. Campaign days are one-based; CityClock owns the
            // inverse conversion to its zero-based elapsed-day counter.
            if (clock != null)
                clock.Restore(file.day, file.hourOfDay);
            var savedGameHour = Ambient.CityClock.GameHourOfCampaignTime(
                file.day, file.hourOfDay);

            var underworld = Underworld.Ensure(file.citySeed);
            OutfitSnapshot.Restore(underworld, file.underworld);
            PressSnapshot.Restore(underworld.Press, file);

            if (runtime != null)
                TerritorySnapshot.Restore(
                    runtime.Racket, runtime.Dues, runtime.Rounds, file.territory);

            for (var i = 0; file.deeds != null && i < file.deeds.Length; i++)
                BusinessDeeds.SetGang(
                    new TerritoryBusinessId(file.deeds[i].businessId),
                    file.deeds[i].gangId, file.deeds[i].legacyBlockId);

            if (business?.Shutdowns != null && file.shutdowns != null)
            {
                var snapshots = new List<BusinessShutdownSnapshot>();
                for (var i = 0; i < file.shutdowns.Length; i++)
                    snapshots.Add(new BusinessShutdownSnapshot
                    {
                        BusinessId = file.shutdowns[i].businessId,
                        Cause = (BusinessShutdownCause)file.shutdowns[i].cause,
                        StartedAt = file.shutdowns[i].startedAt,
                        RecoveryAt = file.shutdowns[i].recoveryAt,
                    });
                business.Shutdowns.Restore(snapshots, savedGameHour);
            }

            for (var i = 0; file.knowledge != null && i < file.knowledge.Length; i++)
                RoadDemo.TurfKnowledge.RestoreFor(
                    file.knowledge[i].gangId, file.knowledge[i].places,
                    file.knowledge[i].men);

            // THE CELLS, after the rosters: the men have to exist before the pipe can
            // hold them.
            if (prison != null)
                Save.PrisonSnapshot.Restore(prison, file);
        }

        // ------------------------------------------------------------------- pieces

        static DeedDto[] Deeds()
        {
            var rows = new List<KeyValuePair<TerritoryBusinessId, BusinessDeeds.Deed>>();
            BusinessDeeds.Collect(rows);
            var dto = new DeedDto[rows.Count];
            for (var i = 0; i < rows.Count; i++)
                dto[i] = new DeedDto
                {
                    businessId = rows[i].Key.Value,
                    gangId = rows[i].Value.GangId,
                    legacyBlockId = rows[i].Value.LegacyBlockId,
                };
            return dto;
        }

        static ShutdownDto[] Shutdowns(BusinessRuntime business)
        {
            if (business?.Shutdowns == null)
                return new ShutdownDto[0];

            var rows = new List<BusinessShutdownSnapshot>();
            business.Shutdowns.CollectSnapshots(rows);
            var dto = new ShutdownDto[rows.Count];
            for (var i = 0; i < rows.Count; i++)
                dto[i] = new ShutdownDto
                {
                    businessId = rows[i].BusinessId,
                    cause = (int)rows[i].Cause,
                    startedAt = rows[i].StartedAt,
                    recoveryAt = rows[i].RecoveryAt,
                };
            return dto;
        }

        /// <summary>The city's pipe, or null in a scene that keeps no prisoners.</summary>
        static Police.PrisonPipeline Pipe()
        {
            var police = Object.FindFirstObjectByType<RoadDemo.PoliceForce>();
            return police != null ? police.Pipeline : null;
        }

        /// <summary>Everybody who has ever come out of the back of a car.</summary>
        static int[] Escapes()
        {
            var pipe = Pipe();
            if (pipe == null)
                return new int[0];
            var out_ = new List<int>();
            pipe.CollectEscapes(out_);
            return out_.ToArray();
        }

        static KnowledgeDto[] Knowledge(Underworld underworld)
        {
            var rows = new List<KnowledgeDto>();
            for (var g = 0; g < underworld.Count; g++)
            {
                RoadDemo.TurfKnowledge.CollectFor(g, out var places, out var men);
                if ((places == null || places.Length == 0) &&
                    (men == null || men.Length == 0))
                    continue;
                rows.Add(new KnowledgeDto { gangId = g, places = places, men = men });
            }
            return rows.ToArray();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => Pending = null;
    }
}
