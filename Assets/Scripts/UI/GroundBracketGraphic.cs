using UnityEngine;
using UnityEngine.UI;

namespace LivingCity.UI
{
    /// <summary>Screen-space corner brackets projected from a subject's ground footprint.</summary>
    public sealed class GroundBracketGraphic : MaskableGraphic
    {
        static readonly Vector2 CentreAnchor = new Vector2(0.5f, 0.5f);

        readonly Vector2[] corners = new Vector2[4];
        float arm = 14f;
        float thickness = 2.5f;
        bool hasGeometry;

        public void Set(Vector2[] source, float armLength, float lineThickness, Color tint)
        {
            var min = source[0];
            var max = source[0];
            for (var i = 0; i < corners.Length; i++)
            {
                min = Vector2.Min(min, source[i]);
                max = Vector2.Max(max, source[i]);
            }

            arm = Mathf.Max(1f, armLength);
            thickness = Mathf.Max(1f, lineThickness);
            color = tint;

            var centre = (min + max) * 0.5f;
            var padding = arm + thickness * 2f;
            rectTransform.anchorMin = CentreAnchor;
            rectTransform.anchorMax = CentreAnchor;
            rectTransform.pivot = CentreAnchor;
            rectTransform.anchoredPosition = centre;
            rectTransform.sizeDelta = new Vector2(
                Mathf.Max(1f, max.x - min.x + padding * 2f),
                Mathf.Max(1f, max.y - min.y + padding * 2f));

            for (var i = 0; i < corners.Length; i++)
                corners[i] = source[i] - centre;

            hasGeometry = true;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (!hasGeometry)
                return;

            for (var i = 0; i < corners.Length; i++)
            {
                var corner = corners[i];
                AddArm(vh, corner, corners[(i + 1) % corners.Length]);
                AddArm(vh, corner, corners[(i + corners.Length - 1) % corners.Length]);
            }
        }

        void AddArm(VertexHelper vh, Vector2 corner, Vector2 toward)
        {
            var delta = toward - corner;
            var length = delta.magnitude;
            if (length <= 0.001f)
                return;

            var end = corner + delta / length * Mathf.Min(arm, length * 0.45f);
            AddLine(vh, corner, end);
        }

        void AddLine(VertexHelper vh, Vector2 a, Vector2 b)
        {
            var delta = b - a;
            var length = delta.magnitude;
            if (length <= 0.001f)
                return;

            var normal = new Vector2(-delta.y, delta.x) / length * (thickness * 0.5f);
            var tint = (Color32)color;
            var start = vh.currentVertCount;
            vh.AddVert(a - normal, tint, Vector2.zero);
            vh.AddVert(a + normal, tint, Vector2.zero);
            vh.AddVert(b - normal, tint, Vector2.zero);
            vh.AddVert(b + normal, tint, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 1, start + 3);
        }
    }

    public static class HumanGroundBracket
    {
        public const float GroundLift = 0.04f;
        public const float Arm = 14f;
        public const float SelectedArm = 18f;
        public const float PulseArm = 6f;
        public const float Thickness = 2.5f;
        public const float PulsePeriod = 0.9f;

        const float HalfSize = 0.48f;

        static readonly Color Own = new Color(0.16f, 0.95f, 0.35f, 0.96f);
        static readonly Color Other = new Color(0.24f, 0.58f, 1f, 0.94f);

        public static Color Tint(bool own) => own ? Own : Other;

        public static float ArmLength(bool selected, bool pulse, float time)
        {
            var arm = selected ? SelectedArm : Arm;
            if (pulse)
            {
                var beat = Mathf.Sin(time * (2f * Mathf.PI / PulsePeriod));
                arm += beat * PulseArm;
            }

            return arm;
        }

        public static bool TryProject(
            Camera camera,
            RectTransform root,
            Transform target,
            Vector3[] worldCorners,
            Vector2[] localCorners,
            float screenWidth,
            float screenHeight)
        {
            if (!camera || !root || !target ||
                worldCorners == null || worldCorners.Length < 4 ||
                localCorners == null || localCorners.Length < 4)
                return false;

            var centre = target.position + Vector3.up * GroundLift;
            var centreScreen = camera.WorldToScreenPoint(centre);
            if (centreScreen.z <= 0f ||
                centreScreen.x < 0f || centreScreen.x > screenWidth ||
                centreScreen.y < 0f || centreScreen.y > screenHeight)
                return false;

            var half = HalfMetres(target);
            worldCorners[0] = centre + new Vector3(-half, 0f, -half);
            worldCorners[1] = centre + new Vector3(half, 0f, -half);
            worldCorners[2] = centre + new Vector3(half, 0f, half);
            worldCorners[3] = centre + new Vector3(-half, 0f, half);

            for (var i = 0; i < 4; i++)
            {
                var screen = camera.WorldToScreenPoint(worldCorners[i]);
                if (screen.z <= 0f ||
                    !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        root, screen, null, out localCorners[i]))
                    return false;
            }

            return true;
        }

        static float HalfMetres(Transform target)
        {
            var scale = target.lossyScale;
            var footprintScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            return Mathf.Clamp(HalfSize * footprintScale, 0.35f, 0.82f);
        }
    }
}
