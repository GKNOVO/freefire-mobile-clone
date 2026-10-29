using System;
using UnityEngine;
using BR.Combat;
using BR.Core;

namespace BR.Player
{
    public enum PlayerState
    {
        InPlane = 0,
        Freefall = 1,
        Parachuting = 2,
        Grounded = 3,
        Dead = 4
    }

    /// <summary>
    /// Controlador do jogador local: Ilha -> Aviao -> Queda livre -> Paraquedas -> Chao.
    /// Usa CharacterController (sem Rigidbody) porque:
    ///  - custo menor que fisica rigida em mobile;
    ///  - movimento determinístico, sem jitter de colisao;
    ///  - permite "teleportar" do aviao para o ceu sem impulsos estranhos.
    ///
    /// A camera fica separada (ThirdPersonCamera) e so recebe yaw/pitch.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(Health))]
    public sealed class PlayerController : MonoBehaviour, IKnockbackReceiver
    {
        [Header("Referencias")]
        [SerializeField] private ThirdPersonCamera cameraRig;
        [SerializeField] private Animator animator;

        [Header("Movimento")]
        [SerializeField] private float walkSpeed = 5.4f;
        [SerializeField] private float sprintSpeed = 7.6f;
        [SerializeField] private float crouchSpeed = 2.8f;
        [SerializeField] private float groundAcceleration = 22f;
        [SerializeField] private float airControl = 12f;
        [SerializeField] private float rotationSpeed = 14f;

        [Header("Queda / Paraquedas")]
        [SerializeField] private float gravity = -25f;
        [SerializeField] private float terminalFallSpeed = -60f;
        [SerializeField] private float freefallHorizontalSpeed = 16f;
        [SerializeField] private float parachuteDescentSpeed = -6.5f;
        [SerializeField] private float parachuteForwardSpeed = 13f;
        [SerializeField] private float parachuteSteerSpeed = 7f;
        [SerializeField] private float autoParachuteHeight = 90f;
        [SerializeField] private float minManualParachuteHeight = 25f;
        [SerializeField] private LayerMask groundMask = ~0;

        [Header("Vida")]
        [SerializeField] private float knockbackDecay = 6f;

        public PlayerState State { get; private set; } = PlayerState.InPlane;
        public bool IsGrounded => State == PlayerState.Grounded;
        public event Action<PlayerState> StateChanged;

        private CharacterController _cc;
        private Health _health;
        private float _verticalVelocity;
        private Vector3 _knockback;
        private Vector3 _horizontalVelocity;
        private bool _parachuteOpen;

