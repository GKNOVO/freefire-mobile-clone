using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using BR.Combat;
using BR.Core;
using BR.Inventory;
using BR.Managers;
using BR.Player;

namespace BR.UI
{
    /// <summary>
    /// HUD da partida: vivos, vida/colete, municao, kill feed, timer da zona,
    /// prompt de loot e botoes de pulo/paraquedas/interacao.
    ///
    /// Binding: 90% event-driven (EventBus) e apenas 2 leituras por frame
    /// (vida do player) — sem GetComponent em Update, sem LINQ, sem alocacao.
    /// </summary>
    public sealed class HudController : MonoBehaviour
    {
        [Header("Texto")]
        [SerializeField] private TMP_Text aliveLabel;
        [SerializeField] private TMP_Text zoneLabel;
        [SerializeField] private TMP_Text ammoLabel;
        [SerializeField] private TMP_Text promptLabel;
        [SerializeField] private TMP_Text killFeedLabel;
        [SerializeField] private TMP_Text phaseLabel;

        [Header("Barras")]
        [SerializeField] private Image healthFill;
        [SerializeField] private Image armorFill;
        [SerializeField] private Image healthFlash;

        [Header("Botoes mobile")]
        [SerializeField] private GameObject jumpButton;
        [SerializeField] private GameObject parachuteButton;
        [SerializeField] private GameObject interactButton;
        [SerializeField] private GameObject fireButton;

        [Header("Config")]
        [SerializeField] private float killFeedVisibleSeconds = 5f;

        private Health _playerHealth;
        private InventoryComponent _inventory;
        private Health _cachedHealth;

        private struct FeedEntry
        {
            public string Text;
            public float ExpiresAt;
        }

        private readonly List<FeedEntry> _feed = new List<FeedEntry>(4);
        private readonly StringBuilder _builder = new StringBuilder(256);
        private float _feedRefreshTimer;
        private float _flashTimer;

        private void OnEnable()
        {
            EventBus.Subscribe<AliveCountChanged>(OnAliveChanged);
            EventBus.Subscribe<WeaponAmmoChanged>(OnAmmoChanged);
            EventBus.Subscribe<KillFeedMessage>(OnKillFeed);
            EventBus.Subscribe<ZoneTimerTick>(OnZoneTick);
            EventBus.Subscribe<MatchPhaseChanged>(OnPhaseChanged);
            EventBus.Subscribe<LootPicked>(OnLootPicked);
            EventBus.Subscribe<PlayerDied>(OnPlayerDied);

            if (killFeedLabel != null) killFeedLabel.text = string.Empty;
            if (promptLabel != null) promptLabel.gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<AliveCountChanged>(OnAliveChanged);
            EventBus.Unsubscribe<WeaponAmmoChanged>(OnAmmoChanged);
            EventBus.Unsubscribe<KillFeedMessage>(OnKillFeed);
            EventBus.Unsubscribe<ZoneTimerTick>(OnZoneTick);
            EventBus.Unsubscribe<MatchPhaseChanged>(OnPhaseChanged);
            EventBus.Unsubscribe<LootPicked>(OnLootPicked);
            EventBus.Unsubscribe<PlayerDied>(OnPlayerDied);
        }

        private void Update()
        {
            /* ---------- Player (poll barato: 2 floats) ---------- */
            if (_playerHealth == null)
            {
                var player = PlayerController.Local;
                if (player != null)
                {
                    _playerHealth = player.GetComponent<Health>();
                    _inventory = player.GetComponent<InventoryComponent>();
                }
            }

            if (_playerHealth != null)
            {
                if (healthFill != null)
                    healthFill.fillAmount = _playerHealth.MaxHealth > 0f ? _playerHealth.HealthPoints / _playerHealth.MaxHealth : 0f;

                if (armorFill != null)
                    armorFill.fillAmount = _playerHealth.MaxArmor > 0f ? _playerHealth.Armor / _playerHealth.MaxArmor : 0f;
            }

            /* ---------- Botoes contextuais ---------- */
            var playerNow = PlayerController.Local;
            var state = playerNow != null ? playerNow.State : PlayerState.Dead;

            if (jumpButton != null) jumpButton.SetActive(state == PlayerState.InPlane);
            if (parachuteButton != null) parachuteButton.SetActive(state == PlayerState.Freefall);
            if (fireButton != null) fireButton.SetActive(state == PlayerState.Grounded || state == PlayerState.Parachuting);

            /* ---------- Prompt de loot ---------- */
            var focused = _inventory != null ? _inventory.FocusedLoot : null;
            var hasPrompt = focused != null;

            if (promptLabel != null)
            {
                promptLabel.gameObject.SetActive(hasPrompt);
                if (hasPrompt) promptLabel.text = "PEGAR  " + focused.Item.displayName;
            }

            if (interactButton != null) interactButton.SetActive(hasPrompt);

            /* ---------- Kill feed (limpeza periodica) ---------- */
            _feedRefreshTimer -= Time.unscaledDeltaTime;
            if (_feedRefreshTimer <= 0f && _feed.Count > 0)
            {
                _feedRefreshTimer = 0.5f;
                PruneFeed();
            }

            /* ---------- Flash de dano ---------- */
            if (_flashTimer > 0f && healthFlash != null)
            {
                _flashTimer -= Time.unscaledDeltaTime;
                var color = healthFlash.color;
                color.a = Mathf.Clamp01(_flashTimer / 0.25f) * 0.35f;
                healthFlash.color = color;
            }
        }

