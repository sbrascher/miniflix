# MiniFlix 1.0.0 — seu catálogo de vídeos offline, no pendrive

O MiniFlix é um "mini streaming" para Windows que roda direto de uma pasta ou pendrive, **sem instalação**. Você adiciona os vídeos uma vez, importando do computador ou baixando do YouTube, e depois assiste **sem internet**, em qualquer PC. Nada é gravado fora da pasta do programa.

## ⬇️ Qual arquivo baixar?

| Arquivo | Tamanho | Para quem |
|---|---|---|
| **`MiniFlix-1.0.0-win-x64.zip`** | ~460 MB | **Recomendado.** Funciona em qualquer Windows 10 ou 11 (64 bits), porque já inclui o componente WebView2. |
| `MiniFlix-1.0.0-win-x64-leve.zip` | ~190 MB | Versão menor, para Windows 11 ou Windows 10 atualizado, que já têm o WebView2. Se aparecer "componente WebView2 não encontrado", use a versão completa. |

Os dois têm as mesmas funcionalidades. A única diferença é o WebView2 embutido.

## 🚀 Como usar

1. Baixe o `.zip` e **extraia tudo** (botão direito → *Extrair Tudo…*) para um pendrive ou uma pasta. O programa não funciona aberto de dentro do `.zip`.
2. Abra a pasta `MiniFlix` e dê dois cliques em **`MiniFlix.exe`**.
3. Na primeira vez, o Windows pode mostrar **"O Windows protegeu o computador"**. Isso acontece porque o programa não tem assinatura digital paga. Clique em **Mais informações → Executar assim mesmo**.
4. Clique na **engrenagem** (canto superior direito) para adicionar os primeiros vídeos.

O arquivo `README.txt` dentro da pasta tem o guia completo: atalhos, dicas para o pendrive e solução de problemas.

## ✨ O que o MiniFlix faz

**Catálogo**
- Vídeos organizados por categoria, com capa, duração e busca instantânea (tecla `/`).
- Fileira **"Continuar assistindo"**: cada vídeo volta exatamente de onde você parou. Dá para tirar um vídeo da fileira pelo "×" no card, com opção de desfazer.
- Selo **"Assistido"** nos vídeos vistos até o fim.
- Tema **claro e escuro**, com visual em relevo, de aparência "emborrachada".

**Player**
- Atalhos de teclado no estilo do YouTube: `Espaço`/`K`, `J`/`L`, setas, `M`, `F`, `<`/`>` para velocidade e `0`–`9` para pular. Aperte `?` para ver a lista.
- Tela cheia de verdade e volume lembrado entre sessões.

**Adicionar vídeos**
- **Importar do computador**, vários arquivos de uma vez. Vídeos em H.264, VP9 ou AV1 (MP4, MKV ou WebM, incluindo os baixados do YouTube) são só copiados, em segundos e sem perder qualidade. AVI antigo, WMV, HEVC/H.265 e outros são convertidos automaticamente, em até 1080p. O arquivo original nunca é alterado.
- **Baixar do YouTube** colando o link: até 1080p, com capa, descrição e data de publicação.
- Fila de processamento com progresso, velocidade, tempo restante e botão de cancelar. Você pode continuar assistindo enquanto ela trabalha.

**Gerenciar**
- Editar título, categoria e descrição; excluir vídeos (com confirmação).
- **Trocar a capa** por uma imagem do computador ou por qualquer cena do vídeo, escolhida num controle deslizante com prévia ao vivo.
- Botão **"Atualizar yt-dlp"** para quando o YouTube mudar algo e os downloads pararem.

**Portátil de verdade**
- Roda do pendrive, sem instalar e sem gravar nada no Registro ou em `AppData`.
- Para fazer backup ou levar o acervo para outro lugar, basta copiar a pasta inteira.

## 💡 Dicas

- Formate o pendrive em **exFAT** (ou NTFS). O formato **FAT32** não aceita arquivos acima de 4 GB, o que impede vídeos longos. *Atenção: formatar apaga o conteúdo do pendrive.*
- Feche o MiniFlix antes de remover o pendrive.
- Pendrives USB 3.0 deixam a abertura e a conversão bem mais rápidas.

## 📋 Requisitos

- Windows 10 ou 11, **64 bits**
- Internet **apenas** para baixar do YouTube ou atualizar o yt-dlp; todo o resto funciona offline
- Espaço: cerca de 1 GB para o programa (versão completa), além dos seus vídeos

## ⚠️ Limitações conhecidas

- O executável **não é assinado digitalmente**, por isso o aviso do SmartScreen na primeira execução.
- Vídeos **privados**, com **restrição de idade** ou **transmissões ao vivo** do YouTube não podem ser baixados.
- O YouTube muda com frequência; se os downloads falharem, use **"Atualizar yt-dlp"** na Administração.

## 🔒 Verificar os arquivos (opcional)

Para conferir se o download chegou íntegro, compare o hash SHA-256 rodando no PowerShell:

```powershell
Get-FileHash .\MiniFlix-1.0.0-win-x64.zip
```

| Arquivo | SHA-256 |
|---|---|
| `MiniFlix-1.0.0-win-x64.zip` | `{{SHA256_COMPLETO}}` |
| `MiniFlix-1.0.0-win-x64-leve.zip` | `{{SHA256_LEVE}}` |

## ⚖️ Uso responsável e licenças

Baixe apenas conteúdo **seu, licenciado ou com permissão do autor**, respeitando os termos de uso do YouTube e os direitos autorais. O MiniFlix não tem vínculo com o YouTube nem com serviços de streaming.

Componentes incluídos: [FFmpeg](https://ffmpeg.org) (GPL v3; código-fonte em [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds)), [yt-dlp](https://github.com/yt-dlp/yt-dlp) (Unlicense), [Deno](https://deno.com) (MIT), [Microsoft Edge WebView2](https://developer.microsoft.com/microsoft-edge/webview2/) (licença Microsoft), [.NET](https://dotnet.microsoft.com) (MIT) e ícones [Material Symbols](https://fonts.google.com/icons) (Apache 2.0). Os textos das licenças estão em `bin\licencas`.
