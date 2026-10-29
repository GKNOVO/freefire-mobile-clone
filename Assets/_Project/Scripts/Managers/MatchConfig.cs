using UnityEngine;

namespace BR.Managers
{
    /// <summary>
    /// Configuracao de partida data-driven (ScriptableObject).
    /// Ajustar ritmo do jogo = editar este asset, sem recompilar.
    /// </summary>
    [CreateAssetMenu(menuName = "BR/Match Config", fileName = "MatchConfig_Default")]
    public sealed class MatchConfig : ScriptableObject
    {
        [Header("Jogadores")]
        [Range(2, 50)] public int totalPlayers = 50; // 1 player + 49 bots

        [Header("Tempos (segundos)")]
        public float waitingIslandSeconds = 35f;
        public float planeFlightSeconds = 28f;
        public float matchEndDelay = 3f;

        [Header("Mapa")]
        public float mapRadius = 450f;
        public Vector3 mapCenter = Vector3.zero;

        [Header("Aviao")]
        public Vector3 planeStart = new Vector3(-520f, 400f, -520f);
        public Vector3 planeEnd = new Vector3(520f, 400f, 520f);
        public float planeAltitude = 400f;

        [Header("Ilha de espera")]
        public Vector3 waitingIslandSpawn = new Vector3(0f, 2f, -18f);

        [Header("Zona")]
        public ZoneManager.ZonePhase[] zonePhases;

        [Header("Loot")]
        [Tooltip("Quantidade de itens espalhados pelo mapa no inicio.")]
        public int lootSpawnCount = 220;

        private void OnValidate()
        {
            totalPlayers = Mathf.Clamp(totalPlayers, 2, 50);
        }

        /// <summary>Fallback de zonas caso o asset ainda nao tenha fases configuradas.</summary>
        public ZoneManager.ZonePhase[] GetZonePhases()
        {
            if (zonePhases != null && zonePhases.Length > 0) return zonePhases;

            return new[]
            {
                new ZoneManager.ZonePhase { waitSeconds = 45f, shrinkSeconds = 35f, targetRadius = 300f, damagePerSecond = 2f },
                new ZoneManager.ZonePhase { waitSeconds = 30f, shrinkSeconds = 30f, targetRadius = 190f, damagePerSecond = 4f },
                new ZoneManager.ZonePhase { waitSeconds = 25f, shrinkSeconds = 25f, targetRadius = 110f, damagePerSecond = 7f },
                new ZoneManager.ZonePhase { waitSeconds = 20f, shrinkSeconds = 20f, targetRadius = 55f,  damagePerSecond = 10f },
                new ZoneManager.ZonePhase { waitSeconds = 15f, shrinkSeconds = 15f, targetRadius = 20f,  damagePerSecond = 15f },
                new ZoneManager.ZonePhase { waitSeconds = 10f, shrinkSeconds = 10f, targetRadius = 2f,   damagePerSecond = 20f }
            };
        }
    }
}
