using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BR.Managers
{
    /// <summary>
    /// Matchmaking simulado (offline). Monta uma "fila" de 50 jogadores
    /// (1 real + 49 bots) apenas para dar o feedback de lobby do Free Fire.
    ///
    /// Nao faz rede, nao usa servidor: e 100% local e roda em ~2.5s.
    /// A lista de nomes/pings existe so para a UI; o MatchManager continua dono dos bots.
    /// </summary>
    public static class FakeMatchmakingService
    {
        public sealed class SimulatedPlayer
        {
            public string Nick;
            public int Level;
            public int PingMs;
            public bool IsLocalPlayer;
        }

        private static readonly string[] Prefixes =
        {
            "Pro", "Sniper", "Lobo", "Ninja", "Fantasma", "Rei", "Dragao", "Sombra",
            "Tigre", "Fenix", "Viking", "Lenda", "Rush", "Alpha", "Bravo", "Zulu"
        };

        private static readonly string[] Suffixes =
        {
            "BR", "FF", "X", "77", "TV", "YT", "God", "OP", "01", "99", "HD", "Elite"
        };

        public static List<SimulatedPlayer> BuildLobby(int totalPlayers, string localNick, int localLevel)
        {
            var players = new List<SimulatedPlayer>(totalPlayers)
            {
                new SimulatedPlayer
                {
                    Nick = string.IsNullOrEmpty(localNick) ? "Você" : localNick,
                    Level = localLevel,
                    PingMs = 12,
                    IsLocalPlayer = true
                }
            };

            for (var i = 1; i < totalPlayers; i++)
            {
                players.Add(new SimulatedPlayer
                {
                    Nick = Prefixes[UnityEngine.Random.Range(0, Prefixes.Length)] +
                           Suffixes[UnityEngine.Random.Range(0, Suffixes.Length)] +
                           UnityEngine.Random.Range(10, 999),
                    Level = UnityEngine.Random.Range(1, 60),
                    PingMs = UnityEngine.Random.Range(18, 160),
                    IsLocalPlayer = false
                });
            }

            return players;
        }

        /// <summary>Coroutine de UI: devolve a lista quando "encontrou" os 50 jogadores.</summary>
        public static IEnumerator Simulate(int totalPlayers, Action<List<SimulatedPlayer>> onComplete)
        {
            var save = SaveManager.Instance;
            var nick = save != null && save.Profile != null ? save.Profile.nick : "Você";
            var level = save != null && save.Profile != null ? save.Profile.level : 1;

            var players = BuildLobby(totalPlayers, nick, level);

            // Progresso visual: nao precisa ser linear, o jogador so precisa sentir a fila.
            var elapsed = 0f;
            const float totalSeconds = 2.6f;

            while (elapsed < totalSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            onComplete?.Invoke(players);
        }
    }
}
