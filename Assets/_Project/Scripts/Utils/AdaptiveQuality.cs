using System.Collections;
using UnityEngine;
using BR.Core;

namespace BR.Utils
{
    /// <summary>
    /// Qualidade grafica adaptativa (opcional, recomendado em mobile).
    ///
    /// Mede o frame time medio em janelas de 2s e troca de "tier" com histerese:
    ///  - abaixo de 55 FPS por 2 janelas -> desce 1 tier
    ///  - acima de 58 FPS por 3 janelas -> sobe 1 tier (se ja nao estiver no topo)
    ///
    /// Evita oscilacao (thrashing) que causa mais engasgo que FPS baixo.
    /// </summary>
    public sealed class AdaptiveQuality : MonoSingleton<AdaptiveQuality>
    {
        [System.Serializable]
        public struct QualityTier
        {
            public string name;
            public int unityQualityLevel;   // indice em Project Settings > Quality
            public int targetFrameRate;
            public float shadowDistance;
            public float renderScale;       // URP: usa este valor no asset
            public int maxBotsFullSimulation;
        }

        [SerializeField] private bool enableInEditor = false;
        [SerializeField] private QualityTier[] tiers;
        [SerializeField] private float sampleWindow = 2f;
        [SerializeField] private int windowsToDowngrade = 2;
        [SerializeField] private int windowsToUpgrade = 4;
        [SerializeField] private float downgradeFps = 55f;
        [SerializeField] private float upgradeFps = 58f;

        public int CurrentTierIndex { get; private set; }
        public float AverageFps { get; private set; }

        private float _windowTime;
        private int _frames;
        private int _badWindows;
        private int _goodWindows;

        protected override void OnSingletonAwake()
        {
            if (!enableInEditor && Application.isEditor)
            {
                enabled = false;
                return;
            }

            if (tiers == null || tiers.Length == 0)
            {
                tiers = BuildDefaultTiers();
            }

            CurrentTierIndex = Mathf.Clamp(tiers.Length - 2, 0, tiers.Length - 1); // comeca no "Alto"
            Apply(CurrentTierIndex);
        }

        private static QualityTier[] BuildDefaultTiers()
        {
            return new[]
            {
                new QualityTier { name = "Baixo",   unityQualityLevel = 0, targetFrameRate = 45, shadowDistance = 22f, renderScale = 0.75f, maxBotsFullSimulation = 18 },
                new QualityTier { name = "Medio",   unityQualityLevel = 1, targetFrameRate = 60, shadowDistance = 38f, renderScale = 0.85f, maxBotsFullSimulation = 28 },
                new QualityTier { name = "Alto",    unityQualityLevel = 2, targetFrameRate = 60, shadowDistance = 55f, renderScale = 1.00f, maxBotsFullSimulation = 40 },
                new QualityTier { name = "Ultra",   unityQualityLevel = 3, targetFrameRate = 60, shadowDistance = 80f, renderScale = 1.00f, maxBotsFullSimulation = 49 }
            };
        }

        private void Update()
        {
            _windowTime += Time.unscaledDeltaTime;
            _frames++;

            if (_windowTime < sampleWindow) return;

            AverageFps = _frames / _windowTime;
            _windowTime = 0f;
            _frames = 0;

            if (AverageFps < downgradeFps)
            {
                _badWindows++;
                _goodWindows = 0;
            }
            else if (AverageFps > upgradeFps)
            {
                _goodWindows++;
                _badWindows = 0;
            }
            else
            {
                _badWindows = 0;
                _goodWindows = 0;
            }

            if (_badWindows >= windowsToDowngrade && CurrentTierIndex > 0)
            {
                CurrentTierIndex--;
                _badWindows = 0;
                Apply(CurrentTierIndex);
                Debug.Log($"[AdaptiveQuality] Downgrade -> {tiers[CurrentTierIndex].name} ({AverageFps:F1} fps).");
            }
            else if (_goodWindows >= windowsToUpgrade && CurrentTierIndex < tiers.Length - 1)
            {
                CurrentTierIndex++;
                _goodWindows = 0;
                Apply(CurrentTierIndex);
                Debug.Log($"[AdaptiveQuality] Upgrade -> {tiers[CurrentTierIndex].name} ({AverageFps:F1} fps).");
            }
        }

        private void Apply(int tierIndex)
        {
            var tier = tiers[Mathf.Clamp(tierIndex, 0, tiers.Length - 1)];

            Application.targetFrameRate = Mathf.Max(30, tier.targetFrameRate);
            QualitySettings.shadowDistance = tier.shadowDistance;

            var level = Mathf.Clamp(tier.unityQualityLevel, 0, QualitySettings.names.Length - 1);
            if (QualitySettings.GetQualityLevel() != level)
                QualitySettings.SetQualityLevel(level, applyExpensiveChanges: false);
        }

        public QualityTier CurrentTier => tiers[Mathf.Clamp(CurrentTierIndex, 0, tiers.Length - 1)];
    }
}
