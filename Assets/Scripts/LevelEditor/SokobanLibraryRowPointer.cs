using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Kuluobishi.Sokoban.Editor
{
    public sealed class SokobanLibraryRowPointer : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IPointerClickHandler, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public Action<PointerEventData> Press, Release, Click, InitializeDrag, BeginDrag, Drag, EndDrag;
        public void OnPointerDown(PointerEventData e) => Press?.Invoke(e);
        public void OnPointerUp(PointerEventData e) => Release?.Invoke(e);
        public void OnPointerClick(PointerEventData e) => Click?.Invoke(e);
        public void OnInitializePotentialDrag(PointerEventData e) => InitializeDrag?.Invoke(e);
        public void OnBeginDrag(PointerEventData e) => BeginDrag?.Invoke(e);
        public void OnDrag(PointerEventData e) => Drag?.Invoke(e);
        public void OnEndDrag(PointerEventData e) => EndDrag?.Invoke(e);
    }
}
