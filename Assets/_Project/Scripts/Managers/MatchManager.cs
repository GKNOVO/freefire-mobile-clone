using System;
using UnityEngine;
using BR.Combat;
using BR.Core;

namespace BR.Managers
{
    /// <summary>
    /// Maquina de estados da partida:
    ///
    ///   WaitingIsland --(35s)--> PlaneFlight --(pulo/28s)--> Freefall --(pouso)--> Active --> Ended
    ///
    /// Responsabilidades:
    ///  - controlar tempo/fase da partida;
    ///  - orquestrar player, bots, aviao, zona e loot;
    ///  - contabilizar vivos e kills do player;
    ///  - publicar eventos para a HUD (nunca falar direto com a UI);
    ///  - entregar o resultado ao GameManager.
    ///
    /// E o "single source of truth" do estado da partida.
    /// </summary>
    public sealed class MatchManager : MonoBehaviour
    {
        public static MatchManager Current { get; private set; }

        [Header("Configuracao")]
        [SerializeField] private MatchConfig config;
        [SerializeField] private Transform planeAnchor;

        [Header("Referencias de cena")]
        [SerializeField] private Player.PlayerController player;
        [SerializeField] private Bots.BotManager botManager;
        [SerializeField] private ZoneManager zoneManager;
        [SerializeField] private LootManager lootManager;
        [SerializeField] private PlaneDirector planeDirector;

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = false;

        public MatchPhase Phase { get; private set; } = MatchPhase.None;
        public float PhaseTimeRemaining => Mathf.Max(0f, _phaseTimer);
        public int AliveCount => (player != null && player.State != Player.PlayerState.Dead ? 1 : 0) + (botManager != null ? botManager.AliveCount : 0);
        public int PlayerKills => _playerKills;
        public float MatchElapsed { get; private set; }
        public MatchConfig Config => config;

        public event Action<MatchPhase> PhaseChanged;
        public event Action<Data.MatchResult> OnMatchEnded;

        private float _phaseTimer;
        private float _initialPhaseDuration;
        private bool _matchRunning;
        private int _playerKills;
        private bool _playerJumped;
        private float _alivePublishTimer;
        private int _lastPublishedAlive = -1;
        private Health _playerHealth;

        private void Awake()
        {
            Current = this;
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(Current, this)) Current = null;
            EventBus.Unsubscribe<KillFeedMessage>(OnKillFeed);

            if (_playerHealth != null) _playerHealth.Died -= OnPlayerDied;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<KillFeedMessage>(OnKillFeed);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<KillFeedMessage>(OnKillFeed);
        }

        // ------------------------------------------------------------ API

        public void Initialize(MatchConfig matchConfig)
        {
            config = matchConfig;

            if (player == null) player = Player.PlayerController.Local;
            if (player == null) player = FindFirstObjectByType<Player.PlayerController>();
            if (botManager == null) botManager = Bots.BotManager.Instance;
            if (zoneManager == null) zoneManager = ZoneManager.Instance;
            if (lootManager == null) lootManager = LootManager.Instance;
            if (planeDirector == null) planeDirector = FindFirstObjectByType<PlaneDirector>();
            if (planeAnchor == null && planeDirector != null) planeAnchor = planeDirector.Anchor;

            if (player != null)
            {
                _playerHealth = player.GetComponent<Health>();
                if (_playerHealth != null) _playerHealth.Died += OnPlayerDied;
            }
        }

        /// <summary>Vincula referências criadas em runtime (cenas geradas por código).</summary>
        public void Bind(
            Player.PlayerController playerController,
            Bots.BotManager bots,
            ZoneManager zone,
            LootManager loot,
            PlaneDirector plane,
            Transform planeRoot)
        {
            if (playerController != null) player = playerController;
            if (bots != null) botManager = bots;
            if (zone != null) zoneManager = zone;
            if (loot != null) lootManager = loot;
            if (plane != null) planeDirector = plane;
            if (planeRoot != null) planeAnchor = planeRoot;
        }

        public void StartMatch()
        {
            if (config == null)
            {
                Debug.LogError("[MatchManager] MatchConfig nao atribuido.");
                return;
            }

            _matchRunning = true;
            _playerKills = 0;
            _playerJumped = false;
            phaseIsEnding = false;
            MatchElapsed = 0f;

            // Fases da zona vem do asset de configuracao (data-driven).
            if (zoneManager != null)
                zoneManager.SetPhases(config.GetZonePhases());

            // 1) Ilha de espera
            if (lootManager != null) lootManager.SpawnAll(config.lootSpawnCount);
            if (botManager != null) botManager.SpawnBots(planeAnchor);

            if (player != null)
                player.TeleportTo(config.waitingIslandSpawn);

            if (zoneManager != null) zoneManager.Begin(config.mapCenter, config.mapRadius);

            SetPhase(MatchPhase.WaitingIsland, config.waitingIslandSeconds);
            PublishAlive(true);
        }

        public void AbortMatch()
        {
            _matchRunning = false;
            SetPhase(MatchPhase.None, 0f);
            if (zoneManager != null) zoneManager.Stop();
            if (botManager != null) botManager.DespawnAll();
        }

        /// <summary>Chamado pelo botao PULAR da HUD.</summary>
        public void RequestPlayerJump()
        {
            if (Phase != MatchPhase.PlaneFlight || player == null || _playerJumped) return;
            JumpLocalPlayer();
        }

