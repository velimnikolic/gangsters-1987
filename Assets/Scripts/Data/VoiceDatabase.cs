using System;
using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Data
{
    /// <summary>Which end of a life an actor sounds like. Cast against the man's own age
    /// so a caporegime of sixty is never given a voice of twenty.</summary>
    public enum VoiceAge { Young, Prime, Old }

    /// <summary>How he says it. Cast against Temper and Discipline - loud men get loud
    /// voices, and the hard ones the flat delivery.</summary>
    public enum VoiceTemper { Hot, Steady, Any }

    /// <summary>
    /// Every spoken line in the game, by bank and by key.
    ///
    /// Two axes, and they are deliberately separate. A KEY says what is said - VOX_ORD_KILL,
    /// the order to go in on a man - and holds the takes of it, the two or three ways an
    /// actor said the same order. A BANK says who is saying it: one actor, one voice, the
    /// whole line sheet recorded through. The clip that plays is the pair, so eight men in
    /// the outfit give the same order in eight voices and one man gives it in his own voice
    /// for as long as he lives (<see cref="LivingCity.Personnel.Character.Voice"/>).
    ///
    /// Baked from Assets/Audio/Voice by VoiceAssetBootstrap, which is where the folder is
    /// read and the variant suffix is split off the key; runtime only ever sees this asset,
    /// the same contract SoundDatabase has with Assets/Audio. Everything here may come back
    /// empty: a key nobody recorded is silence, never an exception - the crowd's rule for a
    /// missing animation clip, applied to speech.
    /// </summary>
    public sealed class VoiceDatabase : ScriptableObject
    {
        /// <summary>One line of the sheet as one actor said it: the key, and every take of
        /// it he recorded (VOX_ORD_MOVE_01..03 are three takes of one key).</summary>
        [Serializable]
        public sealed class Line
        {
            public string key = "";
            public AudioClip[] takes = Array.Empty<AudioClip>();
        }

        /// <summary>One actor, recorded through the sheet.</summary>
        [Serializable]
        public sealed class Bank
        {
            public string id = "";

            [Tooltip("Who it sounds like - the casting note, carried for the Inspector and " +
                     "for anyone re-recording the bank later.")]
            public string actor = "";

            [Tooltip("The age the actor reads as. The casting roll weights this against " +
                     "the man's own years.")]
            public VoiceAge age = VoiceAge.Prime;

            [Tooltip("The delivery. Weighted against Temper and Discipline; Any takes " +
                     "either side without preference.")]
            public VoiceTemper temper = VoiceTemper.Any;

            [Tooltip("The desk voice. Never cast to a man on the street - it speaks the " +
                     "orders filed with the office and the acts that are money and paper.")]
            public bool office;

            public Line[] lines = Array.Empty<Line>();
        }

        public Bank[] banks = Array.Empty<Bank>();

        // bank id -> key -> takes. Built once, on first ask, because the asset is a flat
        // pair of arrays (Unity serializes no dictionary) and a lookup per spoken line
        // through 9 banks of 80 lines is a scan nobody needs to pay.
        Dictionary<string, Dictionary<string, AudioClip[]>> index;

        void OnDisable() => index = null;   // a re-bake in the editor must not serve stale takes

        /// <summary>Every take of one key in one bank, or an empty span. A bank that never
        /// recorded the key falls through to <paramref name="fallbackBank"/> - which is how
        /// a tier that is only cut on some of the actors still speaks on all of them.</summary>
        public AudioClip[] Takes(string bankId, string key, string fallbackBank = null)
        {
            if (string.IsNullOrEmpty(bankId) || string.IsNullOrEmpty(key))
                return Array.Empty<AudioClip>();

            Build();

            if (index.TryGetValue(bankId, out var byKey) &&
                byKey.TryGetValue(key, out var takes) && takes.Length > 0)
                return takes;

            if (!string.IsNullOrEmpty(fallbackBank) && fallbackBank != bankId &&
                index.TryGetValue(fallbackBank, out var other) &&
                other.TryGetValue(key, out var spare))
                return spare;

            return Array.Empty<AudioClip>();
        }

        public bool Has(string bankId, string key) => Takes(bankId, key).Length > 0;

        /// <summary>The ids of every bank that carries lines, in asset order. The casting
        /// roll reads this rather than a list of its own, so adding a ninth actor to the
        /// folder is the whole of adding a ninth voice to the game.</summary>
        public void CollectBankIds(List<string> into, bool streetOnly = true)
        {
            if (into == null) return;
            into.Clear();
            for (var i = 0; i < banks.Length; i++)
            {
                var bank = banks[i];
                if (bank == null || string.IsNullOrEmpty(bank.id) ||
                    bank.lines == null || bank.lines.Length == 0 ||
                    (streetOnly && bank.office))
                    continue;
                into.Add(bank.id);
            }
        }

        /// <summary>The desk. The first bank marked as the office, or none - in which
        /// case the office lines are spoken by whoever is asked for them.</summary>
        public string OfficeBankId
        {
            get
            {
                for (var i = 0; i < banks.Length; i++)
                    if (banks[i] != null && banks[i].office && !string.IsNullOrEmpty(banks[i].id))
                        return banks[i].id;
                return null;
            }
        }

        public Bank Find(string bankId)
        {
            for (var i = 0; i < banks.Length; i++)
                if (banks[i] != null && banks[i].id == bankId)
                    return banks[i];
            return null;
        }

        void Build()
        {
            if (index != null)
                return;

            index = new Dictionary<string, Dictionary<string, AudioClip[]>>(banks.Length);
            for (var i = 0; i < banks.Length; i++)
            {
                var bank = banks[i];
                if (bank == null || string.IsNullOrEmpty(bank.id))
                    continue;

                var byKey = new Dictionary<string, AudioClip[]>(
                    bank.lines != null ? bank.lines.Length : 0);
                if (bank.lines != null)
                {
                    for (var j = 0; j < bank.lines.Length; j++)
                    {
                        var line = bank.lines[j];
                        if (line == null || string.IsNullOrEmpty(line.key) || line.takes == null)
                            continue;
                        byKey[line.key] = line.takes;
                    }
                }
                index[bank.id] = byKey;
            }
        }

        static VoiceDatabase loaded;

        public static VoiceDatabase Instance
        {
            get
            {
                if (loaded)
                    return loaded;

                loaded = Resources.Load<VoiceDatabase>("VoiceDatabase");
                if (!loaded)
                    loaded = CreateInstance<VoiceDatabase>();   // no bake yet: the game is mute
                return loaded;
            }
        }

        /// <summary>Static state outlives Play when domain reload is off - the same reset
        /// WeaponCatalog does for the same reason.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => loaded = null;
    }
}
