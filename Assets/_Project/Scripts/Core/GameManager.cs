using System.Collections;
using UnityEngine;
using BR.Core;
using BR.Data;

namespace BR.Managers
{
    /// <summary>
    /// Orquestrador de alto nivel do app:
    ///   Splash -> Login -> Lobby -> Matchmaking -> Arena (partida) -> Resultados -> Lobby
    ///
    /// Ele NAO conhece regra de combate/zona: apenas conversa com MatchManager,
    /// SaveManager, AdsManager e UIManager.
    /// </summary>
    public sealed class GameManager : MonoSingleton<GameManager>
    {
        [Header("Configuracao")]
        [SerializeField] private MatchConfig defaultMatchConfig;
        [SerializeField] private string arenaSceneName = "Arena";
        [SerializeField] private float minSplashSeconds = 1.6f;
        [SerializeField] private int lobbyAdGoldReward = 100;

        [Header("Debug")]
        [SerializeField] private AppScreen debugStartScreen = AppScreen.Splash;

        public AppScreen CurrentScreen { get; private set; } = AppScreen.Splash;
        public bool InMatch { get; private set; }
        public MatchResult LastResult { get; private set; }
        public bool RewardDoubled { get; private set; }

        private MatchManager _match;
        private Coroutine _matchRoutine;

        /// <summary>Usado pelo bootstrap de runtime (cenas geradas por código).</summary>
        public void ConfigureForRuntime(MatchConfig config, string arenaScene)
        {
            if (config != null) defaultMatchConfig = config;
            if (!string.IsNullOrEmpty(arenaScene)) arenaSceneName = arenaScene;
        }

        protected override void OnSingletonAwake()
        {
            // Meta mobile: 60 FPS, sem vSync (o sistema decide o pacing), tela sempre acesa.
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
        }

        private IEnumerator Start()
        {
            if (UIManager.Instance == null)
            {
                Debug.LogError("[GameManager] UIManager ausente na cena de Boot.");
                yield break;
            }

            UIManager.Instance.Show(AppScreen.Splash);
            yield return new WaitForSecondsRealtime(minSplashSeconds);

            // SaveManager ja carregou/criou o perfil no Awake.
            var profile = SaveManager.Instance != null ? SaveManager.Instance.Profile : null;

            if (profile == null || string.IsNullOrWhiteSpace(profile.nick) || profile.matchesPlayed == 0)
                GoTo(AppScreen.Login);
            else
                GoTo(AppScreen.Lobby);

            if (debugStartScreen != AppScreen.Splash && debugStartScreen != CurrentScreen)
                GoTo(debugStartScreen);
        }

        // ------------------------------------------------------------ Navegacao

        private void GoTo(AppScreen screen)
        {
            CurrentScreen = screen;
            UIManager.Instance.Show(screen);
        }

        public void ConfirmLogin(string nick)
        {
            var save = SaveManager.Instance;
            if (save == null || save.Profile == null) return;

            if (!string.IsNullOrWhiteSpace(nick)) save.Profile.nick = nick.Trim();
            save.Save();

            GoTo(AppScreen.Lobby);
        }

        public void ReturnToLobby()
        {
            if (_matchRoutine != null) StopCoroutine(_matchRoutine);
            _matchRoutine = StartCoroutine(ReturnToLobbyRoutine());
        }

        private IEnumerator ReturnToLobbyRoutine()
        {
            InMatch = false;
            GoTo(AppScreen.Lobby);

            if (_match != null)
            {
                _match.OnMatchEnded -= HandleMatchEnded;
                _match = null;
            }

            yield return SceneLoader.UnloadAsync(arenaSceneName);
        }

        // ------------------------------------------------------------ Match

        public void StartMatch()
        {
            if (_matchRoutine != null || InMatch) return;
            _matchRoutine = StartCoroutine(StartMatchRoutine());
        }

        private IEnumerator StartMatchRoutine()
        {
            InMatch = true;
            GoTo(AppScreen.Matchmaking);

            yield return FakeMatchmakingService.Simulate(
                defaultMatchConfig != null ? defaultMatchConfig.totalPlayers : 50,
                null);

            yield return SceneLoader.LoadAdditiveWithFade(arenaSceneName, UIManager.Instance);

            _match = MatchManager.Current != null ? MatchManager.Current : FindFirstObjectByType<MatchManager>();
            if (_match == null)
            {
                Debug.LogError($"[GameManager] Nenhum MatchManager na cena '{arenaSceneName}'.");
                _matchRoutine = null;
                InMatch = false;
                ReturnToLobby();
                yield break;
            }

            _match.Initialize(defaultMatchConfig);
            _match.OnMatchEnded += HandleMatchEnded;
            _match.StartMatch();

            GoTo(AppScreen.Hud);
            _matchRoutine = null;
        }

        private void HandleMatchEnded(MatchResult result)
        {
            LastResult = result;
            RewardDoubled = false;

            // 1) Credita recompensa base
            SaveManager.Instance?.ApplyMatchResult(result, doubledByAd: false);

            // 2) Mostra resultado (a tela oferece "dobrar com anuncio")
            GoTo(AppScreen.Results);

            // 3) Libera o jogador para jogar de novo apos o delay de fim
            InMatch = false;
        }

        // ------------------------------------------------------------ Ads

        /// <summary>Botao "Assistir anuncio por moedas" do Lobby.</summary>
        public void WatchLobbyAd()
        {
            if (AdsManager.Instance == null) return;

            AdsManager.Instance.ShowRewarded("lobby_coins", earned =>
            {
                if (!earned) return;

                var save = SaveManager.Instance;
                if (save == null || save.Profile == null) return;

                save.AddCurrency(lobbyAdGoldReward, 0);
            });
        }

        /// <summary>Botao "Dobrar recompensa" na tela de resultados.</summary>
        public void RequestDoubleReward()
        {
            if (RewardDoubled || AdsManager.Instance == null) return;

            AdsManager.Instance.ShowRewarded("match_double", earned =>
            {
                if (!earned || RewardDoubled) return;

                RewardDoubled = true;
                SaveManager.Instance?.GrantAdRewardBonus(LastResult);
            });
        }
    }
}
