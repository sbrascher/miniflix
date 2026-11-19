==========================================================================
  MiniFlix {{VERSAO}}  -  seu catálogo de vídeos offline, no pendrive
==========================================================================

{{VARIANTE}}

O MiniFlix é um "mini streaming" que roda direto desta pasta, sem
instalar nada. Você adiciona os vídeos uma vez e depois assiste sem
internet, em qualquer computador com Windows 10 ou 11 (64 bits).

Nada é gravado fora desta pasta: nem no Registro do Windows, nem em
AppData. Ao remover o pendrive, nenhum rastro fica no computador.


--------------------------------------------------------------------------
 1. COMO ABRIR
--------------------------------------------------------------------------

  Se você recebeu um arquivo .zip, primeiro EXTRAIA tudo (botão direito
  no arquivo > "Extrair Tudo...") para o pendrive ou para uma pasta.
  O MiniFlix não funciona aberto de dentro do .zip.

  Dê dois cliques em  MiniFlix.exe

  Na primeira vez, o Windows pode mostrar "O Windows protegeu o
  computador" (SmartScreen). Isso acontece com programas que não são
  instalados pela loja. Clique em "Mais informações" e depois em
  "Executar assim mesmo".


--------------------------------------------------------------------------
 2. ADICIONAR VÍDEOS
--------------------------------------------------------------------------

  Clique na engrenagem (canto superior direito) para abrir a
  Administração.

  IMPORTAR DO COMPUTADOR (funciona sem internet)
    1. Clique em "Escolher arquivos..." e selecione um ou mais vídeos.
    2. Ajuste os títulos, escolha (ou digite) uma categoria e, se
       quiser, coloque tags (valem para todos os vídeos escolhidos).
    3. Clique em "Adicionar à fila".
    Antes de confirmar, cada arquivo mostra o que vai acontecer:
      * "Sem perda": vídeos que o MiniFlix já toca (H.264, VP9 ou AV1,
        inclusive os baixados do YouTube, em MP4, MKV ou WebM) são
        apenas copiados. Leva segundos e a qualidade fica idêntica.
      * Se só o áudio não toca (AC3, DTS, PCM...), o vídeo é copiado e
        o áudio vira FLAC, que também não perde qualidade.
      * "Precisa converter": HEVC/H.265 (comum em celulares), WMV, AVI
        antigo, ProRes... são convertidos em alta qualidade, na
        resolução original. Pode levar alguns minutos e o arquivo pode
        ficar maior que o original. A opção "Reduzir para 1080p" deixa
        o arquivo menor, mas perde detalhe.
    O arquivo original nunca é alterado.

  BAIXAR DO YOUTUBE (precisa de internet)
    1. Cole o link do vídeo no campo "Link do vídeo".
    2. Escolha a categoria e clique em "Baixar".
    O vídeo é salvo em até 1080p, com capa e descrição.

  A FILA
    Os vídeos são processados um por vez. A fila mostra o progresso e
    permite cancelar. Você pode continuar usando o MiniFlix enquanto
    ela trabalha; um ponto pulsante na engrenagem avisa que há algo em
    andamento.


--------------------------------------------------------------------------
 3. ASSISTIR
--------------------------------------------------------------------------

  Clique em qualquer capa. Use a busca (tecla / ) e o filtro de
  categoria no topo para encontrar vídeos. A busca procura no título,
  na descrição, nas tags e na categoria; os vídeos cujo título tem as
  palavras aparecem primeiro.

  RECOLHER CATEGORIAS
    Clique no nome de uma categoria para recolher (ou abrir de novo)
    os vídeos dela. "Recolher todas" / "Expandir todas" fica no alto.
    O MiniFlix lembra quais categorias você deixou recolhidas.

  FAVORITOS
    No player, clique em "Favorito" (estrela) ao lado do título. Os
    favoritos ganham uma estrela na capa e aparecem na fileira
    "Favoritos" do catálogo. Na Administração também há uma estrela
    em cada vídeo da tabela.

  FILTRAR POR TAG
    Se os vídeos tiverem tags, elas aparecem no alto do catálogo.
    Clique numa tag para ver só os vídeos com ela; clique em outras
    para combinar (aparecem os vídeos que têm TODAS as marcadas).
    "Limpar tags" desfaz o filtro. No player, clicar numa tag do
    vídeo também mostra o catálogo filtrado por ela.

  CONTINUAR ASSISTINDO
    O MiniFlix lembra onde você parou em cada vídeo. Os vídeos
    começados aparecem na fileira "Continuar assistindo" e voltam
    exatamente do ponto em que você parou. Vídeos vistos até o fim
    ganham o selo "Assistido".
    Para tirar um vídeo dessa fileira, passe o mouse sobre ele e clique
    no "X" do canto da capa (dá para desfazer logo em seguida).

  ATALHOS DO TECLADO (dentro do player)
    Espaço ou K ........ reproduzir / pausar
    Setas  <-  -> ...... voltar / avançar 5 segundos
    J  /  L ............ voltar / avançar 10 segundos
    Setas  cima/baixo .. volume
    M .................. tirar / devolver o som
    F .................. tela cheia
    <  /  > ............ diminuir / aumentar a velocidade
    0 a 9 .............. pular para 0% ... 90% do vídeo
    Esc ................ sair da tela cheia / voltar ao catálogo
    ? .................. mostrar a lista de atalhos

  TEMA
    O botão de sol/lua no topo alterna entre o tema claro e o escuro.


