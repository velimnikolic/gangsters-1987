using System;
using System.Collections.Generic;
using UnityEngine;
using LivingCity.Generation;

namespace LivingCity.Entities
{
    public enum PedestrianPopulationGroup
    {
        Hispanic,
        White,
        Black,
        Asian,
        Other
    }

    public enum PedestrianAgeCohort
    {
        Adult,
        SchoolChild
    }

    public readonly struct PedestrianAnthropometrySample
    {
        public PedestrianAnthropometrySample(
            PedestrianPopulationGroup populationGroup,
            bool female,
            int ageYears,
            float heightMetres)
        {
            PopulationGroup = populationGroup;
            Female = female;
            AgeYears = ageYears;
            HeightMetres = heightMetres;
        }

        public PedestrianPopulationGroup PopulationGroup { get; }
        public bool Female { get; }
        public int AgeYears { get; }
        public float HeightMetres { get; }
        public float HeightCentimetres => HeightMetres * 100f;
        public bool IsSchoolChild => AgeYears < 18;
    }

    [DisallowMultipleComponent]
    public sealed class PedestrianAnthropometryStamp : MonoBehaviour
    {
        public int Seed { get; private set; }
        public bool Female { get; private set; }
        public int AgeYears { get; private set; }
        public PedestrianPopulationGroup PopulationGroup { get; private set; }
        public float HeightMetres { get; private set; }
        public float AppliedScale { get; private set; }
        public float OverlayHeight { get; private set; }

        internal void Configure(int seed, PedestrianAnthropometrySample sample, float scale)
        {
            Seed = seed;
            Female = sample.Female;
            AgeYears = sample.AgeYears;
            PopulationGroup = sample.PopulationGroup;
            HeightMetres = sample.HeightMetres;
            AppliedScale = scale;
            OverlayHeight = Mathf.Clamp(HeightMetres + 0.25f,
                                        sample.IsSchoolChild ? 1.3f : 1.75f,
                                        sample.IsSchoolChild ? 2.05f : 2.55f);
        }
    }

    /// <summary>
    /// Deterministic height sampler for spawned people.
    ///
    /// Population mix: ACS 2024 1-year Miami city profile, CensusReporter view of Census data.
    /// Adult height curves: CDC/NHANES 2015-2018 table by sex and race/Hispanic-origin.
    /// Adult age adjustments: CDC/NHANES adult median by sex and age band.
    /// Child height curves: CDC/NHANES child/adolescent table by sex and age. A few withheld
    /// 95th-percentile cells are filled from the 85th-to-90th slope, then all draws clamp to
    /// the 5th-to-95th range to avoid visual outliers.
    /// </summary>
    public static class PedestrianAnthropometry
    {
        public const int CivilianSalt = 101;
        public const int PoliceSalt = 102;
        public const int DockWorkerSalt = 103;
        public const int SchoolChildSalt = 104;
        public const int GangSalt = 105;

        const float MinMeasurableHeightMetres = 0.25f;
        const float DefaultAuthoredHeightMetres = 1.75f;
        const float MinScale = 0.55f;
        const float MaxScale = 1.45f;

        static readonly float[] Percentiles =
        {
            0.05f, 0.10f, 0.15f, 0.25f, 0.50f, 0.75f, 0.85f, 0.90f, 0.95f
        };

        static readonly WeightedGroup[] MiamiGroups =
        {
            new(PedestrianPopulationGroup.Hispanic, 71.46f),
            new(PedestrianPopulationGroup.White, 13.29f),
            new(PedestrianPopulationGroup.Black, 10.02f),
            new(PedestrianPopulationGroup.Asian, 1.83f),
            new(PedestrianPopulationGroup.Other, 3.40f)
        };

        static readonly WeightedAge[] MaleAdultAges =
        {
            new(20, 29, 14.89f),
            new(30, 39, 20.33f),
            new(40, 49, 12.41f),
            new(50, 59, 12.87f),
            new(60, 69, 11.20f),
            new(70, 79, 6.10f),
            new(80, 89, 3.08f)
        };

        static readonly WeightedAge[] FemaleAdultAges =
        {
            new(20, 29, 14.90f),
            new(30, 39, 17.06f),
            new(40, 49, 12.55f),
            new(50, 59, 12.65f),
            new(60, 69, 11.96f),
            new(70, 79, 7.59f),
            new(80, 89, 5.49f)
        };

