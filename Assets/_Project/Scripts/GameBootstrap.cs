using UnityEngine;
using BR.Core;
using BR.Managers;
using BR.Player;
using BR.Utils;

namespace BR.Bootstrap
{
    /// <summary>
    /// Entrada da cena Boot. Cria tudo que é persistente:
    /// managers (pool, save, audio, ads, UI, quality), canvas com as 6 telas e o GameManager.
    ///
    /// A cena Boot é gerada pelo Editor (BR > Gerar cenas jogáveis) e contém apenas
    /// este componente: nenhuma referência de Inspector é necessária.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            if (FindFirstObjectByType<GameManager>() != null) return;

            RuntimeContent.Initialize();

            new GameObject("[ObjectPoolManager]").AddComponent<ObjectPoolManager>();
            new GameObject("[SaveManager]").AddComponent<SaveManager>();
            new GameObject("[AudioManager]").AddComponent<AudioManager>();
            new GameObject("[AdsManager]").AddComponent<AdsManager>();
            new GameObject("[AdaptiveQuality]").AddComponent<AdaptiveQuality>();

            var ui = new GameObject("[UIManager]").AddComponent<UIManager>();

            RuntimeUI.CreateEventSystem();

            var canvas = RuntimeUI.CreateCanvas("[UICanvas]", 10);
            RuntimeScreens.Build(canvas.transform, ui.RegisterScreen);

            var game = new GameObject("[GameManager]").AddComponent<GameManager>();
            game.ConfigureForRuntime(RuntimeContent.Config, "Arena");

            if (Application.isEditor)
                new GameObject("[DesktopInput]").AddComponent<DesktopInputDriver>();

            Debug.Log("[GameBootstrap] Managers, telas e GameManager prontos.");
        }
    }
}
