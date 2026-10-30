# Compilar o APK sem PC (GitHub Actions)

Fluxo completo: **publicar o projeto no GitHub → rodar o workflow → baixar o APK → instalar no Galaxy S24**.
Tudo pode ser feito pelo navegador do celular, com **uma única exceção**: a licença da Unity.

> ⚠️ **Requisito da Unity (não do workflow):** todo build em CI precisa de licença ativada.
> No GameCI, **e-mail e senha sozinhos não ativam nada** — o builder aborta com
> `Missing Unity License File and no Serial was found`. Unity **Personal** exige o arquivo
> `Unity_lic.ulf` colado no secret `UNITY_LICENSE` (ativação manual, uma única vez),
> além de `UNITY_EMAIL` + `UNITY_PASSWORD`. Sem esse arquivo, nenhum ajuste de workflow
> faz o build rodar: é uma trava da Unity, não da pipeline.

---

## 1. Publicar o repositório (pelo celular)

Opção A — **GitHub Codespaces** (mais confortável, roda no navegador):

1. Crie um repositório vazio no GitHub (ex.: `br-mobile`).
2. No repositório: **Code ▸ Codespaces ▸ Create codespace on main**.
3. No VS Code web, arraste o `BR-Mobile-Unity.zip` para o explorador de arquivos
   (o seletor de arquivos do Android permite escolher o zip).
4. No terminal do Codespace:

```bash
unzip BR-Mobile-Unity.zip -d /tmp/br
cp -r /tmp/br/BR-Mobile-Unity/. .
rm BR-Mobile-Unity.zip
git add -A
git commit -m "Projeto Unity + workflow de build Android"
git push
```

Opção B — **Termux** (Android, F-Droid):

```bash
pkg install git unzip -y
git clone https://github.com/SEU_USUARIO/br-mobile.git
cd br-mobile
unzip /sdcard/Download/BR-Mobile-Unity.zip
mv BR-Mobile-Unity/* .
git add -A && git commit -m "Projeto Unity" && git push
```

---

## 2. Licença da Unity

O workflow **não faz verificação prévia**: ele apenas repassa as secrets para o
`game-ci/unity-builder`, que ativa a licença sozinho. Rotas aceitas pela action:

| Modo | Secrets necessários | Quando usar |
|---|---|---|
| Personal | `UNITY_LICENSE` (conteúdo do `Unity_lic.ulf`) + `UNITY_EMAIL` + `UNITY_PASSWORD` | Unity **Personal** (grátis) — o caso deste projeto |
| Plus/Pro | `UNITY_SERIAL` (`XX-XXXX-XXXX-XXXX-XXXX-XXXX`) + `UNITY_EMAIL` + `UNITY_PASSWORD` | assinatura paga |
| Servidor | `UNITY_LICENSING_SERVER` (URL) | servidor de licenças flutuantes (Plus/Pro) |

Em **Settings ▸ Secrets and variables ▸ Actions ▸ New repository secret**, crie os
secrets da rota escolhida. Sem eles o job chega ao step **Build APK (GameCI)** e
falha ali com `Missing Unity License File and no Serial was found` — depois de baixar
a imagem do editor (~6–8 GB), então vale conferir os secrets antes de disparar.

### Gerar o `Unity_lic.ulf` (Personal, uma vez só)

O `.ulf` é o único jeito de ativar Personal no GameCI. Ele **não** é gerado por este
repositório nem por linha de comando: precisa deste passo manual, fora do CI.

1. Instale o **Unity Hub** num PC (Windows, macOS ou Linux) e entre com a conta Unity
   usada no build.
2. **Preferences ▸ Licenses ▸ Add ▸ Get a free personal license**.
   Clique em *Add* mesmo que a licença já apareça na lista — sem esse clique o `.ulf`
   pode não ser criado.
3. Pegue o arquivo:
   - Windows: `C:\ProgramData\Unity\Unity_lic.ulf`
   - macOS: `/Library/Application Support/Unity/Unity_lic.ulf`
   - Linux: `~/.local/share/unity3d/Unity/Unity_lic.ulf`