        static readonly WeightedAge[] MaleSchoolAges =
        {
            new(7, 7, 1.067f),
            new(8, 8, 1.067f),
            new(9, 9, 1.067f),
            new(10, 10, 0.843f),
            new(11, 11, 0.843f),
            new(12, 12, 0.843f),
            new(13, 13, 0.843f),
            new(14, 14, 0.843f),
            new(15, 15, 0.843f),
            new(16, 16, 0.843f),
            new(17, 17, 0.843f)
        };

        static readonly WeightedAge[] FemaleSchoolAges =
        {
            new(7, 7, 0.869f),
            new(8, 8, 0.869f),
            new(9, 9, 0.869f),
            new(10, 10, 0.910f),
            new(11, 11, 0.910f),
            new(12, 12, 0.910f),
            new(13, 13, 0.910f),
            new(14, 14, 0.910f),
            new(15, 15, 0.910f),
            new(16, 16, 0.910f),
            new(17, 17, 0.910f)
        };

        static readonly float[] MaleHispanicAdult =
        {
            158.1f, 160.7f, 162.7f, 165.4f, 170.3f, 175.4f, 178.2f, 179.7f, 182.8f
        };

        static readonly float[] MaleWhiteAdult =
        {
            165.6f, 167.8f, 169.3f, 171.7f, 176.7f, 181.4f, 183.6f, 185.4f, 187.8f
        };

        static readonly float[] MaleBlackAdult =
        {
            164.7f, 167.3f, 169.0f, 171.3f, 175.9f, 180.5f, 183.2f, 184.8f, 188.1f
        };

        static readonly float[] MaleAsianAdult =
        {
            158.4f, 161.2f, 162.6f, 165.6f, 170.4f, 175.8f, 178.1f, 179.7f, 182.1f
        };

        static readonly float[] MaleAllAdult =
        {
            162.8f, 165.8f, 167.6f, 170.1f, 175.4f, 180.2f, 182.9f, 184.7f, 187.4f
        };

        static readonly float[] FemaleHispanicAdult =
        {
            146.5f, 149.2f, 150.7f, 152.9f, 157.0f, 161.7f, 164.0f, 166.1f, 168.9f
        };

        static readonly float[] FemaleWhiteAdult =
        {
            151.8f, 153.7f, 154.9f, 157.8f, 162.2f, 166.9f, 169.4f, 170.9f, 173.3f
        };

        static readonly float[] FemaleBlackAdult =
        {
            151.5f, 154.0f, 155.7f, 158.2f, 162.7f, 166.7f, 168.9f, 170.5f, 173.0f
        };

        static readonly float[] FemaleAsianAdult =
        {
            146.4f, 148.6f, 149.8f, 152.2f, 156.3f, 160.2f, 162.4f, 164.1f, 166.3f
        };

        static readonly float[] FemaleAllAdult =
        {
            149.8f, 152.5f, 153.9f, 156.4f, 161.3f, 166.0f, 168.4f, 170.2f, 172.5f
        };

        static readonly float[] MaleAge7 = { 116.5f, 118.2f, 120.2f, 122.3f, 126.1f, 129.8f, 132.3f, 134.3f, 136.4f };
        static readonly float[] MaleAge8 = { 121.2f, 124.2f, 125.9f, 127.8f, 131.8f, 136.4f, 139.0f, 141.5f, 143.5f };
        static readonly float[] MaleAge9 = { 122.6f, 125.7f, 128.3f, 131.0f, 136.4f, 140.3f, 142.1f, 144.4f, 146.7f };
        static readonly float[] MaleAge10 = { 130.7f, 132.2f, 134.7f, 137.4f, 141.1f, 146.3f, 148.4f, 149.7f, 151.7f };
        static readonly float[] MaleAge11 = { 136.3f, 139.7f, 141.1f, 143.3f, 148.3f, 153.7f, 157.5f, 159.6f, 162.9f };
        static readonly float[] MaleAge12 = { 140.3f, 141.4f, 145.3f, 148.1f, 153.9f, 159.0f, 163.8f, 166.7f, 169.4f };
        static readonly float[] MaleAge13 = { 149.4f, 151.7f, 153.2f, 157.0f, 163.6f, 167.6f, 170.2f, 171.6f, 172.8f };
        static readonly float[] MaleAge14 = { 154.8f, 156.5f, 158.3f, 163.0f, 170.0f, 175.3f, 178.5f, 179.5f, 180.5f };
        static readonly float[] MaleAge15 = { 160.1f, 161.8f, 163.6f, 167.3f, 172.7f, 176.4f, 180.0f, 181.6f, 183.2f };
        static readonly float[] MaleAge16 = { 160.1f, 163.2f, 164.2f, 167.4f, 172.6f, 179.5f, 182.4f, 184.9f, 185.8f };
        static readonly float[] MaleAge17 = { 163.7f, 167.4f, 168.6f, 170.2f, 174.9f, 179.8f, 181.5f, 184.9f, 187.2f };

