using LivingCity.Gangs;

namespace LivingCity.News
{
    /// <summary>What the paper's photographer pointed the camera at. The UI resolves the
    /// kind into the right PortraitStudio framing and the right prefab lookup.</summary>
    public enum PhotoSubject
    {
        /// <summary>No picture beside this story - the ordinary case.</summary>
        None,

        /// <summary>A man, shot head and shoulders. The mugshot the ledger already
        /// knows how to take, run through the halftone screen.</summary>
        Person,

        /// <summary>A car or a van, three-quarter from above.</summary>
        Vehicle,
    }

    /// <summary>
    /// One press photo: who is in it, and the italic line under it. Engine-free like the
    /// rest of the News core - the model is a prefab NAME, which the UI resolves through
    /// PortraitStudio's read-only scan of the shipped PrefabDatabase.
    /// </summary>
    public readonly struct NewsPhoto
    {
        public readonly PhotoSubject Subject;
        public readonly string ModelName;
        public readonly string Caption;

        public NewsPhoto(PhotoSubject subject, string modelName, string caption)
        {
            Subject = subject;
            ModelName = modelName;
            Caption = caption;
        }

        public static readonly NewsPhoto None = new NewsPhoto(PhotoSubject.None, "", "");

        public bool HasPicture => Subject != PhotoSubject.None;
    }

    /// <summary>
    /// The picture desk. Chooses a subject and a caption for a story, from the same
    /// models the city fields - so the man in the paper is a man the player can meet on
    /// the street, and a story naming a family prints one of THAT family's soldiers.
    ///
    /// Deterministic: the generator hands it the day's stream, so a reloaded save
    /// develops the same photographs.
    /// </summary>
    public static class PictureDesk
    {
        /// <summary>Captions run under the cut and stay short - the UI gives them one
        /// or two lines of small italic. Proofed against this in the headless suite.</summary>
        public const int CaptionBudget = 52;

        /// <summary>The police officer and the patrol car. Neither lives in the crowd
        /// groups, which is why PortraitStudio's resolvers check the database's two
        /// police fields by name and then fall back to the packs themselves. The car is
        /// the force's own marked body (VehicleCatalog.PoliceCars): the paper photographs
        /// what is actually parked at the kerb.</summary>
        const string OfficerModel = "SM_Chr_Officer_Male_01_AI";
        const string PoliceCarModel = "SM_Veh_Pickup_01_Preset_Police";

        /// <summary>Faces that are not gang faces - the suits, the street, the law.
        /// Held to that literally: nothing here is on GangLooks' cast tables, so the
        /// face over "local businessman" is never the face of a capo two columns down.
        /// (The business suits and the salesman the mob may be dealt were moved off
        /// these lists for exactly that.)</summary>
        static readonly string[] SuitFaces =
        {
            "SM_Chr_Rich_Male_01_AI", "SM_Gen_Chr_Business_Female_01_AI",
            "SM_Chr_Rich_Female_01_AI",
        };

        static readonly string[] StreetFaces =
        {
            "SM_Chr_City_Male_01_AI", "SM_Chr_City_Female_01_AI", "SM_Chr_City_Male_02_AI",
            "SM_Gen_Chr_Street_Male_02_AI", "SM_Chr_Surfer_Female_01_AI",
        };

        static readonly string[] CriminalFaces =
        {
            "SM_Chr_Criminal_Male_01_AI", "SM_Chr_Goon_01_AI", "SM_Chr_Gang_Male_02_AI",
        };

        static readonly string[] SeizedVehicles =
        {
            "SM_Veh_Van_01", "SM_Veh_Pickup_01", "SM_Veh_Sedan_01",
        };

        // Captions, per desk. Written to sit under any headline that desk can print -
        // a caption that contradicts its story is worse than a vague one.

        static readonly string[] CrimeCaptions =
        {
            "FILE PHOTO: THE MAN POLICE WANT TO TALK TO",
            "A FACE KNOWN TO THE ORGANIZED CRIME BUREAU",
            "PHOTOGRAPHED LEAVING THE COURTHOUSE YESTERDAY",
            "A PHOTOGRAPHER REACHED THE SCENE FIRST",
        };

        static readonly string[] DrugWarCaptions =
        {
            "NARCOTICS DETAIL WORKS THE CORNER AT DAWN",
            "SEIZED LAST NIGHT AND TOWED TO THE POUND",
            "THE VEHICLE AGENTS SAY CARRIED THE LOAD",
            "AN OFFICER STANDS THE POST AFTER THE RAID",
        };

        static readonly string[] NationCaptions =
        {
            "THE PROSECUTOR ADDRESSES REPORTERS",
            "FEDERAL AGENTS DECLINED TO BE NAMED",
            "TESTIMONY CONTINUES BEHIND CLOSED DOORS",
        };

