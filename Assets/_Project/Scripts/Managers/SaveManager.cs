using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using BR.Core;
using BR.Data;

namespace BR.Managers
{
    /// <summary>
    /// Save/Load offline: JSON + AES-256-CBC + HMAC-SHA256 (anti-tamper basico).
    ///
    /// Chave derivada de SystemInfo.deviceUniqueIdentifier + salt fixo do app.
    /// Isso NAO e seguranca de servidor (um aparelho rooted acessa tudo), mas:
    ///  - evita que o jogador edite o JSON no bloco de notas;
    ///  - mantem o save invalido de ser lido se copiado para outro aparelho.
    /// Para um jogo com IAP/rank global o passo seguinte e migrar valores
    /// autoritativos (gold/diamonds) para um backend.
    /// </summary>
    public sealed class SaveManager : MonoSingleton<SaveManager>
    {
        private const string FileName = "profile.sav";
        private const string BackupName = "profile.bak";
        private const string Salt = "BR_OFFLINE_v1_9f2c";

        private byte[] _key;
        private byte[] _hmacKey;

        public PlayerProfile Profile { get; private set; }
        public bool IsLoaded => Profile != null;

        protected override void OnSingletonAwake()
        {
            DeriveKeys();
            Load();
        }

        private void DeriveKeys()
        {
            var device = SystemInfo.deviceUniqueIdentifier ?? "unknown-device";
            using (var kdf = new Rfc2898DeriveBytes(device + Salt, Encoding.UTF8.GetBytes(Salt), 10000, HashAlgorithmName.SHA256))
            {
                _key = kdf.GetBytes(32);
                _hmacKey = kdf.GetBytes(32);
            }
        }

        private string SavePath => Path.Combine(Application.persistentDataPath, FileName);
        private string BackupPath => Path.Combine(Application.persistentDataPath, BackupName);

        // ------------------------------------------------------------ Load

        public PlayerProfile Load()
        {
            var loaded = TryRead(SavePath) ?? TryRead(BackupPath);
            if (loaded == null)
            {
                loaded = PlayerProfile.CreateNew("Player" + UnityEngine.Random.Range(1000, 9999));
                Debug.Log("[SaveManager] Nenhum save valido encontrado. Perfil novo criado.");
            }

            loaded.lastLoginUtcTicks = DateTime.UtcNow.Ticks;
            Profile = loaded;
            Save();
            PublishProfile();
            return Profile;
        }

