using UnityEngine;
using UnityEngine.UI;
using TMPro;
using BR.Core;
using BR.Managers;

namespace BR.UI
{
    /// <summary>
    /// Fim de partida: posicao, kills, recompensas e botao de anuncio para dobrar.
    /// Le o resultado do GameManager (fonte unica) e reage ao evento de recompensa.
    /// </summary>
    public sealed class ResultsScreen : MonoBehaviour
    {
        [Header("Textos")]
        [SerializeField] private TMP_Text placementLabel;
        [SerializeField] private TMP_Text killsLabel;
        [SerializeField] private TMP_Text goldLabel;
        [SerializeField] private TMP_Text xpLabel;
        [SerializeField] private TMP_Text levelLabel;
        [SerializeField] private TMP_Text doubleRewardLabel;

        [Header("Botoes")]
        [SerializeField] private Button doubleRewardButton;
        [SerializeField] private Button lobbyButton;

        [Header("Animacao")]
        [SerializeField] private CanvasGroup contentGroup;
        [SerializeField] private float fadeSeconds = 0.35f;

        private float _fadeTime = -1f;

        private void OnEnable()
        {
            EventBus.Subscribe<RewardedAdResult>(OnRewarded);

            Refresh();
            _fadeTime = 0f;
            if (contentGroup != null) contentGroup.alpha = 0f;
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<RewardedAdResult>(OnRewarded);
        }

        private void Update()
        {
            if (_fadeTime < 0f || contentGroup == null) return;

            _fadeTime += Time.unscaledDeltaTime;
            contentGroup.alpha = Mathf.Clamp01(_fadeTime / fadeSeconds);

            if (_fadeTime >= fadeSeconds) _fadeTime = -1f;
        }

        private void Refresh()
        {
            var game = GameManager.Instance;
            if (game == null) return;

            var result = game.LastResult;

            if (placementLabel != null)
            {
                placementLabel.text = result.Victory ? "BOOYAH!" : $"#{result.Placement}";
                placementLabel.color = result.Victory ? new Color(1f, 0.82f, 0.25f) : Color.white;
            }

            if (killsLabel != null) killsLabel.text = result.Kills.ToString();
            if (goldLabel != null) goldLabel.text = "+" + result.GoldReward();
            if (xpLabel != null) xpLabel.text = "+" + result.XpReward();

            var save = SaveManager.Instance;
            if (levelLabel != null && save != null && save.Profile != null)
                levelLabel.text = $"Nível {save.Profile.level}";

            var doubled = game.RewardDoubled;

            if (doubleRewardButton != null) doubleRewardButton.interactable = !doubled;
            if (doubleRewardLabel != null)
                doubleRewardLabel.text = doubled ? "Recompensa dobrada!" : "DOBRAR COM ANÚNCIO";
        }

        private void OnRewarded(RewardedAdResult e)
        {
            if (e.Placement == "match_double" && e.Completed) Refresh();
        }

        public void OnDoubleRewardClicked()
        {
            var game = GameManager.Instance;
            if (game == null) return;

            game.RequestDoubleReward();
            if (doubleRewardButton != null) doubleRewardButton.interactable = false;
            if (doubleRewardLabel != null) doubleRewardLabel.text = "Carregando anúncio...";
        }

        public void OnLobbyClicked()
        {
            GameManager.Instance.ReturnToLobby();
        }
    }
}
