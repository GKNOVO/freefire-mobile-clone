using UnityEngine;

namespace BR.Core
{
    public enum MatchPhase
    {
        None = 0,
        WaitingIsland = 1,   // Ilha de espera (30-40s)
        PlaneFlight = 2,     // Avião em rota reta
        Freefall = 3,        // Queda livre + paraquedas
        Active = 4,          // Partida ativa (zona fechando)
        Ended = 5
    }

    public enum AppScreen
    {
        Splash = 0,
        Login = 1,
        Lobby = 2,
        Matchmaking = 3,
        Hud = 4,
        Results = 5
    }

    // ---------------------------------------------------------------- Match

    public struct MatchPhaseChanged
    {
        public MatchPhase Previous;
        public MatchPhase Current;
        public float Duration;
    }

    public struct AliveCountChanged
    {
        public int Alive;
        public int Total;
    }

    public struct KillFeedMessage
    {
        public string Killer;
        public string Victim;
        public string WeaponId;
        public bool KillerIsLocalPlayer;
    }

    public struct MatchEnded
    {
        public int Placement;          // 1 = booyah
        public int Kills;
        public int SurvivorsAlive;
        public bool LocalPlayerWon;
    }

    // ---------------------------------------------------------------- Player

    public struct PlayerHealthChanged
    {
        public float Health;
        public float MaxHealth;
        public float Armor;
        public float MaxArmor;
    }

    public struct PlayerDied
    {
        public string KillerName;
        public bool IsLocalPlayer;
    }

    public struct WeaponAmmoChanged
    {
        public string WeaponId;
        public int Magazine;
        public int Reserve;
        public bool IsReloading;
    }

    public struct LootPicked
    {
        public string ItemId;
        public string DisplayName;
        public int Amount;
    }

    public struct DamageNumberRequested
    {
        public Vector3 WorldPosition;
        public float Amount;
        public bool IsHeadshot;
    }

    // ---------------------------------------------------------------- Zone

    public struct ZoneUpdated
    {
        public Vector3 Center;
        public float Radius;
        public float NextRadius;
        public int PhaseIndex;
    }

    public struct ZoneTimerTick
    {
        public float SecondsToNextEvent;
        public bool IsShrinking;
        public float DamagePerSecond;
        public bool LocalPlayerInside;
    }

    // ---------------------------------------------------------------- Meta

    public struct ProfileChanged
    {
        public int Level;
        public int Xp;
        public int Gold;
        public int Diamonds;
    }

    public struct RewardedAdResult
    {
        public string Placement;
        public bool Completed;   // false = usuario fechou / sem fill
    }
}