--------------------------------------------------------------------------
 4. GERENCIAR O ACERVO
--------------------------------------------------------------------------

  Na Administração, a tabela "Acervo" lista todos os vídeos. Ali você
  pode assistir, editar (título, categoria, tags e descrição) ou
  excluir. A exclusão apaga o vídeo do disco e não pode ser desfeita.

  RENOMEAR UMA CATEGORIA
    No painel "Categorias" da Administração, clique no lápis ao lado
    da categoria e digite o novo nome: todos os vídeos dela mudam de
    uma vez. Se o nome já for de outra categoria, as duas viram uma
    só. Em "Sem categoria", o lápis dá uma categoria a todos os vídeos
    que ainda não têm.

  TAGS
    Em Editar, no campo "Tags", digite uma palavra e tecle Enter (ou
    vírgula). Um vídeo pode ter várias tags (até 20), por exemplo
    "iniciante" e "aula ao vivo". Para tirar uma tag, clique no X dela.

  FILTRAR O ACERVO
    Acima da tabela "Acervo" dá para filtrar por texto, por categoria,
    por tags (o vídeo precisa ter todas as marcadas), por "Sem tags"
    (vídeos que ainda não têm nenhuma) e por "Só favoritos". Os
    filtros podem ser combinados; "Limpar filtros" desfaz tudo.

  TAGS EM VÁRIOS VÍDEOS DE UMA VEZ
    Na tabela "Acervo", marque as caixinhas à esquerda dos vídeos
    (Shift + clique marca um intervalo; a caixinha do cabeçalho marca
    todos os da lista). Na barra que aparece, use "Adicionar tags..."
    ou "Remover tags...". As outras tags de cada vídeo continuam.

  PASTA DOS VÍDEOS
    Por padrão os vídeos ficam na pasta "videos", ao lado do
    MiniFlix.exe. Em Administração > "Pasta dos vídeos" você pode
    escolher outra pasta: em outro disco, num HD externo etc.
      * Trocar a pasta não move nem apaga nada. Para levar os vídeos
        junto, copie as pastas deles para a pasta nova.
      * Só aparecem vídeos organizados pelo MiniFlix (uma pasta por
        vídeo). Arquivos de vídeo soltos devem ser trazidos por
        "Importar do computador".
      * Se a pasta escolhida ficar num disco desconectado, o catálogo
        avisa; conecte o disco e clique em "Tentar novamente".
      * "Voltar para a pasta padrão" volta a usar a pasta "videos".

  TROCAR A CAPA
    A capa é criada automaticamente (um quadro do vídeo, ou a miniatura
    do YouTube). Para trocar, clique em Editar e, na seção "Capa":
      * "Escolher imagem..." usa uma imagem do computador (JPG, PNG...);
      * "Escolher quadro do vídeo" mostra o vídeo com um controle
        deslizante: arraste até a cena desejada e clique em
        "Usar este quadro".
    A nova capa só é gravada ao clicar em "Salvar alterações".


--------------------------------------------------------------------------
 5. DICAS PARA O PENDRIVE
