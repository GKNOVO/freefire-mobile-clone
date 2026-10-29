using UnityEngine;

namespace BR.Combat
{
    public enum WeaponCategory
    {
        Pistol = 0,
        SMG = 1,
        Rifle = 2,
        Shotgun = 3,
        Sniper = 4,
        Melee = 5
    }

    /// <summary>
    /// Arma data-driven. Um asset por arma em Assets/_Project/ScriptableObjects/Weapons.
    /// Balancear o jogo aqui nao exige tocar em codigo nem recompilar.
    /// </summary>
    [CreateAssetMenu(menuName = "BR/Weapon Definition", fileName = "WPN_New")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        [Header("Identidade")]
        public string itemId = "wpn_pistol";
        public string displayName = "Pistola";
        public WeaponCategory category = WeaponCategory.Pistol;

        [Header("Dano")]
        public float damage = 18f;
        [Range(1f, 4f)] public float headshotMultiplier = 2.2f;
        public int pellets = 1;              // >1 = shotgun
        [Range(0f, 300f)] public float range = 90f;

        [Header("Cadencia")]
        [Tooltip("Tiros por minuto")]
        public float roundsPerMinute = 420f;
        public bool automatic = true;
        [Range(0f, 8f)] public float spreadDegrees = 1.1f;

        [Header("Municao")]
        public int magazineSize = 17;
        public int maxReserve = 120;
        public float reloadSeconds = 1.7f;

        [Header("Sensacao")]
        public float recoilKick = 0.6f;
        public float knockback = 4f;
        public string fireSfxId = "sfx_gun";
        public string muzzleFlashPoolId = "fx_muzzle";

        public float SecondsBetweenShots => 60f / Mathf.Max(1f, roundsPerMinute);
    }
}
