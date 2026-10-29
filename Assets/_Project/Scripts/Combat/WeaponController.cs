using System;
using UnityEngine;
using BR.Core;
using BR.Managers;

namespace BR.Combat
{
    /// <summary>
    /// Executa o tiro (hitscan) de qualquer arma equipada, seja do player ou de um bot.
    /// Sem instanciar nada por tiro: raycast + pooled tracer.
    ///
    /// Performance: um Raycast por pellet (1 na maioria das armas) e nenhum
    /// GetComponent em loop — Hitbox e Health ja estao nos colliders.
    /// </summary>
    public sealed class WeaponController : MonoBehaviour
    {
        [SerializeField] private WeaponDefinition definition;
        [SerializeField] private Transform muzzle;
        [SerializeField] private LayerMask hitMask = ~0;
        [SerializeField] private GameObject tracerPrefab;
        [SerializeField] private float tracerLifetime = 0.6f;

        public WeaponDefinition Definition => definition;
        public int Magazine { get; private set; }
        public int Reserve { get; private set; }
        public bool IsReloading { get; private set; }
        public string OwnerName { get; set; } = "Player";
        public bool HasWeapon => definition != null;

        public event Action<WeaponController> AmmoChanged;
        public event Action<WeaponController> Fired;
        public event Action ReloadStarted;

        private float _nextFireTime;
        private Health _ownerHealth;

        // Buffer estático: RaycastNonAlloc sem alocação por tiro.
        private static readonly RaycastHit[] HitBuffer = new RaycastHit[16];

        private void Awake()
        {
            _ownerHealth = GetComponentInParent<Health>();
        }

        private void Start()
        {
            if (definition != null && Magazine == 0) Magazine = definition.magazineSize;
        }

        private void OnDisable()
        {
            IsReloading = false;
            _nextFireTime = 0f;
        }

        /// <summary>Chamado pelo inventário ao equipar (arma ja vem com munição).</summary>
        public void Equip(WeaponDefinition def, int reserveAmmo = -1)
        {
            definition = def;
            IsReloading = false;
            _nextFireTime = 0f;

            if (def == null)
            {
                Magazine = 0;
                Reserve = 0;
            }
            else
            {
                Magazine = def.magazineSize;
                Reserve = reserveAmmo >= 0 ? reserveAmmo : def.maxReserve;
            }

            NotifyAmmo();
        }

        /// <summary>Configuração em runtime (cenas geradas por código).</summary>
        public void Configure(WeaponDefinition def, Transform muzzleTransform, GameObject tracer, LayerMask mask, string ownerName)
        {
            _ownerHealth = GetComponentInParent<Health>();
            muzzle = muzzleTransform;
            tracerPrefab = tracer;
            hitMask = mask;
            OwnerName = ownerName;
            Equip(def);
        }

        public bool CanFire => definition != null && !IsReloading && Magazine > 0 && Time.time >= _nextFireTime;

        /// <summary>Tenta atirar. Este metodo e chamado tanto por PlayerCombat quanto por BotAI.</summary>
        public bool TryFire(Vector3 origin, Vector3 direction)
        {
            if (definition == null)
            {
                // Sem arma equipada: usa a "arma de soco" (melee) para nunca travar o player.
                if (Time.time < _nextFireTime) return false;
                _nextFireTime = Time.time + 0.45f;
                if (Physics.Raycast(origin, direction, out var punchHit, 2.2f, hitMask, QueryTriggerInteraction.Ignore))
                {
                    var punchHealth = punchHit.collider.GetComponentInParent<Health>();
                    if (punchHealth != null && !ReferenceEquals(punchHealth, _ownerHealth))
                    {
                        var info = new DamageInfo
                        {
                            Amount = 22f,
                            Point = punchHit.point,
                            Direction = direction,
                            AttackerName = OwnerName,
                            WeaponId = "melee",
                            Knockback = 6f
                        };
                        punchHealth.ApplyDamage(info);
                    }
                }
                Fired?.Invoke(this);
                return true;
            }

            if (!CanFire)
            {
                if (Magazine <= 0 && !IsReloading) StartReload();
                return false;
            }

            _nextFireTime = Time.time + definition.SecondsBetweenShots;
            Magazine--;

            var pelletCount = Mathf.Max(1, definition.pellets);
            var spread = definition.spreadDegrees;

            for (var i = 0; i < pelletCount; i++)
            {
                var finalDir = pelletCount > 1 ? ApplySpread(direction, spread * 2.5f) : ApplySpread(direction, spread);
                FireSingle(origin, finalDir);
            }

            NotifyAmmo();
            Fired?.Invoke(this);

            if (Magazine == 0 && Reserve > 0)
                StartReload();

            return true;
        }

