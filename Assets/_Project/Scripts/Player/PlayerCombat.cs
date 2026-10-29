using UnityEngine;
using BR.Combat;
using BR.Inventory;

namespace BR.Player
{
    /// <summary>
    /// Liga a entrada do jogador ao WeaponController e ao InventoryComponent.
    /// Nao contem regra de dano: tudo passa pelo WeaponController (mesmo usado pelos bots).
    /// </summary>
    [RequireComponent(typeof(InventoryComponent))]
    public sealed class PlayerCombat : MonoBehaviour
    {
        [SerializeField] private WeaponController weapon;
        [SerializeField] private Camera aimCamera;
        [SerializeField] private InventoryComponent inventory;

        private void Awake()
        {
            if (inventory == null) inventory = GetComponent<InventoryComponent>();
            if (weapon == null) weapon = GetComponentInChildren<WeaponController>();
            if (aimCamera == null) aimCamera = Camera.main;
        }

        /// <summary>Configuração em runtime (cenas geradas por código).</summary>
        public void Configure(WeaponController weaponController, Camera camera)
        {
            if (weaponController != null) weapon = weaponController;
            if (camera != null) aimCamera = camera;
        }

        private void Start()
        {
            if (weapon == null) return;

            weapon.OwnerName = "Você";
            if (weapon.Definition != null && weapon.Magazine == 0)
                weapon.Equip(weapon.Definition);

            weapon.Fired += OnFired;
        }

        private void OnDestroy()
        {
            if (weapon != null) weapon.Fired -= OnFired;
        }

        private void Update()
        {
            if (weapon == null) return;

            var player = PlayerController.Local;

            // Sem tiro no aviao / morto.
            if (player != null && (player.State == PlayerState.InPlane || player.State == PlayerState.Dead))
                return;

            if (aimCamera == null) aimCamera = Camera.main;
            if (aimCamera == null) return;

            if (InputBridge.ConsumeReload()) weapon.StartReload();

            if (InputBridge.ConsumeInteract())
                inventory.TryPickupFocused();

            if (InputBridge.FireHeld)
                weapon.TryFire(aimCamera.transform.position, aimCamera.transform.forward);
        }

        private void OnFired(WeaponController w)
        {
            var player = PlayerController.Local;
            if (player != null && player.transform != null)
                player.transform.rotation = Quaternion.Euler(0f, aimCamera.transform.eulerAngles.y, 0f);
        }
    }
}