        // ------------------------------------------------------------ Eventos

        private void OnAliveChanged(AliveCountChanged e)
        {
            if (aliveLabel != null) aliveLabel.text = e.Alive.ToString();
        }

        private void OnAmmoChanged(WeaponAmmoChanged e)
        {
            if (ammoLabel == null) return;
            ammoLabel.text = e.IsReloading ? "RECARREGANDO" : $"{e.Magazine} / {e.Reserve}";
        }

        private void OnPhaseChanged(MatchPhaseChanged e)
        {
            if (phaseLabel == null) return;

            switch (e.Current)
            {
                case MatchPhase.WaitingIsland: phaseLabel.text = "PREPARE-SE"; break;
                case MatchPhase.PlaneFlight: phaseLabel.text = "PULE QUANDO QUISER"; break;
                case MatchPhase.Freefall: phaseLabel.text = "QUEDA LIVRE"; break;
                case MatchPhase.Active: phaseLabel.text = ""; break;
                case MatchPhase.Ended: phaseLabel.text = "FIM DE PARTIDA"; break;
                default: phaseLabel.text = ""; break;
            }
        }

        private void OnZoneTick(ZoneTimerTick e)
        {
            if (zoneLabel == null) return;

            var seconds = Mathf.CeilToInt(e.SecondsToNextEvent);
            zoneLabel.text = e.IsShrinking ? $"ZONA FECHANDO {seconds}s" : $"ZONA {seconds}s";

            zoneLabel.color = e.LocalPlayerInside ? Color.white : new Color(1f, 0.35f, 0.35f);
        }

        private void OnKillFeed(KillFeedMessage msg)
        {
            var weapon = string.IsNullOrEmpty(msg.WeaponId) ? "" : $" [{msg.WeaponId}]";
            _feed.Add(new FeedEntry
            {
                Text = $"{msg.Killer} eliminou {msg.Victim}{weapon}",
                ExpiresAt = Time.unscaledTime + killFeedVisibleSeconds
            });

            if (_feed.Count > 4) _feed.RemoveAt(0);
            RebuildFeed();
        }

        private void OnLootPicked(LootPicked loot)
        {
            if (promptLabel == null) return;
            promptLabel.gameObject.SetActive(true);
            promptLabel.text = $"+ {loot.DisplayName}";
            CancelInvoke(nameof(HideLootToast));
            Invoke(nameof(HideLootToast), 1.2f);
        }

        private void HideLootToast()
        {
            if (promptLabel != null) promptLabel.gameObject.SetActive(false);
        }

        private void OnPlayerDied(PlayerDied e)
        {
            if (healthFlash == null) return;
            _flashTimer = 0.25f;
        }

        // ------------------------------------------------------------ Feed

        private void PruneFeed()
        {
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
            if (killFeedLabel == null) return;

            _builder.Clear();
            for (var i = 0; i < _feed.Count; i++)
            {
                if (i > 0) _builder.Append('\n');
                _builder.Append(_feed[i].Text);
            }

            killFeedLabel.text = _builder.ToString();
        }

        // ------------------------------------------------------------ Botoes (Inspector)

        public void OnJumpClicked() => MatchManager.Current?.RequestPlayerJump();
        public void OnParachuteClicked() => InputBridge.PressParachute();
        public void OnInteractClicked() => InputBridge.PressInteract();
        public void OnReloadClicked() => InputBridge.PressReload();

        public void OnFireDown() => InputBridge.FireHeld = true;
        public void OnFireUp() => InputBridge.FireHeld = false;
        public void OnSprintDown() => InputBridge.SprintHeld = true;
        public void OnSprintUp() => InputBridge.SprintHeld = false;
        public void OnCrouchDown() => InputBridge.CrouchHeld = true;
        public void OnCrouchUp() => InputBridge.CrouchHeld = false;
    }
}
