using TMPro;
using UnityEngine;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// FAMILIES: the boss's dossier on the city's other outfits - one index card a
    /// family, a Polaroid of its capo pinned to it, the front, the ground it holds,
    /// its colour on the map, and three tapes for the stance toward it with the
    /// standing choice ringed in red pen. The player's own line reads first, as the
    /// yardstick. Stances turn when the week commits, and the foot of the page says
    /// so, in plain words, along with what each stance does.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        const float FamilyCardH = 104f;
        const float FamilyPitch = 116f;

        RectTransform diplomacyContent;

        void BuildDiplomacyPage(RectTransform sheet)
        {
            var root = NewPageRoot(sheet, LedgerPage.Diplomacy);
            diplomacyContent = NewRect("Families", root);
            Stretch(diplomacyContent);
        }

        void RebuildDiplomacy()
        {
            foreach (Transform old in diplomacyContent)
                Destroy(old.gameObject);

            var heading = Line(diplomacyContent, LedgerStyle.Type, 18f, LedgerStyle.Ink,
                PageLeft, PageTop, 600f, 30f, "FAMILIES OF THE CITY");
            heading.characterSpacing = 4f;

            var gangs = Gangs.GangRegistry.Gangs;
            if (gangs.Count == 0)
            {
                Line(diplomacyContent, LedgerStyle.MonoItalic, 14f, LedgerStyle.InkDim, PageLeft,
                    PageTop - 40f, 800f, 24f, "The families have not shown themselves yet.");

                // DEV, editor only: deal a dummy hand of families so the page can be
                // seen dressed before the street layer seeds the real ones. The real
                // generator with a fixed seed, so the preview IS the live layout.
                if (Application.isEditor)
                    Tape(diplomacyContent, "DEAL DUMMY FAMILIES", PageLeft, PageTop - 76f,
                        200f, 26f, () => Gangs.GangRegistry.Install(
                            Gangs.GangSeeder.Generate(1987, director.Roster)));
                return;
            }

            if (outfit)
                outfit.CollectHoldings(holdings);
            else
                holdings.Clear();
            var y = PageTop - 44f;

            // The player's own line first - the don looks his rivals in the eye.
            foreach (var gang in gangs)
            {
                if (!gang.IsPlayer)
                    continue;

                var raw = Polaroid(diplomacyContent, PageLeft, y, 52f,
                    InitialsOf(Gangs.GangCatalog.BossName), 2.5f, out _);
                PortraitStudio.Request(
                    PortraitStudio.FindPeoplePrefab(Gangs.GangCatalog.BossModel),
                    PortraitStudio.Framing.Bust, raw);
                Swatch(gang.Id, PageLeft + 84f, y + 2f);

                var held = Outfit.Turf.CountOf(holdings, gang.Id);
                var you = Line(diplomacyContent, LedgerStyle.Type, 15f, LedgerStyle.Ink,
                    PageLeft + 106f, y, 640f, 26f,
                    gang.Name.ToUpperInvariant() + "  —  YOURS" +
                    (outfit
                        ? "  ·  " + held + " BUILDING" + (held == 1 ? "" : "S")
                        : ""));
                you.characterSpacing = 1f;
                Line(diplomacyContent, LedgerStyle.Mono, 14.5f, LedgerStyle.InkDim,
                    PageLeft + 106f, y - 26f, 400f, 20f, "Boss: " + Gangs.GangCatalog.BossName);
                Rule(diplomacyContent, PageLeft, y - 90f, PageWidth, LedgerStyle.Ink);
                y -= 104f;
            }

            foreach (var gang in gangs)
            {
                if (gang.IsPlayer)
                    continue;
                y = FamilyCard(gang, y);
            }

            // The legend, under the families - the page must never be the opaque system.
            var legendTop = Mathf.Min(y - 6f, PageBottom + 250f);
            var legendY = Heading(diplomacyContent, PageLeft, legendTop, PageWidth,
                "What a stance does", 12.5f);
            Paragraph(diplomacyContent, LedgerStyle.Mono, 12.5f, LedgerStyle.InkDim, PageLeft,
                legendY, PageWidth, 216f,
                LedgerText.StanceEffect(Outfit.Stance.Peace) + "\n" +
                LedgerText.StanceEffect(Outfit.Stance.Truce) + "\n" +
                LedgerText.StanceEffect(Outfit.Stance.War) + "\n\n" +
                LedgerText.StanceTakesEffect + "  Strength reads UNKNOWN until you have " +
                "eyes inside a family - reconnaissance is work, not a birthright. Their " +
                "turf shows on the map in their colour; the streets are not a secret.",
                lineSpacing: 3f);
        }

        float FamilyCard(Gangs.Gang gang, float y)
        {
            // Square on the page: a tilted card turns every hairline on it into a
            // staircase. The Polaroid pinned to it carries the crookedness instead.
            var card = Card("Family " + gang.Name, diplomacyContent, PageLeft, y, PageWidth,
                FamilyCardH, LedgerStyle.Card);

            // The face of the family: its capo, wearing the model his soldiers
            // answer to on the street, pinned top-left.
            var leader = gang.Members.Count > 0 ? gang.Members[0].FullName : "";
            var raw = Polaroid(card, 8f, -8f, 60f,
                InitialsOf(leader.Length > 0 ? leader : gang.Name),
                gang.Id % 2 == 0 ? -4f : 3f, out _);
            PortraitStudio.Request(
                PortraitStudio.FindPeoplePrefab(Gangs.GangCatalog.LieutenantModels[gang.Id]),
                PortraitStudio.Framing.Bust, raw);

            Swatch(gang.Id, 96f, -12f, card);
            var name = Line(card, LedgerStyle.Type, 15f, LedgerStyle.Ink, 118f, -8f, 330f, 26f,
                gang.Name.ToUpperInvariant());
            name.characterSpacing = 1f;

            Line(card, LedgerStyle.Mono, 14.5f, LedgerStyle.Ink, 118f, -34f, 330f, 20f,
                leader.Length > 0 ? "Run by " + leader : "Run by persons unknown");

            var front = Gangs.GangRegistry.FrontBusinessOf(gang.Id);
            Line(card, LedgerStyle.Mono, 14f, LedgerStyle.InkDim, 118f, -54f, 330f, 20f,
                front ? "Front: " + front.BusinessName : "Front: unknown");

            var held = Outfit.Turf.CountOf(holdings, gang.Id);
            Line(card, LedgerStyle.Mono, 14f, LedgerStyle.InkDim, 118f, -74f, 340f, 20f,
                "Strength: " + LedgerText.StrengthUnknown + "  ·  " +
                (outfit
                    ? "Turf: " + held + (held == 1 ? " building" : " buildings")
                    : "Turf: unknown"));

            var current = outfit ? outfit.Relations.StanceWith(gang.Id) : Outfit.Stance.Peace;
            var pending = Outfit.Stance.Peace;
            var hasPending = outfit && outfit.Relations.TryGetPending(gang.Id, out pending);

            var stance = Line(card, LedgerStyle.Type, 14.5f,
                hasPending ? LedgerStyle.RedPen : LedgerStyle.Ink, 470f, -10f, 400f, 22f,
                "STANCE: " + LedgerText.StanceLabel(current).ToUpperInvariant() +
                (hasPending
                    ? "  >  " + LedgerText.StanceLabel(pending).ToUpperInvariant() +
                      " FROM NEXT WEEK"
                    : ""));
            stance.characterSpacing = 1f;

            var effective = hasPending ? pending : current;
            for (var s = 0; s < 3; s++)
            {
                var choice = (Outfit.Stance)s;
                var gangId = gang.Id;
                var tape = Tape(card, LedgerText.StanceLabel(choice), 470f + s * 118f, -46f,
                    106f, 26f, () =>
                    {
                        if (outfit)
                            outfit.SetStance(gangId, choice);
                        dirty = true;
                    }, red: choice == Outfit.Stance.War);
                if (choice == effective)
                    PenRing((RectTransform)tape.transform.parent, LedgerStyle.RedPen);
            }

            return y - FamilyPitch;
        }

        /// <summary>The family's map colour, as the coloured dot sticker an office
        /// puts on a file.</summary>
        void Swatch(int gangId, float x, float y, Transform parent = null)
        {
            var rect = NewRect("Swatch", parent ? parent : diplomacyContent);
            PlaceTopLeft(rect, x, y, 16f, 16f);
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.sprite = LedgerStyle.Rounded;
            image.type = UnityEngine.UI.Image.Type.Sliced;
            image.color = GangPalette.Of(gangId);
            image.raycastTarget = false;
        }
    }
}
