using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BR.EditorTools
{
    /// <summary>
    /// Ponto de entrada de build Android (usado pelo GitHub Actions/GameCI).
    ///
    /// - Configura Player Settings (bundle id, IL2CPP, ARM64, minSdk, paisagem).
    /// - Usa as cenas de Assets/_Project/Scenes (Boot primeiro, depois Arena).
    /// - Se NÃO existir nenhuma cena, gera uma cena temporária com BuildPlaceholder
    ///   para que o pipeline completo (licença → build → APK → instalação) possa ser
    ///   validado antes do jogo estar montado.
    /// - Saída: build/Android/BRMobile.apk (pasta padrão que o GameCI publica como artifact).
    ///
    /// Também disponível no menu do Editor: BR > Build Android APK (CI).
    /// </summary>
    public static class AndroidBuilder
    {
        private const string BuildFolder = "build/Android";
        private const string ApkName = "BRMobile.apk";
        private const string ScenesFolder = "Assets/_Project/Scenes";
        private const string PlaceholderScenePath = ScenesFolder + "/_BuildPlaceholder.unity";

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

                // Gera Boot.unity/Arena.unity se ainda não existirem (primeiro build em CI).
                PlayableSceneGenerator.EnsureGenerated();

                var scenes = CollectScenes();
                var outputPath = ResolveOutputPath();

                Debug.Log($"[AndroidBuilder] Cenas: {string.Join(", ", scenes)}");
                Debug.Log($"[AndroidBuilder] Saída: {outputPath}");

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
                // Relançar garante exit code != 0 para o GitHub Actions falhar o job.
                Debug.LogError("[AndroidBuilder] " + e);
                throw;
            }
        }

        // ------------------------------------------------------------ Configuração

        private static void EnsureAndroidTarget()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android) return;

            Debug.Log("[AndroidBuilder] Alternando build target para Android...");
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                Debug.LogWarning("[AndroidBuilder] Não foi possível alternar o target; tentando build mesmo assim.");
        }

        private static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, BundleId);
            PlayerSettings.bundleVersion = Version;
            PlayerSettings.Android.bundleVersionCode = 1;

            // Galaxy S24 / Android moderno: arm64 + IL2CPP é obrigatório para performance e para a Play Store.
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Android, ApiCompatibilityLevel.NET_Standard_2_0);

            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

            // Jogo em paisagem, sem auto-rotate vertical.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            // APK (não AAB) e assinatura debug: instala direto no aparelho.
            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.Android.useCustomKeystore = false;

            // Sempre 60 FPS; vSync desligado no mobile.
            QualitySettings.vSyncCount = 0;

            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------ Cenas

        private static string[] CollectScenes()
        {
            var scenes = new List<string>();

            if (AssetDatabase.IsValidFolder(ScenesFolder))
            {
                var guids = AssetDatabase.FindAssets("t:Scene", new[] { ScenesFolder });
                for (var i = 0; i < guids.Length; i++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (!path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) continue;
                    if (path.EndsWith("_BuildPlaceholder.unity", StringComparison.OrdinalIgnoreCase)) continue;
                    scenes.Add(path);
                }
            }

            scenes.Sort(SceneOrder);

            if (scenes.Count > 0) return scenes.ToArray();

            Debug.LogWarning(
                "[AndroidBuilder] Nenhuma cena encontrada em " + ScenesFolder + ". " +
                "Gerando cena de VALIDAÇÃO (BuildPlaceholder). " +
                "Crie Boot.unity e Arena.unity para gerar o APK do jogo de verdade.");

            scenes.Add(CreatePlaceholderScene());
            return scenes.ToArray();
        }

        private static int SceneOrder(string a, string b)
        {
            var sa = ScenePriority(a);
            var sb = ScenePriority(b);
            if (sa != sb) return sa.CompareTo(sb);
            return string.CompareOrdinal(a, b);
        }

        private static int ScenePriority(string path)
        {
            if (path.EndsWith("Boot.unity", StringComparison.OrdinalIgnoreCase)) return 0;
            if (path.EndsWith("Arena.unity", StringComparison.OrdinalIgnoreCase)) return 1;
            return 2;
        }

        private static string CreatePlaceholderScene()
        {
            if (!AssetDatabase.IsValidFolder(ScenesFolder))
                Directory.CreateDirectory(ScenesFolder);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Fundo escuro para o texto IMGUI ficar legível.
            var cameraGo = new GameObject("Main Camera");
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.07f, 0.1f, 1f);
            cameraGo.tag = "MainCamera";
            cameraGo.transform.position = new Vector3(0f, 1f, -10f);

            var placeholder = new GameObject("BuildPlaceholder");
            placeholder.AddComponent<BR.Core.BuildPlaceholder>();

            EditorSceneManager.SaveScene(scene, PlaceholderScenePath);
            AssetDatabase.Refresh();

            return PlaceholderScenePath;
        }

        // ------------------------------------------------------------ Saída

        private static string ResolveOutputPath()
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;

            // GameCI pode passar um caminho customizado via -customBuildPath.
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
