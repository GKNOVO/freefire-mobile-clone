using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BR.EditorTools
{
    /// <summary>
    /// Ponto de entrada de build Android usado pelo GitHub Actions/GameCI
    /// (buildMethod: BR.EditorTools.AndroidBuilder.BuildAndroid).
    ///
    /// Regras para nao gerar exit code 1 por motivo bobo em batchmode:
    /// - troca de build target e geracao de cenas sao best-effort (log de aviso, sem throw);
    /// - o job so falha quando o BuildPipeline realmente falha, com o motivo no log.
    /// </summary>
    public static class AndroidBuilder
    {
        private const string BuildFolder = "build/Android";
        private const string ApkName = "BRMobile.apk";
        private const string BootScenePath = "Assets/_Project/Scenes/Boot.unity";
        private const string ArenaScenePath = "Assets/_Project/Scenes/Arena.unity";

        private const string BundleId = "com.brstudio.battleroyale";
        private const string ProductName = "Battle Royale Mobile";
        private const string CompanyName = "BR Studio";
        private const string Version = "0.1.0";

        private const int MinSdk = 24;
        private const int TargetSdk = 34;

        [MenuItem("BR/Build Android APK (CI)")]
        public static void BuildAndroid()
        {
            try
            {
                LogEnvironment();
                EnsureAndroidTarget();
                ConfigurePlayerSettings();
                EnsureScenes();

                var scenes = CollectScenes();
                if (scenes.Length == 0)
                    throw new InvalidOperationException(
                        $"Nenhuma cena encontrada em {BootScenePath} / {ArenaScenePath}. " +
                        "Rode BR > Gerar cenas jogaveis no Editor e commite as cenas.");

                var outputPath = ResolveOutputPath();

                Debug.Log($"[AndroidBuilder] Cenas: {string.Join(", ", scenes)}");
                Debug.Log($"[AndroidBuilder] Saida: {outputPath}");

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = outputPath,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.None
                };

                var report = BuildPipeline.BuildPlayer(options);
                var summary = report != null ? report.summary : default(BuildSummary);

                Debug.Log($"[AndroidBuilder] Resultado: {summary.result} | " +
                          $"erros: {summary.totalErrors} | avisos: {summary.totalWarnings} | " +
                          $"tamanho: {summary.totalSize / (1024f * 1024f):F1} MB | " +
                          $"tempo: {summary.totalTime.TotalMinutes:F1} min");

                if (report == null || summary.result != BuildResult.Succeeded)
                {
                    throw new Exception(
                        $"Build Android falhou: {summary.result} ({summary.totalErrors} erro(s)). " +
                        "Os erros de compilacao/bundling estao acima no log do Unity.");
                }

                if (!File.Exists(outputPath))
                    throw new FileNotFoundException($"O build retornou sucesso mas o APK nao existe: {outputPath}");

                Debug.Log($"[AndroidBuilder] APK pronto: {outputPath} " +
                          $"({new FileInfo(outputPath).Length / (1024f * 1024f):F1} MB)");
            }
            catch (Exception e)
            {
                // Sempre relanca: o GameCI traduz isso em exit code != 0 (nunca falha silenciosa).
                Debug.LogError("[AndroidBuilder] FALHA: " + e);
                throw;
            }
        }

        private static void LogEnvironment()
        {
            Debug.Log($"[AndroidBuilder] Unity {Application.unityVersion} | batchmode: {Application.isBatchMode} | " +
                      $"alvo ativo: {EditorUserBuildSettings.activeBuildTarget}");
        }

        /// <summary>
        /// Em batchmode o editor ja abre com -buildTarget Android; a troca e apenas um
        /// reforco. Se falhar, NAO aborta: o BuildPlayerOptions ja pede Android.
        /// </summary>
        private static void EnsureAndroidTarget()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android) return;

            Debug.Log("[AndroidBuilder] Alternando build target para Android...");
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                Debug.LogWarning("[AndroidBuilder] Nao foi possivel trocar o build target ativo; " +
                                 "seguindo com BuildPlayerOptions.target = Android.");
        }

        private static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, BundleId);
            PlayerSettings.bundleVersion = Version;
            PlayerSettings.Android.bundleVersionCode = 1;

            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Android, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)MinSdk;
            // API fixa (34): o entrypoint do GameCI le AndroidTargetSdkVersion do
            // ProjectSettings.asset e instala "platforms;android-<valor>" via sdkmanager.
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)TargetSdk;

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.Android.useCustomKeystore = false;
            QualitySettings.vSyncCount = 0;
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// As cenas sao versionadas; a geracao apenas conserta o conteudo. Uma falha aqui
        /// nao deve derrubar o job se as cenas ja existem no repositorio.
        /// </summary>
        private static void EnsureScenes()
        {
            var scenesExistem = File.Exists(BootScenePath) && File.Exists(ArenaScenePath);

            try
            {
                PlayableSceneGenerator.EnsureGenerated();
            }
            catch (Exception e)
            {
                if (!scenesExistem) throw;
                Debug.LogWarning("[AndroidBuilder] Geracao de cenas falhou, seguindo com as cenas do repositorio: " + e.Message);
            }

            if (File.Exists(BootScenePath) && File.Exists(ArenaScenePath)) return;

            throw new FileNotFoundException(
                $"Cenas ausentes: {BootScenePath} / {ArenaScenePath}. " +
                "Rode BR > Gerar cenas jogaveis no Editor e commite as cenas.");
        }

        private static string[] CollectScenes()
        {
            var scenes = new List<string>(2);
            if (File.Exists(BootScenePath)) scenes.Add(BootScenePath);
            if (File.Exists(ArenaScenePath)) scenes.Add(ArenaScenePath);
            return scenes.ToArray();
        }

        /// <summary>
        /// Honra o caminho pedido pelo GameCI (-customBuildPath/-buildPath/-outputPath);
        /// sem argumento, grava em build/Android/BRMobile.apk (mesmo caminho do GameCI).
        /// </summary>
        private static string ResolveOutputPath()
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var custom = GetCommandLineArg("-customBuildPath")
                         ?? GetCommandLineArg("-buildPath")
                         ?? GetCommandLineArg("-outputPath");

            if (!string.IsNullOrEmpty(custom) && custom.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
            {
                var apkFolder = Path.GetDirectoryName(custom);
                if (!string.IsNullOrEmpty(apkFolder)) Directory.CreateDirectory(apkFolder);
                return custom;
            }

            var folder = string.IsNullOrEmpty(custom)
                ? Path.Combine(projectRoot, BuildFolder)
                : (Path.IsPathRooted(custom) ? custom : Path.Combine(projectRoot, custom));

            Directory.CreateDirectory(folder);
            return Path.Combine(folder, ApkName);
        }

        private static string GetCommandLineArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];

            return null;
        }
    }
}