4. Abra no bloco de notas, copie o **conteúdo inteiro** (começa com `<?xml` / `<License`)
   e cole em **Settings ▸ Secrets ▸ Actions ▸ UNITY_LICENSE**.
5. Crie também `UNITY_EMAIL` e `UNITY_PASSWORD` (a senha sem caracteres especiais, se
   possível: o GameCI tem problemas com alguns símbolos).

O arquivo não é vinculado a uma versão da Unity nem ao sistema onde foi ativado: pode
ser gerado no PC com Windows e usado no runner Ubuntu.

### Conta com 2FA

A rota `UNITY_SERIAL` faz login com e-mail e senha, então **2FA bloqueia** esse caminho.
A rota `UNITY_LICENSE` (Personal) usa o `.ulf` e costuma funcionar com 2FA ativo, porque
a revalidação não é um login interativo. Se ainda assim falhar:

1. usar uma conta Unity dedicada ao CI, sem 2FA;
2. **Unity Build Automation** (build na nuvem da Unity, licença do lado deles) —
   configurado no Unity Dashboard, não no GitHub Actions;
3. servidor de licenças flutuantes (Plus/Pro) com `UNITY_LICENSING_SERVER`, por exemplo
   `https://licenses.suaempresa.com:8080`.

> O `.ulf` **não substitui** `UNITY_EMAIL`/`UNITY_PASSWORD`: o GameCI usa os três juntos
> para revalidar a licença a cada runner.

---

## 3. Rodar o build

1. GitHub ▸ **Actions** ▸ **Android APK** ▸ **Run workflow** (branch `main`).
2. Primeiro run: 30–50 min (baixa a imagem do editor ~5–8 GB e compila IL2CPP).
   Com o cache do `Library`, os próximos ficam em ~10–20 min.
3. No fim do job: **Artifacts ▸ BRMobile-APK-<número>** → baixe o `.zip` → descompacte
   (app de arquivos do Android) → `BRMobile.apk`.

## 4. Instalar no Galaxy S24

1. Abra o APK no celular.
2. Permita **instalar apps de fontes desconhecidas** para o app de arquivos/navegador.
3. O APK é assinado com a **chave de debug** da Unity: instala normalmente, mas não serve
   para a Play Store (para publicar, gere um keystore e configure
   `androidKeystoreName/Base64/Pass` + `androidKeyaliasName/Pass` nos secrets).

---

## O que o APK contém

O build gera automaticamente as cenas **Boot** e **Arena** e produz um **jogo jogável**:

- mapa gray-box 900×900 m com prédios e torres, luz e fog;
- player em 3ª pessoa (joystick + arrastar para mirar + FOGO);
- **49 bots** com FSM (drop → loot → patrulha → perseguição → combate → fuga);
- avião com salto, queda livre e paraquedas (manual/automático);
- 380 itens de loot (armas, munição, kit médico, colete);
- zona que fecha em 6 fases com dano progressivo;
- HUD completa: vivos, vida/colete, munição, kill feed, timer da zona, minimapa,
  prompt de loot e botões contextuais;
- telas de login, lobby, matchmaking, resultados e anúncio recompensado (2×).

Como funciona — **script de build padrão do GameCI**, sem código customizado no caminho
crítico (o `buildMethod` do projeto foi removido; era mais um ponto de falha):

1. As cenas `Boot.unity` e `Arena.unity` (Boot primeiro, Arena depois) estão registradas e
   habilitadas em `ProjectSettings/EditorBuildSettings.asset`, e o builder padrão compila
   exatamente essas cenas.
2. minSdk 24, targetSdk 34, ARM64, IL2CPP e o bundle id vêm de
   `ProjectSettings/ProjectSettings.asset`, conferidos pelo step **Validar projeto** antes
   de qualquer download.
