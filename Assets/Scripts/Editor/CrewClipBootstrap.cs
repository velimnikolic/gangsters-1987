using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Sets the import of the Universal Animation Library FBX (Assets/Animations/
    /// UAL1_Standard.fbx) up for the crews once, on editor load - the LedgerArtBootstrap
    /// discipline: idempotent, no menu to remember.
    ///
    /// Left at its defaults the FBX yields one clip per take with root motion NOT baked
    /// into the pose. The demos drive the men by script with root motion off, which is
    /// right for the walks and jogs (the humanoid strips the forward travel and the man
    /// walks in place, moved by the code) and wrong for everything that moves the root
    /// UP OR DOWN: a fall whose descent is root motion leaves a shot man lying flat in
    /// the air at standing height. So every take gets its vertical and its turn baked
    /// into the pose; the loops (name ends "_Loop") get their loop flag; the locomotion
    /// loops keep their travel out of the pose so they stay in place. Clip names are
    /// the take names as they came ("Armature|Death01") - CrewKit matches on the part
    /// after the bar, so nothing downstream moves.
    /// </summary>
    [InitializeOnLoad]
    static class CrewClipBootstrap
    {
        const string UalPath = "Assets/Animations/UAL1_Standard.fbx";

        static CrewClipBootstrap()
        {
            EditorApplication.delayCall += Configure;
        }

        static void Configure()
        {
            var importer = AssetImporter.GetAtPath(UalPath) as ModelImporter;
            if (importer == null)
                return;

            var clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
                clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0)
                return;

            var changed = false;
            foreach (var clip in clips)
            {
                bool loop = clip.name.EndsWith("_Loop") || clip.takeName.EndsWith("_Loop");
                bool travels = Travels(clip.name);

                // bake Y into the pose (based on the original), bake the turn - based on
                // BODY orientation, not the file's: the library's rig faces the other way
                // from Unity's +Z, and "original" would have every man walk backwards
                // into his own stride - and keep a travelling take's travel as root
                // motion (stripped, so it stays in place)
                // A travelling take (walk, jog) bakes NOTHING - every bit of its root
                // motion is stripped and the man is moved by the code, exactly like the
                // Mixamo walk .anim the crowd already uses; baking its bob and turn made
                // the jog hop. An action (the fall, the flinch, the shot) bakes its
                // vertical, its turn and its step, so a shot man goes down to the ground.
                bool bake = !travels;
                changed |= Want(clip.lockRootHeightY, bake);
                changed |= Want(clip.keepOriginalPositionY, true);
                changed |= Want(clip.lockRootRotation, bake);
                changed |= Want(clip.keepOriginalOrientation, false);
                changed |= Want(clip.lockRootPositionXZ, bake);
                changed |= Want(clip.keepOriginalPositionXZ, true);
                changed |= Want(clip.loopTime, loop);
                changed |= Want(clip.loopPose, false);
                clip.lockRootHeightY = bake;
                clip.keepOriginalPositionY = true;
                clip.lockRootRotation = bake;
                clip.keepOriginalOrientation = false;
                clip.lockRootPositionXZ = bake;
                clip.keepOriginalPositionXZ = true;
                clip.loopTime = loop;
                clip.loopPose = false;
            }

            if (!changed && importer.clipAnimations != null && importer.clipAnimations.Length > 0)
                return;

            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            Debug.Log("[CrewClipBootstrap] " + UalPath + ": " + clips.Length +
                      " takes set up for the crews (vertical and turn baked into the pose, loops flagged).");
        }

        /// <summary>A take that carries the man forward - his travel must not be baked
        /// into the pose, or the loop would lurch a stride and snap back.</summary>
        static bool Travels(string name)
        {
            foreach (var word in new[] { "Walk", "Jog", "Sprint", "Crouch_Fwd", "Swim_Fwd", "Push", "Roll", "Driving" })
                if (name.Contains(word))
                    return true;
            return false;
        }

        static bool Want(bool current, bool value) => current != value;
    }
}
