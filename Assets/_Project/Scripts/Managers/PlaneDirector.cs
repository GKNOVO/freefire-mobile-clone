using UnityEngine;

namespace BR.Managers
{
    /// <summary>
    /// Move o aviao em rota reta e fornece o "anchor" onde player e bots ficam.
    /// Sem fisica: apenas lerp de posicao/rotacao — custo praticamente zero.
    /// </summary>
    public sealed class PlaneDirector : MonoBehaviour
    {
        [SerializeField] private Transform anchor;
        [SerializeField] private Transform visual;
        [SerializeField] private float altitude = 400f;
        [SerializeField] private float rollDegrees = -6f;

        public Transform Anchor => anchor != null ? anchor : transform;
        public Vector3 Position => Anchor.position;
        public float Progress { get; private set; }
        public bool IsFlying { get; private set; }

        private Vector3 _start;
        private Vector3 _end;
        private float _duration;
        private float _elapsed;

        private void Awake()
        {
            if (anchor == null) anchor = transform;
        }

        /// <summary>Define o transform onde player e bots ficam durante o voo.</summary>
        public void SetAnchor(Transform value)
        {
            if (value != null) anchor = value;
        }

        public void Configure(Vector3 start, Vector3 end, float duration)
        {
            _start = new Vector3(start.x, altitude, start.z);
            _end = new Vector3(end.x, altitude, end.z);
            _duration = Mathf.Max(1f, duration);
            _elapsed = 0f;
            Progress = 0f;
            IsFlying = true;

            Apply(0f);
        }

        /// <summary>Chamado pelo MatchManager a cada frame da fase de aviao.</summary>
        public void Tick(float deltaTime)
        {
            if (!IsFlying) return;

            _elapsed += deltaTime;
            Progress = Mathf.Clamp01(_elapsed / _duration);
            Apply(Progress);

            if (Progress >= 1f) IsFlying = false;
        }

        private void Apply(float t)
        {
            var position = Vector3.Lerp(_start, _end, t);
            Anchor.position = position;

            var direction = (_end - _start);
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.01f)
                Anchor.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up) * Quaternion.Euler(0f, 0f, rollDegrees);

            if (visual != null) visual.position = position;
        }

        public void Stop() => IsFlying = false;
    }
}
