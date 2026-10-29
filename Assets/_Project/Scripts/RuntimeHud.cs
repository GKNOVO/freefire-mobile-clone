using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using BR.Combat;
using BR.Core;
using BR.Inventory;
using BR.Managers;
using BR.Player;

namespace BR.Bootstrap
{
    /// <summary>
    /// HUD gerada por código (uGUI). Reaproveita o mesmo EventBus dos managers:
    /// vivos, zona, munição, kill feed e loot chegam por evento; vida é lida do Health
    /// do player (2 floats/frame).
    ///
    /// Controles: joystick (esquerda), arrastar a tela para mirar, FOGO/REC/PULAR/PARAQUEDAS.
    /// </summary>
    public sealed class RuntimeHud : MonoBehaviour
    {
        public Text AliveLabel;
        public Text ZoneLabel;
        public Text PhaseLabel;
        public Text AmmoLabel;
        public Text PromptLabel;
        public Text KillFeedLabel;

        public RuntimeBar HealthBar;
        public RuntimeBar ArmorBar;
        public Image DamageFlash;

        public RawImage MinimapImage;
        public GameObject JumpButton;
        public GameObject ParachuteButton;
        public GameObject PickupButton;

        private struct FeedEntry
        {
            public string Text;
            public float ExpiresAt;
        }

        private readonly List<FeedEntry> _feed = new List<FeedEntry>(4);
        private readonly StringBuilder _builder = new StringBuilder(256);

        private Health _health;
        private InventoryComponent _inventory;
        private Camera _minimapCamera;
        private RenderTexture _minimapTexture;
        private float _feedTimer;
        private float _flash;
        private float _lastHealth = -1f;

        // ------------------------------------------------------------ Ciclo

        private void OnEnable()
        {
            EventBus.Subscribe<AliveCountChanged>(OnAliveChanged);
            EventBus.Subscribe<WeaponAmmoChanged>(OnAmmoChanged);
            EventBus.Subscribe<KillFeedMessage>(OnKillFeed);
            EventBus.Subscribe<ZoneTimerTick>(OnZoneTick);
            EventBus.Subscribe<MatchPhaseChanged>(OnPhaseChanged);
            EventBus.Subscribe<PlayerDied>(OnPlayerDied);

            _feed.Clear();
            _lastHealth = -1f;

            if (KillFeedLabel != null) KillFeedLabel.text = string.Empty;
            if (PromptLabel != null) PromptLabel.gameObject.SetActive(false);
            if (AmmoLabel != null) AmmoLabel.text = "--";

            var match = MatchManager.Current;
            if (AliveLabel != null && match != null) AliveLabel.text = match.AliveCount.ToString();

            EnsureMinimap();
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<AliveCountChanged>(OnAliveChanged);
            EventBus.Unsubscribe<WeaponAmmoChanged>(OnAmmoChanged);
            EventBus.Unsubscribe<KillFeedMessage>(OnKillFeed);
            EventBus.Unsubscribe<ZoneTimerTick>(OnZoneTick);
            EventBus.Unsubscribe<MatchPhaseChanged>(OnPhaseChanged);
            EventBus.Unsubscribe<PlayerDied>(OnPlayerDied);

            if (_minimapCamera != null) _minimapCamera.enabled = false;
        }

        private void OnDestroy()
        {
            if (_minimapTexture != null)
            {
                _minimapTexture.Release();
                Destroy(_minimapTexture);
                _minimapTexture = null;
            }
        }

        private void Update()
        {
            UpdatePlayerRefs();
            UpdateBars();
            UpdateButtons();
            UpdateFeed();
            UpdateMinimap();
        }

        // ------------------------------------------------------------ Player

        private void UpdatePlayerRefs()
        {
            if (_health != null) return;

            var player = PlayerController.Local;
            if (player == null) return;

            _health = player.GetComponent<Health>();
            _inventory = player.GetComponent<InventoryComponent>();
            _lastHealth = -1f;
        }

