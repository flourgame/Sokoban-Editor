using UnityEngine;
using UnityEngine.UI;

namespace Kuluobishi.Sokoban
{
    // A centered, size-relative goal marker. It does not depend on font metrics or text wrapping.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SokobanGoalGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = rectTransform.rect; var radius = Mathf.Min(rect.width, rect.height) * 0.38f;
            if (radius <= 0f) return;
            var center = rect.center; var thickness = Mathf.Min(radius * 0.22f, Mathf.Max(1f, radius * 0.10f));
            var points = new[] { center + Vector2.up * radius, center + Vector2.right * radius,
                center + Vector2.down * radius, center + Vector2.left * radius };
            for (var edge = 0; edge < 4; edge++)
            {
                var a = points[edge]; var b = points[(edge + 1) % 4]; var delta = (b - a).normalized;
                var normal = new Vector2(-delta.y, delta.x) * thickness * 0.5f;
                var first = mesh.currentVertCount;
                mesh.AddVert(a + normal, color, Vector2.zero); mesh.AddVert(b + normal, color, Vector2.zero);
                mesh.AddVert(b - normal, color, Vector2.zero); mesh.AddVert(a - normal, color, Vector2.zero);
                mesh.AddTriangle(first, first + 1, first + 2); mesh.AddTriangle(first, first + 2, first + 3);
            }
        }
    }
}
