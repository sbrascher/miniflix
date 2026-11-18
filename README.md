# MiniFlix

**Seu catálogo de vídeos offline, no pendrive.** O MiniFlix é um "mini streaming" para Windows que roda direto de qualquer pasta ou pendrive. Você baixa ou importa vídeos uma vez e depois assiste sem internet, em qualquer PC, sem instalar nada e sem deixar rastros no computador.

> 🚧 **Status: em desenvolvimento.** O protótipo técnico (etapa 1) está pronto e validado. As telas de catálogo, player e administração vêm a seguir. Veja o [roadmap](#roadmap).

---

## Para que serve

- **Aulas e cursos** para assistir onde não há internet (sala de aula, viagem, zona rural).
- **Acervo pessoal** de vídeos organizado por categorias, com capa, descrição e busca.
- **Levar o acervo no bolso:** pluga o pendrive, abre o `MiniFlix.exe` e assiste. Ao tirar o pendrive, nada fica no PC.

## Funcionalidades planejadas

- 📚 **Catálogo** com capas, busca em tempo real e vídeos agrupados por categoria.
- ▶️ **Player** completo: velocidade de reprodução, tela cheia e atalhos de teclado no estilo do YouTube (`Espaço`, `J`/`L`, setas, `F`, `M`, `0`–`9`).
- ⏯️ **Continuar assistindo:** o MiniFlix lembra onde você parou em cada vídeo.
- ⬇️ **Adicionar do YouTube** colando a URL. A fila baixa um vídeo por vez e mostra o progresso em tempo real.
- 📁 **Importar vídeos do computador**, convertendo para MP4 só quando necessário.
- 🗂️ **Gerenciar o acervo:** editar título, categoria e descrição, ou excluir vídeos.
- 🌗 **Modo claro e modo escuro.**
- 🔌 **100% offline e portátil:** nenhuma CDN, nada no Registro, nada em `AppData`.

## Como funciona

```
MiniFlix.exe  (C# / .NET 10, WinForms)
   └── WebView2 ── interface HTML + CSS + JavaScript puro
         │           servida em https://app.miniflix.example/
         │           (host virtual mapeado para a pasta do app)
         └── ponte JS ↔ C#  ──►  yt-dlp · ffmpeg · arquivos JSON
```

- A interface é uma página web local exibida pelo **WebView2** (o motor do Microsoft Edge), sem servidor e sem internet.
- Os vídeos são MP4 (H.264 + AAC) tocados pela tag `<video>`, com seek instantâneo mesmo em arquivos grandes.
- Cada vídeo fica na própria pasta, com `.mp4`, capa `.jpg` e metadados `.json`. Dá para fazer backup copiando pastas.
- O WebView2 pode ir **dentro da pasta do app** (*Fixed Version Runtime*), para funcionar mesmo em PCs que não o têm instalado.

### Estrutura da pasta portátil

```text
MiniFlix\
  ├── MiniFlix.exe
  ├── bin\       yt-dlp, ffmpeg, deno, webview2
  ├── ui\        interface (HTML/CSS/JS)
  ├── data\      configurações, progresso, cache
  └── videos\
        └── aula-01-introducao\
              ├── aula-01-introducao.mp4
              ├── aula-01-introducao.jpg
              └── aula-01-introducao.json
```

## Compilando

**Requisitos:** Windows 10/11 x64 e [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
git clone https://github.com/sbrascher/miniflix.git
cd miniflix
.\build.ps1               # gera dist\MiniFlix (portátil, self-contained)
.\build.ps1 -VideoTeste   # idem + um vídeo de teste de ~1,2 GB (requer ffmpeg no PATH)
```

Para gerar uma versão totalmente portátil, coloque os binários de terceiros em `vendor\bin\`. A pasta não é versionada; o `build.ps1` a copia para `dist\MiniFlix\bin\`.

| Arquivo | Onde obter |
|---|---|
| `webview2\` | [WebView2 Fixed Version Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (x64, extraído) |
| `yt-dlp.exe` | [github.com/yt-dlp/yt-dlp](https://github.com/yt-dlp/yt-dlp/releases) |
| `ffmpeg.exe`, `ffprobe.exe` | [ffmpeg.org](https://ffmpeg.org/download.html) |
| `deno.exe` | [deno.com](https://deno.com/) (runtime JS exigido pelo yt-dlp para o YouTube) |

Sem `vendor\bin\webview2`, o app usa o WebView2 instalado no Windows.

### Autoteste do protótipo

```powershell
.\dist\MiniFlix\MiniFlix.exe --autoteste
```

O autoteste verifica a ponte JS ↔ C#, as requisições HTTP Range, o seek em vários pontos do vídeo e a liberação do arquivo após descarregar o player. O relatório fica em `dist\MiniFlix\data\logs\autoteste.json`.

## Roadmap

- [x] **1. Protótipo técnico:** WebView2 portátil, host virtual, seek em vídeo de mais de 1 GB, liberação de arquivo.
- [ ] 2. Catálogo: leitura de `/videos`, cache e tela inicial.
- [ ] 3. Player: atalhos e "continuar assistindo".
- [ ] 4. Importação de vídeos locais.
- [ ] 5. Download do YouTube (yt-dlp) com fila e progresso.
- [ ] 6. Gerenciamento do acervo.
- [ ] 7. Tema claro/escuro e polimento visual (Tailwind compilado localmente).
- [ ] 8. Empacotamento final e teste em PC limpo, a partir do pendrive.

O plano completo está em [`plano.md`](plano.md).

## Dicas para o pendrive

- Prefira formatar em **exFAT** ou **NTFS**. O **FAT32** não aceita arquivos acima de 4 GB.
- Feche o MiniFlix antes de remover o pendrive.

## Uso responsável

O MiniFlix usa o [yt-dlp](https://github.com/yt-dlp/yt-dlp) para baixar vídeos. Baixe apenas conteúdo **seu, licenciado ou com permissão do autor**, respeitando os termos de uso das plataformas e os direitos autorais. O projeto não tem vínculo com o YouTube nem com nenhum serviço de streaming.
