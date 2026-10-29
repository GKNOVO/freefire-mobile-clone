using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BR.Core;

namespace BR.Managers
{
    /// <summary>
    /// Controla quais telas estao visiveis e o fade global.
    /// Telas sao GameObjects filhos de um Canvas persistente (nunca instanciados em runtime).
    ///
    /// Regra: cada tela escuta o EventBus e se atualiza. UIManager nao conhece regra de jogo.
    /// </summary>
    public sealed class UIManager : MonoSingleton<UIManager>
    {
        [Serializable]
        public sealed class ScreenBinding
        {
            public AppScreen id;
            public GameObject root;
        }

        [Header("Telas")]
        [SerializeField] private ScreenBinding[] screens;

        [Header("Fade")]
        [SerializeField] private CanvasGroup fadeOverlay;
        [SerializeField] private float defaultFadeSeconds = 0.22f;

        private readonly Dictionary<AppScreen, GameObject> _lookup = new Dictionary<AppScreen, GameObject>(8);

        public AppScreen Current { get; private set; } = AppScreen.Splash;
        public bool IsFading { get; private set; }

        protected override void OnSingletonAwake()
        {
            BuildLookup();
            HideAll();
            SetFadeAlpha(0f);
        }

        private void BuildLookup()
        {
            _lookup.Clear();
            if (screens == null) return;

            for (var i = 0; i < screens.Length; i++)
            {
                var binding = screens[i];
                if (binding == null || binding.root == null) continue;
                _lookup[binding.id] = binding.root;
            }
        }

        /// <summary>Registro em runtime (UI gerada por código).</summary>
        public void RegisterScreen(AppScreen id, GameObject root)
        {
            if (root == null) return;
            _lookup[id] = root;
            root.SetActive(false);
        }

        public void Show(AppScreen id)
        {
            if (_lookup.Count == 0) BuildLookup();

            foreach (var pair in _lookup)
                if (pair.Value != null) pair.Value.SetActive(pair.Key == id);

            Current = id;
        }

        public void Hide(AppScreen id)
        {
            if (_lookup.TryGetValue(id, out var root) && root != null)
                root.SetActive(false);
        }

        public void HideAll()
        {
            if (_lookup.Count == 0) BuildLookup();

            foreach (var pair in _lookup)
                if (pair.Value != null) pair.Value.SetActive(false);
        }

        // ------------------------------------------------------------ Fade

        public IEnumerator FadeTo(float targetAlpha, float duration)
        {
            if (fadeOverlay == null)
            {
                yield return null;
                yield break;
            }

            IsFading = true;
            fadeOverlay.blocksRaycasts = targetAlpha > 0.99f;

            var start = fadeOverlay.alpha;
            var elapsed = 0f;
            duration = Mathf.Max(0.01f, duration);

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                SetFadeAlpha(Mathf.Lerp(start, targetAlpha, elapsed / duration));
                yield return null;
            }

            SetFadeAlpha(targetAlpha);
            IsFading = false;
        }

        private void SetFadeAlpha(float alpha)
        {
            if (fadeOverlay == null) return;
            fadeOverlay.alpha = alpha;
            fadeOverlay.blocksRaycasts = alpha > 0.99f;
            fadeOverlay.interactable = false;
        }
    }
}
