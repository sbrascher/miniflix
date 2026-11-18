# MiniFlix — Plano do Projeto

Aplicação desktop **100% portátil para Windows**: um catálogo e player de vídeos offline (um "mini streaming" local). Roda direto de qualquer pasta ou pendrive, sem instalação, sem configuração prévia e sem deixar rastros no PC hospedeiro.

---

## 1. Stack tecnológica

| Camada | Escolha |
|---|---|
| Host desktop | C# / **.NET 10 (LTS)**, **Windows Forms** hospedando um controle **WebView2** |
| Frontend | HTML5 + **Tailwind CSS compilado localmente** + JavaScript puro (Vanilla JS) |
| Download | `yt-dlp.exe` + `deno.exe` (runtime JS exigido pelo yt-dlp para o YouTube) |
| Mídia | `ffmpeg.exe` / `ffprobe.exe` (merge, importação local, capas) |
| Navegador embutido | **WebView2 Fixed Version Runtime** em `/bin/webview2` |

### 1.1 Regras de portabilidade

- **Nenhuma CDN.** Tailwind, fontes e ícones (SVG inline) ficam todos locais.
  - O Tailwind é gerado em tempo de desenvolvimento com o **Tailwind CLI standalone** (`tailwindcss.exe`), sem Node. O `tailwind.css` final é versionado em `/ui`.
- **Caminhos sempre relativos** a `AppContext.BaseDirectory`.
- **Nada no Registro nem em `%AppData%`/`%TEMP%`.**
  - A pasta de dados do WebView2 (`userDataFolder`) aponta para `/data/webview`.
  - Publicar como **arquivo único** (`PublishSingleFile`) com `IncludeNativeLibrariesForSelfExtract=false`: o .NET vai dentro do `MiniFlix.exe` e nada é extraído em `%TEMP%`. O `WebView2Loader.dll` fica em `/bin` (via `SetLoaderDllFolderPath`), então a raiz tem só o `.exe` e o `README.txt`.
- **O WebView2 Runtime vai junto com o app.** O Windows 10 nem sempre tem o runtime instalado. Usar `CoreWebView2Environment.CreateAsync(browserExecutableFolder: "bin/webview2", userDataFolder: "data/webview")`.
  - Se `/bin/webview2` não existir, tentar o runtime instalado no sistema como fallback e mostrar um aviso claro se nenhum for encontrado.

### 1.2 Servindo a interface e os vídeos

- **Um único host virtual** mapeado para a raiz do app com `SetVirtualHostNameToFolderMapping`:
  - `https://app.miniflix.example/` → pasta raiz (usar `.example`, **não** `.local`, para evitar atraso de resolução mDNS).
  - A UI é acessada em `/ui/index.html` e os vídeos em `/videos/<slug>/<slug>.mp4`. Mesma origem, portanto **sem problemas de CORS**.
- A tag `<video>` reproduz o MP4 diretamente, com seek (requisições de range).
  - **Validar isso logo no primeiro protótipo**, com um vídeo grande (> 1 GB).
- Os dados do catálogo (lista de vídeos, categorias, progresso) chegam à UI pela **ponte C# ↔ JS**, não por `fetch` de arquivos JSON.

---

## 2. Estrutura de pastas

```text
/MiniFlix
  ├── MiniFlix.exe (+ DLLs do .NET self-contained)
  ├── README.txt
  ├── /bin
  │     ├── yt-dlp.exe
  │     ├── deno.exe
  │     ├── ffmpeg.exe
  │     ├── ffprobe.exe
  │     └── /webview2              # Fixed Version Runtime
  ├── /ui
  │     ├── index.html
  │     ├── tailwind.css           # compilado
  │     └── app.js
  ├── /data
  │     ├── settings.json          # tema, volume padrão, etc.
  │     ├── progress.json          # posição de cada vídeo ("Continuar assistindo")
  │     ├── catalog-cache.json     # cache reconstruível do catálogo
  │     ├── /webview               # userDataFolder do WebView2
  │     └── /logs                  # saída do yt-dlp/ffmpeg para diagnóstico
  └── /videos
        └── /aula-01-introducao
              ├── aula-01-introducao.mp4    # H.264 + AAC, com faststart
              ├── aula-01-introducao.jpg    # capa
              └── aula-01-introducao.json   # metadados (FONTE DA VERDADE)
```