        private PlayerProfile TryRead(string path)
        {
            if (!File.Exists(path)) return null;

            try
            {
                var blob = Convert.FromBase64String(File.ReadAllText(path));
                if (blob.Length < 48) return null;

                var iv = new byte[16];
                var hmac = new byte[32];
                var cipher = new byte[blob.Length - 48];

                Buffer.BlockCopy(blob, 0, iv, 0, 16);
                Buffer.BlockCopy(blob, 16, hmac, 0, 32);
                Buffer.BlockCopy(blob, 48, cipher, 0, cipher.Length);

                using (var mac = new HMACSHA256(_hmacKey))
                {
                    var payload = new byte[16 + cipher.Length];
                    Buffer.BlockCopy(iv, 0, payload, 0, 16);
                    Buffer.BlockCopy(cipher, 0, payload, 16, cipher.Length);
                    var expected = mac.ComputeHash(payload);
                    if (!CryptographicOperations.FixedTimeEquals(expected, hmac))
                    {
                        Debug.LogWarning($"[SaveManager] HMAC invalido em {path}.");
                        return null;
                    }
                }

                using (var aes = Aes.Create())
                {
                    aes.Key = _key;
                    aes.IV = iv;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    using (var decryptor = aes.CreateDecryptor())
                    {
                        var plain = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
                        var json = Encoding.UTF8.GetString(plain);
                        return JsonUtility.FromJson<PlayerProfile>(json);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveManager] Falha ao ler {path}: {e.Message}");
                return null;
            }
        }

        // ------------------------------------------------------------ Save

        public void Save()
        {
            if (Profile == null) return;

            try
            {
                var json = JsonUtility.ToJson(Profile);
                var bytes = Encoding.UTF8.GetBytes(json);

                using (var aes = Aes.Create())
                {
                    aes.Key = _key;
                    aes.GenerateIV();
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    byte[] cipher;
                    using (var encryptor = aes.CreateEncryptor())
                        cipher = encryptor.TransformFinalBlock(bytes, 0, bytes.Length);

                    byte[] hmac;
                    using (var mac = new HMACSHA256(_hmacKey))
                    {
                        var payload = new byte[aes.IV.Length + cipher.Length];
                        Buffer.BlockCopy(aes.IV, 0, payload, 0, aes.IV.Length);
                        Buffer.BlockCopy(cipher, 0, payload, aes.IV.Length, cipher.Length);
                        hmac = mac.ComputeHash(payload);
                    }

                    var blob = new byte[aes.IV.Length + hmac.Length + cipher.Length];
                    var offset = 0;
                    Buffer.BlockCopy(aes.IV, 0, blob, offset, aes.IV.Length); offset += aes.IV.Length;
                    Buffer.BlockCopy(hmac, 0, blob, offset, hmac.Length); offset += hmac.Length;
                    Buffer.BlockCopy(cipher, 0, blob, offset, cipher.Length);

                    WriteAtomic(SavePath, Convert.ToBase64String(blob));
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveManager] Falha ao salvar: {e.Message}");
            }
        }

        /// <summary>Escreve em .tmp e troca -> evita save corrompido se o app morrer no meio.</summary>
        private void WriteAtomic(string target, string contents)
        {
            var tmp = target + ".tmp";

            if (File.Exists(target))
            {
                try { File.Copy(target, BackupPath, true); }
                catch (Exception e) { Debug.LogWarning($"[SaveManager] Backup falhou: {e.Message}"); }
            }

            File.WriteAllText(tmp, contents);

            if (File.Exists(target)) File.Delete(target);
            File.Move(tmp, target);
        }

        // ------------------------------------------------------------ Gameplay

        public void ApplyMatchResult(in MatchResult result, bool doubledByAd)
        {
            if (Profile == null) return;

            var gold = result.GoldReward();
            var xp = result.XpReward();

            if (doubledByAd)
            {
                gold *= 2;
                xp = Mathf.RoundToInt(xp * 1.5f);
            }

            Profile.gold += gold;
            Profile.AddXp(xp);
            Profile.matchesPlayed++;
            Profile.totalKills += result.Kills;
            Profile.bestPlacement = Math.Min(Profile.bestPlacement, result.Placement);
            if (result.Victory) Profile.wins++;

            Save();
            PublishProfile();
        }

        /// <summary>Bonus de 2x ao assistir o rewarded na tela de resultados (nao conta stats duas vezes).</summary>
        public void GrantAdRewardBonus(in MatchResult result)
        {
            if (Profile == null) return;

            Profile.gold += result.GoldReward();
            Profile.AddXp(result.XpReward());
            Save();
            PublishProfile();
        }

        public void AddCurrency(int gold, int diamonds = 0)
        {
            if (Profile == null) return;

            Profile.gold += Mathf.Max(0, gold);
            Profile.diamonds += Mathf.Max(0, diamonds);
            Save();
            PublishProfile();
        }

        public bool TrySpendGold(int amount)
        {
            if (Profile == null || amount < 0 || Profile.gold < amount) return false;
            Profile.gold -= amount;
            Save();
            PublishProfile();
            return true;
        }

        private void PublishProfile()
        {
            EventBus.Publish(new ProfileChanged
            {
                Level = Profile.level,
                Xp = Profile.xp,
                Gold = Profile.gold,
                Diamonds = Profile.diamonds
            });
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) Save();
        }

        private void OnApplicationQuit() => Save();
    }
}
