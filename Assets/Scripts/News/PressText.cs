using System;
using System.Collections.Generic;
using LivingCity.Gangs;
using LivingCity.Personnel;
using LivingCity.Police;

namespace LivingCity.News
{
    /// <summary>Turns one public record into newspaper copy. It reads only fields on
    /// that record; private orders and account books cannot leak through this seam.</summary>
    public static class PressText
    {
        public const int HeadlineBudget = HeadlineGenerator.TextBudget;

        public static Headline Story(PressRecord record)
        {
            if (record == null)
                return null;
            var desk = IsCourt(record.Kind) ? HeadlineDesk.Courts
                : record.Kind == PressKind.PremisesSold
                    ? HeadlineDesk.Business : HeadlineDesk.City;
            var headline = new Headline
            {
                Desk = desk,
                Text = Head(record),
                Blurb = Copy(record),
                Story = record,
                GangId = record.Attribution == PressAttribution.Unknown
                    ? -1 : record.Family >= 0 ? record.Family : record.NamedGangId,
            };
            headline.Photo = Photo(record);
            return headline;
        }

        public static string Head(PressRecord record)
        {
            if (record == null)
                return "CITY DESK";

            var where = Place(record);
            var family = Family(record.Family);
            var name = FirstName(record);
            string text;

            switch (record.Kind)
            {
                case PressKind.OfficerKilled:
                    text = "OFFICER SLAIN IN " + where + "; MANHUNT ON";
                    break;
                case PressKind.BossKilled:
                    text = Family(record.NamedGangId >= 0
                        ? record.NamedGangId : record.Family) + " BOSS SHOT DEAD";
                    break;
                case PressKind.Killing:
                    if (!string.IsNullOrEmpty(name))
                        text = name + " FOUND SHOT IN " + where;
                    else if (record.CivilianDeaths > 0)
                        text = "PASSER-BY KILLED IN " + where + " CROSSFIRE";
                    else if (record.Attribution == PressAttribution.Seen && record.Family >= 0)
                        text = family + " MEN SEEN FLEEING " + where + " SHOOTING";
                    else
                        text = "GUNMAN KILLS IN " + where;
                    break;
                case PressKind.Shootout:
                    text = "GUNFIRE IN " + where + "; " + record.Shots +
                           " SHOTS, NOBODY HIT";
                    break;
                case PressKind.Arson:
                    text = "FIRE GUTS " + where + " SHOP; ARSON SUSPECTED";
                    break;
                case PressKind.Bombing:
                    text = "BLAST WRECKS " + where + " STOREFRONT";
                    break;
                case PressKind.Arrest:
                    var arrests = Math.Max(1, record.Names?.Length ?? 0);
                    text = arrests + " " + family +
                           (arrests == 1 ? " MAN HELD AFTER " : " MEN HELD AFTER ") +
                           where + " ARREST";
                    break;
                case PressKind.CustodyBroken:
                    text = "PRISONERS FREED IN ATTACK ON POLICE CAR";
                    break;
                case PressKind.FiredOnPolice:
                    text = family + " MEN SOUGHT AFTER SHOTS AT POLICE";
                    break;
                case PressKind.ChargesFiled:
                    text = family + " MEN CHARGED IN " + where + " " + Deed(record.Deed);
                    break;
                case PressKind.Verdict:
                    text = VerdictHead(record, name);
                    break;
                case PressKind.BailJumped:
                    text = name + " SKIPS BAIL; WARRANT ISSUED";
                    break;
                case PressKind.WitnessDead:
                    text = "WITNESS IN " + where + " CASE FOUND DEAD";
                    break;
                case PressKind.FlatRaid:
                    text = string.IsNullOrEmpty(name)
                        ? "POLICE RAID FLAT IN " + where
                        : name + " HELD IN " + where + " FLAT RAID";
                    break;
                case PressKind.PremisesSold:
                    text = "PREMISES CHANGE HANDS IN " + where;
                    break;
                case PressKind.FledPolice:
                    text = family + " MEN FLEE POLICE IN " + where;
                    break;
                case PressKind.PoliceBlotter:
                    text = "POLICE TAKE STATEMENT IN " + where;
                    break;
                default:
                    text = "POLICE BLOTTER: " + where;
                    break;
            }

            return Fit(text.ToUpperInvariant());
        }

