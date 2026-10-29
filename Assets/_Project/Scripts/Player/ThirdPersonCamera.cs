using UnityEngine;

namespace BR.Player
{
    /// <summary>
    /// Camera de terceira pessoa com orbita por toque/mouse, colisao por spherecast
    /// e transicoes de distancia por estado (chao / queda / paraquedas).
    /// Um unico Camera.main na cena inteira (economia de overdraw e CPU).
    /// </summary>
    public sealed class ThirdPersonCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 pivotOffset = new Vector3(0f, 1.55f, 0f);

        [Header("Orbita")]
        [SerializeField] private float minPitch = -35f;
        [SerializeField] private float maxPitch = 65f;
        [SerializeField] private float followLerp = 14f;
        [SerializeField] private float collisionRadius = 0.25f;
        [SerializeField] private LayerMask collisionMask = ~0;

        [Header("Distancias")]
        [SerializeField] private float groundDistance = 3.4f;
        [SerializeField] private float airborneDistance = 6.5f;
        [SerializeField] private float parachuteDistance = 8.5f;

        public float Yaw { get; private set; }
        public float Pitch { get; private set; } = 12f;

        private float _targetDistance;
        private float _currentDistance;
        private bool _isAirborne;

        private void Awake()
        {
            _targetDistance = groundDistance;
            _currentDistance = groundDistance;
        }

        private void Start()
        {
            if (target == null && PlayerController.Local != null)
            {
                target = PlayerController.Local.transform;
                SnapToTarget();
            }
        }

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            if (target != null) SnapToTarget();
        }

        public void ApplyLook(Vector2 deltaDegrees)
        {
            Yaw += deltaDegrees.x;
            Pitch = Mathf.Clamp(Pitch - deltaDegrees.y, minPitch, maxPitch);
        }

        public void SetAirborne(bool airborne)
        {
            _isAirborne = airborne;
            if (airborne) _targetDistance = parachuteDistance;
            else _targetDistance = groundDistance;
        }

        public void SnapToTarget()
        {
            if (target == null) return;
            var pivot = target.position + pivotOffset;
            transform.position = pivot - Quaternion.Euler(Pitch, Yaw, 0f) * Vector3.forward * _currentDistance;
            transform.LookAt(pivot);
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                if (PlayerController.Local == null) return;
                target = PlayerController.Local.transform;
            }

            // Distancia: no ar fica mais aberta para ler o mapa.
            _targetDistance = _isAirborne ? parachuteDistance : groundDistance;
            _currentDistance = Mathf.Lerp(_currentDistance, _targetDistance, 3f * Time.deltaTime);

            var pivot = target.position + pivotOffset;
            var rotation = Quaternion.Euler(Pitch, Yaw, 0f);
            var desired = pivot - rotation * Vector3.forward * _currentDistance;

            // Colisao da camera: impede atravessar paredes.
            if (Physics.SphereCast(pivot, collisionRadius, (desired - pivot).normalized, out var hit,
                    _currentDistance, collisionMask, QueryTriggerInteraction.Ignore))
            {
                desired = pivot - rotation * Vector3.forward * Mathf.Max(0.6f, hit.distance - 0.15f);
            }

            transform.position = Vector3.Lerp(transform.position, desired, followLerp * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(pivot - transform.position), followLerp * Time.deltaTime);
        }
    }
}
