using System.Collections.Generic;
using UnityEngine;
using BR.Combat;
using BR.Core;
using BR.Inventory;
using BR.Managers;

namespace BR.Bots
{
    public enum BotState
    {
        Drop = 0,
        Loot = 1,
        Patrol = 2,
        Chase = 3,
        Attack = 4,
        Flee = 5,
        Dead = 6
    }

    /// <summary>
    /// FSM leve (sem Behavior Tree, sem NavMesh) para ate 49 bots em mobile.
    ///
    /// Custo controlado por:
    ///  - "think" em intervalo (0.12s perto, 0.5s longe) -> nao faz Raycast todo frame;
    ///  - LOD de simulacao controlado pelo BotManager por distancia;
    ///  - zero alocacao por frame (buffers estaticos, sem LINQ, sem closures);
    ///  - avoidance = 1 SphereCast curto (sem A*).
    ///
    /// Fluxo: Drop -> Loot -> Patrol <-> Chase/Attack -> Flee (vida baixa) -> Dead
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(Health))]
    public sealed class BotAI : MonoBehaviour, IKnockbackReceiver
    {
        [Header("Movimento")]
        [SerializeField] private float patrolSpeed = 3.6f;
        [SerializeField] private float chaseSpeed = 5.2f;
        [SerializeField] private float fleeSpeed = 6.0f;
        [SerializeField] private float dropSpeed = 22f;
        [SerializeField] private float rotationSpeed = 10f;
        [SerializeField] private LayerMask groundMask = ~0;

        [Header("Percepcao")]
        [SerializeField] private float viewDistance = 60f;
        [SerializeField] private float fovDegrees = 130f;
        [SerializeField] private float loseTargetDistance = 85f;
        [SerializeField] private LayerMask lineOfSightMask = ~0;

        [Header("Combate")]
        [SerializeField] private float preferredRange = 18f;
        [SerializeField] private float burstDuration = 0.55f;
        [SerializeField] private float burstPause = 0.3f;
        [SerializeField] private float aimErrorDegrees = 2.4f;
        [SerializeField] private float lowHealthThreshold = 30f;

        [Header("UI / feedback")]
        [SerializeField] private string botName = "Bot";

        private CharacterController _cc;
        private Health _health;
        private WeaponController _weapon;
        private InventoryComponent _inventory;
        private Vector3 _verticalVelocity;
        private Vector3 _knockback;

        private float _nextThink;
        private float _nextBurstTime;
        private float _burstEndTime;
        private bool _firing;
        private float _strafeSign = 1f;
        private float _strafeTimer;
        private float _targetLostTime;

        private Vector3 _dropTarget;
        private Vector3 _destination;
        private LootPickup _lootTarget;
        private bool _waitingOnIsland = true;

        public BotState State { get; private set; } = BotState.Drop;
        public Health Health => _health;
        public bool IsAlive => _health != null && _health.IsAlive;
        public float SimulationInterval { get; private set; } = 0f;

        private static readonly Collider[] OverlapBuffer = new Collider[8];

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _health = GetComponent<Health>();
            _weapon = GetComponentInChildren<WeaponController>();
            _inventory = GetComponent<InventoryComponent>();

            _health.Died += OnDied;
        }

        private void OnDestroy()
        {
            if (_health != null) _health.Died -= OnDied;
        }

        /// <summary>Chamado pelo BotManager no spawn.</summary>
        public void Initialize(string displayName, Vector3 dropTarget, Vector3 islandPosition)
        {
            botName = displayName;
            _dropTarget = dropTarget;
            _destination = dropTarget;
            _waitingOnIsland = true;

            // Reset completo: o mesmo bot pode ser reciclado pelo pool em outra partida.
            _firing = false;
            _knockback = Vector3.zero;
            _lootTarget = null;
            Target = null;
            _targetLostTime = 0f;

            if (_cc != null) _cc.enabled = true;
            if (_weapon != null) _weapon.enabled = true;

            _health.Configure(displayName, false, 100f, 0f);
            if (_weapon != null) _weapon.OwnerName = displayName;

            transform.position = islandPosition;
            State = BotState.Drop;
        }

        /// <summary>Atualiza apenas o alvo de pouso (sem resetar vida/nome).</summary>
        public void SetDropTarget(Vector3 dropTarget)
        {
            _dropTarget = dropTarget;
            _destination = dropTarget;
        }

        public void JumpFromPlane(Vector3 planePosition)
        {
            _cc.enabled = true;
            transform.position = planePosition;
            _verticalVelocity = -4f;
            _waitingOnIsland = false;
            State = BotState.Drop;
        }

        public void SetSimulationInterval(float interval) => SimulationInterval = Mathf.Max(0f, interval);

        private void Update()
        {
            if (State == BotState.Dead || !_health.IsAlive) return;

            // Na ilha de espera o bot fica idle (sem correr para loot antes do aviao).
            if (_waitingOnIsland) return;

            var dt = Time.deltaTime;

            if (SimulationInterval > 0f && Time.time < _nextThink) return;

            switch (State)
            {
                case BotState.Drop: UpdateDrop(dt); break;
                case BotState.Loot: UpdateLoot(dt); break;
                case BotState.Patrol: UpdatePatrol(dt); break;
                case BotState.Chase: UpdateChase(dt); break;
                case BotState.Attack: UpdateAttack(dt); break;
                case BotState.Flee: UpdateFlee(dt); break;
            }

            ApplyKnockback(dt);
        }

        // ------------------------------------------------------------ Drop

        private void UpdateDrop(float dt)
        {
            var toTarget = _dropTarget - transform.position;
            var horizontal = new Vector3(toTarget.x, 0f, toTarget.z);
            if (horizontal.sqrMagnitude > 0.5f) horizontal.Normalize();

            _verticalVelocity = Mathf.MoveTowards(_verticalVelocity, -dropSpeed, 40f * dt);
            _cc.Move((horizontal * dropSpeed * 0.55f + Vector3.up * _verticalVelocity) * dt);

            if (horizontal.sqrMagnitude > 0.01f) RotateTowards(horizontal, dt * 0.5f);

            if (_cc.isGrounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = -2f;
                EnterLoot();
            }
        }

        // ------------------------------------------------------------ Loot

        private void EnterLoot()
        {
            if (LootManager.Instance != null &&
                LootManager.Instance.TryClaimNearest(transform.position, 45f, out var loot))
            {
                _lootTarget = loot;
                _destination = loot.transform.position;
                State = BotState.Loot;
                return;
            }

            EnterPatrol();
        }

        private void UpdateLoot(float dt)
        {
            if (_lootTarget == null || !_lootTarget.CanPickup)
            {
                EnterLoot();
                return;
            }

            MoveTowards(_destination, chaseSpeed, dt, 1.4f);

            if ((transform.position - _destination).sqrMagnitude <= 3.2f)
            {
                if (_inventory != null) _inventory.TryPickup(_lootTarget);
                _lootTarget = null;

                // Prioridade: se ainda nao tem arma, procura outro item antes de patrulhar.
                if (_weapon == null || !_weapon.HasWeapon) EnterLoot();
                else EnterPatrol();
            }
        }

        // ------------------------------------------------------------ Patrol

        private void EnterPatrol()
        {
            State = BotState.Patrol;
            _destination = PickPatrolPoint();
        }

        private Vector3 PickPatrolPoint()
        {
            var zone = Managers.ZoneManager.Instance;
            var center = zone != null ? zone.Center : Vector3.zero;
            var radius = zone != null ? zone.Radius * 0.85f : 200f;

            var random = Random.insideUnitCircle * radius;
            return new Vector3(center.x + random.x, transform.position.y, center.z + random.y);
        }

        private void UpdatePatrol(float dt)
        {
            MoveTowards(_destination, patrolSpeed, dt, 1.5f);

            if ((transform.position - _destination).sqrMagnitude <= 4f)
                _destination = PickPatrolPoint();

            if (TryAcquireTarget()) return;

            // Se ficou fora da zona, corre para dentro.
            if (IsOutsideZone()) _destination = ZoneCenter();
        }

        // ------------------------------------------------------------ Chase

        private void EnterChase(Health target)
        {
            Target = target;
            State = BotState.Chase;
            _nextThink = 0f;
        }

        public Health Target { get; private set; }

        private void UpdateChase(float dt)
        {
            if (!IsTargetValid())
            {
                EnterPatrol();
                return;
            }

            var targetPos = Target.transform.position;
            var distance = Vector3.Distance(transform.position, targetPos);

            if (distance > loseTargetDistance * 0.9f)
            {
                _targetLostTime += dt;
                if (_targetLostTime > 2.5f) { EnterPatrol(); return; }
            }
            else _targetLostTime = 0f;

            if (TryAcquireBetterTarget()) return;

            if (distance <= viewDistance * 0.6f && HasLineOfSight(Target))
                State = BotState.Attack;
            else
                MoveTowards(targetPos, chaseSpeed, dt, preferredRange * 0.8f);

            if (IsOutsideZone()) _destination = ZoneCenter();
        }

        // ------------------------------------------------------------ Attack

        private void UpdateAttack(float dt)
        {
            if (!IsTargetValid())
            {
                EnterPatrol();
                return;
            }

            if (_health.HealthPoints <= lowHealthThreshold)
            {
                EnterFlee();
                return;
            }

            var targetPos = Target.transform.position;
            var distance = Vector3.Distance(transform.position, targetPos);

            if (distance > viewDistance || !HasLineOfSight(Target))
            {
                State = BotState.Chase;
                return;
            }

            // Strafe: muda de direcao a cada ~1.1s, mantendo distancia preferida.
            _strafeTimer -= dt;
            if (_strafeTimer <= 0f)
            {
                _strafeTimer = Random.Range(0.8f, 1.5f);
                _strafeSign = -_strafeSign;
            }

            var toTarget = (targetPos - transform.position);
            toTarget.y = 0f;
            var direction = toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : transform.forward;
            var right = Vector3.Cross(Vector3.up, direction) * _strafeSign;

            var radial = distance > preferredRange ? 1f : (distance < preferredRange * 0.6f ? -0.7f : 0f);
            var move = (right * 0.75f + direction * radial).normalized;
            Move(move * chaseSpeed * 0.75f, dt);

            RotateTowards(direction, dt * 1.6f);
            AimAndShoot(targetPos, dt);
        }

        private void AimAndShoot(Vector3 targetPos, float dt)
        {
            if (_weapon == null) return;

            if (_weapon.Magazine <= 0 && !_weapon.IsReloading)
            {
                _weapon.StartReload();
                return;
            }

            if (Time.time < _nextBurstTime) return;

            if (!_firing)
            {
                _firing = true;
                _burstEndTime = Time.time + burstDuration;
            }

            if (Time.time >= _burstEndTime)
            {
                _firing = false;
                _nextBurstTime = Time.time + burstPause + Random.Range(0f, 0.15f);
                return;
            }

            var origin = transform.position + Vector3.up * 1.45f;
            var aimPoint = targetPos + Vector3.up * 1.15f;

            // Erro de mira: bots nao sao aimbot (importante para a sensacao de jogo justo).
            var error = Random.insideUnitSphere * (aimErrorDegrees * 0.02f) * Vector3.Distance(origin, aimPoint);
            var direction = (aimPoint + error - origin).normalized;

            _weapon.TryFire(origin, direction);
        }

        // ------------------------------------------------------------ Flee

        private void EnterFlee()
        {
            State = BotState.Flee;
            _firing = false;
        }

        private void UpdateFlee(float dt)
        {
            var threat = IsTargetValid() ? Target.transform.position : transform.position + transform.forward;
            var away = transform.position - threat;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = transform.forward;

            var zoneCenter = ZoneCenter();
            var toZone = zoneCenter - transform.position;
            toZone.y = 0f;

            // Foge do inimigo, mas nunca para fora da zona.
            var move = (away.normalized * 0.6f + toZone.normalized * 0.4f).normalized;
            Move(move * fleeSpeed, dt);
            RotateTowards(move, dt);

            if (_health.HealthPoints > lowHealthThreshold + 25f)
                State = BotState.Patrol;

            if ((transform.position - zoneCenter).sqrMagnitude < 25f)
                State = BotState.Patrol;
        }

        // ------------------------------------------------------------ Perception

        private bool TryAcquireTarget()
        {
            if (Time.time < _nextThink) return Target != null && IsTargetValid();
            _nextThink = Time.time + 0.15f + SimulationInterval;

            var best = FindVisibleEnemy();
            if (best == null) return false;

            EnterChase(best);
            return true;
        }

        private bool TryAcquireBetterTarget()
        {
            if (_nextThink > Time.time) return false;
            _nextThink = Time.time + 0.2f + SimulationInterval;

            // So troca se o atual morreu ou se apareceu alguem bem mais perto.
            var candidate = FindVisibleEnemy();
            if (candidate == null) return false;

            var currentSqr = Target != null ? (Target.transform.position - transform.position).sqrMagnitude : float.MaxValue;
            var candidateSqr = (candidate.transform.position - transform.position).sqrMagnitude;

            if (candidateSqr < currentSqr * 0.5f)
            {
                EnterChase(candidate);
                return true;
            }

            return false;
        }

        private Health FindVisibleEnemy()
        {
            Health best = null;
            var bestSqr = viewDistance * viewDistance;

            var all = Health.All;
            for (var i = 0; i < all.Count; i++)
            {
                var candidate = all[i];
                if (candidate == null || !candidate.IsAlive || ReferenceEquals(candidate, _health)) continue;

                var delta = candidate.transform.position - transform.position;
                var sqr = delta.sqrMagnitude;
                if (sqr >= bestSqr) continue;

                // FOV horizontal
                var flat = new Vector3(delta.x, 0f, delta.z);
                if (flat.sqrMagnitude > 0.01f && Vector3.Angle(transform.forward, flat) > fovDegrees * 0.5f)
                    continue;

                if (!HasLineOfSight(candidate)) continue;

                bestSqr = sqr;
                best = candidate;
            }

            return best;
        }

        private bool HasLineOfSight(Health target)
        {
            var origin = transform.position + Vector3.up * 1.45f;
            var aim = target.transform.position + Vector3.up * 1.1f;
            var dir = aim - origin;

            if (Physics.Raycast(origin, dir.normalized, out var hit, dir.magnitude + 0.4f, lineOfSightMask, QueryTriggerInteraction.Ignore))
            {
                var hitHealth = hit.collider.GetComponentInParent<Health>();
                return ReferenceEquals(hitHealth, target);
            }

            return false;
        }

        private bool IsTargetValid()
        {
            return Target != null && Target.IsAlive &&
                   (Target.transform.position - transform.position).sqrMagnitude < loseTargetDistance * loseTargetDistance;
        }

        private bool IsOutsideZone()
        {
            var zone = Managers.ZoneManager.Instance;
            if (zone == null) return false;
            return (transform.position - zone.Center).sqrMagnitude > zone.Radius * zone.Radius;
        }

        private Vector3 ZoneCenter()
        {
            var zone = Managers.ZoneManager.Instance;
            return zone != null ? zone.Center : Vector3.zero;
        }

        // ------------------------------------------------------------ Movement

        private void MoveTowards(Vector3 destination, float speed, float dt, float stopDistance = 0.8f)
        {
            var delta = destination - transform.position;
            delta.y = 0f;

            if (delta.sqrMagnitude <= stopDistance * stopDistance)
            {
                Move(Vector3.zero, dt);
                return;
            }

            var direction = delta.normalized;
            direction = Avoid(direction);

            Move(direction * speed, dt);
            RotateTowards(direction, dt);
        }

        private void Move(Vector3 velocity, float dt)
        {
            if (_cc.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
            else _verticalVelocity += -25f * dt;

            _cc.Move((velocity + Vector3.up * _verticalVelocity) * dt);
        }

        /// <summary>Avoidance barato: 1 SphereCast. Sem NavMesh, sem A*.</summary>
        private Vector3 Avoid(Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.01f) return direction;

            var origin = transform.position + Vector3.up * 0.9f;
            if (Physics.SphereCast(origin, 0.4f, direction, out var hit, 1.4f, groundMask, QueryTriggerInteraction.Ignore))
            {
                var tangent = Vector3.Cross(Vector3.up, hit.normal).normalized;
                if (Vector3.Dot(tangent, direction) < 0f) tangent = -tangent;

                // Mantem um pouco da direcao original para nao "grudar" na parede.
                return (direction * 0.3f + tangent * 0.7f).normalized;
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

        public void ApplyKnockback(Vector3 direction, float force)
        {
            direction.y = 0f;
            _knockback += direction.normalized * force;
        }

        private void ApplyKnockback(float dt)
        {
            if (_knockback.sqrMagnitude < 0.01f) return;
            _cc.Move(_knockback * dt);
            _knockback = Vector3.MoveTowards(_knockback, Vector3.zero, 7f * dt);
        }

        // ------------------------------------------------------------ Death

        private void OnDied(Health health, DamageInfo info)
        {
            State = BotState.Dead;
            _firing = false;
            _cc.enabled = false;
            if (_weapon != null) _weapon.enabled = false;

            EventBus.Publish(new KillFeedMessage
            {
                Killer = string.IsNullOrEmpty(info.AttackerName) ? "Zona" : info.AttackerName,
                Victim = botName,
                WeaponId = info.WeaponId,
                KillerIsLocalPlayer = info.AttackerName == "Você"
            });

            // MatchManager/BotManager decide o despawn (pool) e contabiliza.
            BotManager.NotifyBotDied(this, info);
        }
    }
}