3. O APK sai em `build/Android/BRMobile.apk` (`buildName: BRMobile` + `buildsPath: build`).
4. O step **Conferir APK gerado** falha o job se o build terminar “verde” sem `.apk`, para
   não existir sucesso silencioso.

Para regerar as cenas no Editor existe **BR ▸ Gerar cenas jogáveis**
(`Assets/_Project/Editor/PlayableSceneGenerator.cs`); no CI isso não é necessário, porque
as cenas estão versionadas.

Nada de Inspector: todos os managers, prefabs, armas e a HUD são criados por código
(`Assets/_Project/Scripts/Bootstrap/`).

## Configurações do build (onde mexer)

Tudo em `ProjectSettings/ProjectSettings.asset` (nada de `buildMethod` customizado):

| Config | Valor atual |
|---|---|
| bundle id | `com.brstudio.battleroyale` |
| versão / versionCode | `0.1.0` / `1` |
| minSdk / targetSdk | 24 / 34 (fixos no `ProjectSettings.asset`) |
| arquitetura | ARM64 (obrigatório para S24/Play Store) |
| backend | IL2CPP |
| orientação | paisagem |
| saída | `build/Android/BRMobile.apk` |

## Problemas comuns

| Sintoma | Causa / solução |
|---|---|
| `Missing Unity License File and no Serial was found` | falta o secret `UNITY_LICENSE` (Personal) ou `UNITY_SERIAL` (Plus/Pro). E-mail e senha sozinhos não ativam — veja o passo 2 |
| `Unable to activate Unity license` | `.ulf` de outra conta, senha com símbolos especiais ou 2FA na rota `UNITY_SERIAL` — veja o passo 2 |
| `Unable to activate Unity license` / job morre logo após baixar a imagem | nenhum conjunto completo: Personal (`UNITY_LICENSE` + e-mail + senha), Plus/Pro (`UNITY_SERIAL` + e-mail + senha) ou `UNITY_LICENSING_SERVER` — veja o passo 2 |
| `toomanyrequests: You have reached your pull rate limit` | limite anônimo do Docker Hub (100 pulls/6h por IP; runners do GitHub compartilham IP). Rode o workflow de novo mais tarde ou configure `docker/login-action` com uma conta Docker Hub gratuita |
| `Warning: Failed to find package 'platforms;android'` | `AndroidTargetSdkVersion: 0` (Auto) no `ProjectSettings.asset` — o GameCI lê esse valor e roda `sdkmanager "platforms;android-<valor>"` antes do build. O projeto fixa `34` |
| `ProjectSettings.asset: No such file or directory` | falta `ProjectSettings/ProjectSettings.asset` no repositório — ele precisa ser enviado junto com `ProjectVersion.txt` (o projeto é a raiz do repositório; não use `projectPath`) |
| `manifest for unityci/editor:... not found` / falha ao baixar a imagem | o workflow não fixa imagem nem tag: usa `game-ci/unity-builder@v4` com `unityVersion: auto`, que monta `unityci/editor:ubuntu-<versão do projeto>-android-<versão da imagem>`. Se a tag não existir no Docker Hub, ajuste `ProjectSettings/ProjectVersion.txt` para uma 2022.3.x publicada |
| Build “verde” mas sem APK | o step **Conferir APK gerado** falha o job; veja se `build/Android/BRMobile.apk` aparece no log do builder |
| `Projeto validado` falhou antes do build | o step de validação reprovou `ProjectSettings.asset` (targetSdk/minSdk/ARM64/keystore), uma cena ou o `EditorBuildSettings.asset` — a mensagem diz qual |
| Erro de compilação C# | o log do editor mostra arquivo e linha; o CI compila `Assembly-CSharp` + `Assembly-CSharp-Editor` antes do player |
| App abre, mas o mapa está magenta | shader `Standard` removido no stripping; o gerador já adiciona os shaders em *Always Included* |
| `No space left on device` | rode de novo (cache corrompido) ou remova caches antigos em Actions ▸ Caches |
| APK não instala | desinstale versão anterior com assinatura diferente; verifique ARM64 |
