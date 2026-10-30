# Battle Royale Mobile (offline-first) — C# para Unity 2022.3 LTS

Protótipo jogável de Battle Royale mobile inspirado no Free Fire: **1 player + 49 bots**,
avião, paraquedas, zona que fecha, loot, combate, progressão local e rewarded ads.
Sem servidor, sem rede.

> Arquitetura completa, diagramas de estado, setup no Unity, orçamento de performance
> e roadmap: **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)**

## O que já está implementado (código)

| Sistema | Arquivo |
|---|---|
| Fluxo do app (Splash → Lobby → Partida → Resultado) | `Core/GameManager.cs` |
| FSM da partida (ilha, avião, queda, ativo, fim) | `Managers/MatchManager.cs` |
| Config data-driven | `Managers/MatchConfig.cs` |
| Avião em rota reta | `Managers/PlaneDirector.cs` |
| Zona que fecha + dano progressivo | `Managers/ZoneManager.cs` |
| Spawn de loot + reserva por bot | `Managers/LootManager.cs` |
| Pool global | `Managers/ObjectPoolManager.cs` |
| Save criptografado (AES + HMAC) | `Managers/SaveManager.cs` |
| Rewarded ads (AdMob / simulado) | `Managers/AdsManager.cs` |
| Player: queda, paraquedas, movimento, knockback | `Player/PlayerController.cs` |
| Câmera 3ª pessoa com colisão | `Player/ThirdPersonCamera.cs` |
| IA dos 49 bots (FSM + LOD) | `Bots/BotAI.cs`, `Bots/BotManager.cs` |
| Armas, dano, headshot, colete | `Combat/*.cs` |
| Inventário, itens, pickup | `Inventory/*.cs` |
| UI (6 telas + HUD + minimapa) | `UI/*.cs`, `Managers/UIManager.cs` |
| Matchmaking simulado | `Networking/FakeMatchmakingService.cs` |
| Qualidade adaptativa | `Utils/AdaptiveQuality.cs` |

## Como abrir

1. Crie um projeto Unity **2022.3 LTS (URP, 3D)**.
2. Copie a pasta `Assets/_Project` para o `Assets/` do projeto (este repositório já está
   no formato `Assets/...`, então basta abrir a pasta como projeto Unity).
3. Siga o **checklist de cenas/prefabs** em `docs/ARCHITECTURE.md`, seção 6.
4. Instale `com.unity.textmeshpro` (TMP Essentials) — a UI usa `TMP_Text`.
5. Play no Editor: `DesktopInputDriver` já mapeia WASD + mouse (esquerdo atira, E pega loot,
   Espaço pula do avião, F abre o paraquedas, R recarrega).

## Estrutura de código

Ver seção 3 do `docs/ARCHITECTURE.md`.

## Compilar APK sem PC (GitHub Actions)

O repositório inclui `.github/workflows/build.yml` (GameCI + Unity 2022.3 + Android/IL2CPP/ARM64)
usando o script de build **padrão** do GameCI — as Player Settings ficam em
`ProjectSettings/ProjectSettings.asset` e as cenas em `ProjectSettings/EditorBuildSettings.asset`.
O APK sai em `build/Android/BRMobile.apk`.

Passo a passo pelo celular, secrets de licença e instalação no Galaxy S24:
**[docs/BUILD_ANDROID.md](docs/BUILD_ANDROID.md)**.

As cenas `Boot` e `Arena` são **geradas por código** (`Assets/_Project/Editor/PlayableSceneGenerator.cs`):
o primeiro build no GitHub Actions já produz um APK jogável — mapa gray-box, player, 49 bots,
loot, zona que fecha e HUD touch completa. Nenhum passo manual no Editor é necessário.

## Não incluído (precisa de arte/Editor)

- Cenas, prefabs, animações, modelos, sons e texturas.
- Assets `ScriptableObject` (armas/itens/config) — os menus `Create > BR/...` já existem.
- Integração AdMob real (código pronto atrás da define `GOOGLE_MOBILE_ADS`).
