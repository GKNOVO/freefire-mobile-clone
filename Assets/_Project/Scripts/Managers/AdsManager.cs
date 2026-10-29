using System;
using System.Collections;
using UnityEngine;
using BR.Core;

namespace BR.Managers
{
    public interface IRewardedAdProvider
    {
        bool IsReady { get; }
        void Initialize();
        void Load();
        void Show(Action<bool> onClosed); // true = recompensa ganha
    }

    /// <summary>
    /// Fachada de anuncios recompensados.
    ///
    /// - Em Editor/desenvolvimento usa SimulatedRewardedProvider (2s de "anuncio").
    /// - Em build com o SDK AdMob instalado e a define GOOGLE_MOBILE_ADS
    ///   usa AdMobRewardedProvider (codigo pronto no fim do arquivo).
    /// - IAP futuro: a mesma fachada pode expor "PremiumRemoveAds".
    ///
    /// Regras: 1 anuncio por vez, cooldown global, fallback silencioso quando
    /// nao ha fill (o jogo NUNCA trava por causa de anuncio).
    /// </summary>
    public sealed class AdsManager : MonoSingleton<AdsManager>
    {
        [Header("Configuracao")]
        [SerializeField] private bool useSimulatedProviderInEditor = true;
        [SerializeField] private bool useSimulatedProviderIfSdkMissing = true;
        [SerializeField] private float minSecondsBetweenAds = 20f;
        [SerializeField] private float simulatedAdDuration = 2f;

        [Header("AdMob (build)")]
        [SerializeField] private string androidRewardedUnitId = "ca-app-pub-3940256099942544/5224354917";
        [SerializeField] private string iosRewardedUnitId = "ca-app-pub-3940256099942544/1712485313";

        private IRewardedAdProvider _provider;
        private bool _busy;
        private float _lastAdTime = -999f;

        public bool IsReady => _provider != null && _provider.IsReady;
        public bool IsBusy => _busy;

        protected override void OnSingletonAwake()
        {
            CreateProvider();
        }

        private void CreateProvider()
        {
#if GOOGLE_MOBILE_ADS && !UNITY_EDITOR
            var unitId = Application.platform == RuntimePlatform.IPhonePlayer ? iosRewardedUnitId : androidRewardedUnitId;
            _provider = new AdMobRewardedProvider(unitId);
#else
            if (useSimulatedProviderInEditor || useSimulatedProviderIfSdkMissing)
                _provider = new SimulatedRewardedProvider(this, simulatedAdDuration);
            else
                _provider = null;
#endif

            if (_provider != null)
            {
                _provider.Initialize();
                _provider.Load();
            }
            else
            {
                Debug.LogWarning("[AdsManager] Nenhum provider de anuncio disponivel. Jogo segue sem ads.");
            }
        }

        /// <summary>
        /// Mostra um rewarded. onFinished(true) somente se o usuario assistiu ate o fim.
        /// Sempre retorna (nunca deixa a UI esperando).
        /// </summary>
        public void ShowRewarded(string placement, Action<bool> onFinished)
        {
            if (_busy)
            {
                onFinished?.Invoke(false);
                return;
            }

            if (Time.unscaledTime - _lastAdTime < minSecondsBetweenAds)
            {
                Debug.Log("[AdsManager] Cooldown de anuncios ativo.");
                onFinished?.Invoke(false);
                return;
            }

            if (_provider == null || !_provider.IsReady)
            {
                Debug.Log("[AdsManager] Anuncio indisponivel (sem fill). Tentando recarregar.");
                _provider?.Load();
                onFinished?.Invoke(false);
                return;
            }

            _busy = true;
            _provider.Show(earned =>
            {
                _busy = false;
                _lastAdTime = Time.unscaledTime;

                if (earned) GrantReward(placement);

                _provider.Load(); // prepara o proximo
                onFinished?.Invoke(earned);
            });
        }

        private void GrantReward(string placement)
        {
            // Ponto central de premiacao: hoje so publica evento; IAP/loja futura pluga aqui.
            EventBus.Publish(new RewardedAdResult { Placement = placement, Completed = true });
            Debug.Log($"[AdsManager] Recompensa concedida para '{placement}'.");
        }

        // ------------------------------------------------------------ Providers

        /// <summary>Simulado: espera X segundos e considera "assistido".</summary>
        private sealed class SimulatedRewardedProvider : IRewardedAdProvider
        {
            private readonly AdsManager _runner;
            private readonly float _duration;
            private bool _ready;

            public SimulatedRewardedProvider(AdsManager runner, float duration)
            {
                _runner = runner;
                _duration = Mathf.Max(0.5f, duration);
            }

            public bool IsReady => _ready;

            public void Initialize() => _ready = true;
            public void Load() => _ready = true;

            public void Show(Action<bool> onClosed)
            {
                if (!_ready)
                {
                    onClosed?.Invoke(false);
                    return;
                }

                _ready = false;
                _runner.StartCoroutine(Run(onClosed));
            }

            private IEnumerator Run(Action<bool> onClosed)
            {
                // unscaled: a HUD pode pausar o jogo (timeScale = 0) durante o anuncio.
                yield return new WaitForSecondsRealtime(_duration);
                _ready = true;
                onClosed?.Invoke(true);
            }
        }

#if GOOGLE_MOBILE_ADS && !UNITY_EDITOR
        /// <summary>Provider real (SDK Google Mobile Ads 8.x). Ativado pela define GOOGLE_MOBILE_ADS.</summary>
        private sealed class AdMobRewardedProvider : IRewardedAdProvider
        {
            private readonly string _unitId;
            private GoogleMobileAds.Api.RewardedAd _ad;
            private bool _ready;
            private Action<bool> _pendingCallback;

            public AdMobRewardedProvider(string unitId) => _unitId = unitId;

            public bool IsReady => _ready && _ad != null;

            public void Initialize()
            {
                GoogleMobileAds.Api.MobileAds.Initialize(_ => { });
            }

            public void Load()
            {
                if (_ready) return;

                GoogleMobileAds.Api.RewardedAd.Load(_unitId, new GoogleMobileAds.Api.AdRequest(),
                    (ad, error) =>
                    {
                        if (error != null || ad == null)
                        {
                            _ready = false;
                            Debug.LogWarning($"[AdsManager] Falha ao carregar rewarded: {error}");
                            return;
                        }

                        _ad = ad;
                        _ad.OnAdFullScreenContentClosed += HandleClosed;
                        _ad.OnAdFullScreenContentFailed += _ => HandleClosed();
                        _ready = true;
                    });
            }

            public void Show(Action<bool> onClosed)
            {
                _pendingCallback = onClosed;
                _earnedThisShow = false;

                if (!IsReady)
                {
                    Complete(false);
                    return;
                }

                _ad.Show(_ => { _earnedThisShow = true; });
            }

            private bool _earnedThisShow;

            private void HandleClosed()
            {
                _ready = false;
                _ad = null;
                Complete(_earnedThisShow);
            }

            private void Complete(bool earned)
            {
                var callback = _pendingCallback;
                _pendingCallback = null;
                callback?.Invoke(earned);
                Load();
            }
        }
#endif
    }
}