        static readonly float[] FemaleAge7 = { 114.9f, 116.7f, 117.9f, 119.8f, 123.7f, 127.9f, 129.9f, 131.6f, 133.7f };
        static readonly float[] FemaleAge8 = { 120.2f, 120.9f, 122.4f, 126.0f, 129.8f, 133.9f, 137.4f, 138.4f, 141.8f };
        static readonly float[] FemaleAge9 = { 126.1f, 128.2f, 129.6f, 131.8f, 136.5f, 142.5f, 145.3f, 145.9f, 147.4f };
        static readonly float[] FemaleAge10 = { 130.2f, 133.2f, 135.4f, 138.9f, 142.3f, 148.2f, 150.2f, 152.5f, 154.7f };
        static readonly float[] FemaleAge11 = { 136.6f, 140.2f, 142.0f, 145.1f, 150.8f, 156.1f, 158.8f, 160.1f, 162.8f };
        static readonly float[] FemaleAge12 = { 142.0f, 145.3f, 147.6f, 151.9f, 154.3f, 158.8f, 161.3f, 164.5f, 167.7f };
        static readonly float[] FemaleAge13 = { 145.0f, 150.3f, 151.4f, 154.2f, 157.7f, 162.8f, 164.2f, 164.8f, 166.3f };
        static readonly float[] FemaleAge14 = { 150.5f, 152.0f, 154.1f, 156.9f, 161.2f, 166.6f, 169.6f, 170.6f, 172.1f };
        static readonly float[] FemaleAge15 = { 151.3f, 153.1f, 154.7f, 156.1f, 160.0f, 165.4f, 168.7f, 171.3f, 173.9f };
        static readonly float[] FemaleAge16 = { 151.9f, 154.2f, 156.2f, 157.5f, 161.7f, 165.7f, 168.4f, 169.4f, 172.7f };
        static readonly float[] FemaleAge17 = { 152.8f, 155.2f, 156.1f, 157.7f, 162.4f, 167.1f, 170.4f, 171.9f, 173.4f };

        static readonly Dictionary<string, Measurement> Measurements = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => Measurements.Clear();

        public static int Seed(int citySeed, int localIndex, int salt)
        {
            unchecked
            {
                var z = (uint)(citySeed + SeedOffsets.Anthropometry);
                z ^= (uint)localIndex * 0x9E3779B9u;
                z ^= (uint)salt * 0x85EBCA6Bu;
                return (int)Mix(z);
            }
        }

        public static PedestrianAgeCohort CohortFor(string groupLabel, string prefabName)
        {
            if (string.Equals(groupLabel, "Children", StringComparison.Ordinal))
                return PedestrianAgeCohort.SchoolChild;

            if (string.IsNullOrEmpty(prefabName))
                return PedestrianAgeCohort.Adult;

            var name = prefabName.ToLowerInvariant();
            return name.Contains("child") || name.Contains("school") ||
                   name.Contains("boy") || name.Contains("girl")
                ? PedestrianAgeCohort.SchoolChild
                : PedestrianAgeCohort.Adult;
        }

        public static PedestrianAnthropometrySample Sample(
            int seed,
            bool female,
            PedestrianAgeCohort cohort)
        {
            var group = PickGroup(seed);
            var age = cohort == PedestrianAgeCohort.SchoolChild
                ? PickAge(female ? FemaleSchoolAges : MaleSchoolAges, seed, 0xA01u)
                : PickAge(female ? FemaleAdultAges : MaleAdultAges, seed, 0xA02u);

            var heightCm = cohort == PedestrianAgeCohort.SchoolChild
                ? ChildHeightCentimetres(female, age, Unit(seed, 0xA03u))
                : AdultHeightCentimetres(female, age, group, Unit(seed, 0xA03u));

            return new PedestrianAnthropometrySample(group, female, age, heightCm * 0.01f);
        }