        public static string Copy(PressRecord record)
        {
            if (record == null)
                return "";

            var where = SentencePlace(record);
            var business = string.IsNullOrWhiteSpace(record.Business)
                ? "the premises" : record.Business.Trim();
            var family = Family(record.Family);
            var names = Names(record.Names);
            var time = Clock(record.Hour);

            switch (record.Kind)
            {
                case PressKind.Shootout:
                    var sighting = record.Attribution == PressAttribution.Seen &&
                                   record.Family >= 0
                        ? "; witnesses described men believed tied to the " + family +
                          " family"
                        : "";
                    return "Residents counted " + record.Shots + " shots shortly after " +
                           time + " in " + where + sighting +
                           ". Police found casings and no one to charge.";
                case PressKind.Killing:
                    var victim = string.IsNullOrEmpty(names) ? "One person" : names;
                    var namedFamily = Family(record.NamedGangId);
                    var identified = record.NamedGangId >= 0 && !string.IsNullOrEmpty(names)
                        ? ", identified with the " + namedFamily + " family," : "";
                    var attribution = record.Attribution == PressAttribution.Seen &&
                                      record.Family >= 0
                        ? " Witnesses described men believed to be tied to the " + family +
                          " family; police have made no arrest."
                        : " Police have made no arrest.";
                    return victim + identified + " was dead on the pavement in " + where + "." +
                           attribution;
                case PressKind.OfficerKilled:
                    return "Every available patrol car was called to " + where +
                           " after an officer was killed. A citywide search is under way.";
                case PressKind.Arson:
                    return business + " was closed after a fire in " + where +
                           ". Investigators suspect arson.";
                case PressKind.Bombing:
                    return "A blast wrecked the front of " + business + " in " + where +
                           ". The premises remains closed.";
                case PressKind.SmashUp:
                    return "Windows at " + business + " were smashed overnight.";
                case PressKind.Assault:
                    return "A proprietor at " + business + " was assaulted in " + where +
                           ". " + record.Witnesses + " witnesses spoke to police.";
                case PressKind.Arrest:
                    return ArrestCopy(record, names, business, where);
                case PressKind.CustodyBroken:
                    return "Prisoners escaped when a police vehicle was attacked in " + where +
                           ". Officers have issued a citywide alert.";
                case PressKind.FiredOnPolice:
                    return "Police said identified men fired on officers in " + where +
                           ". Warrants are expected.";
                case PressKind.ChargesFiled:
                    return (string.IsNullOrEmpty(names) ? "Defendants" : names) +
                           " were charged with " + Charge(record.Deed) + ".";
                case PressKind.Verdict:
                    return VerdictCopy(record, names);
                case PressKind.BailJumped:
                    return (string.IsNullOrEmpty(names) ? "The defendant" : names) +
                           " failed to appear. The court ordered the bail forfeited.";
                case PressKind.WitnessDead:
                    return (string.IsNullOrEmpty(names) ? "A witness" : names) +
                           " was found dead in " + where + ". Detectives opened an inquiry.";
                case PressKind.FlatRaid:
                    return "Police raided a flat in " + where +
                           (string.IsNullOrEmpty(names) ? "." : " and detained " + names + ".");
                case PressKind.BossKilled:
                    return (string.IsNullOrEmpty(names) ? "The family boss" : names) +
                           " was shot dead. Police announced no arrest.";
                case PressKind.PremisesSold:
                    return business + " changed ownership in " + where +
                           ". Terms of the sale were not disclosed.";
                case PressKind.FledPolice:
                    return (record.Attribution == PressAttribution.Named &&
                            record.Family >= 0
                                ? "Police identified the men as members of the " + family +
                                  " family. They"
                                : "Several men") + " fled an officer in " + where + ".";
                case PressKind.PoliceBlotter:
                    return "Police took a statement at " + business + " in " + where + ".";
                default:
                    return "Police entered the matter in the overnight blotter.";
            }
        }

        public static string Blotter(IEnumerable<PressRecord> records)
        {
            if (records == null)
                return "No entries filed.";
            var lines = new List<string>();
            foreach (var row in records)
            {
                if (row == null) continue;
                lines.Add("• " + Copy(row));
            }
            return lines.Count == 0 ? "No entries filed." : string.Join("\n", lines);
        }

        public static bool IsBlotter(PressRecord record) => record != null &&
            (record.Kind == PressKind.SmashUp || record.Kind == PressKind.Assault ||
             record.Kind == PressKind.FlatRaid ||
             record.Kind == PressKind.PoliceBlotter || record.Kind == PressKind.FledPolice ||
             (record.Kind == PressKind.ChargesFiled &&
              (record.Deed == LivingCity.Personnel.Deed.Extortion ||
               record.Deed == LivingCity.Personnel.Deed.WitnessTampering)));

        static bool IsCourt(PressKind kind) =>
            kind == PressKind.ChargesFiled || kind == PressKind.Verdict ||
            kind == PressKind.BailJumped;

