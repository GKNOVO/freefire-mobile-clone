using UnityEngine;
using BR.Inventory;
using BR.Managers;

namespace BR.Inventory
{
    /// <summary>
    /// Item caido no mapa. Vem do pool (nunca Instantiate em partida).
    /// </summary>
    public sealed class LootPickup : MonoBehaviour, IPoolable
    {
        [SerializeField] private float spinSpeed = 45f;
        [SerializeField] private float bobHeight = 0.12f;
        [SerializeField] private float bobSpeed = 2f;

        public ItemDefinition Item { get; private set; }
        public int Amount { get; private set; }
        public bool CanPickup => Item != null;

        private Vector3 _basePosition;
        private float _phase;

        public void Configure(ItemDefinition item, int amount)
        {
            Item = item;
            Amount = Mathf.Max(1, amount);
            _basePosition = transform.position;
            _phase = Random.Range(0f, Mathf.PI * 2f);
        }

        private void Update()
        {
            var t = transform;
            t.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
            var pos = _basePosition;
            pos.y += Mathf.Sin(Time.time * bobSpeed + _phase) * bobHeight;
            t.position = pos;
        }

        public void Consume()
        {
            Item = null;
            if (LootManager.Instance != null) LootManager.Instance.NotifyConsumed(this);

            if (ObjectPoolManager.IsReady) ObjectPoolManager.Instance.Despawn(gameObject);
            else gameObject.SetActive(false);
        }

        public void OnSpawned()
        {
            _basePosition = transform.position;
        }

        public void OnDespawned()
        {
            Item = null;
            Amount = 0;
        }
    }
}