        static readonly string[] WorldCaptions =
        {
            "A DIPLOMAT ARRIVES FOR A SECOND SESSION",
            "A CORRESPONDENT CABLED THIS PICTURE",
            "THE DELEGATION LEFT WITHOUT COMMENT",
        };

        static readonly string[] BusinessCaptions =
        {
            "THE FLOOR AT THE CLOSING BELL",
            "A TRADER READS THE TAPE",
            "BUSINESS AS USUAL, THE MANAGEMENT INSISTS",
        };

        static readonly string[] CultureCaptions =
        {
            "SEEN THIS WEEK ON THE AVENUE",
            "THE LOOK EVERY WINDOW IS SELLING",
            "THE LINE STRETCHED AROUND THE BLOCK",
        };

        /// <summary>
        /// Picks the photo for a story. <paramref name="gangId"/> is the family the
        /// headline named, or -1; the crime desk prints that family's soldier when it
        /// has one. <paramref name="alreadyPrinted"/> holds the models already on this
        /// page - two desks drawing from the same table of suits would otherwise run
        /// the same man twice, which no sub-editor would let through. May be null.
        ///
        /// Draws exactly once from the stream whatever the desk decides (the dodge
        /// walks the table rather than re-rolling), so a page's later stories never
        /// shift because an earlier one chose differently.
        /// </summary>
        public static NewsPhoto For(HeadlineDesk desk, int gangId, System.Random rng,
            System.Collections.Generic.HashSet<string> alreadyPrinted = null)
        {
            var roll = rng.Next(1000);

            switch (desk)
            {
                case HeadlineDesk.Crime:
                    // A story that names a family prints THAT family's soldier, dodge
                    // or no dodge - the picture is the point, not the variety.
                    var face = gangId >= 0 && gangId < GangCatalog.SoldierModels.Length
                        ? GangCatalog.SoldierModels[gangId]
                        : Pick(CriminalFaces, roll, alreadyPrinted);
                    return new NewsPhoto(PhotoSubject.Person, face,
                        CrimeCaptions[roll % CrimeCaptions.Length]);

                case HeadlineDesk.DrugWar:
                    // Half the drug-war pictures are the seized vehicle, half the cop
                    // standing over it - the two stock shots of the era. The caption
                    // draws off roll/2: the low bit already went to choosing the
                    // branch, so reusing it would pin each branch to one caption.
                    return (roll & 1) == 0
                        ? new NewsPhoto(PhotoSubject.Vehicle,
                            Pick(SeizedVehicles, roll, alreadyPrinted),
                            DrugWarCaptions[1 + (roll / 2) % 2])
                        : new NewsPhoto(PhotoSubject.Person, OfficerModel,
                            (roll / 2) % 2 == 0 ? DrugWarCaptions[0] : DrugWarCaptions[3]);

                case HeadlineDesk.Nation:
                    return new NewsPhoto(PhotoSubject.Person,
                        roll % 3 == 0 ? "SM_Chr_Detective_Male_01_AI"
                                      : Pick(SuitFaces, roll / 3, alreadyPrinted),
                        NationCaptions[roll % NationCaptions.Length]);

                case HeadlineDesk.World:
                    return new NewsPhoto(PhotoSubject.Person, Pick(SuitFaces, roll, alreadyPrinted),
                        WorldCaptions[roll % WorldCaptions.Length]);

                case HeadlineDesk.Business:
                    // The car outside the exchange, or the trader himself.
                    return roll % 4 == 0
                        ? new NewsPhoto(PhotoSubject.Vehicle, "SM_Veh_Sedan_01",
                            BusinessCaptions[2])
                        : new NewsPhoto(PhotoSubject.Person, Pick(SuitFaces, roll, alreadyPrinted),
                            BusinessCaptions[roll % 2]);

                default:
                    return new NewsPhoto(PhotoSubject.Person, Pick(StreetFaces, roll, alreadyPrinted),
                        CultureCaptions[roll % CultureCaptions.Length]);
            }
        }

        /// <summary>
        /// The table entry at <paramref name="roll"/>, stepping forward past anything
        /// already on the page. A table entirely used up falls back to the plain
        /// entry - a repeat beats no picture.
        /// </summary>
        static string Pick(string[] table, int roll, System.Collections.Generic.HashSet<string> avoid)
        {
            var start = roll % table.Length;
            if (avoid == null)
                return table[start];

            for (var step = 0; step < table.Length; step++)
            {
                var candidate = table[(start + step) % table.Length];
                if (!avoid.Contains(candidate))
                    return candidate;
            }

            return table[start];
        }

        /// <summary>
        /// The patrol car, for a caller that wants the police fleet's body by name
        /// rather than through a draw. Kept beside its officer so the two names live
        /// in one place.
        /// </summary>
        public static string PatrolCarModel => PoliceCarModel;
    }
}
