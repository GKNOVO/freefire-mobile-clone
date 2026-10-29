using System.Collections.Generic;
using UnityEngine;
using BR.Core;

namespace BR.Managers
{
    public interface IPoolable
    {
        void OnSpawned();
        void OnDespawned();
    }

    /// <summary>Marcador colocado automaticamente em toda instancia criada pelo pool.</summary>
    public sealed class PooledInstance : MonoBehaviour
    {
        public GameObject SourcePrefab;
    }

    /// <summary>
    /// Pool global obrigatório: balas/tracers, impacto, loot, bots, avião, partículas.
    /// Objetivo: ZERO Instantiate/Destroy durante a partida -> sem GC spikes.
    ///
    /// Uso:
    ///   var go = ObjectPoolManager.Instance.Spawn(prefab, pos, rot);
    ///   ObjectPoolManager.Instance.Despawn(go, 2f); // com delay opcional
    /// </summary>
    public sealed class ObjectPoolManager : MonoSingleton<ObjectPoolManager>
    {
        [SerializeField] private int defaultPrewarm = 8;

        private readonly Dictionary<GameObject, Stack<GameObject>> _pools = new Dictionary<GameObject, Stack<GameObject>>(64);
        private static readonly List<IPoolable> PoolableBuffer = new List<IPoolable>(8);
        private Transform _root;

        protected override void OnSingletonAwake()
        {
            _root = new GameObject("[PoolRoot]").transform;
            _root.SetParent(transform, false);
        }

        public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null) return null;

            if (!_pools.TryGetValue(prefab, out var stack))
            {
                stack = new Stack<GameObject>(defaultPrewarm);
                _pools[prefab] = stack;
            }

            GameObject instance = null;
            while (stack.Count > 0 && instance == null)
                instance = stack.Pop();

            if (instance == null)
            {
                instance = Instantiate(prefab, _root);
                var marker = instance.GetComponent<PooledInstance>();
                if (marker == null) marker = instance.AddComponent<PooledInstance>();
                marker.SourcePrefab = prefab;
            }

            var t = instance.transform;
            t.SetParent(null, false);
            t.SetPositionAndRotation(position, rotation);
            instance.SetActive(true);

            PoolableBuffer.Clear();
            instance.GetComponents(PoolableBuffer);
            for (var i = 0; i < PoolableBuffer.Count; i++)
                PoolableBuffer[i].OnSpawned();

            return instance;
        }

        public T Spawn<T>(T prefab, Vector3 position, Quaternion rotation) where T : Component
        {
            if (prefab == null) return null;
            var go = Spawn(prefab.gameObject, position, rotation);
            return go != null ? go.GetComponent<T>() : null;
        }

        public void Despawn(GameObject instance, float delay = 0f)
        {
            if (instance == null) return;

            if (delay > 0f)
            {
                StartCoroutine(DespawnDelayed(instance, delay));
                return;
            }

            var marker = instance.GetComponent<PooledInstance>();
            if (marker == null || marker.SourcePrefab == null)
            {
                // Objeto fora do pool: pelo menos evita vazar na cena.
                Destroy(instance);
                return;
            }

            PoolableBuffer.Clear();
            instance.GetComponents(PoolableBuffer);
            for (var i = 0; i < PoolableBuffer.Count; i++)
                PoolableBuffer[i].OnDespawned();

            instance.SetActive(false);
            instance.transform.SetParent(_root, false);

            if (!_pools.TryGetValue(marker.SourcePrefab, out var stack))
            {
                stack = new Stack<GameObject>(defaultPrewarm);
                _pools[marker.SourcePrefab] = stack;
            }

            stack.Push(instance);
        }

        public void Despawn(Component instance, float delay = 0f)
        {
            if (instance != null) Despawn(instance.gameObject, delay);
        }

        private System.Collections.IEnumerator DespawnDelayed(GameObject instance, float delay)
        {
            yield return new WaitForSeconds(delay);
            Despawn(instance);
        }

        /// <summary>Pré-aquece no loading (evita hitch no primeiro tiro/primeiro loot).</summary>
        public void Prewarm(GameObject prefab, int count)
        {
            if (prefab == null || count <= 0) return;

            if (!_pools.TryGetValue(prefab, out var stack))
            {
                stack = new Stack<GameObject>(count);
                _pools[prefab] = stack;
            }

            for (var i = 0; i < count; i++)
            {
                var go = Instantiate(prefab, _root);
                go.SetActive(false);
                var marker = go.GetComponent<PooledInstance>();
                if (marker == null) marker = go.AddComponent<PooledInstance>();
                marker.SourcePrefab = prefab;
                stack.Push(go);
            }
        }
    }
}
