using UnityEngine;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// Brightens the pedestrian under the pointer. A MaterialPropertyBlock per renderer
    /// rather than a material swap: no material instance is created, no batch is broken
    /// permanently, and clearing the block puts the renderer back exactly as it was.
    ///
    /// Stateless - the InteractionController owns WHO is hovered; this only knows how the
    /// hover looks. Event-driven at the hover boundary, so cost is one property-block write
    /// per renderer per enter/leave, never per frame.
    /// </summary>
    public static class HoverHighlight
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();

        /// <summary>How much the base colour lifts. Enough to read, short of glowing.</summary>
        const float Brighten = 1.35f;

        public static void Apply(InteractableNpc npc)
        {
            if (!npc)
                return;

            foreach (var renderer in npc.Renderers)
            {
                if (!renderer)
                    continue;

                var material = renderer.sharedMaterial;
                var baseColour = material && material.HasProperty(BaseColorId)
                    ? material.GetColor(BaseColorId)
                    : Color.white;

                Block.Clear();
                Block.SetColor(BaseColorId, baseColour * Brighten);
                renderer.SetPropertyBlock(Block);
            }
        }

        public static void Clear(InteractableNpc npc)
        {
            if (!npc)
                return;

            foreach (var renderer in npc.Renderers)
                if (renderer)
                    renderer.SetPropertyBlock(null);
        }
    }
}
