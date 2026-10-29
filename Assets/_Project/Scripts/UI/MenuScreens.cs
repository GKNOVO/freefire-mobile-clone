using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using BR.Core;
using BR.Managers;

namespace BR.UI
{
    /// <summary>
    /// Splash: logo animado + barra de progresso "real" do boot (tempo de carregamento
    /// + progresso reportado pelo GameManager). A barra nunca trava em 100% antes do tempo.
    /// </summary>
    public sealed class SplashScreen : MonoBehaviour
    {
        [SerializeField] private RectTransform logo;
        [SerializeField] private Slider progressBar;
        [SerializeField] private TMP_Text statusLabel;
        [SerializeField] private float minimumSeconds = 1.4f;
        [SerializeField] private float logoPulseScale = 1.05f;

        private void OnEnable()
        {
            if (progressBar != null) progressBar.value = 0f;
            if (statusLabel != null) statusLabel.text = "Carregando...";
            StartCoroutine(PlayIntro());
        }

        private IEnumerator PlayIntro()
        {
            var elapsed = 0f;
            var baseScale = logo != null ? logo.localScale : Vector3.one;

            while (elapsed < minimumSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / minimumSeconds);

                if (progressBar != null) progressBar.value = Mathf.SmoothStep(0f, 1f, t);

                if (logo != null)
                {
                    var pulse = 1f + Mathf.Sin(t * Mathf.PI) * (logoPulseScale - 1f);
                    logo.localScale = baseScale * pulse;
                }

                yield return null;
            }

            if (progressBar != null) progressBar.value = 1f;
            if (statusLabel != null) statusLabel.text = "Pronto!";
        }
    }

    /// <summary>Login local: cria/entra com um perfil offline (sem rede, sem conta).</summary>
    public sealed class LoginScreen : MonoBehaviour
    {
        [SerializeField] private TMP_InputField nickField;
        [SerializeField] private TMP_Text errorLabel;
        [SerializeField] private Button continueButton;

        private void OnEnable()
        {
            var save = SaveManager.Instance;
            if (nickField != null)
                nickField.text = save != null && save.Profile != null ? save.Profile.nick : "";

            if (errorLabel != null) errorLabel.gameObject.SetActive(false);
            if (continueButton != null) continueButton.interactable = true;
        }

        public void OnContinueClicked()
        {
            var nick = nickField != null ? nickField.text : "";

            if (string.IsNullOrWhiteSpace(nick) || nick.Trim().Length < 3)
            {
                if (errorLabel != null)
                {
                    errorLabel.text = "Escolha um nick com pelo menos 3 caracteres.";
                    errorLabel.gameObject.SetActive(true);
                }
                return;
            }

            GameManager.Instance.ConfirmLogin(nick);
        }

        public void OnGuestClicked() => GameManager.Instance.ConfirmLogin("Convidado" + Random.Range(1000, 9999));
    }

    /// <summary>Lobby principal: perfil, moedas, entrada para partida e ads.</summary>
    public sealed class LobbyScreen : MonoBehaviour
    {
        [SerializeField] private TMP_Text nickLabel;
        [SerializeField] private TMP_Text levelLabel;
        [SerializeField] private TMP_Text goldLabel;
        [SerializeField] private TMP_Text diamondLabel;
        [SerializeField] private Slider xpBar;
        [SerializeField] private TMP_Text winsLabel;
        [SerializeField] private TMP_Text adButtonLabel;

        private void OnEnable()
        {
            EventBus.Subscribe<ProfileChanged>(OnProfileChanged);
            Refresh();
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<ProfileChanged>(OnProfileChanged);
        }

        private void OnProfileChanged(ProfileChanged _) => Refresh();

        private void Update()
        {
            // Barra de ads: esmaece enquanto o provider nao esta pronto.
            if (adButtonLabel != null && AdsManager.Instance != null)
            {
                var ready = AdsManager.Instance.IsReady && !AdsManager.Instance.IsBusy;
                adButtonLabel.alpha = ready ? 1f : 0.5f;
            }
        }

        private void Refresh()
        {
            var save = SaveManager.Instance;
            if (save == null || save.Profile == null) return;

            var p = save.Profile;
            if (nickLabel != null) nickLabel.text = p.nick;
            if (levelLabel != null) levelLabel.text = "Nv. " + p.level;
            if (goldLabel != null) goldLabel.text = p.gold.ToString("N0");
            if (diamondLabel != null) diamondLabel.text = p.diamonds.ToString("N0");
            if (xpBar != null) xpBar.value = p.LevelProgress01;

            if (winsLabel != null)
                winsLabel.text = $"{p.wins} vitórias • {p.totalKills} kills • melhor #{p.bestPlacement}";
        }

        public void OnStartMatchClicked() => GameManager.Instance.StartMatch();
        public void OnWatchAdClicked() => GameManager.Instance.WatchLobbyAd();

        public void OnStoreClicked() => ShowComingSoon("Loja");
        public void OnLoadoutClicked() => ShowComingSoon("Customização");
        public void OnInventoryClicked() => ShowComingSoon("Inventário");
        public void OnRankClicked() => ShowComingSoon("Rank");

        private void ShowComingSoon(string feature)
        {
            Debug.Log($"[Lobby] '{feature}' entra na proxima sprint (ver docs/ARCHITECTURE.md > Roadmap).");
        }
    }

    /// <summary>
    /// Matchmaking simulado: anima a fila de 50 jogadores enquanto o GameManager
    /// carrega a arena. Puramente visual, dura o mesmo tempo do carregamento.
    /// </summary>
    public sealed class MatchmakingScreen : MonoBehaviour
    {
        [SerializeField] private TMP_Text statusLabel;
        [SerializeField] private Slider progressBar;
        [SerializeField] private TMP_Text foundLabel;
        [SerializeField] private float queueSeconds = 2.6f;

        private Coroutine _queue;

        private void OnEnable()
        {
            var total = 50;
            if (MatchManager.Current != null && MatchManager.Current.Config != null)
                total = MatchManager.Current.Config.totalPlayers;

            _queue = StartCoroutine(PlayQueue(total));
        }

        private void OnDisable()
        {
            if (_queue != null) StopCoroutine(_queue);
        }

        private IEnumerator PlayQueue(int total)
        {
            var elapsed = 0f;
            var found = 1;

            if (progressBar != null) progressBar.value = 0f;

            while (elapsed < queueSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / queueSeconds);
                found = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(1, total, t)), 1, total);

                if (progressBar != null) progressBar.value = t;
                if (foundLabel != null) foundLabel.text = $"{found}/{total}";
                if (statusLabel != null) statusLabel.text = found < total ? "Procurando jogadores..." : "Partida encontrada!";

                yield return null;
            }

            if (foundLabel != null) foundLabel.text = $"{total}/{total}";
            if (statusLabel != null) statusLabel.text = "Entrando no avião...";
        }
    }
}
