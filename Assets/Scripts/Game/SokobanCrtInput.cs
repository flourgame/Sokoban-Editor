using UnityEngine;
using UnityEngine.EventSystems;

namespace Kuluobishi.Sokoban
{
    /// <summary>Keep hover, click, slider dragging and touch aligned with curved UI.</summary>
    public sealed class SokobanCrtInput : BaseInput
    {
        public override Vector2 mousePosition => SokobanCrtOverlay.ScreenToSource(base.mousePosition);

        public override Touch GetTouch(int index)
        {
            var touch = base.GetTouch(index);
            var previous = touch.position - touch.deltaPosition;
            touch.position = SokobanCrtOverlay.ScreenToSource(touch.position);
            touch.rawPosition = SokobanCrtOverlay.ScreenToSource(touch.rawPosition);
            touch.deltaPosition = touch.position - SokobanCrtOverlay.ScreenToSource(previous);
            return touch;
        }
    }
}
