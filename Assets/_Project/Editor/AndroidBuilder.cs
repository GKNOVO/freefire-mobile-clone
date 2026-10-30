using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BR.EditorTools
{
    /// <summary>
    /// Ponto de entrada de build Android usado pelo GitHub Actions/GameCI.
    /// Gera as cenas jogaveis e compila o APK em build/Android/BRMobile.apk.
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

        [MenuItem("BR/Build Android APK (CI)")]
        public static void BuildAndroid()
        {
            try
            {
                EnsureAndroidTarget();
                ConfigurePlayerSettings();
                PlayableSceneGenerator.EnsureGenerated();

                if (!File.Exists(BootScenePath) || !File.Exists(ArenaScenePath))
                    throw new FileNotFoundException("As cenas Boot/Arena nao foram geradas corretamente.");

                var scenes = new[] { BootScenePath, ArenaScenePath };
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
                var summary = report.summary;

                Debug.Log($"[AndroidBuilder] Resultado: {summary.result} | " +
                          $"erros: {summary.totalErrors} | avisos: {summary.totalWarnings} | " +
                          $"tamanho: {summary.totalSize / (1024f * 1024f):F1} MB | " +
                          $"tempo: {summary.totalTime.TotalMinutes:F1} min");

                if (summary.result != BuildResult.Succeeded)
                {
                    throw new Exception(
                        $"Build Android falhou: {summary.result} ({summary.totalErrors} erro(s)). " +
                        "Veja os erros acima no log do Unity.");
                }

                Debug.Log($"[AndroidBuilder] APK pronto: {outputPath}");
            }
            catch (Exception e)
            {
                Debug.LogError("[AndroidBuilder] " + e);
                throw;
            }
        }

        private static void EnsureAndroidTarget()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android) return;

            Debug.Log("[AndroidBuilder] Alternando build target para Android...");
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                throw new InvalidOperationException("Nao foi possivel selecionar Android como build target.");
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
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

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

        private static string ResolveOutputPath()
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var custom = GetCommandLineArg("-customBuildPath")
                         ?? GetCommandLineArg("-buildPath")
                         ?? GetCommandLineArg("-outputPath");

            var folder = string.IsNullOrEmpty(custom)
                ? Path.Combine(projectRoot, BuildFolder)
                : (custom.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetDirectoryName(custom)
                    : custom);

            if (string.IsNullOrEmpty(folder)) folder = Path.Combine(projectRoot, BuildFolder);
            Directory.CreateDirectory(folder);

            if (!string.IsNullOrEmpty(custom) && custom.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                return custom;

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