        private void JumpLocalPlayer()
        {
            if (player == null || planeDirector == null) return;

            _playerJumped = true;
            player.JumpFromPlane(planeDirector.Position);
            SetPhase(MatchPhase.Freefall, 0f);
            if (verboseLogs) Debug.Log("[MatchManager] Player saltou do aviao.");
        }

        // ------------------------------------------------------------ Update

        private void Update()
        {
            if (!_matchRunning) return;

            var dt = Time.deltaTime;
            MatchElapsed += dt;

            switch (Phase)
            {
                case MatchPhase.WaitingIsland: TickWaitingIsland(dt); break;
                case MatchPhase.PlaneFlight: TickPlaneFlight(dt); break;
                case MatchPhase.Freefall: TickFreefall(dt); break;
                case MatchPhase.Active: TickActive(dt); break;
            }

            if (_matchRunning) _phaseTimer -= dt;
        }

        private void TickWaitingIsland(float dt)
        {
            if (_phaseTimer > 0f) return;

            BeginPlanePhase();
        }

        private void BeginPlanePhase()
        {
            if (config == null) return;

            var start = config.planeStart;
            var end = config.planeEnd;

            if (planeDirector != null)
                planeDirector.Configure(start, end, config.planeFlightSeconds);

            if (player != null && planeAnchor != null)
                player.EnterPlane(planeDirector != null ? planeDirector.Anchor : planeAnchor);

            if (botManager != null)
            {
                botManager.EnterPlane(planeDirector != null ? planeDirector.Anchor : planeAnchor);
                botManager.PreparePlaneRoute(start, end, config.planeFlightSeconds);
            }

            SetPhase(MatchPhase.PlaneFlight, config.planeFlightSeconds);
        }

        private void TickPlaneFlight(float dt)
        {
            if (planeDirector != null) planeDirector.Tick(dt);
            if (botManager != null) botManager.TickPlaneFlight(dt);

            if (!_playerJumped && BR.Player.InputBridge.ConsumeJump())
                JumpLocalPlayer();

            if (_phaseTimer <= 0f || (planeDirector != null && planeDirector.Progress >= 0.97f && !_playerJumped))
            {
                if (!_playerJumped) JumpLocalPlayer();
                if (botManager != null) botManager.EndPlaneFlight();
                SetPhase(MatchPhase.Freefall, 0f);
            }
        }

        private void TickFreefall(float dt)
        {
            // Bots continuam saltando pelos seus horarios.
            if (botManager != null) botManager.TickPlaneFlight(dt);

            if (player != null && player.State == Player.PlayerState.Grounded)
            {
                // Garante que nenhum bot fique preso no aviao se o player pousar antes deles.
                if (botManager != null) botManager.EndPlaneFlight();
                if (zoneManager != null) zoneManager.BeginPhases();
                SetPhase(MatchPhase.Active, 0f);
            }
        }

        private void TickActive(float dt)
        {
            _alivePublishTimer -= dt;
            if (_alivePublishTimer <= 0f)
            {
                _alivePublishTimer = 0.4f;
                PublishAlive(false);
            }

            if (player != null && player.State == Player.PlayerState.Dead) return; // OnPlayerDied trata o fim

            if (botManager != null && botManager.AliveCount == 0)
                EndMatch(victory: true, placement: 1);
        }

        private void PublishAlive(bool force)
        {
            var alive = AliveCount;
            if (!force && alive == _lastPublishedAlive) return;

            _lastPublishedAlive = alive;
            EventBus.Publish(new AliveCountChanged
            {
                Alive = alive,
                Total = config != null ? config.totalPlayers : 50
            });
        }

        // ------------------------------------------------------------ Death / End

        private void OnKillFeed(KillFeedMessage msg)
        {
            if (msg.KillerIsLocalPlayer) _playerKills++;
        }

        private void OnPlayerDied(Health health, DamageInfo info)
        {
            if (phaseIsEnding) return;

            if (player != null) player.Die();
            if (botManager != null) botManager.OnPlayerDead();

            var placement = Mathf.Max(1, AliveCount + 1); // morreu -> e o proximo a sair
            EndMatch(victory: false, placement);
        }

        private bool phaseIsEnding;

        private void EndMatch(bool victory, int placement)
        {
            if (phaseIsEnding) return;
            phaseIsEnding = true;

            if (zoneManager != null) zoneManager.Stop();
            SetPhase(MatchPhase.Ended, 0f);
            _matchRunning = false;

            var result = new Data.MatchResult
            {
                Placement = placement,
                Kills = _playerKills,
                TotalPlayers = config != null ? config.totalPlayers : 50,
                SurvivalSeconds = MatchElapsed,
                Victory = victory
            };

            EventBus.Publish(new MatchEnded
            {
                Placement = placement,
                Kills = _playerKills,
                SurvivorsAlive = AliveCount,
                LocalPlayerWon = victory
            });

            OnMatchEnded?.Invoke(result);

            if (verboseLogs)
                Debug.Log($"[MatchManager] Fim: pos={placement}, kills={_playerKills}, vitoria={victory}");
        }

        // ------------------------------------------------------------ Helpers

        private void SetPhase(MatchPhase next, float duration)
        {
            var previous = Phase;
            Phase = next;
            _phaseTimer = duration;
            _initialPhaseDuration = duration;

            if (previous == next) return;

            PhaseChanged?.Invoke(next);
            EventBus.Publish(new MatchPhaseChanged
            {
                Previous = previous,
                Current = next,
                Duration = duration
            });
        }
    }
}
