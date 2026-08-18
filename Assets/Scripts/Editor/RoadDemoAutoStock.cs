using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Rolls the city's blocks for you when you press Play on the road demo, so the demo
    /// never comes up out of the generic terrace because a menu command was not run.
    ///
    /// It has to be an editor hook rather than something RoadDemoBuilder does itself: the
    /// builder is a MonoBehaviour in the player assembly and cannot reach the composing
    /// passes, which are all editor-only. And it has to happen BEFORE Play rather than
    /// during it, because rolling a block means standing it on a lot pad in the CATALOG
    /// scene - which cannot be opened while the demo scene is entering Play.
    ///
    /// So the sequence is: catch the transition into Play, stop it, open the catalog scene,
    /// roll what is missing, put the demo scene back exactly as it was, and start Play
    /// again. From the outside that is one Play press with a pause in the middle, the first
    /// time; afterwards the stock is there and Play is immediate.
    ///
    /// Only what is MISSING is rolled - a code with no block to its name. Re-rolling all of
    /// it takes a minute or so, which is no way to spend every Play press, so a fresh city
    /// is a deliberate act: either Tools/City/Catalog/Randomise Blocks For Every Lot, or
    /// the toggle below, which re-rolls on every Play for as long as it is on.
    /// </summary>
    [InitializeOnLoad]
    public static class RoadDemoAutoStock
    {
        const string RerollPref = "LivingCity.RoadDemo.RerollOnPlay";
        const string RerollMenu = "Tools/City/Catalog/Re-roll Blocks On Every Play";

        static RoadDemoAutoStock()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        static bool Reroll
        {
            get => EditorPrefs.GetBool(RerollPref, false);
            set => EditorPrefs.SetBool(RerollPref, value);
        }

        [MenuItem(RerollMenu, priority = 65)]
        static void ToggleReroll() => Reroll = !Reroll;

        [MenuItem(RerollMenu, validate = true)]
        static bool ToggleRerollCheck()
        {
            Menu.SetChecked(RerollMenu, Reroll);
            return true;
        }

        /// <summary>Whether the Play press now going through is the one this pass started
        /// itself. Without it a roll that could not fill every lot - or the re-roll toggle,
        /// which is never satisfied - would cancel Play, roll, start Play, cancel it again,
        /// for ever. It lives only between <see cref="Restock"/> and the transition it asks
        /// for, both in edit mode, so the domain reload on entering Play never sees it.</summary>
        static bool _rolled;

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode || !IsDemoScene())
                return;

            if (_rolled)
            {
                _rolled = false;
                return;
            }

            var missing = BlockLotStock.Missing();
            // A stock that stands the same place twice is as unusable as one with a lot
            // size missing: the demo would show two fairgrounds out of it. Rolled before
            // the one-place-per-city rule, or a hand-made block captured since with a
            // place a roll already had - either way the roll is redone. Once per editor
            // session only: a re-roll that comes back doubled would otherwise be run
            // again on every Play, and the demo passes doubles over on its own anyway.
            var doubled = missing.Count == 0 && !SessionState.GetBool(DoubledGivenUp, false)
                ? BlockLotStock.Doubled()
                : new System.Collections.Generic.List<string>();
            // A stock rolled by an older roller - before the floor was calmed, before
            // slices gave way to trimmed rows - is redone too, so the change is in the
            // next city and not waiting on the menu.
            var outdated = missing.Count == 0 && doubled.Count == 0 && BlockLotStock.Outdated();
            if (missing.Count == 0 && doubled.Count == 0 && !outdated && !Reroll)
                return;

            // Stop this run. Setting isPlaying false here cancels the transition; the
            // scene work waits for the next editor tick, by which time the editor is
            // settled back in edit mode and the scenes can be swapped safely.
            EditorApplication.isPlaying = false;
            var why = Reroll
                ? "re-rolling every block (Re-roll Blocks On Every Play is on)"
                : missing.Count > 0
                    ? $"no block has been rolled for lot(s) {string.Join(", ", missing)}"
                    : doubled.Count > 0
                        ? $"the rolled stock stands the same place more than once ({string.Join("; ", doubled)})"
                        : "the rolled stock was made by an older roller";
            Debug.Log($"[RoadDemo] holding Play: {why}. The catalog scene opens, the blocks " +
                      "are rolled and baked, this scene comes back and Play starts on its own.");

            EditorApplication.delayCall += Restock;
        }

        static void Restock()
        {
            // Where to come back to, taken before anything opens the catalog scene. The
            // demo scene has to be saved first or restoring the setup would throw its
            // unsaved changes away - and that is the user's call, not this pass's.
            var setup = EditorSceneManager.GetSceneManagerSetup();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("[RoadDemo] the scene was not saved, so the blocks were not " +
                                 "rolled and Play did not start. Press Play again, or run " +
                                 "Tools/City/Catalog/Randomise Blocks For Every Lot yourself.");
                return;
            }

            try
            {
                BlockLotStock.Stock();
            }
            finally
            {
                // The roll leaves the catalog scene dirty (it clears the pads behind
                // itself); saved here so restoring the setup has nothing to throw away and
                // asks nothing. Back to the demo whatever the roll did, or a failed roll
                // would leave the catalog open with Play never starting and no word why.
                var catalog = SceneManager.GetActiveScene();
                if (catalog.isDirty && catalog.path == SyntyBuildingCatalog.ScenePath)
                    EditorSceneManager.SaveScene(catalog);
                if (setup != null && setup.Length > 0)
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
            }

            if (BlockLotStock.Missing().Count > 0)
            {
                Debug.LogWarning("[RoadDemo] some lot sizes still have no block after the roll - " +
                                 "see the [Stock] lines above. Play starts anyway; those " +
                                 "interiors fall back to the feature pool and the terrace.");
            }
            var doubled = BlockLotStock.Doubled();
            if (doubled.Count > 0)
            {
                SessionState.SetBool(DoubledGivenUp, true);
                Debug.LogWarning("[RoadDemo] the fresh stock still stands a place more than once (" +
                                 string.Join("; ", doubled) + "). Not rolled again this session; the " +
                                 "demo lays each place once and passes the doubles over.");
            }

            _rolled = true;
            EditorApplication.isPlaying = true;
        }

        /// <summary>Set once a doubled stock was re-rolled and came back doubled, so the
        /// check is not what holds every Play for a minute. Session-scoped: a restart of
        /// the editor tries again.</summary>
        const string DoubledGivenUp = "LivingCity.RoadDemo.DoubledStockGivenUp";

        /// <summary>Whether the scene about to run is the road demo - asked of the scene
        /// itself rather than of a path, so a copy of the demo under another name is
        /// stocked as readily as the original.</summary>
        static bool IsDemoScene()
        {
            if (!SceneManager.GetActiveScene().IsValid())
                return false;
            return Object.FindObjectsByType<RoadDemo.RoadDemoBuilder>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0;
        }
    }
}
