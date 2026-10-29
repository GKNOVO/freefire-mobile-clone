using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BR.Core
{
    /// <summary>
    /// Carregamento assincrono e aditivo de cenas.
    /// O Boot/Main (UI + managers) fica sempre carregada; a Arena entra e sai por partida.
    /// Isso evita recarregar o lobby inteiro a cada match (mobile: menos hitch).
    /// </summary>
    public static class SceneLoader
    {
        public static bool IsLoaded(string sceneName)
        {
            var scene = SceneManager.GetSceneByName(sceneName);
            return scene.IsValid() && scene.isLoaded;
        }

        public static IEnumerator LoadAdditiveAsync(string sceneName, System.Action<float> onProgress = null)
        {
            if (string.IsNullOrEmpty(sceneName) || IsLoaded(sceneName)) yield break;

            var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (op == null)
            {
                Debug.LogError($"[SceneLoader] Cena '{sceneName}' nao encontrada no Build Settings.");
                yield break;
            }

            op.allowSceneActivation = false;

            while (op.progress < 0.9f)
            {
                onProgress?.Invoke(Mathf.Clamp01(op.progress / 0.9f));
                yield return null;
            }

            onProgress?.Invoke(1f);
            op.allowSceneActivation = true;

            while (!op.isDone) yield return null;
        }

        public static IEnumerator UnloadAsync(string sceneName)
        {
            if (!IsLoaded(sceneName)) yield break;

            var op = SceneManager.UnloadSceneAsync(sceneName);
            while (op != null && !op.isDone) yield return null;

            // Devolve memoria de assets nao usados (texturas/meshes da arena).
            yield return Resources.UnloadUnusedAssets();
        }

        /// <summary>Variante com fade simples para transicoes de tela.</summary>
        public static IEnumerator LoadAdditiveWithFade(string sceneName, Managers.UIManager ui, System.Action<float> onProgress = null)
        {
            if (ui != null) yield return ui.FadeTo(1f, 0.25f);
            yield return LoadAdditiveAsync(sceneName, onProgress);
            if (ui != null) yield return ui.FadeTo(0f, 0.25f);
        }
    }
}
