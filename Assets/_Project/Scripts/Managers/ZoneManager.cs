using System;
using UnityEngine;
using BR.Combat;
using BR.Core;

namespace BR.Managers
{
    /// <summary>
    /// Circulo da morte:
    ///  - fases com espera + fechamento configurados no MatchConfig;
    ///  - dano progressivo fora da zona (1 tick/s em Health.All, sem GC);
    ///  - visual com LineRenderer (barato) + publicacao de eventos para a HUD.
    ///
    /// Nao usa collider nem trigger de zona: matematica de distancia e mais barata
    /// que 50 OnTriggerStay por segundo.
    /// </summary>
    public sealed class ZoneManager : MonoBehaviour
    {
        public static ZoneManager Instance { get; private set; }

        [Serializable]
        public struct ZonePhase
        {
            public float waitSeconds;
            public float shrinkSeconds;
            public float targetRadius;
            public float damagePerSecond;
        }

        [Header("Configuracao")]
        [SerializeField] private ZonePhase[] phases;
        [SerializeField] private float defaultMapRadius = 450f;

        [Header("Visual")]
        [SerializeField] private LineRenderer ringRenderer;
        [SerializeField] private int ringSegments = 72;
        [SerializeField] private float ringHeight = 0.3f;
        [SerializeField] private float damageTickInterval = 1f;
        [SerializeField] private float publishInterval = 0.2f;

        public Vector3 Center { get; private set; }
        public float Radius { get; private set; } = 450f;
        public float NextRadius { get; private set; }
        public int PhaseIndex { get; private set; } = -1;
        public bool IsShrinking { get; private set; }
        public bool IsRunning { get; private set; }
        public float DamagePerSecond { get; private set; }
        public float SecondsToNextEvent => _timer;
        public float TargetRadiusAfterShrink => NextRadius;

