using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RoadDemo;
using LivingCity.Personnel;
using LivingCity.Police;
using UnityEngine;

namespace LivingCity.Tests
{
    public static partial class MiniCoreRegressionTests
    {
        sealed class AlarmScope : IDisposable
        {
            readonly Dictionary<FieldInfo, object> saved = new Dictionary<FieldInfo, object>();
            public AlarmScope()
            {
                foreach (var field in typeof(StreetAlarm).GetFields(BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (field.IsLiteral) continue;
                    var value = field.GetValue(null);
                    saved[field] = value is Array array ? array.Clone() : value;
                }
            }
            public void Dispose()
            {
                foreach (var pair in saved)
                    if (pair.Value is Array previous && pair.Key.GetValue(null) is Array current)
                        Array.Copy(previous, current, previous.Length);
                    else if (!pair.Key.IsInitOnly) pair.Key.SetValue(null, pair.Value);
            }
        }

        static void ScopedSwarmCharges()
        {
            using var alarm = new AlarmScope();
            var priorWorld = LivingCity.Outfit.Underworld.Current;
            try
            {
                for (int repetition = 0; repetition < 5; repetition++)
                {
                    using var fixture = new Fixture();
                    var world = LivingCity.Outfit.Underworld.Deal(1987 + repetition, 3);
                    typeof(LivingCity.Outfit.Underworld).GetProperty("Current").SetValue(null, world);
                    var crews = fixture.Root.AddComponent<DemoCrews>();
                    var dispatch = fixture.Root.AddComponent<PoliceDispatch>();
                    Write(dispatch, "_crews", crews);
                    var distant = fixture.Unit(fixture.Man());
                    distant.Faction = distant.Boss.Faction = 1; distant.CrewId = 1001;
                    distant.Boss.CharacterId = world.Of(1).Roster.Members[1].Id;
                    var culprit = fixture.Unit(fixture.Man());
                    culprit.Faction = culprit.Boss.Faction = 2; culprit.CrewId = 2001;
                    culprit.Boss.CharacterId = world.Of(2).Roster.Members[1].Id;
                    culprit.Boss.Tf.position += Vector3.right * 500f;
                    crews.Units.Add(distant); crews.Units.Add(culprit);
                    var pipeline = new PrisonPipeline();
                    culprit.ArrestCase = pipeline.OpenCase(Deed.Extortion, 2, 1, 4);
                    culprit.ArrestDeed = Deed.Extortion;
                    StreetAlarm.Report(distant.Position, distant.Boss, 1, 40f);
                    var raise = typeof(PoliceDispatch).GetMethod("RaiseSwarm", Private);
                    var grade = raise.GetParameters()[1].ParameterType;
                    var hunted = (IList)Read(dispatch, "_hunted");
                    Call(dispatch, "RaiseSwarm", culprit.Position, Enum.Parse(grade, "ShotsFired"), culprit);
                    Require(!hunted.Contains(distant) && hunted.Contains(culprit),
                        "shooting 500 metres away made an unrelated crew a swarm suspect");
                    Require(culprit.ArrestDeed == Deed.AssaultOnOfficer,
                        "firing at an officer failed to record the actual shooter's assault");
                    Call(dispatch, "RaiseSwarm", distant.Position, Enum.Parse(grade, "ShotsFired"), distant);
                    StreetAlarm.Death(culprit.Position, StreetAlarm.DeathOf.Officer, attacker: culprit.Boss);
                    Call(dispatch, "RaiseSwarm", culprit.Position, Enum.Parse(grade, "OfficerDown"), culprit);
                    Require(distant.ArrestDeed == Deed.AssaultOnOfficer &&
                        culprit.ArrestDeed == Deed.CopKilling && culprit.ArrestCase.Deed == Deed.CopKilling &&
                        culprit.ArrestCase.ExtraCharges.Contains(Deed.Extortion) &&
                        culprit.ArrestCase.ExtraCharges.Contains(Deed.AssaultOnOfficer),
                        "one officer death upgraded every hunted crew's charge");
                    Require((Deed)Call(dispatch, "TheDeed", distant) == Deed.AssaultOnOfficer &&
                        (Deed)Call(dispatch, "TheDeed", culprit) == Deed.CopKilling,
                        "arrest recreated another crew's cop-killing charge from the citywide death count");
                    var answerCase = pipeline.OpenCase(Deed.Extortion, 1, 1, 4);
                    Write(dispatch, "_arrestCrew", distant);
                    Write(dispatch, "_arrestCase", answerCase);
                    Write(dispatch, "_arrestDeed", Deed.Extortion);
                    Call(dispatch, "Fight", true);
                    Require(answerCase.Deed == Deed.AssaultOnOfficer,
                        "refusing arrest copied an unrelated officer death into the new case");
                    Call(dispatch, "StandDown");
                    Require(world.Of(1).Roster.Find(distant.Boss.CharacterId).WantedLevel == WantedLevels.ShotAtOfficer &&
                        world.Of(2).Roster.Find(culprit.Boss.CharacterId).WantedLevel == WantedLevels.CopKiller,
                        "ending the swarm marked every escapee as a cop-killer");
                }
            }
            finally { typeof(LivingCity.Outfit.Underworld).GetProperty("Current").SetValue(null, priorWorld); }
        }

        static void OfficerDeathRetainsAttacker()
        {
            using var alarm = new AlarmScope();
            for (int repetition = 0; repetition < 5; repetition++)
            {
                using var fixture = new Fixture();
                var crews = fixture.Root.AddComponent<DemoCrews>();
                var dispatch = fixture.Root.AddComponent<PoliceDispatch>();
                Write(dispatch, "_crews", crews);
                var unrelated = fixture.Unit(fixture.Man()); unrelated.Faction = unrelated.Boss.Faction = 1;
                var culprit = fixture.Unit(fixture.Man()); culprit.Faction = culprit.Boss.Faction = 2;
                culprit.Boss.Tf.position += Vector3.right * 500f;
                crews.Units.Add(unrelated); crews.Units.Add(culprit);
                StreetAlarm.Report(unrelated.Position, unrelated.Boss, 1, 40f);
                Action<Vector3, StreetAlarm.DeathOf, int> heard = (at, kind, faction) =>
                    Call(dispatch, "OnDeath", at, kind, faction);
                StreetAlarm.OnDeath += heard;
                try
                {
                    StreetAlarm.Death(culprit.Position, StreetAlarm.DeathOf.Officer, attacker: culprit.Boss);
                    var hunted = (IList)Read(dispatch, "_hunted");
                    Require(StreetAlarm.LastDeathAttacker == culprit.Boss && hunted.Count == 1 &&
                        hunted.Contains(culprit) && culprit.ArrestDeed == Deed.CopKilling,
                        "the shared death event lost its known attacker or accused an unrelated shooter");
                    StreetAlarm.Death(unrelated.Position, StreetAlarm.DeathOf.Officer);
                    Require(StreetAlarm.LastDeathAttacker == null && hunted.Count == 1 && !hunted.Contains(unrelated),
                        "an unattributed death inherited the last killer or blamed another nearby crew");
                }
                finally { StreetAlarm.OnDeath -= heard; }
            }
        }
    }
}
