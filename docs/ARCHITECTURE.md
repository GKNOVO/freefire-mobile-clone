# Battle Royale Mobile — Arquitetura (Unity 2022.3 LTS)

Jogo mobile **offline-first** inspirado no Free Fire: 1 player humano + 49 bots, avião,
paraquedas, zona que fecha, loot, combate hitscan, progressão local e rewarded ads.

Alvo: **60 FPS estáveis** em Galaxy S24 (Snapdragon 8 Gen 3) e iPhone 13+.

---

## 1. Princípios de arquitetura

| Princípio | Como está aplicado |
|---|---|
| **Offline-first** | Nenhuma dependência de rede. Perfil local criptografado, matchmaking simulado. |
| **Data-driven** | Armas, itens, tempos de fase, zonas e loot são `ScriptableObject`. Balancear = editar asset. |
| **Event-driven** | Managers publicam em `EventBus` (structs). UI consome; gameplay nunca referencia UI. |
| **Scene composition** | `Boot` (persistente: managers + UI) + `Arena` (carregada aditivamente por partida). |
| **Zero alocação em gameplay** | Object pool para bots/loot/tracers, buffers estáticos, sem LINQ/LINQ-like em Update. |
| **Single source of truth** | `MatchManager` é dono do estado da partida; `SaveManager` é dono do perfil. |
| **Determinismo de rede futura** | Combate por raycast com `WeaponController` compartilhado (player/bot) — fácil de trocar por servidor autoritativo depois. |

### Camadas

```
┌──────────────────────────────────────────────────────────────┐
│ UI (Canvas persistente)                                      │
│ Splash / Login / Lobby / Matchmaking / HUD / Results         │
└───────────────▲──────────────────────────┬───────────────────┘
                │ EventBus (structs)       │ comandos (botões)
┌───────────────┴──────────────────────────▼───────────────────┐
│ Managers de aplicação                                        │
│ GameManager · UIManager · SaveManager · AdsManager · Audio   │
│ ObjectPoolManager · AdaptiveQuality                          │
└───────────────▲──────────────────────────┬───────────────────┘
                │                          │
┌───────────────┴──────────────────────────▼───────────────────┐
│ Managers de partida (cena Arena)                             │
│ MatchManager · BotManager · ZoneManager · LootManager        │
│ PlaneDirector                                                │
└───────────────▲──────────────────────────┬───────────────────┘
                │                          │
┌───────────────┴──────────────────────────▼───────────────────┐
│ Gameplay                                                     │
│ PlayerController · PlayerCombat · BotAI (FSM) · Weapon       │
│ Health / Hitbox · InventorySystem · LootPickup               │
└──────────────────────────────────────────────────────────────┘
```

---

## 2. Fluxo de estados

### 2.1 Fluxo de telas (app)

```
Splash ──(1.6s / perfil carregado)──► Login (se perfil novo)
                                   └► Lobby
Lobby ──"Iniciar Partida"──► Matchmaking ──(arena carregada)──► HUD
HUD ──(player morre ou é o último vivo)──► Results ──► Lobby
```

`GameManager` controla essa máquina; `UIManager` só liga/desliga telas.

### 2.2 Máquina de estados da partida (`MatchManager`)

```
                    ┌──────────────────────────────────────────┐
                    │             MatchPhase.None              │
                    └───────────────────┬──────────────────────┘
                                        │ StartMatch()
                                        ▼
                    ┌──────────────────────────────────────────┐
                    │        WaitingIsland  (35s)              │
                    │  50 personagens na ilha, zona neutra     │
                    └───────────────────┬──────────────────────┘
                                        │ timer
                                        ▼
                    ┌──────────────────────────────────────────┐
                    │         PlaneFlight  (28s)               │
                    │  câmera segue o avião, HUD mostra PULAR  │
                    │  bots saltam em t ∈ [0.1, 0.95] da rota  │
                    └───────┬──────────────────────┬───────────┘
              PULAR/timer   │                      │ fim da rota
                            ▼                      ▼
                    ┌──────────────────────────────────────────┐
                    │            Freefall                      │
                    │  queda livre + paraquedas manual/auto    │
                    └───────────────────┬──────────────────────┘
                                        │ player.State == Grounded
                                        ▼
                    ┌──────────────────────────────────────────┐
                    │              Active                      │
                    │  zona fecha, dano fora, contador vivo    │
                    └───────┬──────────────────────┬───────────┘
                    player morre                   último vivo
                            ▼                      ▼
                    ┌──────────────────────────────────────────┐
                    │               Ended                      │
                    │  placement, kills, rewards, ads 2x       │
                    └──────────────────────────────────────────┘
```