        public static PedestrianAnthropometryStamp Apply(
            GameObject person,
            int seed,
            bool female,
            PedestrianAgeCohort cohort,
            string prefabName = null)
        {
            if (!person)
                return null;

            var sample = Sample(seed, female, cohort);
            var measurement = Measure(person.transform, prefabName);
            var scale = 1f;

            if (measurement.HeightMetres > MinMeasurableHeightMetres)
            {
                scale = Mathf.Clamp(sample.HeightMetres / measurement.HeightMetres, MinScale, MaxScale);
                person.transform.localScale *= scale;
                person.transform.position += Vector3.up * measurement.BottomOffsetMetres * (1f - scale);
            }

            var stamp = person.GetComponent<PedestrianAnthropometryStamp>();
            if (!stamp)
                stamp = person.AddComponent<PedestrianAnthropometryStamp>();

            stamp.Configure(seed, sample, scale);
            PersonSkinTint.Apply(person, seed, sample.PopulationGroup);
            return stamp;
        }

        public static float MiamiPopulationShare(PedestrianPopulationGroup group)
        {
            foreach (var weighted in MiamiGroups)
            {
                if (weighted.Group == group)
                    return weighted.Weight;
            }

            return 0f;
        }

        static PedestrianPopulationGroup PickGroup(int seed)
        {
            var roll = Unit(seed, 0xB01u) * 100f;
            foreach (var weighted in MiamiGroups)
            {
                roll -= weighted.Weight;
                if (roll <= 0f)
                    return weighted.Group;
            }

            return MiamiGroups[MiamiGroups.Length - 1].Group;
        }

        static int PickAge(WeightedAge[] ages, int seed, uint salt)
        {
            var total = 0f;
            foreach (var age in ages)
                total += age.Weight;

            var roll = Unit(seed, salt) * total;
            foreach (var age in ages)
            {
                roll -= age.Weight;
                if (roll > 0f)
                    continue;

                var span = age.Max - age.Min + 1;
                return age.Min + Mathf.FloorToInt(Unit(seed, salt + 0x51u) * span);
            }

            return ages[ages.Length - 1].Max;
        }

        static float AdultHeightCentimetres(
            bool female,
            int age,
            PedestrianPopulationGroup group,
            float percentileRoll)
        {
            var height = Quantile(AdultCurve(female, group), percentileRoll);
            height += AdultAgeAdjustmentCentimetres(female, age);
            return Mathf.Clamp(height, female ? 140f : 152f, female ? 178f : 193f);
        }

        static float ChildHeightCentimetres(bool female, int age, float percentileRoll)
        {
            return Quantile(ChildCurve(female, Mathf.Clamp(age, 7, 17)), percentileRoll);
        }

        static float[] AdultCurve(bool female, PedestrianPopulationGroup group)
        {
            if (female)
            {
                return group switch
                {
                    PedestrianPopulationGroup.Hispanic => FemaleHispanicAdult,
                    PedestrianPopulationGroup.White => FemaleWhiteAdult,
                    PedestrianPopulationGroup.Black => FemaleBlackAdult,
                    PedestrianPopulationGroup.Asian => FemaleAsianAdult,
                    _ => FemaleAllAdult
                };
            }

            return group switch
            {
                PedestrianPopulationGroup.Hispanic => MaleHispanicAdult,
                PedestrianPopulationGroup.White => MaleWhiteAdult,
                PedestrianPopulationGroup.Black => MaleBlackAdult,
                PedestrianPopulationGroup.Asian => MaleAsianAdult,
                _ => MaleAllAdult
            };
        }

        static float AdultAgeAdjustmentCentimetres(bool female, int age)
        {
            if (female)
            {
                if (age < 30) return 1.5f;
                if (age < 40) return 1.3f;
                if (age < 50) return 1.3f;
                if (age < 60) return -0.4f;
                if (age < 70) return -0.5f;
                if (age < 80) return -3.4f;
                return -5.5f;
            }

            if (age < 30) return 0.6f;
            if (age < 40) return 1.3f;
            if (age < 50) return 1.1f;
            if (age < 60) return -0.1f;
            if (age < 70) return -1.1f;
            if (age < 80) return -2.3f;
            return -5.1f;
        }

