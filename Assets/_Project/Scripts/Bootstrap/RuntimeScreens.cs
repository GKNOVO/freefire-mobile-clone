using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using BR.Core;
using BR.Managers;

namespace BR.Bootstrap
{
    /// <summary>Tela de splash: apenas feedback visual; o tempo real vem do GameManager.</summary>
    public sealed class RuntimeSplashScreen : MonoBehaviour
    {
        public RuntimeBar Progress;
        public Text Status;

        private void OnEnable()
        {
            if (Progress != null) Progress.SetValue(0f);
            if (Status != null) Status.text = "Carregando...";
            StartCoroutine(Animate());
        }

        private IEnumerator Animate()
        {
            var elapsed = 0f;
            const float duration = 1.2f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                if (Progress != null) Progress.SetValue(elapsed / duration);
                yield return null;
            }

            if (Progress != null) Progress.SetValue(1f);
            if (Status != null) Status.text = "Pronto";
        }
    }

    /// <summary>Login local: nick salvo no perfil (sem conta, sem rede).</summary>
    public sealed class RuntimeLoginScreen : MonoBehaviour
    {
        public InputField NickField;
        public Text ErrorLabel;

        private void OnEnable()
        {
            var save = SaveManager.Instance;
            if (NickField != null)
                NickField.text = save != null && save.Profile != null ? save.Profile.nick : "Player";

            if (ErrorLabel != null) ErrorLabel.gameObject.SetActive(false);
        }

        public void OnPlayClicked()
        {
            var nick = NickField != null ? NickField.text : string.Empty;

            if (string.IsNullOrWhiteSpace(nick) || nick.Trim().Length < 3)
            {
                if (ErrorLabel != null)
                {
                    ErrorLabel.text = "O nick precisa ter pelo menos 3 caracteres.";
                    ErrorLabel.gameObject.SetActive(true);
                }
                return;
            }

            GameManager.Instance.ConfirmLogin(nick);
        }

        public void OnGuestClicked()
        {
            GameManager.Instance.ConfirmLogin("Convidado" + Random.Range(1000, 9999));
        }
    }

    /// <summary>Lobby: perfil, moedas, entrada para partida e rewarded do lobby.</summary>
    public sealed class RuntimeLobbyScreen : MonoBehaviour
    {
        public Text ProfileLabel;
        public Text CurrencyLabel;
        public Text StatsLabel;
        public Text AdLabel;

        private void OnEnable()
        {
            EventBus.Subscribe<ProfileChanged>(OnProfileChanged);
            Refresh();
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<ProfileChanged>(OnProfileChanged);
        }

        private void OnProfileChanged(ProfileChanged changed) => Refresh();

        private void Refresh()
        {
            var save = SaveManager.Instance;
            if (save == null || save.Profile == null) return;

            var profile = save.Profile;

            if (ProfileLabel != null)
                ProfileLabel.text = profile.nick + "   •   Nível " + profile.level;

            if (CurrencyLabel != null)
                CurrencyLabel.text = "OURO " + profile.gold + "     DIAMANTES " + profile.diamonds;

            if (StatsLabel != null)
                StatsLabel.text = profile.wins + " vitórias  •  " + profile.totalKills +
                                  " eliminações  •  melhor #" + profile.bestPlacement;
        }

        public void OnPlayClicked() => GameManager.Instance.StartMatch();

        public void OnAdClicked() => GameManager.Instance.WatchLobbyAd();
    }

    /// <summary>Matchmaking simulado (visual) enquanto a Arena carrega.</summary>
    public sealed class RuntimeMatchmakingScreen : MonoBehaviour
    {
        public Text StatusLabel;
        public RuntimeBar Progress;

        private Coroutine _routine;

        private void OnEnable()
        {
            if (Progress != null) Progress.SetValue(0f);
            _routine = StartCoroutine(Animate());
        }

        private void OnDisable()
        {
            if (_routine != null) StopCoroutine(_routine);
        }

        private IEnumerator Animate()
        {
            var elapsed = 0f;
            const float duration = 2.6f;

            if (StatusLabel != null) StatusLabel.text = "Procurando jogadores... 1/50";

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / duration);

                if (Progress != null) Progress.SetValue(t);

                if (StatusLabel != null)
                    StatusLabel.text = t < 0.99f
                        ? "Procurando jogadores... " + Mathf.RoundToInt(t * 50) + "/50"
                        : "Partida encontrada!";

                yield return null;
            }

            if (StatusLabel != null) StatusLabel.text = "Entrando no avião...";
        }
    }

    /// <summary>Resultados: colocação, recompensas e anúncio para dobrar.</summary>
    public sealed class RuntimeResultsScreen : MonoBehaviour
    {
        public Text PlacementLabel;
        public Text KillsLabel;
        public Text RewardLabel;
        public Text DoubleLabel;
        public Button DoubleButton;

        private void OnEnable()
        {
            EventBus.Subscribe<RewardedAdResult>(OnRewarded);
            Refresh();
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<RewardedAdResult>(OnRewarded);
        }

        private void OnRewarded(RewardedAdResult result)
        {
            if (result.Completed) Refresh();
        }

        private void Refresh()
        {
            var game = GameManager.Instance;
            if (game == null) return;

            var result = game.LastResult;

            if (PlacementLabel != null)
            {
                PlacementLabel.text = result.Victory ? "BOOYAH!" : "#" + result.Placement;
                PlacementLabel.color = result.Victory ? new Color(1f, 0.85f, 0.25f) : Color.white;
            }

            if (KillsLabel != null)
                KillsLabel.text = result.Kills + " eliminações";

            if (RewardLabel != null)
                RewardLabel.text = "+" + result.GoldReward() + " ouro     •     +" + result.XpReward() + " XP";

            var doubled = game.RewardDoubled;

            if (DoubleButton != null) DoubleButton.interactable = !doubled;
            if (DoubleLabel != null) DoubleLabel.text = doubled ? "RECOMPENSA DOBRADA" : "DOBRAR COM ANÚNCIO";
        }

        public void OnDoubleClicked() => GameManager.Instance.RequestDoubleReward();

        public void OnLobbyClicked() => GameManager.Instance.ReturnToLobby();
    }

    /// <summary>Constrói as telas de menu por código e registra no UIManager.</summary>
    public static class RuntimeScreens
    {
        public static void Build(Transform canvas, System.Action<AppScreen, GameObject> register)
        {
            register(AppScreen.Splash, BuildSplash(canvas));
            register(AppScreen.Login, BuildLogin(canvas));
            register(AppScreen.Lobby, BuildLobby(canvas));
            register(AppScreen.Matchmaking, BuildMatchmaking(canvas));
            register(AppScreen.Results, BuildResults(canvas));
            register(AppScreen.Hud, RuntimeHud.Build(canvas));
        }

        private static GameObject CreateScreen(Transform canvas, string screenName)
        {
            var rect = RuntimeUI.CreateRect(canvas, screenName, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            RuntimeUI.Stretch(rect);

            var background = rect.gameObject.AddComponent<Image>();
            background.color = new Color(0.055f, 0.07f, 0.10f, 1f);
            background.raycastTarget = true;

            return rect.gameObject;
        }

        private static Text CenteredText(Transform parent, string textName, string content, int size, Color color,
            Vector2 anchoredPosition, Vector2 rectSize)
        {
            return RuntimeUI.CreateText(parent, textName, content, size, TextAnchor.MiddleCenter, color,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchoredPosition, rectSize);
        }

        // ------------------------------------------------------------ Splash

        private static GameObject BuildSplash(Transform canvas)
        {
            var root = CreateScreen(canvas, "Screen_Splash");

            CenteredText(root.transform, "Title", "BATTLE ROYALE", 96, Color.white, new Vector2(0f, 120f), new Vector2(1600f, 140f));
            CenteredText(root.transform, "Subtitle", "MOBILE  •  100% OFFLINE  •  49 BOTS", 28,
                new Color(1f, 1f, 1f, 0.65f), new Vector2(0f, 30f), new Vector2(1400f, 60f));

            var bar = RuntimeUI.CreateBar(root.transform, "Progress", new Vector2(700f, 18f),
                new Color(0.25f, 0.85f, 1f), new Color(1f, 1f, 1f, 0.12f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -80f));

            var status = CenteredText(root.transform, "Status", "Carregando...", 24,
                new Color(1f, 1f, 1f, 0.55f), new Vector2(0f, -140f), new Vector2(900f, 50f));

            var screen = root.AddComponent<RuntimeSplashScreen>();
            screen.Progress = bar;
            screen.Status = status;

            return root;
        }

        // ------------------------------------------------------------ Login

        private static GameObject BuildLogin(Transform canvas)
        {
            var root = CreateScreen(canvas, "Screen_Login");

            CenteredText(root.transform, "Title", "ENTRAR", 72, Color.white, new Vector2(0f, 270f), new Vector2(1200f, 120f));
            CenteredText(root.transform, "Hint", "Escolha seu nick (salvo apenas neste aparelho)", 26,
                new Color(1f, 1f, 1f, 0.6f), new Vector2(0f, 190f), new Vector2(1200f, 50f));

            var field = RuntimeUI.CreateInputField(root.transform, "NickField", "Digite seu nick...",
                new Vector2(0f, 80f), new Vector2(620f, 84f));

            var error = CenteredText(root.transform, "Error", "", 24, new Color(1f, 0.45f, 0.45f),
                new Vector2(0f, 10f), new Vector2(900f, 46f));
            error.gameObject.SetActive(false);

            var play = RuntimeUI.CreateButton(root.transform, "Play", "JOGAR", 38,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -95f), new Vector2(380f, 96f), new Color(0.16f, 0.62f, 0.36f), null);

            var guest = RuntimeUI.CreateButton(root.transform, "Guest", "ENTRAR COMO CONVIDADO", 26,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -210f), new Vector2(380f, 70f), new Color(1f, 1f, 1f, 0.12f), null);

            var screen = root.AddComponent<RuntimeLoginScreen>();
            screen.NickField = field;
            screen.ErrorLabel = error;

            play.onClick.AddListener(screen.OnPlayClicked);
            guest.onClick.AddListener(screen.OnGuestClicked);

            return root;
        }

        // ------------------------------------------------------------ Lobby

        private static GameObject BuildLobby(Transform canvas)
        {
            var root = CreateScreen(canvas, "Screen_Lobby");

            var profile = RuntimeUI.CreateText(root.transform, "Profile", "Player", 30, TextAnchor.UpperLeft, Color.white,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -32f), new Vector2(800f, 50f));

            var currency = RuntimeUI.CreateText(root.transform, "Currency", "OURO 0", 28, TextAnchor.UpperRight,
                new Color(1f, 0.85f, 0.4f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-32f, -32f), new Vector2(700f, 50f));

            CenteredText(root.transform, "Title", "BATTLE ROYALE", 84, Color.white, new Vector2(0f, 250f), new Vector2(1400f, 130f));
            CenteredText(root.transform, "Mode", "SOLO  •  1 JOGADOR + 49 BOTS", 26,
                new Color(1f, 1f, 1f, 0.6f), new Vector2(0f, 175f), new Vector2(1200f, 50f));

            var stats = CenteredText(root.transform, "Stats", "0 vitórias  •  0 eliminações", 24,
                new Color(1f, 1f, 1f, 0.5f), new Vector2(0f, 110f), new Vector2(1200f, 46f));

            var play = RuntimeUI.CreateButton(root.transform, "Play", "JOGAR", 40,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -20f), new Vector2(470f, 110f), new Color(0.16f, 0.62f, 0.36f), null);

            var ad = RuntimeUI.CreateButton(root.transform, "Ad", "ANÚNCIO GRÁTIS: +100 OURO", 24,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -160f), new Vector2(470f, 76f), new Color(0.95f, 0.72f, 0.2f), null);

            var adLabel = ad.GetComponentInChildren<Text>();

            CenteredText(root.transform, "Footer",
                "Toque: joystick move  •  arraste a tela para mirar  •  FOGO atira",
                22, new Color(1f, 1f, 1f, 0.35f), new Vector2(0f, -300f), new Vector2(1500f, 40f));

            var screen = root.AddComponent<RuntimeLobbyScreen>();
            screen.ProfileLabel = profile;
            screen.CurrencyLabel = currency;
            screen.StatsLabel = stats;
            screen.AdLabel = adLabel;

            play.onClick.AddListener(screen.OnPlayClicked);
            ad.onClick.AddListener(screen.OnAdClicked);

            return root;
        }

        // ------------------------------------------------------------ Matchmaking

        private static GameObject BuildMatchmaking(Transform canvas)
        {
            var root = CreateScreen(canvas, "Screen_Matchmaking");

            CenteredText(root.transform, "Title", "PROCURANDO PARTIDA", 58, Color.white, new Vector2(0f, 170f), new Vector2(1400f, 100f));
            var status = CenteredText(root.transform, "Status", "Procurando jogadores...", 30,
                new Color(1f, 1f, 1f, 0.75f), new Vector2(0f, 80f), new Vector2(1200f, 50f));

            var bar = RuntimeUI.CreateBar(root.transform, "Progress", new Vector2(760f, 22f),
                new Color(0.25f, 0.85f, 1f), new Color(1f, 1f, 1f, 0.12f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f));

            CenteredText(root.transform, "Hint", "1 jogador real + 49 bots  •  sem internet", 24,
                new Color(1f, 1f, 1f, 0.45f), new Vector2(0f, -90f), new Vector2(1200f, 50f));

            var screen = root.AddComponent<RuntimeMatchmakingScreen>();
            screen.StatusLabel = status;
            screen.Progress = bar;

            return root;
        }

        // ------------------------------------------------------------ Results

        private static GameObject BuildResults(Transform canvas)
        {
            var root = CreateScreen(canvas, "Screen_Results");

            var placement = CenteredText(root.transform, "Placement", "#0", 110, Color.white, new Vector2(0f, 230f), new Vector2(1200f, 150f));

            var kills = CenteredText(root.transform, "Kills", "0 eliminações", 34, new Color(1f, 1f, 1f, 0.8f),
                new Vector2(0f, 120f), new Vector2(1000f, 60f));

            var rewards = CenteredText(root.transform, "Rewards", "+0 ouro", 30, new Color(1f, 0.85f, 0.4f),
                new Vector2(0f, 50f), new Vector2(1000f, 60f));

            var doubleButton = RuntimeUI.CreateButton(root.transform, "Double", "DOBRAR COM ANÚNCIO", 28,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -90f), new Vector2(520f, 90f), new Color(0.95f, 0.72f, 0.2f), null);

            var doubleLabel = doubleButton.GetComponentInChildren<Text>();

            var lobby = RuntimeUI.CreateButton(root.transform, "Lobby", "VOLTAR AO LOBBY", 26,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -215f), new Vector2(430f, 80f), new Color(1f, 1f, 1f, 0.14f), null);

            var screen = root.AddComponent<RuntimeResultsScreen>();
            screen.PlacementLabel = placement;
            screen.KillsLabel = kills;
            screen.RewardLabel = rewards;
            screen.DoubleLabel = doubleLabel;
            screen.DoubleButton = doubleButton;

            doubleButton.onClick.AddListener(screen.OnDoubleClicked);
            lobby.onClick.AddListener(screen.OnLobbyClicked);

            return root;
        }
    }
}