        private void UpdateBars()
        {
            if (_health != null && HealthBar != null)
            {
                HealthBar.SetValue(_health.MaxHealth > 0f ? _health.HealthPoints / _health.MaxHealth : 0f);

                if (ArmorBar != null)
                    ArmorBar.SetValue(_health.MaxArmor > 0f ? _health.Armor / _health.MaxArmor : 0f);

                if (_lastHealth >= 0f && _health.HealthPoints < _lastHealth - 0.01f)
                    _flash = 0.35f;

                _lastHealth = _health.HealthPoints;
            }

            if (DamageFlash == null) return;

            if (_flash > 0f)
            {
                _flash -= Time.unscaledDeltaTime;
                var color = DamageFlash.color;
                color.a = Mathf.Clamp01(_flash);
                DamageFlash.color = color;
            }
            else if (DamageFlash.color.a > 0f)
            {
                var color = DamageFlash.color;
                color.a = 0f;
                DamageFlash.color = color;
            }
        }

        private void UpdateButtons()
        {
            var player = PlayerController.Local;
            var state = player != null ? player.State : PlayerState.Dead;

            if (JumpButton != null) JumpButton.SetActive(state == PlayerState.InPlane);
            if (ParachuteButton != null) ParachuteButton.SetActive(state == PlayerState.Freefall);

            var focused = _inventory != null ? _inventory.FocusedLoot : null;
            var hasLoot = focused != null && focused.Item != null;

            if (PickupButton != null) PickupButton.SetActive(hasLoot);

            if (PromptLabel != null)
            {
                PromptLabel.gameObject.SetActive(hasLoot);
                if (hasLoot) PromptLabel.text = "PEGAR  " + focused.Item.displayName;
            }
        }

        // ------------------------------------------------------------ Feed

        private void UpdateFeed()
        {
            if (_feed.Count == 0) return;

            _feedTimer -= Time.unscaledDeltaTime;
            if (_feedTimer > 0f) return;
            _feedTimer = 0.5f;

            var now = Time.unscaledTime;
            var removed = false;

            for (var i = _feed.Count - 1; i >= 0; i--)
            {
                if (_feed[i].ExpiresAt > now) continue;
                _feed.RemoveAt(i);
                removed = true;
            }

            if (removed) RebuildFeed();
        }

        private void RebuildFeed()
        {
            if (KillFeedLabel == null) return;

            _builder.Clear();
            for (var i = 0; i < _feed.Count; i++)
            {
                if (i > 0) _builder.Append('\n');
                _builder.Append(_feed[i].Text);
            }

            KillFeedLabel.text = _builder.ToString();
        }

        // ------------------------------------------------------------ Eventos

        private void OnAliveChanged(AliveCountChanged e)
        {
            if (AliveLabel != null) AliveLabel.text = e.Alive.ToString();
        }

        private void OnAmmoChanged(WeaponAmmoChanged e)
        {
            if (AmmoLabel == null) return;
            AmmoLabel.text = e.IsReloading ? "RECARREGANDO" : e.Magazine + " / " + e.Reserve;
        }

        private void OnPhaseChanged(MatchPhaseChanged e)
        {
            if (PhaseLabel == null) return;

            switch (e.Current)
            {
                case MatchPhase.WaitingIsland: PhaseLabel.text = "PREPARE-SE"; break;
                case MatchPhase.PlaneFlight: PhaseLabel.text = "PULE QUANDO QUISER"; break;
                case MatchPhase.Freefall: PhaseLabel.text = "QUEDA LIVRE"; break;
                case MatchPhase.Active: PhaseLabel.text = ""; break;
                case MatchPhase.Ended: PhaseLabel.text = "FIM DE PARTIDA"; break;
                default: PhaseLabel.text = ""; break;
            }
        }

        private void OnZoneTick(ZoneTimerTick e)
        {
            if (ZoneLabel == null) return;

            var seconds = Mathf.CeilToInt(e.SecondsToNextEvent);
            ZoneLabel.text = e.IsShrinking ? "ZONA FECHANDO " + seconds + "s" : "ZONA " + seconds + "s";
            ZoneLabel.color = e.LocalPlayerInside ? Color.white : new Color(1f, 0.4f, 0.4f);
        }