        static float[] ChildCurve(bool female, int age)
        {
            if (female)
            {
                return age switch
                {
                    7 => FemaleAge7,
                    8 => FemaleAge8,
                    9 => FemaleAge9,
                    10 => FemaleAge10,
                    11 => FemaleAge11,
                    12 => FemaleAge12,
                    13 => FemaleAge13,
                    14 => FemaleAge14,
                    15 => FemaleAge15,
                    16 => FemaleAge16,
                    _ => FemaleAge17
                };
            }

            return age switch
            {
                7 => MaleAge7,
                8 => MaleAge8,
                9 => MaleAge9,
                10 => MaleAge10,
                11 => MaleAge11,
                12 => MaleAge12,
                13 => MaleAge13,
                14 => MaleAge14,
                15 => MaleAge15,
                16 => MaleAge16,
                _ => MaleAge17
            };
        }

        static float Quantile(float[] centimetres, float roll)
        {
            if (roll <= Percentiles[0])
                return centimetres[0];

            var last = Percentiles.Length - 1;
            if (roll >= Percentiles[last])
                return centimetres[last];

            for (var i = 1; i < Percentiles.Length; i++)
            {
                if (roll > Percentiles[i])
                    continue;

                var t = Mathf.InverseLerp(Percentiles[i - 1], Percentiles[i], roll);
                return Mathf.Lerp(centimetres[i - 1], centimetres[i], t);
            }

            return centimetres[last];
        }

        static Measurement Measure(Transform root, string prefabName)
        {
            var key = string.IsNullOrEmpty(prefabName) ? root.name : prefabName;
            if (Measurements.TryGetValue(key, out var cached))
                return cached;

            var measurement = MeasureRenderers(root);
            if (!measurement.Valid)
                measurement = MeasureCapsule(root);
            if (!measurement.Valid)
                measurement = new Measurement(DefaultAuthoredHeightMetres, 0f, false);

            Measurements[key] = measurement;
            return measurement;
        }

        static Measurement MeasureRenderers(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var hasBounds = false;
            var bounds = default(Bounds);

            foreach (var renderer in renderers)
            {
                if (!renderer)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds && bounds.size.y > MinMeasurableHeightMetres
                ? new Measurement(bounds.size.y, bounds.min.y - root.position.y, true)
                : default;
        }

        static Measurement MeasureCapsule(Transform root)
        {
            var capsule = root.GetComponentInChildren<CapsuleCollider>(true);
            if (!capsule)
                return default;

            var scale = capsule.transform.lossyScale;
            var axisScale = capsule.direction switch
            {
                0 => Mathf.Abs(scale.x),
                1 => Mathf.Abs(scale.y),
                _ => Mathf.Abs(scale.z)
            };
            var height = Mathf.Abs(capsule.height * axisScale);
            if (height <= MinMeasurableHeightMetres)
                return default;

            var centre = capsule.transform.TransformPoint(capsule.center);
            return new Measurement(height, centre.y - height * 0.5f - root.position.y, true);
        }

        static float Unit(int seed, uint salt)
        {
            unchecked
            {
                return ((Mix((uint)seed ^ salt) >> 8) & 0x00FFFFFFu) / 16777216f;
            }
        }

        static uint Mix(uint value)
        {
            unchecked
            {
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                value *= 0x846CA68Bu;
                value ^= value >> 16;
                return value;
            }
        }

        readonly struct WeightedGroup
        {
            public WeightedGroup(PedestrianPopulationGroup group, float weight)
            {
                Group = group;
                Weight = weight;
            }

            public PedestrianPopulationGroup Group { get; }
            public float Weight { get; }
        }

        readonly struct WeightedAge
        {
            public WeightedAge(int min, int max, float weight)
            {
                Min = min;
                Max = max;
                Weight = weight;
            }

            public int Min { get; }
            public int Max { get; }
            public float Weight { get; }
        }

        readonly struct Measurement
        {
            public Measurement(float heightMetres, float bottomOffsetMetres, bool valid)
            {
                HeightMetres = heightMetres;
                BottomOffsetMetres = bottomOffsetMetres;
                Valid = valid;
            }

            public float HeightMetres { get; }
            public float BottomOffsetMetres { get; }
            public bool Valid { get; }
        }
    }
}
