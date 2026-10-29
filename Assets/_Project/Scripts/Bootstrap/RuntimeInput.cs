using UnityEngine;
using UnityEngine.EventSystems;
using BR.Player;

namespace BR.Bootstrap
{
    /// <summary>Joystick virtual: escreve em InputBridge.Move (mesmo contrato do teclado).</summary>
    public sealed class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform Area;
        public RectTransform Handle;
        public float Radius = 110f;

        public void OnPointerDown(PointerEventData eventData) => Apply(eventData);

        public void OnDrag(PointerEventData eventData) => Apply(eventData);

        public void OnPointerUp(PointerEventData eventData) => ResetStick();

        private void Apply(PointerEventData eventData)
        {
            if (Area == null) return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Area, eventData.position, eventData.pressEventCamera, out var local))
                return;

            var clamped = Vector2.ClampMagnitude(local, Radius);
            InputBridge.Move = clamped / Mathf.Max(1f, Radius);

            if (Handle != null) Handle.anchoredPosition = clamped;
        }

        private void ResetStick()
        {
            InputBridge.Move = Vector2.zero;
            if (Handle != null) Handle.anchoredPosition = Vector2.zero;
        }

        private void OnDisable() => ResetStick();
    }

    /// <summary>Área de arrasto para girar a câmera (toque no lado direito/livre).</summary>
    public sealed class TouchLookArea : MonoBehaviour, IDragHandler
    {
        public float Sensitivity = 0.32f;

        public void OnDrag(PointerEventData eventData)
        {
            InputBridge.Look += eventData.delta * Sensitivity;
        }

        private void LateUpdate()
        {
            // Limpa depois do Update do PlayerController (que é quem consome o Look).
            InputBridge.Look = Vector2.zero;
        }
    }

    /// <summary>Botão com pressão contínua (tiro/sprint).</summary>
    public sealed class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public System.Action OnPress;
        public System.Action OnRelease;

        public void OnPointerDown(PointerEventData eventData) => OnPress?.Invoke();

        public void OnPointerUp(PointerEventData eventData) => OnRelease?.Invoke();

        public void OnPointerExit(PointerEventData eventData) => OnRelease?.Invoke();

        private void OnDisable() => OnRelease?.Invoke();
    }
}
