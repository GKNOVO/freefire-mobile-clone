using System;
using System.Collections.Generic;
using UnityEngine;
using BR.Core;

namespace BR.Combat
{
    public struct DamageInfo
    {
        public float Amount;
        public Vector3 Point;
        public Vector3 Direction;
        public string AttackerName;
        public string WeaponId;
        public bool IsHeadshot;
        public float Knockback;
    }

    public interface IDamageable
    {
        bool IsAlive { get; }
        void ApplyDamage(in DamageInfo info);
    }

    /// <summary>Implementado por PlayerController/BotAI para receber o knockback leve.</summary>
    public interface IKnockbackReceiver
    {
        void ApplyKnockback(Vector3 direction, float force);
    }

    /// <summary>
    /// Vida + colete. Registro estatico evita FindObjectsOfType (caro) para:
    ///  - ZoneManager aplicar dano fora da zona;
    ///  - bots encontrarem alvos proximos;
    ///  - MatchManager calcular vivos.
    /// </summary>
    public sealed class Health : MonoBehaviour, IDamageable
    {
        public static readonly List<Health> All = new List<Health>(64);

        [Header("Config")]
        [SerializeField] private float maxHealth = 100f;
        [Tooltip("Capacidade maxima de colete. O jogador comeca sem colete.")]
        [SerializeField] private float maxArmor = 100f;
        [SerializeField] private float startingArmor = 0f;
        [SerializeField] private bool isPlayer = false;
        [SerializeField] private string displayName = "Player";
        [SerializeField] private float armorAbsorption = 0.5f; // colete absorve 50% ate quebrar

        public float HealthPoints { get; private set; }
        public float Armor { get; private set; }
        public float MaxHealth => maxHealth;
        public float MaxArmor => maxArmor;
        public bool IsPlayer => isPlayer;
        public string DisplayName => displayName;

        public bool IsAlive => HealthPoints > 0f;

        public event Action<Health, DamageInfo> Damaged;
        public event Action<Health, DamageInfo> Died;

        private IKnockbackReceiver _knockback;
        private float _lastDamageTime = -99f;

        private void Awake()
        {
            HealthPoints = maxHealth;
            Armor = Mathf.Clamp(startingArmor, 0f, maxArmor);
            _knockback = GetComponent<IKnockbackReceiver>();
        }

        private void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
        }

        private void OnDisable()
        {
            All.Remove(this);
        }

        public void Configure(string name, bool player, float hp, float armor = 0f)
        {
            displayName = name;
            isPlayer = player;
            maxHealth = hp;
            HealthPoints = hp;
            Armor = Mathf.Clamp(armor, 0f, maxArmor);
        }

        public void ApplyDamage(in DamageInfo info)
        {
            if (!IsAlive) return;

            var remaining = info.Amount;

            if (Armor > 0f)
            {
                var absorbed = Mathf.Min(Armor, remaining * armorAbsorption);
                Armor -= absorbed;
                remaining -= absorbed;
            }

            HealthPoints = Mathf.Max(0f, HealthPoints - remaining);
            _lastDamageTime = Time.time;

            Damaged?.Invoke(this, info);

            if (info.Knockback > 0f && _knockback != null)
                _knockback.ApplyKnockback(info.Direction, info.Knockback);

            EventBus.Publish(new DamageNumberRequested
            {
                WorldPosition = info.Point,
                Amount = info.Amount,
                IsHeadshot = info.IsHeadshot
            });

            if (!IsAlive)
            {
                Died?.Invoke(this, info);
                if (isPlayer)
                {
                    EventBus.Publish(new PlayerDied
                    {
                        KillerName = string.IsNullOrEmpty(info.AttackerName) ? "Zona" : info.AttackerName,
                        IsLocalPlayer = true
                    });
                }
            }
        }

        public void Heal(float amount)
        {
            HealthPoints = Mathf.Min(maxHealth, HealthPoints + amount);
        }

        public void AddArmor(float amount)
        {
            Armor = Mathf.Min(maxArmor, Armor + amount);
        }

        public bool WasRecentlyDamaged(float window) => Time.time - _lastDamageTime <= window;

        /// <summary>Busca o alvo vivo mais proximo em um raio (usa sqrMagnitude, sem alocacao).</summary>
        public static Health FindNearest(Vector3 origin, float maxRange, Health exclude, bool excludeSelf = true)
        {
            Health best = null;
            var bestSqr = maxRange * maxRange;

            for (var i = 0; i < All.Count; i++)
            {
                var candidate = All[i];
                if (candidate == null || !candidate.IsAlive) continue;
                if (excludeSelf && ReferenceEquals(candidate, exclude)) continue;

                var sqr = (candidate.transform.position - origin).sqrMagnitude;
                if (sqr >= bestSqr) continue;

                bestSqr = sqr;
                best = candidate;
            }

            return best;
        }
    }
}