        public static PlayerController Local { get; private set; }

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _health = GetComponent<Health>();
            if (cameraRig == null) cameraRig = FindFirstObjectByType<ThirdPersonCamera>();
            Local = this;
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(Local, this)) Local = null;
        }

        private void Update()
        {
            if (State == PlayerState.Dead || State == PlayerState.InPlane) return;

            var dt = Time.deltaTime;

            // Camera primeiro: o movimento e sempre relativo ao yaw atual.
            if (cameraRig != null && InputBridge.Look.sqrMagnitude > 0f)
                cameraRig.ApplyLook(InputBridge.Look);

            switch (State)
            {
                case PlayerState.Freefall: UpdateFreefall(dt); break;
                case PlayerState.Parachuting: UpdateParachute(dt); break;
                case PlayerState.Grounded: UpdateGrounded(dt); break;
            }

            ApplyKnockback(dt);
        }

        // ------------------------------------------------------------ Plane

        public void EnterPlane(Transform anchor)
        {
            SetState(PlayerState.InPlane);
            _cc.enabled = false;
            _verticalVelocity = 0f;
            _parachuteOpen = false;

            if (anchor != null)
            {
                transform.SetParent(anchor, false);
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
            }
        }

        /// <summary>Chamado pelo botao PULAR da HUD.</summary>
        public void JumpFromPlane(Vector3 worldPosition)
        {
            transform.SetParent(null, true);
            transform.position = worldPosition;
            _cc.enabled = true;
            _verticalVelocity = -3f;
            _parachuteOpen = false;
            SetState(PlayerState.Freefall);
            if (cameraRig != null) cameraRig.SetAirborne(true);
        }

        public bool CanJump => State == PlayerState.InPlane || State == PlayerState.Freefall;

        // ------------------------------------------------------------ Freefall

        private void UpdateFreefall(float dt)
        {
            var yawRotation = Quaternion.Euler(0f, cameraRig != null ? cameraRig.Yaw : 0f, 0f);
            var input = new Vector3(InputBridge.Move.x, 0f, InputBridge.Move.y);

            var desiredHorizontal = yawRotation * input * freefallHorizontalSpeed;
            _horizontalVelocity = Vector3.Lerp(_horizontalVelocity, desiredHorizontal, airControl * dt);

            _verticalVelocity = Mathf.MoveTowards(_verticalVelocity, terminalFallSpeed, 30f * dt);

            var move = _horizontalVelocity + Vector3.up * _verticalVelocity;
            _cc.Move(move * dt);

            if (input.sqrMagnitude > 0.05f)
                RotateTowards(_horizontalVelocity, dt);

            var heightToGround = HeightAboveGround();

            // Automatico: so abre quando o chao foi realmente detectado perto.
            if (heightToGround >= 0f && _verticalVelocity < -8f && heightToGround <= autoParachuteHeight)
                OpenParachute();
            // Manual: o jogador pode abrir a qualquer momento (altura minima so informativa).
            else if (InputBridge.ConsumeParachute() && (heightToGround < 0f || heightToGround >= minManualParachuteHeight))
                OpenParachute();
            // Pouso sem paraquedas (salto baixo).
            else if (_cc.isGrounded)
                Land();
        }

        private void OpenParachute()
        {
            if (_parachuteOpen) return;

            _parachuteOpen = true;
            _verticalVelocity = parachuteDescentSpeed;
            SetState(PlayerState.Parachuting);
            if (animator != null) animator.SetTrigger("Parachute");
        }

        // ------------------------------------------------------------ Parachute

        private void UpdateParachute(float dt)
        {
            var yawRotation = Quaternion.Euler(0f, cameraRig != null ? cameraRig.Yaw : 0f, 0f);
            var input = new Vector3(InputBridge.Move.x, 0f, InputBridge.Move.y);

            // Planeio: sempre desce um pouco, avanca e aceita correcao lateral.
            var forward = yawRotation * Vector3.forward * parachuteForwardSpeed;
            var steer = yawRotation * input * parachuteSteerSpeed;
            var horizontal = Vector3.Lerp(_horizontalVelocity, forward + steer, 3.5f * dt);
            _horizontalVelocity = horizontal;

            _verticalVelocity = Mathf.MoveTowards(_verticalVelocity, parachuteDescentSpeed, 6f * dt);

            _cc.Move((_horizontalVelocity + Vector3.up * _verticalVelocity) * dt);

            RotateTowards(_horizontalVelocity, dt * 0.6f);

            if (_cc.isGrounded && _verticalVelocity <= 0f)
                Land();
        }

        private void Land()
        {
            SetState(PlayerState.Grounded);
            _verticalVelocity = -2f;
            _horizontalVelocity = Vector3.zero;
            if (animator != null) animator.SetTrigger("Land");
            if (cameraRig != null) cameraRig.SetAirborne(false);
        }

        // ------------------------------------------------------------ Ground

        private void UpdateGrounded(float dt)
        {
            var yawRotation = Quaternion.Euler(0f, cameraRig != null ? cameraRig.Yaw : 0f, 0f);
            var input = new Vector3(InputBridge.Move.x, 0f, InputBridge.Move.y);

            var speed = InputBridge.CrouchHeld ? crouchSpeed : (InputBridge.SprintHeld ? sprintSpeed : walkSpeed);
            var worldMove = yawRotation * input;
            if (worldMove.sqrMagnitude > 1f) worldMove.Normalize();

            worldMove = BotAvoidanceSteer(worldMove);

            var targetVelocity = worldMove * speed;
            _horizontalVelocity = Vector3.Lerp(_horizontalVelocity, targetVelocity, groundAcceleration * dt);

            if (_cc.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
            _verticalVelocity += gravity * dt;

            _cc.Move((_horizontalVelocity + Vector3.up * _verticalVelocity) * dt);

            if (worldMove.sqrMagnitude > 0.01f)
                RotateTowards(worldMove, dt);

            if (animator != null)
            {
                animator.SetFloat("Speed", _horizontalVelocity.magnitude, 0.1f, dt);
                animator.SetBool("Crouch", InputBridge.CrouchHeld);
            }
        }

        /// <summary>Desvio barato de obstaculos: um spherecast curto, sem NavMesh.</summary>
        private Vector3 BotAvoidanceSteer(Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.01f) return direction;

            var origin = transform.position + Vector3.up * 1f;
            if (Physics.SphereCast(origin, 0.35f, direction, out var hit, 1.1f, groundMask, QueryTriggerInteraction.Ignore))
            {
                var tangent = Vector3.Cross(Vector3.up, hit.normal).normalized;
                if (Vector3.Dot(tangent, direction) < 0f) tangent = -tangent;
                return (direction * 0.35f + tangent * 0.65f).normalized;
            }

            return direction;
        }

        private void RotateTowards(Vector3 direction, float dt)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f) return;

            var targetRotation = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * dt);
        }

        // ------------------------------------------------------------ Util

        public float HeightAboveGround()
        {
            if (Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, out var hit, 500f, groundMask, QueryTriggerInteraction.Ignore))
                return Vector3.Distance(transform.position, hit.point);
            return -1f;
        }

        public void TeleportTo(Vector3 position)
        {
            _cc.enabled = false;
            transform.position = position;
            _cc.enabled = true;
            _verticalVelocity = -2f;
            SetState(PlayerState.Grounded);
            if (cameraRig != null) cameraRig.SnapToTarget();
        }

        public void ApplyKnockback(Vector3 direction, float force)
        {
            direction.y = 0f;
            _knockback += direction.normalized * force;
        }

        private void ApplyKnockback(float dt)
        {
            if (_knockback.sqrMagnitude < 0.01f) return;

            _cc.Move(_knockback * dt);
            _knockback = Vector3.MoveTowards(_knockback, Vector3.zero, knockbackDecay * dt);
        }

        public void Die()
        {
            SetState(PlayerState.Dead);
            _cc.enabled = false;
            if (animator != null) animator.SetTrigger("Die");
            if (cameraRig != null) cameraRig.SetTarget(null);
        }

        private void SetState(PlayerState next)
        {
            if (State == next) return;
            State = next;
            StateChanged?.Invoke(next);
            EventBus.Publish(new PlayerStateChangedEvent { State = next });
        }
    }

    /// <summary>Evento opcional para HUD (botao PULAR/PARAQUEDAS aparece conforme estado).</summary>
    public struct PlayerStateChangedEvent
    {
        public PlayerState State;
    }
}
