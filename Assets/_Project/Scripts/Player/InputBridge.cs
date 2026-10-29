using UnityEngine;

namespace BR.Player
{
    /// <summary>
    /// Camada de entrada agnostica de dispositivo.
    ///
    /// - Desktop/Editor: DesktopInputDriver preenche os campos.
    /// - Mobile: VirtualJoystick escreve em Move; TouchLookArea acumula Look (graus);
    ///   os botoes da HUD escrevem em FireHeld / ParachutePressed / InteractPressed.
    ///
    /// Assim PlayerController/PlayerCombat nunca conhecem UI nem plataforma.
    /// </summary>
    public static class InputBridge
    {
        public static Vector2 Move;          // -1..1 (x = strafe, y = frente)
        public static Vector2 Look;          // delta em graus, ja multiplicado pela sensibilidade
        public static bool SprintHeld;
        public static bool CrouchHeld;
        public static bool FireHeld;
        public static bool JumpHeld;

        // Flags de "aperto" consumidas uma vez por frame por quem lê.
        private static bool _reloadPressed;
        private static bool _interactPressed;
        private static bool _parachutePressed;
        private static bool _jumpPressed;
        private static bool _firePressed;

        public static void PressReload() => _reloadPressed = true;
        public static void PressInteract() => _interactPressed = true;
        public static void PressParachute() => _parachutePressed = true;
        public static void PressJump() => _jumpPressed = true;
        public static void PressFire() => _firePressed = true;

        public static bool ConsumeReload() => Consume(ref _reloadPressed);
        public static bool ConsumeInteract() => Consume(ref _interactPressed);
        public static bool ConsumeParachute() => Consume(ref _parachutePressed);
        public static bool ConsumeJump() => Consume(ref _jumpPressed);
        public static bool ConsumeFire() => Consume(ref _firePressed);

        private static bool Consume(ref bool flag)
        {
            if (!flag) return false;
            flag = false;
            return true;
        }

        /// <summary>Chamado no fim do frame para limpar deltas continuos.</summary>
        public static void ClearDeltas()
        {
            Look = Vector2.zero;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Move = Vector2.zero;
            Look = Vector2.zero;
            SprintHeld = CrouchHeld = FireHeld = JumpHeld = false;
            _reloadPressed = _interactPressed = _parachutePressed = _jumpPressed = _firePressed = false;
        }
    }
}