### 2.3 Estados do jogador (`PlayerController`)

```
InPlane ──JumpFromPlane()──► Freefall ──auto/manual (altura)──► Parachuting
                                                                   │
                                          _cc.isGrounded            │
                                                                   ▼
                                                               Grounded ──► Dead
```

- Queda livre: gravidade acumulada até `terminalFallSpeed`, controle horizontal com `airControl`.
- Paraquedas: descida constante + planeio, abre automaticamente a 90 m do chão
  (configurável) e manualmente abaixo de 25 m.
- Pouso: `CharacterController.isGrounded` → `Grounded` → `MatchManager` entra em `Active`.

### 2.4 FSM dos bots (`BotAI`)

| Estado | Entrada | Comportamento | Saída |
|---|---|---|---|
| `Drop` | spawn / salto | desce em direção ao `dropTarget` | toca o chão → `Loot` |
| `Loot` | pousou | vai até item reservado no `LootManager`, pega arma/colete | item pego / sem item → `Patrol` |
| `Patrol` | loot concluído | anda por pontos aleatórios dentro da zona, percepção a cada ~150 ms | vê inimigo → `Chase`; fora da zona → corrige rota |
| `Chase` | alvo visível e longe | persegue até `preferredRange`, revalida LOS | perto + LOS → `Attack`; alvo perdido 2,5 s → `Patrol` |
| `Attack` | dentro do alcance | para, faz strafe, rajadas com erro de mira, recarrega | vida < 30 → `Flee`; sem LOS → `Chase` |
| `Flee` | vida baixa | corre para longe do alvo **e** para dentro da zona | vida recuperada distância → `Patrol` |
| `Dead` | `Health.Died` | desativa CC/arma, publica kill feed, volta ao pool em 1,5 s | — |

Otimizações da IA:
- `think` em intervalo (`0.12 s` perto, `0.35 s` a 160 m, `0.9 s` além) — definido pelo `BotManager` por distância do player.
- LOS com **1 raycast** por candidato e limite de 60 m; alvo mais próximo primeiro.
- Avoidance com **1 spherecast** curto (sem NavMesh, sem A*).
- Buffers estáticos (`Collider[16]`, `List<Health>` global) → zero GC.

---

## 3. Estrutura de pastas

```
Assets/
├── _Project/
│   ├── Scripts/
│   │   ├── Core/                    GameManager, SceneLoader, EventBus, Singleton, GameEvents
│   │   ├── Managers/                MatchManager, MatchConfig, ZoneManager, LootManager,
│   │   │                            ObjectPoolManager, UIManager, AudioManager,
│   │   │                            AdsManager, SaveManager, PlaneDirector
│   │   ├── Player/                  PlayerController, PlayerCombat, ThirdPersonCamera,
│   │   │                            InputBridge, DesktopInputDriver
│   │   ├── Bots/                    BotAI (FSM), BotManager
│   │   ├── Combat/                  Health, Hitbox, WeaponDefinition, WeaponController, Tracer
│   │   ├── Inventory/               ItemDefinition, InventorySystem, InventoryComponent, LootPickup
│   │   ├── UI/                      MenuScreens, HudController, ResultsScreen, Minimap
│   │   ├── Networking/              FakeMatchmakingService (simulação offline)
│   │   ├── Data/                    PlayerProfile, MatchResult
│   │   └── Utils/                   AdaptiveQuality
│   ├── Prefabs/
│   │   ├── Player/                  Player.prefab
│   │   ├── Bots/                    Bot.prefab
│   │   ├── Loot/                    LootCrate.prefab
│   │   ├── FX/                      Tracer.prefab, MuzzleFlash.prefab, Impact.prefab
│   │   └── Environment/             Plane.prefab, ZoneRing.prefab
│   ├── ScriptableObjects/
│   │   ├── Weapons/                 WPN_Pistol, WPN_SMG, WPN_Rifle, WPN_Shotgun, WPN_Sniper
│   │   ├── Items/                   ITM_* (munição, kit, colete, granada)
│   │   ├── Config/                  MatchConfig_Default
│   │   └── Characters/              CHR_* (skins)
│   ├── Scenes/                      Boot.unity, Arena.unity
│   ├── Art/
│   │   ├── Characters/  Weapons/  Environment/  UI/
│   ├── Audio/                       Music/, SFX/
│   └── Resources/                   (evitar — usar Addressables)
├── Plugins/                         GoogleMobileAds (quando integrar)
└── ThirdParty/
```

