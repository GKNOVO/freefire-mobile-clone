# Compilar o APK sem PC (GitHub Actions)

Fluxo completo: **publicar o projeto no GitHub → rodar o workflow → baixar o APK → instalar no Galaxy S24**.
Tudo pode ser feito pelo navegador do celular, com **uma única exceção**: a licença da Unity.

> ⚠️ **Requisito da Unity (não do workflow):** builds em CI precisam de uma licença.
> Para Unity **Personal**, é um arquivo `Unity_lic.ulf` gerado **uma vez** pelo Unity Hub
> (Windows/macOS/Linux). A Unity descontinuou a ativação manual por upload de `.alf`,
> então não existe caminho 100% pelo celular para gerar esse arquivo. Depois de gerado,
> ele é um secret do repositório e **todos os builds seguintes são 100% pelo celular**.

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

## 2. Licença da Unity (uma vez)

1. Em **um computador** (seu, de um amigo, ou um Windows virtual no navegador),
   instale o **Unity Hub** e entre com a conta Unity.
2. **Unity Hub ▸ Preferences ▸ Licenses ▸ Add ▸ Get a free personal license**.
   Confirme que `Unity_lic.ulf` foi criado:
   - Windows: `C:\ProgramData\Unity\Unity_lic.ulf`
   - macOS: `/Library/Application Support/Unity/Unity_lic.ulf`
   - Linux: `~/.local/share/unity3d/Unity/Unity_lic.ulf`
3. No GitHub: **Settings ▸ Secrets and variables ▸ Actions ▸ New repository secret**

| Secret | Valor |
|---|---|
| `UNITY_LICENSE` | conteúdo completo do `Unity_lic.ulf` (abra no bloco de notas e cole) |
| `UNITY_EMAIL` | e-mail da conta Unity |
| `UNITY_PASSWORD` | senha da conta Unity (sem caracteres especiais, se possível) |

> Se você usa Unity Plus/Pro: use `UNITY_SERIAL` + `UNITY_EMAIL` + `UNITY_PASSWORD`
> e remova o `UNITY_LICENSE` do workflow.
>
> Licenças Personal expiram/revalidam de tempos em tempos; se o build falhar na ativação,
> repita este passo 2 e atualize o secret.

Alternativa sem licença local: **Unity Build Automation** (build na nuvem da Unity,
licenciamento do lado deles, plano por minutos). O restante do repositório funciona igual.

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
3. Se a geração falhar por algum motivo, o builder cai no plano B: uma cena com a tela
   “BUILD DE VALIDAÇÃO”, para você validar licença/assinatura/instalação sem APK quebrado.

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
| `Unable to activate Unity license` | secrets ausentes/expirados — refaça o passo 2 |
| `Unity version 2022.3.62f1 not found` | troque `ProjectSettings/ProjectVersion.txt` por outra 2022.3.x (ex.: `2022.3.22f1`) |
| App abre na tela “BUILD DE VALIDAÇÃO” | a geração das cenas falhou: rode o menu **BR ▸ Gerar cenas jogáveis** no Editor e veja o log |
| App abre, mas o mapa está magenta | shader `Standard` removido no stripping; o gerador já adiciona os shaders em *Always Included* |
| `No space left on device` | rode de novo (cache corrompido) ou remova caches antigos em Actions ▸ Caches |
| APK não instala | desinstale versão anterior com assinatura diferente; verifique ARM64 |
