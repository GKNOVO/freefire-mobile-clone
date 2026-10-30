# Compilar o APK sem PC (GitHub Actions)

Fluxo completo: **publicar o projeto no GitHub → rodar o workflow → baixar o APK → instalar no Galaxy S24**.
Tudo pode ser feito pelo navegador do celular, com **uma única exceção**: a licença da Unity.

> ⚠️ **Requisito da Unity (não do workflow):** todo build em CI precisa de licença.
> No GameCI, **Unity Personal só ativa com e-mail e senha da conta** (`UNITY_EMAIL` +
> `UNITY_PASSWORD`); não existe modo sem credenciais para Personal. O arquivo `.ulf`
> não substitui a senha: a Unity revalida a licença a cada build.
> As rotas **sem credenciais** exigem Plus/Pro (servidor de licenças) ou o build na nuvem
> da Unity (**Unity Build Automation**), que não roda no GitHub Actions.

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

O workflow tem duas rotas e escolhe sozinho antes de baixar o editor:

| Modo | Secrets | Quando usar |
|---|---|---|
| `credentials` | `UNITY_EMAIL` + `UNITY_PASSWORD` | Unity **Personal** (grátis) — única opção |
| `server` | `UNITY_LICENSING_SERVER` | Unity **Plus/Pro** com servidor de licenças flutuantes |

Em **Settings ▸ Secrets and variables ▸ Actions ▸ New repository secret**, crie os
secrets do modo escolhido. Se nenhum estiver completo, o job falha no primeiro step
com a mensagem "Nenhum modo de licença configurado" — sem gastar 40 minutos.

### Conta com 2FA

A ativação do GameCI faz login com e-mail e senha, então **2FA bloqueia o build**.
Opções, da mais simples para a mais cara:

1. desativar a verificação em duas etapas na conta Unity usada no CI (ou usar uma conta
   dedicada ao build, sem 2FA);
2. **Unity Build Automation** (build na nuvem da Unity, licença do lado deles, sem
   `UNITY_EMAIL`/`UNITY_PASSWORD` no repositório) — configurado no Unity Dashboard,
   não no GitHub Actions;
3. servidor de licenças flutuantes da Unity (requer Plus/Pro) e o secret
   `UNITY_LICENSING_SERVER`, por exemplo `https://licenses.suaempresa.com:8080`.

> O arquivo `Unity_lic.ulf` do Unity Hub **não** resolve sozinho: o GameCI continua
> precisando de e-mail e senha para revalidar a licença Personal em cada runner.
>
> A ativação manual em `license.unity3d.com` (`.alf` → `.ulf`) foi descontinuada
> para licenças Personal.

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

Como funciona (`BR.EditorTools.AndroidBuilder`):

1. `PlayableSceneGenerator.EnsureGenerated()` cria `Boot.unity`/`Arena.unity` se não existirem
   (cada cena contém só um GameObject com o bootstrap; o conteúdo é montado em runtime).
2. O build usa essas cenas (Boot primeiro, Arena depois).
3. Se a geração ou compilação falhar, o builder encerra o job com erro; não é gerado um APK de validação. Confira o log do step **Build APK (GameCI)**.

Nada de Inspector: todos os managers, prefabs, armas e a HUD são criados por código
(`Assets/_Project/Scripts/Bootstrap/`).

## Configurações do build (onde mexer)

Tudo em `Assets/_Project/Editor/AndroidBuilder.cs`:

| Config | Valor atual |
|---|---|
| bundle id | `com.brstudio.battleroyale` |
| versão / versionCode | `0.1.0` / `1` |
| minSdk / targetSdk | 24 / automático (maior instalado na imagem) |
| arquitetura | ARM64 (obrigatório para S24/Play Store) |
| backend | IL2CPP |
| orientação | paisagem |
| saída | `build/Android/BRMobile.apk` |

## Problemas comuns

| Sintoma | Causa / solução |
|---|---|
| `Unable to activate Unity license` | credenciais erradas, 2FA na conta ou secrets ausentes — veja o passo 2 |
| `Nenhum modo de licença configurado` | crie `UNITY_EMAIL` + `UNITY_PASSWORD` ou `UNITY_LICENSING_SERVER` |
| `Warning: Failed to find package 'platforms;android'` | `AndroidTargetSdkVersion: 0` (Auto) no `ProjectSettings.asset` — o GameCI lê esse valor e roda `sdkmanager "platforms;android-<valor>"` antes do build. Use uma API real (o projeto usa `34`, em `ProjectSettings.asset` e no `AndroidBuilder`) |
| `ProjectSettings.asset: No such file or directory` | falta `ProjectSettings/ProjectSettings.asset` no repositório — ele precisa ser enviado junto com `ProjectVersion.txt` (o projeto é a raiz do repositório; não use `projectPath`) |
| `Unity version 2022.3.62f1 not found` | troque `ProjectSettings/ProjectVersion.txt` por outra 2022.3.x (ex.: `2022.3.22f1`) |
| Build falha na geração de cenas | rode **BR ▸ Gerar cenas jogáveis** no Editor e veja o log; o workflow não publica APK de validação |
| App abre, mas o mapa está magenta | shader `Standard` removido no stripping; o gerador já adiciona os shaders em *Always Included* |
| `No space left on device` | rode de novo (cache corrompido) ou remova caches antigos em Actions ▸ Caches |
| APK não instala | desinstale versão anterior com assinatura diferente; verifique ARM64 |
