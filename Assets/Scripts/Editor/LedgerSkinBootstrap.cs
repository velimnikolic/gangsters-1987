using LivingCity.UI;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Bakes LedgerSkinSet.asset (the ledger's Modern Menus wardrobe) on every editor
    /// load - the LedgerArtBootstrap discipline: no menu item to re-run, idempotent,
    /// and it only fills slots that are EMPTY, so a hand-picked override in the
    /// Inspector is never fought.
    ///
    /// Everything comes from Assets/Synty/InterfaceModernMenus by fixed path. A missing
    /// file (pack not imported yet, a rename upstream) leaves that slot null and the
    /// almanac keeps its UiSkin / flat-colour fallback for that piece - the next domain
    /// reload picks it up.
    /// </summary>
    [InitializeOnLoad]
    static class LedgerSkinBootstrap
    {
        const string AssetPath = "Assets/Configs/UI/Resources/LedgerSkinSet.asset";
        const string Pack = "Assets/Synty/InterfaceModernMenus/";

        static LedgerSkinBootstrap()
        {
            // delayCall: the asset database is not safe to touch from a static
            // constructor running mid-refresh.
            EditorApplication.delayCall += Bake;
        }

        static void Bake()
        {
            var set = AssetDatabase.LoadAssetAtPath<LedgerSkinSet>(AssetPath);
            if (!set)
            {
                set = ScriptableObject.CreateInstance<LedgerSkinSet>();
                AssetDatabase.CreateAsset(set, AssetPath);
            }

            var changed = false;
            // Button_03 is Button_ModernMenus_Menu_Item_01's face - the user's pick
            // for GIVE and every action like it. Rewire, not Wire: assets baked with
            // the earlier Button_04 default migrate; a hand-picked override stands.
            changed |= Rewire(ref set.button,
                Pack + "Sprites/ModernMenus/SPR_ModernMenus_Button_04.png",
                Pack + "Sprites/ModernMenus/SPR_ModernMenus_Button_03.png");
            // The flat chip, NOT the slab: toolbars must read as a different species.
            changed |= Wire(ref set.toolbarButton,
                Pack + "Sprites/ModernMenus/SPR_ModernMenus_Button_08.png");
            changed |= Wire(ref set.tab,
                Pack + "Sprites/ModernMenus/SPR_ModernMenus_Button_04.png");
            // Button_ModernMenus_Icon_01's icon - the pack's own gold star.
            changed |= Wire(ref set.star,
                Pack + "Sprites/Icons_ModernMenus/SPR_ModernMenus_Icon_Star_01_Persp.png");
            changed |= Wire(ref set.panel,
                Pack + "Sprites/ModernMenus/SPR_ModernMenus_Frame_Box_Medium_01_Background.png");
            // The pack's own key art - a neon drive-by that could be this city's cover.
            changed |= Wire(ref set.backdrop,
                Pack + "Samples/Sprites/SPR_ModernMenus_Example_Background_Heist_01.png");
            changed |= Wire(ref set.bodyFont,
                Pack + "Fonts/BarlowCondensed/BarlowCondensed-Medium SDF.asset");
            changed |= Wire(ref set.headlineFont,
                Pack + "Fonts/SairaCondensed/SairaCondensed-ExtraBold SDF.asset");

            if (!changed)
                return;

            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssetIfDirty(set);
        }

        /// <summary>Wire with a migration: an empty slot OR one still holding the old
        /// default moves to the new default; anything else is a hand-picked override
        /// and stands.</summary>
        static bool Rewire(ref Sprite slot, string oldDefaultPath, string newPath)
        {
            if (slot && slot != AssetDatabase.LoadAssetAtPath<Sprite>(oldDefaultPath))
                return false;

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(newPath);
            if (!sprite || slot == sprite)
                return false;

            slot = sprite;
            return true;
        }

        static bool Wire(ref Sprite slot, string path)
        {
            if (slot)
                return false;

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (!sprite)
                return false;

            slot = sprite;
            return true;
        }

        static bool Wire(ref TMP_FontAsset slot, string path)
        {
            if (slot)
                return false;

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (!font)
                return false;

            slot = font;
            return true;
        }
    }
}
