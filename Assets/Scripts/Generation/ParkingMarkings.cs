using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Generation
{
    /// <summary>
    /// Turns a block's parking lines into one flat mesh laid over the asphalt.
    ///
    /// The merged-mesh machinery this used to own now lives in GroundPaint, which serves every
    /// kind of paint on the ground - bay lines, paving joints, footpaths. What is left here is
    /// the one thing specific to parking: how wide a bay line is.
    /// </summary>
    public static class ParkingMarkings
    {
        /// <summary>Real bay lines are 10cm. 15 reads better at the default camera distance.</summary>
        public const float LineWidth = 0.15f;

        /// <summary>
        /// Clear of every ground layer - GroundPlacer now stacks a block slab, a lot slab and a
        /// courtyard patch below this. Still far below the 4-unit pavement, so the paint never
        /// climbs a kerb.
        /// </summary>
        public const float LineLift = GroundPaint.PaintLift;

        /// <summary>
        /// Null when there is nothing to draw or no material to draw it with - a PrefabDatabase
        /// whose lineMaterial was never assigned should cost the caller a bare lot, not an
        /// exception.
        /// </summary>
        public static GameObject Emit(
            List<ParkingLayout.Line> lines, Material material, string name, Transform parent)
        {
            if (lines == null || lines.Count == 0)
                return null;

            var strokes = new List<GroundPaint.Stroke>(lines.Count);
            foreach (var line in lines)
                strokes.Add(new GroundPaint.Stroke(line.A, line.B, LineWidth));

            return GroundPaint.Emit(strokes, material, LineLift, name, parent);
        }
    }
}
