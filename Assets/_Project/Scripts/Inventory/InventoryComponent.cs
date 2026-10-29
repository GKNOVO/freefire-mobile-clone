using UnityEngine;
using BR.Core;
using BR.Combat;

namespace BR.Inventory
{
    /// <summary>
    /// Componente de inventario do player (e opcionalmente de bots).
    /// - Slots fixos (InventorySystem)
    /// - 2 slots de arma que equipam no WeaponController
    /// - Deteccao de loot por proximidade com overlap non-alloc (sem GC)
    /// </summary>
    [RequireComponent(typeof(Health))]
    public sealed class InventoryComponent : MonoBehaviour
    {
        [Header("Inventario")]
        [SerializeField] private int capacity = 6;
        [SerializeField] private int weaponSlots = 2;

        [Header("Pickup")]
        [SerializeField] private float pickupRadius = 2.6f;
        [SerializeField] private LayerMask lootMask = ~0;
        [SerializeField] private float scanInterval = 0.2f;

        [Header("Armas")]
        [SerializeField] private WeaponController weaponController;

        private static readonly Collider[] OverlapBuffer = new Collider[16];

        private InventorySystem _inventory;
        private float _nextScan;

        public InventorySystem Inventory => _inventory ??= new InventorySystem(capacity);
        public LootPickup FocusedLoot { get; private set; }

        /// <summary>Bots desligam o scan próprio: eles pegam loot via LootManager.</summary>
        public bool AutoScan { get; set; } = true;
        public WeaponDefinition EquippedWeapon => weaponController != null ? weaponController.Definition : null;
        public bool HasWeapon => EquippedWeapon != null;

        private void Awake()
        {
            _inventory = new InventorySystem(capacity);
            if (weaponController == null) weaponController = GetComponentInChildren<WeaponController>();
        }

        private void Update()
        {
            if (!AutoScan) return;
            if (Time.time < _nextScan) return;
            _nextScan = Time.time + scanInterval;
            ScanForLoot();
        }

        private void ScanForLoot()
        {
            FocusedLoot = null;

            var count = Physics.OverlapSphereNonAlloc(transform.position, pickupRadius, OverlapBuffer, lootMask, QueryTriggerInteraction.Collide);
            var bestSqr = float.MaxValue;

            for (var i = 0; i < count; i++)
            {
                var col = OverlapBuffer[i];
                if (col == null) continue;

                var loot = col.GetComponentInParent<LootPickup>();
                if (loot == null || !loot.CanPickup) continue;

                var sqr = (loot.transform.position - transform.position).sqrMagnitude;
                if (sqr >= bestSqr) continue;

                bestSqr = sqr;
                FocusedLoot = loot;
            }
        }

        /// <summary>Chamado pelo botao de interacao (HUD) ou pela tecla E no desktop.</summary>
        public bool TryPickupFocused()
        {
            if (FocusedLoot == null) return false;
            var picked = TryPickup(FocusedLoot);
            FocusedLoot = null;
            return picked;
        }

        public bool TryPickup(LootPickup loot)
        {
            if (loot == null || !loot.CanPickup) return false;

            var item = loot.Item;
            var amount = loot.Amount;

            switch (item.type)
            {
                case ItemType.Weapon:
                    if (item.weapon == null || weaponController == null) return false;
                    weaponController.Equip(item.weapon);
                    break;

                case ItemType.Armor:
                    var health = GetComponent<Health>();
                    if (health != null)
                    {
                        if (health.Armor >= health.MaxArmor && health.MaxArmor > 0f) return false;
                        health.AddArmor(item.effectAmount);
                    }
                    break;

                case ItemType.Medkit:
                    var hp = GetComponent<Health>();
                    if (hp != null)
                    {
                        if (hp.HealthPoints >= hp.MaxHealth) return false;
                        hp.Heal(item.effectAmount);
                    }
                    break;

                case ItemType.Ammo:
                    if (weaponController != null) weaponController.AddReserveAmmo(Mathf.RoundToInt(item.effectAmount));
                    break;

                default:
                    if (!Inventory.TryAdd(item, amount))
                    {
                        // Sem espaco: tenta so o que couber.
                        Inventory.TryAdd(item, amount, out var leftover);
                        if (leftover == amount) return false;
                        amount -= leftover;
                    }
                    break;
            }

            loot.Consume();

            EventBus.Publish(new LootPicked
            {
                ItemId = item.itemId,
                DisplayName = item.displayName,
                Amount = amount
            });

            return true;
        }

        public void UseMedkit(ItemDefinition medkit)
        {
            if (medkit == null || medkit.type != ItemType.Medkit) return;
            if (!Inventory.Remove(medkit.itemId, 1)) return;

            var health = GetComponent<Health>();
            if (health != null) health.Heal(medkit.effectAmount);
        }
    }
}
