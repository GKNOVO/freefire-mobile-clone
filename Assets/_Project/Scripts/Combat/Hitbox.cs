using UnityEngine;

namespace BR.Combat
{
    /// <summary>
    /// Colocado em colliders filhos (Head, Torso, Limbs).
    /// O Raycast do WeaponController pega este componente para aplicar multiplicador.
    /// </summary>
    public sealed class Hitbox : MonoBehaviour
    {
        [SerializeField] private float damageMultiplier = 1f;
        [SerializeField] private bool isHeadshot = false;

        public float DamageMultiplier => damageMultiplier;
        public bool IsHeadshot => isHeadshot;

        public Health Owner { get; private set; }

        private void Awake()
        {
            Owner = GetComponentInParent<Health>();
        }
    }
}