---

## 4. Managers e responsabilidades

| Manager | Escopo | Responsabilidade | Não faz |
|---|---|---|---|
| `GameManager` | app (singleton) | fluxo Splash→Lobby→Partida→Resultados, target FPS, ads do lobby/2x | regra de combate |
| `MatchManager` | partida (cena) | FSM da partida, vivos, kills, placement, resultado | UI, dano |
| `PlayerController` | player | movimento, queda, paraquedas, knockback | dano, tiro |
| `PlayerCombat` | player | entrada → `WeaponController` | regra de arma |
| `BotManager` | partida | spawn/reciclagem dos 49 bots, rota de salto, LOD de IA | comportamento |
| `BotAI` | bot | FSM, percepção, combate, fuga | spawn |
| `ZoneManager` | partida | círculos, dano fora, timer, visual | mover jogadores |
| `LootManager` | partida | spawn ponderado, reserva de item por bot | inventário |
| `InventoryManager`¹ | player | `InventorySystem` + pickup por proximidade + equipar arma | UI |
| `UIManager` | app | telas ativas, fade | dados de jogo |
| `AudioManager` | app | pool de 20 vozes, música, SFX 3D | mixers (asset) |
| `SaveManager` | app | perfil local AES+HMAC, XP/level, moedas | compras |
| `AdsManager` | app | rewarded (AdMob ou simulado), cooldown, fallback | concessão de prêmio (caller decide) |
| `ObjectPoolManager` | app | pool global, prewarm, despawn com delay | lógica de jogo |
| `AdaptiveQuality` | app | tiers por FPS medido | mudanças de gameplay |