        private void OnKillFeed(KillFeedMessage message)
        {
            var weapon = string.IsNullOrEmpty(message.WeaponId) ? string.Empty : " [" + message.WeaponId + "]";

            _feed.Add(new FeedEntry
            {
                Text = message.Killer + " eliminou " + message.Victim + weapon,
                ExpiresAt = Time.unscaledTime + 5f
            });

            if (_feed.Count > 4) _feed.RemoveAt(0);

            RebuildFeed();
        }

        private void OnPlayerDied(PlayerDied e)
        {
            _flash = 0.35f;
        }

        // ------------------------------------------------------------ Minimapa

        private void EnsureMinimap()
        {
            if (MinimapImage == null) return;

            if (_minimapTexture == null)
            {
                _minimapTexture = new RenderTexture(256, 256, 16, RenderTextureFormat.RGB565)
                {
                    name = "MinimapRT"
                };
                _minimapTexture.Create();
            }

            if (_minimapCamera == null)
            {
                var go = new GameObject("MinimapCamera");
                _minimapCamera = go.AddComponent<Camera>();
                _minimapCamera.orthographic = true;
                _minimapCamera.orthographicSize = 170f;
                _minimapCamera.clearFlags = CameraClearFlags.SolidColor;
                _minimapCamera.backgroundColor = new Color(0.05f, 0.08f, 0.1f, 1f);
                _minimapCamera.nearClipPlane = 0.3f;
                _minimapCamera.farClipPlane = 600f;
                _minimapCamera.depth = -20;
                _minimapCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }

            _minimapCamera.targetTexture = _minimapTexture;
            _minimapCamera.enabled = true;

            MinimapImage.texture = _minimapTexture;
            MinimapImage.color = Color.white;
        }

        private void UpdateMinimap()
        {
            if (_minimapCamera == null) return;

            var player = PlayerController.Local;
            if (player == null) return;

            var position = player.transform.position;
            _minimapCamera.transform.position = new Vector3(position.x, position.y + 220f, position.z);
        }

        // ------------------------------------------------------------ Build

        public static GameObject Build(Transform canvas)
        {
            var rootRect = RuntimeUI.CreateRect(canvas, "Screen_Hud", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            RuntimeUI.Stretch(rootRect);
            var root = rootRect.gameObject;

            var hud = root.AddComponent<RuntimeHud>();

            // Área de arrasto para mirar (fica atrás de todos os botões).
            var look = RuntimeUI.CreateImage(root.transform, "LookArea", new Color(0f, 0f, 0f, 0f),
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            RuntimeUI.Stretch(look.rectTransform);
            look.raycastTarget = true;
            look.gameObject.AddComponent<TouchLookArea>();

            // Vivos
            hud.AliveLabel = RuntimeUI.CreateText(root.transform, "Alive", "50", 46, TextAnchor.UpperLeft, Color.white,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -20f), new Vector2(260f, 60f));

            RuntimeUI.CreateText(root.transform, "AliveCaption", "VIVOS", 20, TextAnchor.UpperLeft, new Color(1f, 1f, 1f, 0.6f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -76f), new Vector2(220f, 30f));

            // Zona / fase
            hud.ZoneLabel = RuntimeUI.CreateText(root.transform, "Zone", "ZONA", 30, TextAnchor.UpperCenter, Color.white,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(760f, 48f));

            hud.PhaseLabel = RuntimeUI.CreateText(root.transform, "Phase", "", 26, TextAnchor.UpperCenter, new Color(1f, 0.85f, 0.3f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -74f), new Vector2(820f, 44f));

            // Minimapa
            var minimapRect = RuntimeUI.CreateRect(root.transform, "Minimap", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(240f, 240f));
            var minimap = minimapRect.gameObject.AddComponent<RawImage>();
            minimap.color = new Color(0.1f, 0.12f, 0.15f, 0.9f);
            minimap.raycastTarget = false;
            hud.MinimapImage = minimap;

            // Kill feed
            hud.KillFeedLabel = RuntimeUI.CreateText(root.transform, "KillFeed", "", 24, TextAnchor.UpperRight, new Color(1f, 0.86f, 0.86f),
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -284f), new Vector2(660f, 220f));

            // Vida / colete
            hud.HealthBar = RuntimeUI.CreateBar(root.transform, "Health", new Vector2(430f, 30f),
                new Color(0.2f, 0.9f, 0.35f), new Color(0f, 0f, 0f, 0.55f),
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(30f, 350f));

            hud.ArmorBar = RuntimeUI.CreateBar(root.transform, "Armor", new Vector2(430f, 18f),
                new Color(0.35f, 0.7f, 1f), new Color(0f, 0f, 0f, 0.55f),
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(30f, 386f));

            // Joystick
            var stick = RuntimeUI.CreateImage(root.transform, "Joystick", new Color(1f, 1f, 1f, 0.10f),
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 40f), new Vector2(300f, 300f));
            stick.sprite = RuntimeUI.CircleSprite;
            stick.raycastTarget = true;

