using System.Collections.Generic;
using UnityEngine;
using BR.Inventory;

namespace BR.Managers
{
    /// <summary>
    /// Espalha loot pelo mapa e coordena a "reserva" de itens pelos bots
    /// (evita que 5 bots corram para o mesmo item).
    ///
    /// Todos os pickups vem do ObjectPoolManager: nenhum Instantiate em partida.
    /// </summary>
    public sealed class LootManager : MonoBehaviour
    {
        public static LootManager Instance { get; private set; }

        [Header("Prefab / tabela")]
        [SerializeField] private GameObject lootPrefab;
        [SerializeField] private ItemDefinition[] lootTable;

        [Header("Spawn")]
        [SerializeField] private Transform[] spawnPoints;
        [SerializeField] private int randomSpawnCount = 220;
        [SerializeField] private float mapRadius = 450f;
        [SerializeField] private float minDistanceBetweenLoot = 8f;
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private float groundOffset = 0.35f;

        [Header("Bots")]
        [SerializeField] private float claimDuration = 10f;

        private readonly List<LootPickup> _active = new List<LootPickup>(256);
        private readonly Dictionary<LootPickup, float> _claims = new Dictionary<LootPickup, float>(64);
        private readonly List<Vector3> _usedPositions = new List<Vector3>(256);

        private void Awake()
        {
            Instance = this;
        }

        /// <summary>Configuração em runtime (cenas geradas por código).</summary>
        public void Configure(GameObject prefab, ItemDefinition[] table, float radius)
        {
            if (prefab != null) lootPrefab = prefab;
            if (table != null && table.Length > 0) lootTable = table;
            mapRadius = Mathf.Max(50f, radius);
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(Instance, this)) Instance = null;
        }

        // ------------------------------------------------------------ Spawn

        public void SpawnAll(int count)
        {
            ClearAll();

            if (lootPrefab == null)
            {
                Debug.LogError("[LootManager] lootPrefab nao configurado.");
                return;
            }

            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                for (var i = 0; i < spawnPoints.Length; i++)
                {
                    var point = spawnPoints[i];
                    if (point == null) continue;
                    SpawnAt(point.position + Vector3.up * groundOffset);
                }
            }

            var randomCount = count > 0 ? count : randomSpawnCount;
            for (var i = 0; i < randomCount; i++)
                SpawnAt(RandomMapPoint());

            Debug.Log($"[LootManager] {_active.Count} itens no mapa.");
        }

        private Vector3 RandomMapPoint()
        {
            var player = Player.PlayerController.Local;
            var center = player != null ? player.transform.position : Vector3.zero;

            for (var attempt = 0; attempt < 12; attempt++)
            {
                var random = Random.insideUnitCircle * mapRadius;
                var candidate = new Vector3(random.x, 100f, random.y);

                if (!Physics.Raycast(candidate, Vector3.down, out var hit, 250f, groundMask, QueryTriggerInteraction.Ignore))
                    continue;

                var point = hit.point + Vector3.up * groundOffset;

                var tooClose = false;
                for (var i = 0; i < _usedPositions.Count; i++)
                {
                    if ((_usedPositions[i] - point).sqrMagnitude < minDistanceBetweenLoot * minDistanceBetweenLoot)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (tooClose) continue;

                _usedPositions.Add(point);
                return point;
            }

            return center + Vector3.up * 1.5f;
        }

        private void SpawnAt(Vector3 position)
        {
            var item = PickRandomItem();
            if (item == null) return;

            var go = ObjectPoolManager.Instance.Spawn(lootPrefab, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            if (go == null) return;

            var pickup = go.GetComponent<LootPickup>();
            if (pickup == null)
            {
                ObjectPoolManager.Instance.Despawn(go);
                return;
            }

            pickup.Configure(item, Random.Range(item.minAmount, item.maxAmount + 1));
            _active.Add(pickup);
        }

        private ItemDefinition PickRandomItem()
        {
            if (lootTable == null || lootTable.Length == 0) return null;

            var totalWeight = 0f;
            for (var i = 0; i < lootTable.Length; i++)
                if (lootTable[i] != null) totalWeight += lootTable[i].spawnWeight;

            if (totalWeight <= 0f) return lootTable[0];

            var roll = Random.value * totalWeight;
            for (var i = 0; i < lootTable.Length; i++)
            {
                var item = lootTable[i];
                if (item == null) continue;

                roll -= item.spawnWeight;
                if (roll <= 0f) return item;
            }

            return lootTable[lootTable.Length - 1];
        }

        public void ClearAll()
        {
            for (var i = 0; i < _active.Count; i++)
                if (_active[i] != null) ObjectPoolManager.Instance.Despawn(_active[i].gameObject);

            _active.Clear();
            _claims.Clear();
            _usedPositions.Clear();
        }

        // ------------------------------------------------------------ Bots

        /// <summary>Reserva o item nao reservado mais proximo (usado pela IA).</summary>
        public bool TryClaimNearest(Vector3 origin, float maxRange, out LootPickup loot)
        {
            loot = null;

            var bestSqr = maxRange * maxRange;
            var now = Time.time;

            for (var i = 0; i < _active.Count; i++)
            {
                var candidate = _active[i];
                if (candidate == null || !candidate.CanPickup) continue;

                if (_claims.TryGetValue(candidate, out var until) && until > now) continue;

                var sqr = (candidate.transform.position - origin).sqrMagnitude;
                if (sqr >= bestSqr) continue;

                bestSqr = sqr;
                loot = candidate;
            }

            if (loot == null) return false;

            _claims[loot] = now + claimDuration;
            return true;
        }

        public void ReleaseClaim(LootPickup loot)
        {
            if (loot != null) _claims.Remove(loot);
        }

        public void NotifyConsumed(LootPickup loot)
        {
            if (loot == null) return;
            _claims.Remove(loot);
            _active.Remove(loot);
        }
    }
}
