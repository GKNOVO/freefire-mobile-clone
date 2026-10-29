using UnityEngine;

namespace BR.Core
{
    /// <summary>
    /// Base para todos os managers do jogo.
    /// - Instancia unica e persistente entre cenas
    /// - Nao usa Reflection no Update (zero custo por frame)
    /// - Destroi duplicatas caso a cena seja recarregada
    /// </summary>
    public abstract class MonoSingleton<T> : MonoBehaviour where T : MonoSingleton<T>
    {
        public static T Instance { get; private set; }
        public static bool IsReady => Instance != null;

        [SerializeField] private bool _persistAcrossScenes = true;

        protected virtual void Awake()
        {
            if (Instance != null && !ReferenceEquals(Instance, this))
            {
                Destroy(gameObject);
                return;
            }

            Instance = (T)this;

            if (_persistAcrossScenes && transform.parent == null)
                DontDestroyOnLoad(gameObject);

            OnSingletonAwake();
        }

        /// <summary>Chamado uma unica vez, quando a instancia valida e criada.</summary>
        protected virtual void OnSingletonAwake() { }

        protected virtual void OnDestroy()
        {
            if (ReferenceEquals(Instance, this))
                Instance = null;
        }
    }
}
