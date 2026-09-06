using UnityEngine;
using UnityEngine.EventSystems;

namespace RoadDemo
{
    /// <summary>Owns a minimap press and its fixed ground projection until release.
    /// Moving the street camera must not move the destination beneath a held pointer.</summary>
    public sealed class TurfMinimapNavigation : MonoBehaviour, IPointerDownHandler,
        IPointerUpHandler, IInitializePotentialDragHandler, IDragHandler, IEndDragHandler
    {
        TurfMinimap _map;
        DemoCamera _rig;
        RectTransform _view;
        int _pointer;
        float _scale;

        public bool Held { get; private set; }
        public Vector2 Centre { get; private set; }
        public float Heading { get; private set; }
        public float Pitch { get; private set; }

        public void Init(TurfMinimap map, DemoCamera rig)
        {
            _map = map;
            _rig = rig;
            _view = (RectTransform)transform;
        }

        bool Available => _map != null && _map.isActiveAndEnabled && _map.Printed &&
            !_map.Collapsed && _rig != null && !_rig.SuppressInput && !_rig.MapOut &&
            !TurfMapHud.IsOpen && !LivingCity.UI.ModalGate.PaperUp;

        public void OnPointerDown(PointerEventData data)
        {
            if (Held || data.button != PointerEventData.InputButton.Left || !Available)
                return;
            Held = true;
            _pointer = data.pointerId;
            Centre = new Vector2(_rig.pivot.x, _rig.pivot.z);
            Heading = _rig.yaw;
            Pitch = _rig.pitch;
            _scale = _map.CanvasPerMetre;
            PointerGesture.ClaimPress();
            Move(data);
        }

        public void OnInitializePotentialDrag(PointerEventData data) => data.useDragThreshold = false;

        public void OnDrag(PointerEventData data)
        {
            if (!Held || data.pointerId != _pointer) return;
            if (!Available) { Held = false; return; }
            Move(data);
        }

        void Move(PointerEventData data)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _view, data.position, data.pressEventCamera, out var local)) return;
            var rect = _view.rect;
            local = new Vector2(Mathf.Clamp(local.x, rect.xMin, rect.xMax),
                Mathf.Clamp(local.y, rect.yMin, rect.yMax)) - rect.center;
            var offset = TurfMapHud.RotateForHeading(TurfMapHud.RemoveTilt(
                local / _scale, TurfMapHud.PitchTilt(Pitch)), -Heading);
            _rig.Drop();
            _rig.PanBy(Centre + offset - new Vector2(_rig.pivot.x, _rig.pivot.z));
        }

        public void OnPointerUp(PointerEventData data)
        {
            if (data.pointerId == _pointer && data.button == PointerEventData.InputButton.Left)
                Held = false;
        }

        public void OnEndDrag(PointerEventData data)
        {
            if (data.pointerId == _pointer) Held = false;
        }

        void Update()
        {
            if (Held && !Available) Held = false;
        }

        void OnDisable() => Held = false;
        void OnApplicationFocus(bool focused) { if (!focused) Held = false; }
    }
}
