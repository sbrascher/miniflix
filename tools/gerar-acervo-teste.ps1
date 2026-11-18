<#
  Gera um acervo de exemplo em <Raiz>\videos: vídeos curtos sintéticos (ffmpeg) com capa .jpg e metadados .json,
  no mesmo formato que o MiniFlix grava. Serve para desenvolver e testar o catálogo sem internet.

  Uso:  .\tools\gerar-acervo-teste.ps1 -Raiz .\dist\MiniFlix [-Segundos 20]
#>
param(
  [Parameter(Mandatory)] [string] $Raiz,
  [int] $Segundos = 20
)
$ErrorActionPreference = 'Stop'

$ffmpeg = Join-Path $Raiz 'bin\ffmpeg.exe'
if (-not (Test-Path $ffmpeg)) { $ffmpeg = (Get-Command ffmpeg -ErrorAction Stop).Source }

# slug, título, categoria, fonte lavfi, descrição
$acervo = @(
  @('aula-01-introducao',          'Aula #01: Introdução!',                    'Cursos',        'testsrc2',    "Primeira aula do curso.`nVisão geral do conteúdo e dos materiais."),
  @('aula-02-variaveis-e-tipos',   'Aula #02: Variáveis e Tipos',              'Cursos',        'testsrc2',    'Tipos primitivos, conversões e boas práticas de nomes.'),
  @('aula-03-funcoes',             'Aula #03: Funções',                        'Cursos',        'testsrc2',    'Parâmetros, retorno e escopo.'),
  @('aula-04-listas-e-lacos',      'Aula #04: Listas e Laços de Repetição',    'Cursos',        'testsrc2',    ''),
  @('oceanos-profundos',           'Oceanos Profundos: a Vida no Escuro',      'Documentários', 'mandelbrot',  'Uma viagem às fossas oceânicas e às criaturas bioluminescentes.'),
  @('a-historia-do-cafe',          'A História do Café',                       'Documentários', 'cellauto',    'Da Etiópia ao Brasil: como o café conquistou o mundo.'),
  @('vulcoes-ativos',              'Vulcões Ativos do Planeta',                'Documentários', 'mandelbrot',  ''),
  @('bolo-de-cenoura-com-chocolate','Bolo de Cenoura com Cobertura de Chocolate','Receitas',     'smptehdbars', "Ingredientes:`n- 3 cenouras`n- 4 ovos`n- 2 xícaras de farinha"),
  @('pao-caseiro-sem-sova',        'Pão Caseiro Sem Sova',                     'Receitas',      'smptehdbars', 'Receita fácil, fermentação longa na geladeira.'),
  @('violao-para-iniciantes',      'Violão para Iniciantes — Acordes Básicos', 'Música',        'life',        'Dó, Ré, Mi, Sol e Lá: os primeiros acordes.'),
  @('teoria-musical-escalas',      'Teoria Musical: Escalas Maiores',          'Música',        'life',        ''),
  @('video-sem-categoria',         'Vídeo Sem Categoria',                      '',              'rgbtestsrc',  'Este vídeo não tem categoria definida.'),
  @('sem-capa-exemplo',            'Exemplo sem capa (usa a capa substituta)', 'Cursos',        'testsrc',     'Este vídeo não tem arquivo .jpg.')
)

$pastaVideos = Join-Path $Raiz 'videos'
New-Item -ItemType Directory -Force $pastaVideos | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)
$i = 0

foreach ($a in $acervo) {
  $slug, $titulo, $categoria, $fonte, $descricao = $a
  $i++
  $dir = Join-Path $pastaVideos $slug
  New-Item -ItemType Directory -Force $dir | Out-Null
  $mp4 = Join-Path $dir "$slug.mp4"
  $jpg = Join-Path $dir "$slug.jpg"
  $hue = ($i * 47) % 360

  Write-Host "[$i/$($acervo.Count)] $slug"
  $src = switch ($fonte) {
    'life'     { "life=size=1280x720:rate=30:mold=10:ratio=0.3:death_color=#202040:life_color=#ff9040" }
    'cellauto' { "cellauto=size=1280x720:rate=30:rule=110" }
    default    { "${fonte}=size=1280x720:rate=30" }
  }
  & $ffmpeg -hide_banner -loglevel error -y `
    -f lavfi -i $src -f lavfi -i "sine=frequency=$(220 + $i * 40)" -t $Segundos `
    -vf "hue=h=${hue},format=yuv420p" -c:v libx264 -preset veryfast -crf 28 -c:a aac -b:a 96k -movflags +faststart $mp4
  if ($LASTEXITCODE -ne 0) { throw "ffmpeg falhou em $slug" }

  if ($slug -ne 'sem-capa-exemplo') {
    & $ffmpeg -hide_banner -loglevel error -y -ss ($Segundos / 2) -i $mp4 -frames:v 1 -vf "scale=640:-2" -q:v 4 $jpg
  } elseif (Test-Path $jpg) { Remove-Item $jpg }

  $meta = [ordered]@{
    id              = "local:exemplo-$slug"
    slug            = $slug
    titulo          = $titulo
    descricao       = $descricao
    categoria       = $categoria
    origem          = 'local'
    urlOrigem       = $null
    duracaoSegundos = $Segundos
    dataPublicacao  = (Get-Date).AddDays(-$i * 9).ToString('yyyy-MM-dd')
    dataAdicionado  = (Get-Date).AddMinutes(-$i).ToString('s')
    versao          = 1
  }
  [IO.File]::WriteAllText((Join-Path $dir "$slug.json"), ($meta | ConvertTo-Json), $utf8)
}

Write-Host "Acervo de exemplo pronto: $($acervo.Count) vídeos em $pastaVideos"