¹ Implementado como `InventoryComponent` (MonoBehaviour no player) + `InventorySystem` (C# puro).
Se precisar de um manager global de inventário/loja, ele deve **envelopar** esses dois, não duplicar regra.

### Contratos de evento (`EventBus`)

`MatchPhaseChanged`, `AliveCountChanged`, `KillFeedMessage`, `MatchEnded`,
`PlayerHealthChanged`, `PlayerDied`, `WeaponAmmoChanged`, `LootPicked`,
`DamageNumberRequested`, `ZoneUpdated`, `ZoneTimerTick`, `ProfileChanged`, `RewardedAdResult`.

Todos são `struct` → publicar não aloca.

---

## 5. Detalhes por sistema

### 5.1 Combate
- `WeaponDefinition` (SO): dano, headshot ×2.2, RPM, munição, spread, pellets, alcance, knockback.
- Hitscan com **1 raycast por pellet** (shotgun = 8). Sem projétil físico: latência zero e custo previsível.
- `Hitbox` nos colliders filhos define multiplicador (cabeça 2.2×, membros 0.75×).
- `Health` aplica colete absorvendo 50% até quebrar, publica dano e morte.
- Tracer vem do pool (LineRenderer), nunca `Instantiate`.

### 5.2 Loot
- Tabela ponderada por `spawnWeight`; 220 itens por partida por padrão.
- Reserva de item pelos bots (`claimDuration = 10 s`) evita 5 bots no mesmo caixote.
- Pickup do player: `OverlapSphereNonAlloc` a cada 0,2 s + botão de interação (estilo Free Fire).

### 5.3 Zona
- Fases no `MatchConfig` (espera → fechamento → raio alvo → DPS).
- Dano em tick de 1 s percorrendo `Health.All` (sem triggers, sem colliders de zona).
- Círculo visual em `LineRenderer` com 72 segmentos e `useWorldPositions`.

### 5.4 Economia / progressão
- `PlayerProfile`: XP com curva `100 × 1.08^(level-2)`, nível máx. 60, ouro, diamantes, skins.
- Recompensa: `GoldReward = 40 + (50 - placement) * 2 + kills * 15 + 200 (vitória)`.
- Anúncio 2x: `SaveManager.GrantAdRewardBonus` adiciona o valor novamente (sem duplicar estatísticas).

### 5.5 Save/Load
```
JSON (JsonUtility)
  └─ AES-256-CBC (chave PBKDF2 de deviceUniqueIdentifier + salt do app)
      └─ HMAC-SHA256 (anti-tamper)
          └─ Base64 → profile.sav (escrita atômica com .tmp + profile.bak)
```
- Falha de HMAC/leitura → tenta `.bak` → cria perfil novo (nunca crasha o boot).
- `OnApplicationPause(true)` e `OnApplicationQuit` salvam.
- Limitação honesta: **não é segurança de servidor**. Para IAP/rank global, valores autoritativos precisam ir para backend.

### 5.6 Ads
- `IRewardedAdProvider` → `SimulatedRewardedProvider` (editor/dev) e `AdMobRewardedProvider`
  (compilado só com `GOOGLE_MOBILE_ADS`, SDK 8.x, usando IDs de teste do Google por padrão).
- Regras: 1 por vez, cooldown 20 s, sem fill → callback `false` e o jogo segue.
- Locais: Lobby (+100 ouro), após morrer/resultado (2x), após vitória (mesma tela de resultado).

---

## 6. Setup no Unity (passo a passo)

### 6.1 Projeto
1. Unity **2022.3 LTS**, template **3D (URP)**, plataformas Android + iOS.
2. `Project Settings > Player > Active Input Handling`: *Both* (o protótipo usa `Input` legado via `DesktopInputDriver`; a ponte isola isso).
3. `Project Settings > Quality`: 4 níveis (Baixo/Médio/Alto/Ultra), vSync off.
4. `Project Settings > Time`: Fixed Timestep 0.02 (não usamos física rígida, mas mantém estável).
5. Layers novas: `Player`, `Bot`, `Ground`, `Loot`, `Hitbox`, `Minimap`.
   Ajuste `groundMask`/`hitMask`/`lineOfSightMask` nos prefabs.

### 6.2 Cenas
- `Boot.unity`: `[GameManager]`, `[SaveManager]`, `[UIManager]`, `[AdsManager]`, `[AudioManager]`,
  `[ObjectPoolManager]`, `[AdaptiveQuality]`, `Canvas` com todas as telas + `EventSystem`.
- `Arena.unity`: `[MatchManager]`, `[BotManager]`, `[ZoneManager]`, `[LootManager]`,
  `[PlaneDirector]`, `Player.prefab`, terreno, spawn points de loot, `ZoneRing`.
- Build Settings: `Boot` (0) e `Arena` (1).

### 6.3 Player.prefab
`CharacterController` (height 1.8, radius 0.35) · `Health` (100 HP, maxArmor 100, startingArmor 0) ·
`PlayerController` · `InventoryComponent` (capacity 6) · `PlayerCombat` · `WeaponController` + muzzle ·
`Hitbox` (Head 2.2×, Torso 1×, Limbs 0.75×) · `Animator` opcional · tag `Player`, layer `Player`.

### 6.4 Bot.prefab
`CharacterController` · `Health` · `BotAI` · `InventoryComponent` · `WeaponController`
(e variante com `Hitbox`), layer `Bot`. Um único prefab reutilizado 49× pelo pool.

### 6.5 Assets
- `WPN_*`: 5 armas (Pistola, SMG, Rifle, Shotgun, Sniper) com stats iniciais sugeridos abaixo.
- `ITM_*`: munição leve/pesada, kit médico, colete, granada.
- `MatchConfig_Default`: 50 jogadores, ilha 35 s, avião 28 s, 220 loots.
- Referencie tudo no Inspector dos managers; nada é `Resources.Load` em runtime.

Stats iniciais sugeridos:

| Arma | Dano | RPM | Pente | Alcance | Spread |
|---|---|---|---|---|---|
| Pistola | 18 | 300 | 17 | 60 | 1.6° |
| SMG | 14 | 780 | 30 | 70 | 2.2° |
| Rifle | 22 | 600 | 30 | 120 | 1.1° |
| Shotgun | 12×8 | 90 | 6 | 35 | 6° |
| Sniper | 85 | 45 | 5 | 250 | 0.2° |

### 6.6 AdMob (quando for publicar)
1. Instalar **Google Mobile Ads Unity Plugin 8.x**.
2. `Project Settings > Player > Scripting Define Symbols`: adicionar `GOOGLE_MOBILE_ADS`.
3. Trocar os IDs de teste em `AdsManager` pelos IDs reais (Android/iOS).
4. Testar com `AdRequest` em dispositivo real antes do release.

---

### 6.7 Cenas jogáveis geradas por código

`Assets/_Project/Editor/PlayableSceneGenerator.cs` (menu **BR ▸ Gerar cenas jogáveis**):

- cria `Boot.unity` com apenas `GameBootstrap` (managers persistentes, canvas e 6 telas);
- cria `Arena.unity` com apenas `ArenaBootstrap` (mundo gray-box, player, avião, zona, loot, 49 bots);
- adiciona as duas ao Build Settings e garante `Standard`/`Sprites`/`UI` em *Always Included Shaders*.

O conteúdo dos managers e prefabs é criado em runtime (`Assets/_Project/Scripts/Bootstrap/`):
`RuntimeContent` (armas/itens/config/materiais/prefabs), `RuntimeUI`/`RuntimeScreens`/`RuntimeHud`
(UI por código com Text nativo, sem depender de TMP Essentials) e `RuntimeInput`
(joystick, área de mira, botão de pressão contínua).

Por que assim? Assets de cena/prefab exigem o Editor, e o build acontece no GitHub Actions sem
ninguém para arrastar referências no Inspector. Com o bootstrap, o APK sai jogável no primeiro run
e a cena continua sendo só um ponto de entrada (fácil de substituir por cenas autorais depois).

## 7. Otimização mobile (S24 / iPhone 13+)

### Orçamento por frame (16,6 ms)
| Item | Alvo |
|---|---|
| CPU gameplay (player + 49 bots + zona) | ≤ 6 ms |
| Render (SetPass calls) | ≤ 40 |
| Draw calls | ≤ 120 |
| Triângulos visíveis | ≤ 350k |
| GC alloc em partida | **0 B/frame** |
| Texturas | ≤ 400 MB, ASTC 4×4/6×6 |

### O que já está no código
- **Pool obrigatório** (`ObjectPoolManager`): bots, loot, tracers, FX → sem `Instantiate/Destroy` em partida.
- **LOD de IA** por distância (0 / 0,35 / 0,9 s de think) — o maior ganho de CPU em BR.
- **Buffers estáticos** para overlap e listas; `struct` nos eventos; sem LINQ em runtime.
- **Hitscan** em vez de projétil físico; sem `Rigidbody` no player/bots.
- **Sem triggers de zona**: distância ao quadrado.
- **Quality adaptativa** com histerese (evita thrashing).

### O que configurar no Editor
1. **LOD Groups** em todos os personagens e props: LOD0 (0–25 m), LOD1 (25–60 m), LOD2 (60–120 m), Culled.
2. **Occlusion Culling**: bake na arena; marque `Occluder Static` em prédios e `Occludee Static` em props.
3. **URP Asset** (mobile): 
   - SRP Batcher ON, GPU Resident Drawer / GPU Instancing para vegetação;
   - 1 luz direta + shadows 1024, cascatas 2, distância 40–55 m;
   - Render Scale 0.85–1.0; MSAA 2× ou FXAA;
   - Desligar HDR no tier baixo; post-processing mínimo (sem DOF/bloom pesado).
4. **Texturas**: ASTC 6×6 (mobile), mipmaps ON, max size 1024 para props, 2048 para personagens.
5. **Audio**: Vorbis, SFX 22 kHz mono, música streaming; `AudioManager` limita 20 vozes.
6. **Animação**: Animator com `Culling Mode = Cull Update Transforms`; **Animation Instancing** ou skins simples para os 49 bots.
7. **Addressables** para mapa/skins: `Boot` leve (~30 MB), `Arena` carregada sob demanda e liberada com `UnloadUnusedAssets`.
8. **Battery**: target 60 FPS com `QualitySettings.vSyncCount = 0`; `AdaptiveQuality` cai para 45 FPS/0.75 render scale se o device aquecer.
9. **IL2CPP + ARM64**, **Managed Stripping Medium**, **Incremental GC** ligado.
10. **Frame Debugger** e **Profiler (Deep Profile desligado)** antes de cada release; meta de 60 FPS no S24 com 49 bots vivos.

### Armadilhas comuns (e como o código evita)
- `FindObjectsOfType` em loop → `Health.All` estático.
- `GetComponent` em Update → cache no Awake.
- `new List`/LINQ por frame → buffers e structs.
- `Instantiate` de FX → pool com `Despawn(delay)`.
- NavMesh com 49 agentes → steering + spherecast.
- 50 Animators em Skinned Mesh → LOD + culling agressivo de animação.

---

## 8. Roadmap — do protótipo ao jogo jogável

### Sprint 1 — Jogável (1–2 semanas)
- Montar `Boot`/`Arena` e os prefabs do checklist acima.
- 5 armas + 4 itens + animações placeholder (mixamo).
- Testar o loop completo: Lobby → avião → queda → zona → morte/vitória → resultado → lobby.

### Sprint 2 — Sensação de Free Fire (2–3 semanas)
- Ajustar câmera (FOV, shake, recoil visual), hitmarker, damage numbers (pool).
- HUD completa: minimapa com ícones de loot/zona, kill feed com ícones de arma.
- Áudio: passos, tiro por categoria, avião, paraquedas, zona.
- 3 personagens + 5 skins, loja com moedas (sem IAP).

### Sprint 3 — IA e conteúdo (2–3 semanas)
- Melhorar IA: usar cobertura, revidar de longe, lootear corpos, usar granadas.
- 3 pontos de interesse no mapa (cidade, porto, fazenda) com loot raro.
- Dificuldade adaptativa dos bots (precisão/reação por tier de conta).
- Missões diárias offline (3 por dia) e passe simples.

### Sprint 4 — Monetização e retenção
- IAP real (pacotes de diamantes), loja de skins, `PremiumRemoveAds`.
- Banner/interstitial **nunca durante a partida**; rewarded apenas nos locais já previstos.
- Analytics local agregado (sem PII) para tuning de retenção.

### Sprint 5 — Online (opcional, arquitetura já preparada)
- Trocar `FakeMatchmakingService` por backend; manter `BotManager` como "backfill" de lobby.
- Autoridade do servidor para dano/loot; `WeaponController` já centraliza o hitscan.
- Migrar gold/diamonds para backend (o save local passa a ser cache).

---

## 9. Limitações conhecidas deste código base

1. **Cenas são geradas por código** (Boot/Arena) e o conteúdo é gray-box (cápsulas/cubos), sem modelos nem animações finais — ver 6.7.
2. **Minimapa** usa segunda câmera (1 draw extra); versão por UV é o próximo passo.
3. **Animações**: apenas ganchos (`Animator.SetFloat/SetTrigger`); sem Animation Controller pronto.
4. **Bots não usam granadas/veículos** e não looteiam corpos.
5. **AdMob** compila apenas com o SDK instalado + define; por padrão roda simulado.
6. **Save** é local e ofuscado, não à prova de root — ver 5.5.
7. **Sem testes automatizados** incluídos; `InventorySystem` e `PlayerProfile` são puros (fáceis de testar em EditMode).
