using System;
using System.Collections.Generic;

namespace BR.Data
{
    /// <summary>
    /// Perfil 100% local (offline-first). Serializado em JSON e criptografado pelo SaveManager.
    /// Nunca guardar aqui referencias de cena/objetos Unity.
    /// </summary>
    [Serializable]
    public sealed class PlayerProfile
    {
        public int schemaVersion = 1;
        public string profileId = "";
        public string nick = "Player";
        public int avatarIndex = 0;

        public int level = 1;
        public int xp = 0;
        public int gold = 500;
        public int diamonds = 25;

        public int matchesPlayed = 0;
        public int wins = 0;
        public int totalKills = 0;
        public int bestPlacement = 50;

        public string equippedCharacterId = "chr_default";
        public List<string> ownedItemIds = new List<string> { "chr_default", "skin_default" };

        public long lastLoginUtcTicks = 0;
        public long createdUtcTicks = 0;

        // ---- Curva de progressao centralizada (evita numeros magicos espalhados)
        public const int MaxLevel = 60;

        public static int XpForLevel(int targetLevel)
        {
            if (targetLevel <= 1) return 0;
            // 100 XP no nivel 2, crescendo ~8% por nivel (curva suave, ritmo Free Fire)
            return (int)(100.0 * Math.Pow(1.08, targetLevel - 2));
        }

        public int XpIntoCurrentLevel => xp - XpForLevel(level);
        public int XpForNextLevel => level >= MaxLevel ? 0 : XpForLevel(level + 1) - XpForLevel(level);
        public float LevelProgress01
        {
            get
            {
                if (level >= MaxLevel) return 1f;
                var span = XpForNextLevel;
                return span <= 0 ? 0f : Math.Clamp((float)XpIntoCurrentLevel / span, 0f, 1f);
            }
        }

        public void AddXp(int amount)
        {
            if (amount <= 0) return;
            xp += amount;

            while (level < MaxLevel && xp >= XpForLevel(level + 1))
                level++;
        }

        public static PlayerProfile CreateNew(string nick)
        {
            var now = DateTime.UtcNow.Ticks;
            return new PlayerProfile
            {
                profileId = Guid.NewGuid().ToString("N"),
                nick = string.IsNullOrWhiteSpace(nick) ? "Player" : nick.Trim(),
                createdUtcTicks = now,
                lastLoginUtcTicks = now
            };
        }
    }

    /// <summary>Resultado de uma partida, consumido por SaveManager.ApplyMatchResult.</summary>
    public struct MatchResult
    {
        public int Placement;
        public int Kills;
        public int TotalPlayers;
        public float SurvivalSeconds;
        public bool Victory;

        public int GoldReward(int baseGold = 40)
        {
            var placeBonus = Math.Max(0, TotalPlayers - Placement) * 2;
            return baseGold + placeBonus + Kills * 15 + (Victory ? 200 : 0);
        }

        public int XpReward()
        {
            var placeBonus = Math.Max(0, TotalPlayers - Placement) * 3;
            return 30 + placeBonus + Kills * 20 + (Victory ? 150 : 0);
        }
    }
}