### 2.1 Metadados por vídeo (`<slug>.json`)

```json
{
  "id": "yt:dQw4w9WgXcQ",
  "slug": "aula-01-introducao",
  "titulo": "Aula #01: Introdução!",
  "descricao": "…",
  "categoria": "Cursos",
  "origem": "youtube",
  "urlOrigem": "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
  "duracaoSegundos": 1834,
  "dataPublicacao": "2026-03-10",
  "dataAdicionado": "2026-11-18T09:20:00",
  "versao": 1
}
```

- O `id` é `yt:<id-do-youtube>` para vídeos baixados e `local:<hash>` para importados (hash SHA-256 do primeiro 1 MB + tamanho do arquivo). Ele é usado para **impedir duplicatas**.

### 2.2 Regras de dados

- **Fonte da verdade:** os `.json` individuais de cada pasta.
- **`catalog-cache.json`:** apenas um cache. No startup, o app varre `/videos`:
  - reconstrói o cache;
  - ignora pastas sem `.mp4` ou sem `.json` válido e registra em log;
  - remove do cache vídeos cuja pasta foi apagada manualmente.
- **Categorias:** derivadas dos vídeos existentes. Uma categoria nova surge ao ser usada pela primeira vez e some quando não tem mais vídeos.
- **Gravação atômica:** toda escrita de JSON vai para `arquivo.tmp` e depois `File.Replace`/`File.Move`. Isso protege contra remoção do pendrive ou fechamento abrupto.
- **Regra de slug:** gerado a partir do título.
  - Remove acentos, remove caracteres especiais, converte para minúsculas, troca espaços por hífens e colapsa hífens repetidos. Limite de 60 caracteres.
  - Exemplo: *Aula #01: Introdução!* → `aula-01-introducao`.
  - Colisão de nome (vídeo diferente, mesmo slug) → sufixo `-2`, `-3`…
  - **O slug é imutável.** Editar o título não renomeia a pasta.
