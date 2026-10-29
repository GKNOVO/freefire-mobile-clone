using UnityEngine;

namespace BR.Inventory
{
    public enum ItemType
    {
        Weapon = 0,
        Ammo = 1,
        Armor = 2,
        Medkit = 3,
        Grenade = 4,
        Attachment = 5
    }

    /// <summary>
    /// Definicao de item. Para armas, o campo weapon aponta para o WeaponDefinition
    /// correspondente (mesma identidade itemId).
    /// </summary>
    [CreateAssetMenu(menuName = "BR/Item Definition", fileName = "ITM_New")]
    public sealed class ItemDefinition : ScriptableObject
    {
        [Header("Identidade")]
        public string itemId = "item_new";
        public string displayName = "Item";
        public ItemType type = ItemType.Medkit;
        public Sprite icon;

        [Header("Regras de inventario")]
        [Min(1)] public int maxStack = 1;
        [Min(1)] public int weight = 1;

        [Header("Efeitos")]
        [Tooltip("Armas: referencia do WeaponDefinition.")]
        public Combat.WeaponDefinition weapon;
        [Tooltip("Armor: armadura adicionada. Medkit: vida curada. Ammo: municao.")]
        public float effectAmount = 0f;
        [Tooltip("Ammo/Medkit: id do item alvo (opcional).")]
        public string ammoForWeaponId = "";

        [Header("Loot")]
        [Range(0f, 1f)] public float spawnWeight = 0.5f;
        [Min(0)] public int minAmount = 1;
        [Min(1)] public int maxAmount = 1;
    }
}