        private float _timer;
        private float _shrinkProgress;
        private Vector3 _shrinkFrom;
        private Vector3 _shrinkTo;
        private float _shrinkStartRadius;
        private float _shrinkEndRadius;
        private float _damageTimer;
        private float _publishTimer;
        private bool _damageTickThisFrame;
        private int _phaseCursor;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(Instance, this)) Instance = null;
        }

        public void Begin(Vector3 center, float radius)
        {
            Center = center;
            Radius = radius;
            NextRadius = radius;
            IsRunning = false;
            PhaseIndex = -1;
            _phaseCursor = 0;
            _timer = 0f;
            _damageTimer = 0f;

            UpdateVisual();
            PublishZone();
        }

        /// <summary>Inicia o fechamento da zona (chamado quando o player pousa).</summary>
        public void BeginPhases()
        {
            if (phases == null || phases.Length == 0) return;

            IsRunning = true;
            _phaseCursor = 0;
            StartPhase(0);
        }

        public void Stop()
        {
            IsRunning = false;
            IsShrinking = false;
        }

        private void StartPhase(int index)
        {
            PhaseIndex = index;
            var phase = phases[index];

            NextRadius = Mathf.Max(0f, phase.targetRadius);
            DamagePerSecond = phase.damagePerSecond;
            IsShrinking = false;
            _timer = Mathf.Max(0.1f, phase.waitSeconds);
            _shrinkProgress = 0f;

            PublishZone();
        }

        private void Update()
        {
            if (!IsRunning) return;

            var dt = Time.deltaTime;
            _timer -= dt;

            if (!IsShrinking)
            {
                if (_timer <= 0f) StartShrink();
            }
            else
            {
                _shrinkProgress += dt / Mathf.Max(0.1f, phases[PhaseIndex].shrinkSeconds);
                var t = Mathf.Clamp01(_shrinkProgress);

                Radius = Mathf.Lerp(_shrinkStartRadius, _shrinkEndRadius, t);
                Center = Vector3.Lerp(_shrinkFrom, _shrinkTo, t);
                UpdateVisual();

                if (t >= 1f) FinishShrink();
            }

            TickDamage(dt);
            PublishTimer(dt);
        }

        private void StartShrink()
        {
            var phase = phases[PhaseIndex];

            _shrinkFrom = Center;
            _shrinkTo = PickNextCenter(Center, NextRadius);
            _shrinkStartRadius = Radius;
            _shrinkEndRadius = NextRadius;
            _shrinkProgress = 0f;
            IsShrinking = true;
            _timer = Mathf.Max(0.1f, phase.shrinkSeconds);

            PublishZone();
        }

        private void FinishShrink()
        {
            IsShrinking = false;
            Radius = _shrinkEndRadius;
            Center = _shrinkTo;
            UpdateVisual();

            PhaseIndex++;
            if (PhaseIndex < phases.Length) StartPhase(PhaseIndex);
            else
            {
                PhaseIndex = phases.Length - 1;
                IsRunning = false;
            }
        }

        /// <summary>Novo centro sempre dentro do circulo atual (zona "puxa" o jogador).</summary>
        private Vector3 PickNextCenter(Vector3 current, float nextRadius)
        {
            var maxOffset = Mathf.Max(0f, Radius - nextRadius);
            var random = UnityEngine.Random.insideUnitCircle * maxOffset;
            var candidate = current + new Vector3(random.x, 0f, random.y);

            var edge = candidate - current;
            if (edge.magnitude + nextRadius > Radius && edge.sqrMagnitude > 0.01f)
            {
                var allowed = Mathf.Max(0f, Radius - nextRadius);
                candidate = current + edge.normalized * allowed;
            }

            candidate.y = current.y;
            return candidate;
        }

        // ------------------------------------------------------------ Damage

        private void TickDamage(float dt)
        {
            if (DamagePerSecond <= 0f) return;

            _damageTimer += dt;
            if (_damageTimer < damageTickInterval) return;

            var tickAmount = DamagePerSecond * _damageTimer;
            _damageTimer = 0f;

            var all = Health.All;
            // Loop reverso: morte/despawn pode alterar a lista durante a iteracao.
            for (var i = all.Count - 1; i >= 0; i--)
            {
                if (i >= all.Count) continue;
                var health = all[i];
                if (health == null || !health.IsAlive) continue;

                var delta = health.transform.position - Center;
                if (delta.sqrMagnitude <= Radius * Radius) continue;

                var info = new DamageInfo
                {
                    Amount = tickAmount,
                    Point = health.transform.position,
                    Direction = (Center - health.transform.position).normalized,
                    AttackerName = "Zona",
                    WeaponId = "zone",
                    Knockback = 0f
                };

                health.ApplyDamage(info);
            }
        }

        private void PublishTimer(float dt)
        {
            _publishTimer -= dt;
            if (_publishTimer > 0f) return;
            _publishTimer = publishInterval;

            var player = Player.PlayerController.Local;
            var inside = true;
            if (player != null)
            {
                var delta = player.transform.position - Center;
                inside = delta.sqrMagnitude <= Radius * Radius;
            }

            EventBus.Publish(new ZoneTimerTick
            {
                SecondsToNextEvent = Mathf.Max(0f, _timer),
                IsShrinking = IsShrinking,
                DamagePerSecond = DamagePerSecond,
                LocalPlayerInside = inside
            });
        }

        private void PublishZone()
        {
            EventBus.Publish(new ZoneUpdated
            {
                Center = Center,
                Radius = Radius,
                NextRadius = NextRadius,
                PhaseIndex = PhaseIndex
            });
        }

        // ------------------------------------------------------------ Visual

        private void UpdateVisual()
        {
            if (ringRenderer == null) return;

            ringRenderer.useWorldSpace = true;
            ringRenderer.loop = true;
            ringRenderer.positionCount = ringSegments;

            var step = Mathf.PI * 2f / ringSegments;
            for (var i = 0; i < ringSegments; i++)
            {
                var angle = step * i;
                var point = new Vector3(
                    Center.x + Mathf.Cos(angle) * Radius,
                    ringHeight,
                    Center.z + Mathf.Sin(angle) * Radius);
                ringRenderer.SetPosition(i, point);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
            Gizmos.DrawWireSphere(new Vector3(Center.x, 0f, Center.z), Radius);
        }

        /// <summary>Fallback caso o MatchConfig nao traga fases.</summary>
        public void SetPhases(ZonePhase[] newPhases) => phases = newPhases;

        /// <summary>Vincula o anel visual em runtime (cenas geradas por código).</summary>
        public void SetRing(LineRenderer renderer) => ringRenderer = renderer;

        private void OnValidate()
        {
            if (ringSegments < 12) ringSegments = 12;
            if (defaultMapRadius < 10f) defaultMapRadius = 10f;
        }
    }
}
