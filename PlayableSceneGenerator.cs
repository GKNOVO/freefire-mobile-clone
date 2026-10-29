using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BR.EditorTools
{
    /// <summary>
    /// Gera as cenas jogáveis por código:
    ///   Boot.unity  -> GameObject com GameBootstrap (managers persistentes + UI)
    ///   Arena.unity -> GameObject com ArenaBootstrap (mapa gray-box + player + bots)
    ///
    /// As cenas não têm referências de Inspector: todo o conteúdo é criado em runtime,
    /// então elas continuam válidas mesmo que prefabs/assets mudem.
    ///
    /// O AndroidBuilder chama EnsureGenerated() antes do build, então o primeiro build
    /// no GitHub Actions já produz o APK jogável.
    /// </summary>
    public static class PlayableSceneGenerator
    {
        public const string BootPath = "Assets/_Project/Scenes/Boot.unity";
        public const string ArenaPath = "Assets/_Project/Scenes/Arena.unity";

        [MenuItem("BR/Gerar cenas jogáveis (Boot + Arena)")]
        public static void Generate()
        {
            EnsureFolder("Assets/_Project/Scenes");

            var bootScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var bootRoot = new GameObject("Boot");
            bootRoot.AddComponent<BR.Bootstrap.GameBootstrap>();
            EditorSceneManager.SaveScene(bootScene, BootPath);

            var arenaScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var arenaRoot = new GameObject("Arena");
            arenaRoot.AddComponent<BR.Bootstrap.ArenaBootstrap>();
            EditorSceneManager.SaveScene(arenaScene, ArenaPath);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BootPath, true),
                new EditorBuildSettingsScene(ArenaPath, true)
            };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EnsureAlwaysIncludedShaders("Standard", "Legacy Shaders/Diffuse", "Sprites/Default", "UI/Default");

            Debug.Log("[PlayableSceneGenerator] Boot.unity + Arena.unity geradas e configuradas no Build Settings.");
        }

        /// <summary>Chamado pelo AndroidBuilder: gera somente se as cenas não existirem.</summary>
        public static void EnsureGenerated()
        {
            var boot = AssetDatabase.LoadAssetAtPath<SceneAsset>(BootPath);
            var arena = AssetDatabase.LoadAssetAtPath<SceneAsset>(ArenaPath);

            if (boot == null || arena == null) Generate();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            var parent = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(parent)) return;

            parent = parent.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        /// <summary>
        /// Materiais são criados por código em runtime; sem isso o shader Standard pode
        /// ser removido pelo stripping do build e os objetos ficariam magenta/invisíveis.
        /// </summary>
        private static void EnsureAlwaysIncludedShaders(params string[] shaderNames)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (assets == null || assets.Length == 0) return;

            var serialized = new SerializedObject(assets[0]);
            var shaders = serialized.FindProperty("m_AlwaysIncludedShaders");
            if (shaders == null) return;

            foreach (var shaderName in shaderNames)
            {
                var shader = Shader.Find(shaderName);
                if (shader == null) continue;

                var exists = false;
                for (var i = 0; i < shaders.arraySize; i++)
                {
                    if (shaders.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                    {
                        exists = true;
                        break;
                    }
                }

                if (exists) continue;

                shaders.InsertArrayElementAtIndex(shaders.arraySize);
                shaders.GetArrayElementAtIndex(shaders.arraySize - 1).objectReferenceValue = shader;
            }

            serialized.ApplyModifiedProperties();
        }
    }
}
