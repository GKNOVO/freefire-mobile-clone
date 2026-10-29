using System.Collections.Generic;
using UnityEngine;
using BR.Combat;
using BR.Core;
using BR.Managers;

namespace BR.Bots
{
    /// <summary>
    /// Gerencia o ciclo de vida dos 49 bots: spawn na ilha, distribuicao de saltos
    /// pelo mapa, LOD de simulacao e reciclagem no pool.
    ///
    /// Distribuicao de queda: cada bot recebe um "t" na rota do aviao + um offset
    /// lateral aleatorio -> os bots cobrem o mapa em vez de caírem todos juntos.
    /// </summary>
    public sealed class BotManager : MonoBehaviour
    {
        public static BotManager Instance { get; private set; }

        [Header("Prefab / Spawn")]
        [SerializeField] private GameObject botPrefab;
        [SerializeField] private int botCount = 49;
        [SerializeField] private float islandRadius = 22f;
        [SerializeField] private Transform islandCenter;

        [Header("LOD de simulacao")]
        [Tooltip("Distancia a partir da qual o bot passa a pensar em intervalos maiores.")]
        [SerializeField] private float fullSimulationDistance = 70f;
        [SerializeField] private float farSimulationDistance = 160f;
        [SerializeField] private float farThinkInterval = 0.35f;
        [SerializeField] private float veryFarThinkInterval = 0.9f;

        [Header("Nomes")]
        [SerializeField] private string[] botNamePool;

        private static readonly List<BotAI> Bots = new List<BotAI>(64);
        private readonly List<BotAI> _aliveScratch = new List<BotAI>(64);
        private readonly List<float> _botJumpTimes = new List<float>(64);
        private readonly List<Vector3> _botDropPoints = new List<Vector3>(64);

        private Transform _planeRoot;
        private float _matchTime;
        private bool _planeFlightActive;
        private Vector3 _planeStart;
        private Vector3 _planeEnd;
        private float _planeDuration;
        private int _nextBotToJump;
        private float _lodTimer;