            var knob = RuntimeUI.CreateImage(stick.transform, "Knob", new Color(1f, 1f, 1f, 0.35f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(130f, 130f));
            knob.sprite = RuntimeUI.CircleSprite;

            var joystick = stick.gameObject.AddComponent<VirtualJoystick>();
            joystick.Area = stick.rectTransform;
            joystick.Handle = knob.rectTransform;
            joystick.Radius = 110f;

            // Fogo (pressão contínua)
            var fire = RuntimeUI.CreateButton(root.transform, "Fire", "FOGO", 30,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-40f, 60f), new Vector2(190f, 190f), new Color(0.85f, 0.22f, 0.22f, 0.9f), null);
            fire.GetComponent<Image>().sprite = RuntimeUI.CircleSprite;

            var hold = fire.gameObject.AddComponent<HoldButton>();
            hold.OnPress = () => InputBridge.FireHeld = true;
            hold.OnRelease = () => InputBridge.FireHeld = false;

            var reload = RuntimeUI.CreateButton(root.transform, "Reload", "REC", 26,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-252f, 95f), new Vector2(130f, 100f), new Color(1f, 1f, 1f, 0.16f), null);
            reload.onClick.AddListener(() => InputBridge.PressReload());

            // Pular / paraquedas / pegar
            var jump = RuntimeUI.CreateButton(root.transform, "Jump", "PULAR", 30,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 150f), new Vector2(320f, 100f), new Color(0.2f, 0.55f, 0.95f, 0.92f), null);
            jump.onClick.AddListener(RequestJump);
            hud.JumpButton = jump.gameObject;

            var parachute = RuntimeUI.CreateButton(root.transform, "Parachute", "PARAQUEDAS", 28,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 150f), new Vector2(320f, 100f), new Color(0.2f, 0.55f, 0.95f, 0.92f), null);
            parachute.onClick.AddListener(OpenParachute);
            hud.ParachuteButton = parachute.gameObject;

            var pickup = RuntimeUI.CreateButton(root.transform, "Pickup", "PEGAR", 28,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 270f), new Vector2(320f, 84f), new Color(0.95f, 0.78f, 0.2f, 0.92f), null);
            pickup.onClick.AddListener(PressInteract);
            hud.PickupButton = pickup.gameObject;

            hud.PromptLabel = RuntimeUI.CreateText(root.transform, "Prompt", "", 26, TextAnchor.MiddleCenter, Color.white,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 370f), new Vector2(700f, 50f));

            // Flash de dano
            var flash = RuntimeUI.CreateFullScreen(root.transform, "DamageFlash", new Color(0.8f, 0.1f, 0.1f, 0f));
            flash.raycastTarget = false;
            hud.DamageFlash = flash;

            return root;
        }

        private static void RequestJump()
        {
            if (MatchManager.Current != null) MatchManager.Current.RequestPlayerJump();
        }

        private static void OpenParachute() => InputBridge.PressParachute();

        private static void PressInteract() => InputBridge.PressInteract();
    }
}