--------------------------------------------------------------------------

  * Formate o pendrive em exFAT (ou NTFS). O formato FAT32 não aceita
    arquivos com mais de 4 GB, o que impede vídeos longos.
    Atenção: formatar apaga tudo o que estiver no pendrive.

  * Feche o MiniFlix antes de remover o pendrive.

  * Pendrives USB 3.0 deixam a abertura e a conversão bem mais rápidas.

  * Para fazer backup ou levar o acervo para outro pendrive, copie a
    pasta inteira do MiniFlix. Cada vídeo fica numa pasta própria
    dentro de "videos", com o vídeo (.mp4), a capa (.jpg) e os
    dados (.json).


--------------------------------------------------------------------------
 6. O QUE HÁ NESTA PASTA
--------------------------------------------------------------------------

  MiniFlix.exe ... o programa
  README.txt ..... este guia
  bin\ ........... ferramentas usadas pelo MiniFlix (yt-dlp, ffmpeg,
                   deno e, na versão completa, o componente WebView2)
  ui\ ............ a interface do programa

  Criadas na primeira vez que o MiniFlix é aberto:
  videos\ ........ seus vídeos (uma pasta por vídeo), a menos que você
                   escolha outra pasta na Administração
  data\ .......... configurações, posição de cada vídeo e registros


--------------------------------------------------------------------------
 7. PROBLEMAS COMUNS
--------------------------------------------------------------------------

  "O MiniFlix precisa do Windows 10 ou 11 (64 bits)"
    O computador usa um Windows mais antigo (7, 8, 8.1) ou de 32 bits.
    Os componentes que o MiniFlix usa não existem mais para esses
    sistemas, então não há como rodá-lo nesse computador.

  "Este computador não tem o componente WebView2"
    Acontece com a versão LEVE em computadores sem o WebView2 (o motor
    do Microsoft Edge que o MiniFlix usa). Baixe a versão COMPLETA, que
    já o inclui, ou instale o "Microsoft Edge WebView2 Runtime" pelo
    site da Microsoft.

  "Não foi possível abrir a interface do MiniFlix"
    Normalmente o .zip não foi extraído por inteiro ou um antivírus
    bloqueou algum arquivo da pasta "bin". Extraia tudo de novo, numa
    pasta nova. O motivo técnico fica em data\logs\miniflix.log.

  O YouTube parou de baixar ("pediu uma verificação", "falha no
  download")
    O YouTube muda com frequência. Na Administração, clique em
    "Atualizar yt-dlp" (precisa de internet) e tente de novo.
    Vídeos privados, com restrição de idade ou transmissões ao vivo
    não podem ser baixados.

  Um vídeo importado não aparece
    Veja a fila na Administração: se houve erro, a mensagem explica o
    motivo. Se você copiou pastas de vídeo manualmente para "videos",
    clique em "Recarregar pasta".

  Não consigo excluir um vídeo ("está em uso")
    Feche o vídeo (volte ao catálogo) e tente novamente.

  Para qualquer outro problema, o arquivo data\logs\miniflix.log tem
  os detalhes técnicos do que aconteceu.


--------------------------------------------------------------------------
 8. PRIVACIDADE E USO RESPONSÁVEL
--------------------------------------------------------------------------

  O MiniFlix só acessa a internet quando você baixa um vídeo do
  YouTube ou atualiza o yt-dlp. Nenhum dado é enviado a terceiros.

  Baixe apenas conteúdo seu, licenciado ou com permissão do autor,
  respeitando os termos de uso do YouTube e os direitos autorais.
  O MiniFlix não tem vínculo com o YouTube nem com serviços de
  streaming.


--------------------------------------------------------------------------
 9. CRÉDITOS E LICENÇAS
--------------------------------------------------------------------------

  MiniFlix ............ https://github.com/sbrascher/miniflix

  Componentes de terceiros incluídos na pasta bin:
    FFmpeg ............ GPL v3 - https://ffmpeg.org
                        (compilação: https://github.com/BtbN/FFmpeg-Builds,
                        código-fonte disponível nesse endereço;
                        texto da licença em bin\licencas)
    yt-dlp ............ Unlicense - https://github.com/yt-dlp/yt-dlp
    Deno .............. MIT - https://deno.com
    Microsoft Edge
    WebView2 Runtime .. licença Microsoft - https://developer.microsoft.com/microsoft-edge/webview2/

  Também usa:
    .NET .............. MIT - https://dotnet.microsoft.com
    Material Symbols .. Apache 2.0 - https://fonts.google.com/icons

  Versão {{VERSAO}} - gerada em {{DATA}}