        private static Vector3 ApplySpread(Vector3 direction, float degrees)
        {
            if (degrees <= 0f) return direction;

            var random = UnityEngine.Random.insideUnitCircle * Mathf.Tan(degrees * Mathf.Deg2Rad);
            var right = Vector3.Cross(direction, Vector3.up).normalized;
            var up = Vector3.Cross(right, direction).normalized;
            return (direction + right * random.x + up * random.y).normalized;
        }

        private void FireSingle(Vector3 origin, Vector3 direction)
        {
            // RaycastNonAlloc + filtro do próprio atirador: a câmera fica atrás do personagem,
            // então o primeiro hit seria o próprio player e bloquearia todo tiro.
            var hitCount = Physics.RaycastNonAlloc(origin, direction, HitBuffer, definition.range, hitMask, QueryTriggerInteraction.Ignore);

            var found = false;
            var closest = float.MaxValue;
            RaycastHit hit = default;

            for (var i = 0; i < hitCount; i++)
            {
                var candidate = HitBuffer[i];
                var candidateHealth = candidate.collider.GetComponentInParent<Health>();

                if (candidateHealth != null && ReferenceEquals(candidateHealth, _ownerHealth)) continue;
                if (candidate.distance >= closest) continue;

                closest = candidate.distance;
                hit = candidate;
                found = true;
            }

            if (!found)
            {
                SpawnTracer(origin, origin + direction * definition.range);
                return;
            }

            SpawnTracer(origin, hit.point);

            var hitbox = hit.collider.GetComponent<Hitbox>();
            var health = hitbox != null ? hitbox.Owner : hit.collider.GetComponentInParent<Health>();
            if (health == null || !health.IsAlive) return;

            var multiplier = hitbox != null ? hitbox.DamageMultiplier : 1f;
            var isHead = hitbox != null && hitbox.IsHeadshot;

            var info = new DamageInfo
            {
                Amount = definition.damage * multiplier,
                Point = hit.point,
                Direction = direction,
                AttackerName = OwnerName,
                WeaponId = definition.itemId,
                IsHeadshot = isHead,
                Knockback = definition.knockback
            };

            health.ApplyDamage(info);
        }

        private void SpawnTracer(Vector3 from, Vector3 to)
        {
            if (tracerPrefab == null || !ObjectPoolManager.IsReady) return;

            var direction = to - from;
            if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;

            var tracer = ObjectPoolManager.Instance.Spawn(tracerPrefab, from, Quaternion.LookRotation(direction));
            if (tracer.TryGetComponent<Tracer>(out var t))
                t.Play(from, to, tracerLifetime);
            else
                ObjectPoolManager.Instance.Despawn(tracer, tracerLifetime);
        }

        public void StartReload()
        {
            if (definition == null || IsReloading) return;
            if (Magazine >= definition.magazineSize || Reserve <= 0) return;
            if (!isActiveAndEnabled) return;

            IsReloading = true;
            ReloadStarted?.Invoke();
            NotifyAmmo();
            CancelInvoke(nameof(FinishReload));
            Invoke(nameof(FinishReload), definition.reloadSeconds);
        }

        private void FinishReload()
        {
            if (definition == null) return;

            var needed = definition.magazineSize - Magazine;
            var taken = Mathf.Min(needed, Reserve);
            Magazine += taken;
            Reserve -= taken;
            IsReloading = false;
            NotifyAmmo();
        }

        public void AddReserveAmmo(int amount)
        {
            if (definition == null) return;
            Reserve = Mathf.Min(definition.maxReserve, Reserve + amount);
            NotifyAmmo();
        }

        private void NotifyAmmo()
        {
            AmmoChanged?.Invoke(this);
            EventBus.Publish(new WeaponAmmoChanged
            {
                WeaponId = definition != null ? definition.itemId : "none",
                Magazine = Magazine,
                Reserve = Reserve,
                IsReloading = IsReloading
            });
        }
    }

    /// <summary>Tracer reutilizavel do pool: uma linha que desaparece.</summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class Tracer : MonoBehaviour, IPoolable
    {
        private LineRenderer _line;

        private void Awake() => _line = GetComponent<LineRenderer>();

        public void Play(Vector3 from, Vector3 to, float lifetime)
        {
            if (_line == null) _line = GetComponent<LineRenderer>();
            _line.positionCount = 2;
            _line.SetPosition(0, from);
            _line.SetPosition(1, to);
            CancelInvoke(nameof(Despawn));
            Invoke(nameof(Despawn), lifetime);
        }

        private void Despawn()
        {
            if (ObjectPoolManager.IsReady) ObjectPoolManager.Instance.Despawn(gameObject);
            else gameObject.SetActive(false);
        }

        public void OnSpawned() { }
        public void OnDespawned() => CancelInvoke();
    }
}
