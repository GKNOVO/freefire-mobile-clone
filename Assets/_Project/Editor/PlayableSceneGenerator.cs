using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using BR.Bootstrap;

namespace BR.EditorTools
{
    /// <summary>
    /// Gera as cenas de entrada do prototipo. O conteudo jogavel e montado em runtime
    /// pelos componentes GameBootstrap e ArenaBootstrap.
    /// </summary>
    public static class PlayableSceneGenerator
    {
        private const string ScenesFolder = "Assets/_Project/Scenes";
        private const string BootScenePath = ScenesFolder + "/Boot.unity";
        private const string ArenaScenePath = ScenesFolder + "/Arena.unity";
        private const string GameBootstrapScriptPath = "Assets/_Project/Scripts/Bootstrap/GameBootstrap.cs";
        private const string ArenaBootstrapScriptPath = "Assets/_Project/Scripts/Bootstrap/ArenaBootstrap.cs";

        [MenuItem("BR/Gerar cenas jogaveis")]
        public static void EnsureGenerated()
        {
            EnsureScenesFolder();
            AssetDatabase.Refresh();

            EnsureBootstrapScript("GameBootstrap", GameBootstrapScriptPath, typeof(GameBootstrap), BootScenePath);
            EnsureBootstrapScript("ArenaBootstrap", ArenaBootstrapScriptPath, typeof(ArenaBootstrap), ArenaScenePath);

            AssetDatabase.Refresh();
            RegisterBuildScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("[PlayableSceneGenerator] Cenas Boot.unity e Arena.unity prontas para build.");
        }

        private static void EnsureScenesFolder()
        {
            if (AssetDatabase.IsValidFolder(ScenesFolder)) return;

            var projectFolder = "Assets/_Project";
            if (!AssetDatabase.IsValidFolder(projectFolder))
                throw new DirectoryNotFoundException($"Pasta Unity nao encontrada: {projectFolder}");

            AssetDatabase.CreateFolder(projectFolder, "Scenes");
        }

        private static void EnsureBootstrapScript(string objectName, string scriptPath, Type bootstrapType, string scenePath)
        {
            if (!File.Exists(scriptPath))
                throw new FileNotFoundException($"Script de bootstrap nao encontrado: {scriptPath}");

            var sourceScript = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
            if (sourceScript == null || sourceScript.GetClass() != bootstrapType)
                throw new InvalidOperationException($"Unity nao reconhece o script de bootstrap: {scriptPath}");

            var scriptGuid = AssetDatabase.AssetPathToGUID(scriptPath);
            if (string.IsNullOrEmpty(scriptGuid))
                throw new InvalidOperationException($"Unity nao conseguiu gerar o GUID do script: {scriptPath}");

            var scriptMetaPath = scriptPath + ".meta";
            if (!File.Exists(scriptMetaPath))
                throw new FileNotFoundException($"Unity nao gerou o arquivo meta do script: {scriptMetaPath}");

            var scene = SceneManager.GetSceneByPath(scenePath);
            var openedHere = !scene.IsValid() || !scene.isLoaded;
            var createdScene = openedHere && !File.Exists(scenePath);
            var previousActiveScene = SceneManager.GetActiveScene();

            try
            {
                if (openedHere)
                {
                    scene = createdScene
                        ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive)
                        : EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                }

                if (!SceneManager.SetActiveScene(scene))
                    throw new InvalidOperationException($"Nao foi possivel ativar a cena Unity: {scenePath}");

                var roots = scene.GetRootGameObjects();
                var bootstrap = Array.Find(roots, root => root.name == objectName);
                if (bootstrap == null)
                    bootstrap = Array.Find(roots, root => root.name == "GameBootstrap" || root.name == "ArenaBootstrap");

                if (bootstrap == null)
                {
                    bootstrap = new GameObject(objectName);
                    SceneManager.MoveGameObjectToScene(bootstrap, scene);
                }

                bootstrap.name = objectName;
                if (bootstrap.GetComponent(bootstrapType) == null)
                    bootstrap.AddComponent(bootstrapType);

                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root != bootstrap && (root.name == "BuildPlaceholder" || root.name == "Main Camera"))
                        UnityEngine.Object.DestroyImmediate(root);
                }

                if (!EditorSceneManager.SaveScene(scene, scenePath))
                    throw new IOException($"Nao foi possivel salvar a cena Unity: {scenePath}");
            }
            finally
            {
                if (openedHere && !createdScene && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);

                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
            }
        }

        private static void RegisterBuildScenes()
        {
            var bootGuid = AssetDatabase.AssetPathToGUID(BootScenePath);
            var arenaGuid = AssetDatabase.AssetPathToGUID(ArenaScenePath);
            if (string.IsNullOrEmpty(bootGuid) || string.IsNullOrEmpty(arenaGuid))
                throw new FileNotFoundException("Unity nao conseguiu localizar as cenas Boot/Arena.");

            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(BootScenePath, true),
                new EditorBuildSettingsScene(ArenaScenePath, true)
            };

            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
