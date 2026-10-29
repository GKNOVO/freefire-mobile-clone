using UnityEngine;

namespace BR.Core
{
    /// <summary>
    /// Tela de validação usada SOMENTE quando o build roda sem nenhuma cena real.
    ///
    /// Objetivo: permitir validar o pipeline (licença, GameCI, IL2CPP, APK, instalação
    /// no aparelho) antes de existirem as cenas Boot/Arena. O build script
    /// (BR.EditorTools.AndroidBuilder) cria uma cena temporária com este componente
    /// se não encontrar nenhuma cena em Assets/_Project/Scenes.
    ///
    /// Assim que Boot.unity/Arena.unity existirem, esta tela deixa de ser usada.
    /// Usa IMGUI de propósito: não depende de Canvas, TMP, fontes ou prefabs.
    /// </summary>
    public sealed class BuildPlaceholder : MonoBehaviour
    {
        private void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
        }

        private void OnGUI()
        {
            var width = Mathf.Min(Screen.width - 48f, 940f);
            var height = Mathf.Min(Screen.height - 48f, 400f);
            var rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

            GUI.Box(rect, "BATTLE ROYALE MOBILE — BUILD DE VALIDAÇÃO");

            GUILayout.BeginArea(new Rect(rect.x + 28f, rect.y + 56f, rect.width - 56f, rect.height - 80f));

            GUILayout.Label("OK: o projeto compilou e o APK foi gerado pelo GitHub Actions.");
            GUILayout.Space(10f);
            GUILayout.Label("Este app ainda NÃO é o jogo: as cenas Boot.unity e Arena.unity");
            GUILayout.Label("(e os prefabs) precisam ser criadas no Unity Editor.");
            GUILayout.Label("Checklist completo: docs/ARCHITECTURE.md, seção 6.");
            GUILayout.Space(14f);
            GUILayout.Label($"Unity: {Application.unityVersion}");
            GUILayout.Label($"Produto: {Application.productName}  |  Versão: {Application.version}");
            GUILayout.Label($"Plataforma: {Application.platform}  |  Tela: {Screen.width}x{Screen.height}");
            GUILayout.Label($"Scripting backend: {(Application.isEditor ? "Editor" : "IL2CPP/ARM64")}");

            GUILayout.EndArea();
        }
    }
}
