using System;
using System.Collections.Generic;
using LivingCity.Personnel;
using LivingCity.Territory;
using TMPro;
using UnityEngine;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>Content-sized crew and individual choices shared by both door surfaces.</summary>
    public static class CrewMissionPicker
    {
        public static float Draw(RectTransform panel, float x, float top, float width,
            Roster roster, TerritoryBlockId block, bool restricted, int selectedCrew,
            int selectedPerson, int defaultCrew, Action<int> selectCrew, Action<int> selectPerson,
            bool dark)
        {
            var crews = new List<Crew>();
            var reserve = new List<Character>();
            var physical = Physical();
            BlockMissionChoice.Collect(roster, block, restricted, crews, reserve, physical);
            var ink = dark ? LedgerV2.HeadCream : LedgerV2.Ink;
            var dim = dark ? LedgerV2.HeadDim : LedgerV2.Label;
            var y = top;
            Caps(panel, x, -y, width, "WHO GOES", 9.5f, dim, 4f).font = LedgerStyle.Mono;
            y += 19f;
            var cursor = x;
            foreach (var crew in crews)
            {
                var id = crew.Id;
                var reason = BlockMissionChoice.Refusal(roster, block, id, restricted);
                Chip(BlockMissionChoice.Label(roster, crew), id == selectedCrew,
                    selectedCrew < 0 && selectedPerson < 0 && id == defaultCrew,
                    reason, () => selectCrew(id));
            }
            foreach (var man in reserve)
            {
                var id = man.Id;
                var busy = roster.DoorOrders.Find(id) != null;
                Chip(man.FullName, id == selectedPerson, false,
                    busy ? "already on a doorstep errand" : null, () => selectPerson(id));
            }
            if (cursor > x) y += 31f;
            if (crews.Count == 0 && reserve.Count == 0)
            {
                LedgerV2.Mono(panel, x, -y, width, "No men are available to send.", 10f, dim, 0f);
                y += 20f;
            }
            return y - top + 5f;

            void Chip(string label, bool picked, bool fallback, string refusal, Action select)
            {
                var row = NewRect(label, panel);
                var word = LedgerV2.Mono(row, 10f, -5f, width - 20f, label, 11f,
                    refusal != null ? dim : picked ? LedgerV2.HeadCream : ink, 0.5f);
                word.font = LedgerStyle.MonoBold;
                word.textWrappingMode = TextWrappingModes.NoWrap;
                var w = Mathf.Min(width, Mathf.Ceil(word.GetPreferredValues(label).x) + 22f);
                if (cursor > x && cursor + w > x + width) { cursor = x; y += 31f; }
                PlaceTopLeft(row, cursor, -y, w, 27f);
                word.rectTransform.sizeDelta = new Vector2(w - 20f, 18f);
                word.overflowMode = TextOverflowModes.Ellipsis;
                Fill(row, picked ? LedgerV2.Red : dark ? LedgerV2.DarkPlate : LedgerV2.PanelDark);
                if (fallback) Rule(row, 0f, -26f, w, LedgerV2.Red);
                if (refusal == null) RowButton(row, ClickSurface(row), () => select());
                cursor += w + 6f;
            }
        }

        internal static List<TacticalPersonnelMapping> Physical()
        {
            var rows = new List<TacticalPersonnelMapping>();
            Gameplay.PersonnelDirector.Instance?.Organization?.CollectPhysicalMappings(rows);
            return rows;
        }
    }
}