        public int AliveCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < Bots.Count; i++)
                    if (Bots[i] != null && Bots[i].IsAlive) count++;
                return count;
            }
        }

        public int TotalBots => Bots.Count;

        private void Awake()
        {
            Instance = this;
            if (islandCenter == null) islandCenter = transform;
        }

        /// <summary>Configuração em runtime (cenas geradas por código).</summary>
        public void Configure(GameObject prefab, int count, Transform center)
        {
            if (prefab != null) botPrefab = prefab;
            botCount = Mathf.Clamp(count, 1, 49);
            islandCenter = center != null ? center : transform;
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(Instance, this)) Instance = null;
        }

        // ------------------------------------------------------------ Spawn

        public void SpawnBots(Transform planeAnchor)
        {
            DespawnAll();
            _planeRoot = planeAnchor;

            if (botPrefab == null)
            {
                Debug.LogError("[BotManager] botPrefab nao configurado.");
                return;
            }

            var count = Mathf.Max(1, botCount);
            var names = botNamePool != null && botNamePool.Length > 0 ? botNamePool : new[] { "Bot" };

            for (var i = 0; i < count; i++)
            {
                var angle = i / (float)count * Mathf.PI * 2f;
                var ring = islandRadius * Mathf.Sqrt((i + 1f) / count);
                var islandPos = islandCenter.position + new Vector3(Mathf.Cos(angle) * ring, 0.2f, Mathf.Sin(angle) * ring);

                var go = ObjectPoolManager.Instance.Spawn(botPrefab, islandPos, Quaternion.Euler(0f, angle * Mathf.Rad2Deg, 0f));
                if (go == null) continue;

                var bot = go.GetComponent<BotAI>();
                if (bot == null) continue;

                var name = names[i % names.Length] + "_" + (i + 1).ToString("00");
                bot.Initialize(name, islandPos, islandPos);

                Bots.Add(bot);
            }

            Debug.Log($"[BotManager] {Bots.Count} bots prontos na ilha.");
        }

        public void DespawnAll()
        {
            for (var i = 0; i < Bots.Count; i++)
            {
                if (Bots[i] == null) continue;
                Bots[i].transform.SetParent(null, true);
                ObjectPoolManager.Instance.Despawn(Bots[i].gameObject);
            }

            Bots.Clear();
            _botJumpTimes.Clear();
            _botDropPoints.Clear();
        }

        // ------------------------------------------------------------ Plane

        /// <summary>Prepara a distribuicao de saltos ao longo da rota do aviao.</summary>
        public void PreparePlaneRoute(Vector3 start, Vector3 end, float duration)
        {
            _planeStart = start;
            _planeEnd = end;
            _planeDuration = Mathf.Max(1f, duration);
            _matchTime = 0f;
            _planeFlightActive = true;
            _nextBotToJump = 0;

            _botJumpTimes.Clear();
            _botDropPoints.Clear();

            var direction = (end - start).normalized;
            var right = Vector3.Cross(Vector3.up, direction).normalized;

            for (var i = 0; i < Bots.Count; i++)
            {
                // 70% salta em pontos "bons" espalhados; 30% aleatorio puro.
                var t = i < Bots.Count * 0.7f
                    ? Mathf.Lerp(0.12f, 0.9f, (float)i / Mathf.Max(1, Bots.Count))
                    : Random.Range(0.1f, 0.95f);

                var lateral = Random.Range(-90f, 90f);
                var point = Vector3.Lerp(start, end, t) + right * lateral;
                point.y = 0f;

                _botJumpTimes.Add(t * _planeDuration);
                _botDropPoints.Add(point);

                if (Bots[i] != null) Bots[i].SetDropTarget(point);
            }
        }

        public void EnterPlane(Transform anchor)
        {
            _planeRoot = anchor;
            for (var i = 0; i < Bots.Count; i++)
            {
                if (Bots[i] == null) continue;
                Bots[i].transform.SetParent(anchor, true);
                Bots[i].transform.localPosition = Vector3.zero;
            }
        }

        public void TickPlaneFlight(float deltaTime)
        {
            if (!_planeFlightActive) return;

            _matchTime += deltaTime;

            while (_nextBotToJump < Bots.Count && _matchTime >= _botJumpTimes[_nextBotToJump])
            {
                var bot = Bots[_nextBotToJump];
                if (bot != null && bot.IsAlive)
                {
                    bot.transform.SetParent(null, true);
                    var planePos = Vector3.Lerp(_planeStart, _planeEnd, Mathf.Clamp01(_matchTime / _planeDuration));
                    planePos.y = 380f;

                    // Espalha o ponto de salto: todos no mesmo ponto causaria overlap no CharacterController.
                    var lateral = Random.insideUnitCircle * 25f;
                    planePos += new Vector3(lateral.x, 0f, lateral.y);

                    bot.JumpFromPlane(planePos);
                }
                _nextBotToJump++;
            }
        }

        public void EndPlaneFlight()
        {
            _planeFlightActive = false;
            // Quem nao saltou e forcado a saltar no fim da rota.
            for (; _nextBotToJump < Bots.Count; _nextBotToJump++)
            {
                var bot = Bots[_nextBotToJump];
                if (bot != null && bot.IsAlive)
                {
                    bot.transform.SetParent(null, true);
                    bot.JumpFromPlane(new Vector3(_planeEnd.x, 300f, _planeEnd.z));
                }
            }
        }

        // ------------------------------------------------------------ LOD

        private void Update()
        {
            _lodTimer -= Time.deltaTime;
            if (_lodTimer > 0f) return;
            _lodTimer = 0.5f;

            var player = Player.PlayerController.Local;
            if (player == null) return;

            var playerPos = player.transform.position;

            for (var i = 0; i < Bots.Count; i++)
            {
                var bot = Bots[i];
                if (bot == null || !bot.IsAlive) continue;

                var distance = Vector3.Distance(bot.transform.position, playerPos);
                if (distance <= fullSimulationDistance) bot.SetSimulationInterval(0f);
                else if (distance <= farSimulationDistance) bot.SetSimulationInterval(farThinkInterval);
                else bot.SetSimulationInterval(veryFarThinkInterval);
            }
        }

        // ------------------------------------------------------------ Death

        public static void NotifyBotDied(BotAI bot, DamageInfo info)
        {
            if (Instance != null) Instance.HandleBotDied(bot, info);
        }

        private void HandleBotDied(BotAI bot, DamageInfo info)
        {
            // Delay curto para o corpo/ragdoll e o kill feed aparecerem antes de reciclar.
            if (bot != null)
            {
                bot.transform.SetParent(null, true);
                ObjectPoolManager.Instance.Despawn(bot.gameObject, 1.5f);
            }

            _aliveScratch.Clear();
            Bots.Remove(bot);

            EventBus.Publish(new AliveCountChanged
            {
                Alive = AliveCount + (Player.PlayerController.Local != null && Player.PlayerController.Local.State != Player.PlayerState.Dead ? 1 : 0),
                Total = botCount + 1
            });
        }

        /// <summary>Player eliminado: remove o alvo dos bots para nao atirarem em corpo caido.</summary>
        public void OnPlayerDead()
        {
            for (var i = 0; i < Bots.Count; i++)
                if (Bots[i] != null) Bots[i].SetSimulationInterval(veryFarThinkInterval);
        }
    }
}