        static string ArrestCopy(PressRecord record, string names, string business,
            string where)
        {
            var one = record.Names == null || record.Names.Length <= 1;
            var who = string.IsNullOrEmpty(names)
                ? (one ? "The suspect" : "The suspects") : names;
            var at = string.IsNullOrWhiteSpace(record.Business)
                ? " in " + where : " at " + business;
            return who + (one ? " was held" : " were held") + at + " on " +
                   Charge(record.Deed) + " charges.";
        }

        static string VerdictHead(PressRecord record, string name)
        {
            if (string.IsNullOrEmpty(name)) name = "DEFENDANT";
            var outcome = (CaseOutcome)record.Outcome;
            if (outcome == CaseOutcome.Dismissed)
                return "CASE AGAINST " + name + " COLLAPSES";
            if (outcome == CaseOutcome.Acquitted)
                return name + " ACQUITTED";
            return name + " GETS " + Sentence(record.SentenceDays);
        }

        static string VerdictCopy(PressRecord record, string names)
        {
            var who = string.IsNullOrEmpty(names) ? "The defendant" : names;
            var outcome = (CaseOutcome)record.Outcome;
            if (outcome == CaseOutcome.Dismissed)
                return "The case against " + who +
                       " collapsed after the state's witness failed to appear.";
            if (outcome == CaseOutcome.Acquitted)
                return who + " was acquitted after trial.";
            return who + " was convicted and sentenced to " +
                   Sentence(record.SentenceDays).ToLowerInvariant() + ".";
        }

        static string Sentence(int days)
        {
            if (Sentencing.IsLife(days)) return "LIFE";
            if (days >= 365)
                return Math.Max(1, days / 365) + (days / 365 == 1 ? " YEAR" : " YEARS");
            if (days >= 30)
                return Math.Max(1, days / 30) + (days / 30 == 1 ? " MONTH" : " MONTHS");
            return Math.Max(1, days) + (days == 1 ? " DAY" : " DAYS");
        }

        static string Charge(Deed deed) => Sentencing.ChargeFor(deed).ToLowerInvariant();

        static string Deed(Deed deed)
        {
            var text = Sentencing.ChargeFor(deed).ToUpperInvariant();
            return text == "AFFRAY - DISCHARGING FIREARMS IN THE STREET" ? "GUN CASE" : text;
        }

        static string Place(PressRecord record) => string.IsNullOrWhiteSpace(record.Where)
            ? "THE CITY" : record.Where.Trim().ToUpperInvariant();

        static string SentencePlace(PressRecord record) => string.IsNullOrWhiteSpace(record.Where)
            ? "the city" : record.Where.Trim();

        static string Family(int gangId)
        {
            if (gangId < 0 || gangId >= GangCatalog.Names.Length)
                return "UNKNOWN";
            var value = GangCatalog.Names[gangId].Trim();
            if (value.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(4);
            return value.ToUpperInvariant();
        }

        static string FirstName(PressRecord record) =>
            record.Names != null && record.Names.Length > 0
                ? record.Names[0].Trim().ToUpperInvariant() : "";

        static string Names(string[] values)
        {
            if (values == null || values.Length == 0) return "";
            if (values.Length == 1) return values[0] ?? "";
            if (values.Length == 2) return (values[0] ?? "") + " and " + (values[1] ?? "");
            return string.Join(", ", values, 0, values.Length - 1) +
                   ", and " + (values[values.Length - 1] ?? "");
        }

        static string Clock(float hour)
        {
            if (hour < 0f) hour = 0f;
            if (hour >= 24f) hour %= 24f;
            var whole = (int)hour;
            var minute = (int)Math.Round((hour - whole) * 60f);
            if (minute >= 60) { minute = 0; whole = (whole + 1) % 24; }
            return whole.ToString("00") + ":" + minute.ToString("00");
        }

        static NewsPhoto Photo(PressRecord record)
        {
            if (record.Models != null && record.Models.Length > 0 &&
                !string.IsNullOrWhiteSpace(record.Models[0]))
                return new NewsPhoto(PhotoSubject.Person, record.Models[0],
                    "IDENTIFIED IN THE PUBLIC RECORD");
            if (record.Kind == PressKind.OfficerKilled ||
                record.Kind == PressKind.CustodyBroken ||
                record.Kind == PressKind.FiredOnPolice ||
                record.Kind == PressKind.Arrest || record.Kind == PressKind.FlatRaid)
                return new NewsPhoto(PhotoSubject.Vehicle, PictureDesk.PatrolCarModel,
                    "A PATROL CAR AT THE SCENE");
            return NewsPhoto.None;
        }

        static string Fit(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= HeadlineBudget)
                return text ?? "";
            var cut = text.LastIndexOf(' ', HeadlineBudget - 1);
            if (cut < HeadlineBudget / 2)
                cut = HeadlineBudget - 1;
            return text.Substring(0, cut).TrimEnd(' ', ';', ',', ':') + "…";
        }
    }
}
