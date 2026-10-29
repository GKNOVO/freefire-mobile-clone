using UnityEngine;

namespace BR.Player
{
    /// <summary>
    /// Driver de teclado/mouse para testar no Editor. Nao existe em build mobile
    /// (o componente e desativado por plataforma).
    /// </summary>
    public sealed class DesktopInputDriver : MonoBehaviour
    {
        [SerializeField] private float mouseSensitivity = 2.2f;
        [SerializeField] private bool activeInEditorOnly = true;

        private void Awake()
        {
            if (activeInEditorOnly && !Application.isEditor)
                enabled = false;
        }

        private void Update()
        {
            var move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (move.sqrMagnitude > 1f) move.Normalize();
            InputBridge.Move = move;

            InputBridge.SprintHeld = Input.GetKey(KeyCode.LeftShift);
            InputBridge.CrouchHeld = Input.GetKey(KeyCode.LeftControl);
            InputBridge.FireHeld = Input.GetMouseButton(0);
            InputBridge.JumpHeld = Input.GetKey(KeyCode.Space);

            if (Input.GetKeyDown(KeyCode.R)) InputBridge.PressReload();
            if (Input.GetKeyDown(KeyCode.E)) InputBridge.PressInteract();
            if (Input.GetKeyDown(KeyCode.F)) InputBridge.PressParachute();
            if (Input.GetKeyDown(KeyCode.Space)) InputBridge.PressJump();
            if (Input.GetMouseButtonDown(0)) InputBridge.PressFire();

            var look = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * mouseSensitivity;
            InputBridge.Look += look;
        }

        private void LateUpdate()
        {
            InputBridge.ClearDeltas();
        }
    }
}