- **Validação de slug na ponte:** qualquer operação vinda da UI (excluir, editar) só aceita slugs que casem com `^[a-z0-9-]+$` e que existam em `/videos`. Isso impede path traversal (`..\`).

### 2.3 Sistema de arquivos do pendrive

- Em **FAT32** o limite é **4 GB por arquivo**. O app detecta o sistema de arquivos da unidade (`DriveInfo.DriveFormat`):
  - em FAT32, mostra um aviso na tela admin e limita o download a **1080p**;
  - o README recomenda formatar o pendrive em **exFAT** ou NTFS.

---

## 3. Design e interface (Tailwind)

- Visual moderno, limpo e minimalista.
- **Modo claro e modo escuro**, alternados por um botão no cabeçalho. A preferência fica em `/data/settings.json`, via ponte.
- **Acessibilidade:** botões com alto contraste, rótulos claros, foco visível (`focus-visible:ring`) e toda a navegação possível por teclado.
- **Cards de vídeo:**
  - mostram a capa, a duração (badge no canto), o título e uma **barra de progresso** na base, se o vídeo já foi iniciado;
  - no hover: transição suave, `scale-105` na imagem, destaque de borda/sombra e ícone "▶ Assistir";
  - vídeos assistidos até o fim exibem o selo "✓ Assistido".

---

## 4. Telas e navegação (SPA com 3 telas)

### Tela 1 — Catálogo (inicial)

- **Cabeçalho fixo** com:
  - busca em tempo real por título;
  - filtro por categoria;
  - botão de tema;
  - ícone de **engrenagem** que leva à tela admin.
- **Linha "Continuar assistindo"** no topo: vídeos com progresso entre 5% e 95%, ordenados pelo mais recente.
- Abaixo, os vídeos agrupados **por categoria**, em seções horizontais ou grade responsiva.
- Estado vazio amigável quando não há vídeos ("Adicione seu primeiro vídeo na área administrativa").
- Clicar em um card abre o player.

### Tela 2 — Player

- Player HTML5 em destaque, com controles completos: play/pause, barra de progresso, volume, velocidade e tela cheia.
- Abaixo do vídeo: título, badge da categoria, data e descrição (quebras de linha preservadas e links como texto).
- **Continuar assistindo:**
  - ao abrir um vídeo com progresso salvo, retoma automaticamente do ponto salvo, com um botão "Começar do início" visível por alguns segundos;
  - a posição é salva no pause, ao sair do player, ao fechar o app e a cada 15 segundos durante a reprodução (com debounce, para não gastar escrita no pendrive);
  - ao passar de 95%, marca o vídeo como assistido e zera a posição.
- **Atalhos de teclado:**

  | Tecla | Ação |
  |---|---|
  | `Espaço` / `K` | Play / pause |
  | `←` / `→` | Voltar / avançar 5 s |
  | `J` / `L` | Voltar / avançar 10 s |
  | `↑` / `↓` | Volume +/− 10% |
  | `M` | Mudo |
  | `F` | Tela cheia |
  | `Shift + <` / `Shift + >` | Diminuir / aumentar velocidade |
  | `0`–`9` | Pular para 0%–90% do vídeo |
  | `Esc` | Sai da tela cheia; fora dela, volta ao catálogo |

  - Os atalhos são ignorados quando o foco está em um campo de texto.
  - Ao usar um atalho, um overlay discreto mostra a ação ("+10 s", "Volume 70%").
- **Botão "Voltar"** no topo. Ao sair: pausa, salva a posição, executa `video.removeAttribute('src'); video.load()` para **liberar o arquivo no Windows**.

### Tela 3 — Área administrativa

**a) Adicionar do YouTube (requer internet)**
- Campo para a URL e seletor de categoria, com a opção de digitar uma categoria nova (`<input list>` + `<datalist>`).
- Se o `id` já existir no acervo, bloqueia e informa "Este vídeo já está no catálogo".
- **Fila de downloads:** **um download por vez**.
  - Itens adicionados enquanto outro baixa entram na fila, visível na tela, com a opção de remover itens pendentes.
  - Botão **Cancelar** no item atual: mata a árvore de processos e apaga a pasta parcial.
- **Etapas com progresso em tempo real** (C# → JS via `PostWebMessageAsJson`):
  1. Obtendo metadados (`yt-dlp --dump-single-json`)
  2. Baixando vídeo (% / velocidade / tempo restante, lidos de `--newline --progress-template`)
  3. Baixando capa
  4. Finalizando MP4 (merge + faststart)
  5. Salvando metadados
- Sem internet ou com erro do yt-dlp: mensagem amigável que bloqueia **apenas** o download. O resto do app continua funcionando. O detalhe técnico do erro vai para `/data/logs`.
- Botão **"Atualizar yt-dlp"** (`yt-dlp -U`), porque o YouTube quebra versões antigas com frequência.

**b) Importar vídeo local (offline)**
- O usuário seleciona um ou mais arquivos de vídeo (diálogo nativo do Windows aberto pelo C#) e escolhe título e categoria. O título é sugerido a partir do nome do arquivo.
- Fluxo (entra na **mesma fila** dos downloads):
  1. `ffprobe` verifica os codecs.
  2. Se já for H.264/AAC em MP4, **copia** e aplica `-movflags +faststart` (remux, sem reencode). Caso contrário, **converte** com ffmpeg (`libx264 -crf 22 -preset medium`, `aac 160k`), com barra de progresso calculada pela duração.
  3. Gera a capa com ffmpeg: um frame em 10% da duração, salvo em `.jpg`.
  4. Salva o `.json` com `origem: "local"`.
- O arquivo original nunca é alterado ou movido.

**c) Gerenciar acervo**
- Tabela com capa, título, categoria, duração, origem e data, além de busca.
- **Editar** título, categoria e descrição (o slug não muda).
- **Excluir** com confirmação feita na própria UI (modal HTML, não `alert`). Remove a pasta do disco e o progresso salvo. Se o vídeo estiver aberto no player, o app o descarrega antes de excluir.

**d) Informações**
- Espaço livre na unidade, sistema de arquivos (com o aviso de FAT32) e versões do yt-dlp e do ffmpeg.

---

## 5. Ponte JS ↔ C#

- JS → C#: `window.chrome.webview.postMessage({ tipo, idRequisicao, dados })`.
- C# → JS: `PostWebMessageAsJson({ tipo, idRequisicao, dados | erro })`.
- Envelope com `idRequisicao`, para que o JS use `await` em cada chamada (wrapper `bridge.call(tipo, dados)`).
- Comandos principais: `catalogo.listar`, `video.editar`, `video.excluir`, `progresso.salvar`, `fila.adicionarYoutube`, `fila.importarLocal`, `fila.cancelar`, `fila.remover`, `config.obter`, `config.salvar`, `sistema.info`, `ytdlp.atualizar`.
- Eventos enviados pelo C#: `fila.atualizada`, `fila.progresso`, `catalogo.atualizado`.
- **Segurança:**
  - validar o tipo e os dados de toda mensagem;
  - bloquear navegação para fora de `https://app.miniflix.example/` (`NavigationStarting`) e abrir links externos no navegador padrão;
  - em Release, desativar DevTools, o menu de contexto padrão e os atalhos do navegador (`AreBrowserAcceleratorKeysEnabled = false`, mantendo os atalhos do player, que são tratados no JS).

---

## 6. Processos externos

- Todos executados com `Process` assíncrono: `CreateNoWindow`, stdout/stderr redirecionados e lidos linha a linha.
- `yt-dlp` sempre recebe `--ffmpeg-location bin/` e o caminho do Deno, via `--js-runtimes deno:bin/deno.exe` ou pondo `bin/` no `PATH` do processo filho.
- Comando base do download:
  ```
  yt-dlp -f "bv*[vcodec^=avc1][height<=1080]+ba[acodec^=mp4a]/b[ext=mp4]/bv*+ba/b"
         --merge-output-format mp4
         --postprocessor-args "ffmpeg:-movflags +faststart"
         --write-thumbnail --convert-thumbnails jpg
         --no-playlist --newline --progress-template "..."
         -o "videos/<slug>/<slug>.%(ext)s" <URL>
  ```
  - Se o formato final não for H.264/AAC (fallback), reconverter com ffmpeg como na importação local.
- Ao fechar o app com um item em andamento: perguntar ao usuário (modal na UI). Se ele confirmar, **matar a árvore de processos** (`Process.Kill(entireProcessTree: true)`) e limpar a pasta parcial.
- Pastas parciais deixadas por quedas são detectadas no startup (sem `.json` final) e removidas.

---

## 7. Build e publicação

```powershell
# CSS (desenvolvimento)
.\tools\tailwindcss.exe -i .\src\ui\input.css -o .\ui\tailwind.css --minify

# Publicação portátil (arquivo único; opções no .csproj) — ou simplesmente .\build.ps1
dotnet publish -c Release -r win-x64 --self-contained true -o .\dist\MiniFlix
```

- Binários de terceiros ficam em `vendor\bin` (fora do código-fonte). Um script `build.ps1` copia `/ui` e `vendor\bin` → `/bin` (incluindo o WebView2 Fixed Runtime) para `dist\MiniFlix` e gera o `README.txt`.
- Tamanho estimado: cerca de 150 MB (.NET) + cerca de 200 MB (WebView2) + cerca de 150 MB (ffmpeg, yt-dlp, deno).

---

## 8. Entregáveis

1. Projeto `.NET 10` WinForms (`.csproj`, host do WebView2, ponte JS ↔ C#, serviços de catálogo, fila, yt-dlp, ffmpeg e progresso).
2. Frontend completo (`index.html`, `app.js`, `input.css` e `tailwind.css` compilado).
3. `build.ps1` + comando `dotnet publish` gerando a pasta portátil pronta para o pendrive.
4. `README.txt` com:
   - o modo de uso;
   - a recomendação de exFAT;
   - como atualizar o yt-dlp;
   - a nota de uso legal: baixar apenas conteúdo próprio, licenciado ou com permissão, respeitando os termos do YouTube.

---

## 9. Ordem de implementação sugerida

1. **Protótipo de riscos:** WinForms + WebView2 Fixed Runtime + host virtual + `<video>` com seek em um MP4 grande, rodando de um pendrive.
2. Catálogo: varredura de `/videos`, cache, ponte e Tela 1.
3. Player: Tela 2, atalhos e "Continuar assistindo".
4. Importação local (exercita o ffmpeg e a fila sem depender de internet).
5. Download do YouTube (yt-dlp + Deno + progresso).
6. Gerenciar acervo (editar e excluir).
7. Tema, polimento visual, estados vazios e de erro.
8. `build.ps1`, README e teste final em um PC limpo, a partir do pendrive.
