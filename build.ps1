<#
  Build do MiniFlix.

  Uso:  .\build.ps1                  # CSS + publish Release em dist\MiniFlix (pasta de uso/teste local)
        .\build.ps1 -SoCss           # só recompila src\ui\css\app.css
        .\build.ps1 -VideoTeste      # + gera um vídeo de teste grande em dist\MiniFlix\videos
        .\build.ps1 -AcervoTeste     # + gera um acervo de exemplo (vídeos curtos com capa e metadados)
        .\build.ps1 -Pacote          # gera os ZIPs para distribuição em dist\pacotes:
                                     #   MiniFlix-<versão>-win-x64.zip       (completo, com WebView2)
                                     #   MiniFlix-<versão>-win-x64-leve.zip  (usa o WebView2 do Windows)
                                     #   SHA256SUMS.txt

  Os pacotes são montados a partir de uma publicação nova e limpa: nada de dist\MiniFlix
  (vídeos, logs, progresso, perfil do navegador) vai parar neles.
#>
param([switch] $SoCss, [switch] $VideoTeste, [switch] $AcervoTeste, [switch] $Pacote)
$ErrorActionPreference = 'Stop'
$raiz = $PSScriptRoot
$csproj = Join-Path $raiz 'src\MiniFlix\MiniFlix.csproj'
$vendorBin = Join-Path $raiz 'vendor\bin'

# ---------------------------------------------------------------- funções

function Compilar-Css {
  $tailwind = Join-Path $raiz 'tools\tailwindcss.exe'
  if (Test-Path $tailwind) {
    Write-Host 'Compilando Tailwind ...'
    Push-Location (Join-Path $raiz 'src\ui')
    # Sem redirecionar stderr: no Windows PowerShell 5.1, "2>&1" + ErrorActionPreference=Stop
    # transforma o banner do Tailwind (escrito em stderr) em erro fatal.
    try { & $tailwind -i css\input.css -o css\app.css --minify }
    finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw 'Tailwind falhou' }
  } elseif ($SoCss) {
    throw 'tools\tailwindcss.exe não encontrado. Rode .\tools\baixar-tailwind.ps1'
  } else {
    Write-Warning 'tools\tailwindcss.exe não encontrado: usando o src\ui\css\app.css já versionado.'
  }
}

function Obter-Versao {
  ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}

function Publicar($destino) {
  # Remove restos de publicações anteriores na raiz (ex.: as ~230 DLLs de antes do arquivo
  # único) sem tocar em bin\, data\ e videos\, que guardam ferramentas e dados do usuário.
  if (Test-Path $destino) {
    Get-ChildItem $destino -File | Remove-Item -Force
    foreach ($d in 'pt-BR', 'runtimes', 'ui') {
      $p = Join-Path $destino $d
      if (Test-Path $p) { Remove-Item $p -Recurse -Force }
    }
  }

  dotnet publish $csproj -c Release -r win-x64 --self-contained true -o $destino
  if ($LASTEXITCODE -ne 0) { throw 'dotnet publish falhou' }

  # A raiz fica só com o MiniFlix.exe: o loader nativo do WebView2 vai para bin\
  # (o app aponta para lá com CoreWebView2Environment.SetLoaderDllFolderPath).
  $loader = Join-Path $destino 'WebView2Loader.dll'
  if (Test-Path $loader) {
    New-Item -ItemType Directory -Force (Join-Path $destino 'bin') | Out-Null
    Move-Item $loader (Join-Path $destino 'bin\WebView2Loader.dll') -Force
  }
}

function Copiar-Pasta($origem, $destino, [string[]] $excluirPastas = @()) {
  $parametros = @($origem, $destino, '/E', '/NFL', '/NDL', '/NJH', '/NJS', '/NP')
  if ($excluirPastas.Count) { $parametros += '/XD'; $parametros += $excluirPastas }
  robocopy @parametros | Out-Null
  if ($LASTEXITCODE -ge 8) { throw "robocopy falhou ($LASTEXITCODE): $origem -> $destino" }
}

# README.txt para o usuário final, em UTF-8 com BOM e CRLF (abre certo no Bloco de Notas).
function Escrever-Leiame($destino, $variante) {
  $texto = [IO.File]::ReadAllText((Join-Path $raiz 'pacote\README.txt'))
  $texto = $texto.Replace('{{VERSAO}}', (Obter-Versao)).Replace('{{DATA}}', (Get-Date -Format 'dd/MM/yyyy')).Replace('{{VARIANTE}}', $variante)
  $texto = $texto -replace "`r?`n", "`r`n"
  [IO.File]::WriteAllText((Join-Path $destino 'README.txt'), $texto, (New-Object System.Text.UTF8Encoding($true)))
}

$textoCompleto = 'Versão COMPLETA: inclui o WebView2 e funciona em qualquer Windows 10/11 de 64 bits.'
$textoLeve = 'Versão LEVE: usa o WebView2 já instalado no Windows (presente no Windows 11 e na maioria dos Windows 10 atualizados).'

function Tamanho-MB($pasta) { (Get-ChildItem $pasta -Recurse -File | Measure-Object Length -Sum).Sum / 1MB }

# ---------------------------------------------------------------- CSS

Compilar-Css
if ($SoCss) { return }

# ---------------------------------------------------------------- pacotes de distribuição

if ($Pacote) {
  # Tudo o que precisa ir junto. Falta algo → para antes de gerar um pacote quebrado.
  $obrigatorios = 'yt-dlp.exe', 'deno.exe', 'ffmpeg.exe', 'ffprobe.exe', 'licencas\FFmpeg-GPLv3.txt', 'webview2\msedgewebview2.exe'
  $faltando = $obrigatorios | Where-Object { -not (Test-Path (Join-Path $vendorBin $_)) }
  if ($faltando) {
    throw "Faltam em vendor\bin: $($faltando -join ', '). Rode .\tools\baixar-dependencias.ps1 -WebView2"
  }

  $versao = Obter-Versao
  $saida = Join-Path $raiz 'dist\pacotes'
  $montagem = Join-Path $saida '_montagem'
  if (Test-Path $montagem) { Remove-Item $montagem -Recurse -Force }
  New-Item -ItemType Directory -Force $saida, $montagem | Out-Null

  Write-Host "`n== Publicando versão $versao (pasta limpa) ..."
  $base = Join-Path $montagem 'base'
  Publicar $base

  Add-Type -AssemblyName System.IO.Compression.FileSystem
  $variantes = @(
    @{ Nome = "MiniFlix-$versao-win-x64.zip";      SemWebView2 = $false; Texto = $textoCompleto },
    @{ Nome = "MiniFlix-$versao-win-x64-leve.zip"; SemWebView2 = $true;  Texto = $textoLeve }
  )
  $resumo = @()

  foreach ($v in $variantes) {
    Write-Host "`n== Montando $($v.Nome) ..."
    # A pasta de dentro do ZIP se chama MiniFlix: ao extrair, a pessoa recebe MiniFlix\MiniFlix.exe.
    $pasta = Join-Path $montagem ([IO.Path]::GetFileNameWithoutExtension($v.Nome)) | Join-Path -ChildPath 'MiniFlix'
    Copiar-Pasta $base $pasta
    $excluir = if ($v.SemWebView2) { @('webview2') } else { @() }
    Copiar-Pasta $vendorBin (Join-Path $pasta 'bin') $excluir
    Escrever-Leiame $pasta $v.Texto
    # videos\ e data\ não vão no pacote: o app as cria na primeira execução.

    $zip = Join-Path $saida $v.Nome
    if (Test-Path $zip) { Remove-Item $zip -Force }
    [IO.Compression.ZipFile]::CreateFromDirectory($pasta, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)

    # Conferência: estrutura esperada e nenhum dado pessoal dentro do ZIP.
    $arq = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
      $nomes = $arq.Entries | ForEach-Object { $_.FullName.Replace('\', '/') }
      $proibidos = $nomes | Where-Object { $_ -match '^MiniFlix/(data|videos)/' -or $_ -match '\.mp4$' }
      if ($proibidos) { throw "O pacote contém arquivos que não deveriam estar lá: $($proibidos | Select-Object -First 3)" }
      foreach ($esperado in 'MiniFlix/MiniFlix.exe', 'MiniFlix/README.txt', 'MiniFlix/ui/index.html',
                            'MiniFlix/bin/yt-dlp.exe', 'MiniFlix/bin/WebView2Loader.dll') {
        if ($nomes -notcontains $esperado) { throw "Falta $esperado em $($v.Nome)" }
      }
      # Na raiz, só o executável e o manual: é o que a pessoa vê ao abrir a pasta.
      $soltos = $nomes | Where-Object { $_ -match '^MiniFlix/[^/]+$' } | Sort-Object
      if (($soltos -join ',') -ne 'MiniFlix/MiniFlix.exe,MiniFlix/README.txt') {
        throw "Arquivos inesperados na raiz de $($v.Nome): $($soltos -join ', ')"
      }
      $temWebView2 = [bool]($nomes -contains 'MiniFlix/bin/webview2/msedgewebview2.exe')
      if ($temWebView2 -eq $v.SemWebView2) { throw "WebView2 incorreto em $($v.Nome)" }
      $qtd = $nomes.Count
    } finally { $arq.Dispose() }

    $resumo += [pscustomobject]@{
      Pacote       = $v.Nome
      'Extraído MB' = [math]::Round((Tamanho-MB $pasta))
      'ZIP MB'     = [math]::Round((Get-Item $zip).Length / 1MB)
      Arquivos     = $qtd
    }
  }

  # Hashes para quem baixar conferir a integridade (Get-FileHash arquivo.zip).
  $somas = Get-ChildItem $saida -Filter "MiniFlix-$versao-*.zip" | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name
  }
  Set-Content (Join-Path $saida 'SHA256SUMS.txt') $somas -Encoding ascii

  Remove-Item $montagem -Recurse -Force
  Write-Host "`n== Pacotes em $saida"
  $resumo | Format-Table -AutoSize | Out-String | Write-Host
  return
}

# ---------------------------------------------------------------- pasta local (dist\MiniFlix)

$dist = Join-Path $raiz 'dist\MiniFlix'
$aberto = Get-Process MiniFlix -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $dist 'MiniFlix.exe') }
if ($aberto) { throw "O MiniFlix está aberto a partir de $dist. Feche-o e rode o build novamente." }

Publicar $dist

if (Test-Path $vendorBin) {
  Write-Host 'Copiando vendor\bin -> bin ...'
  Copiar-Pasta $vendorBin (Join-Path $dist 'bin')
} else {
  Write-Warning 'vendor\bin não encontrado: o app usará o WebView2 instalado no sistema.'
}

foreach ($d in 'videos', 'data') { New-Item -ItemType Directory -Force (Join-Path $dist $d) | Out-Null }
$temWebView2Local = Test-Path (Join-Path $dist 'bin\webview2\msedgewebview2.exe')
Escrever-Leiame $dist $(if ($temWebView2Local) { $textoCompleto } else { $textoLeve })
if (-not (Test-Path (Join-Path $dist 'bin\licencas'))) {
  Write-Warning 'bin\licencas não encontrado: rode .\tools\baixar-dependencias.ps1 -SoLicencas antes de distribuir.'
}

if ($VideoTeste)  { & (Join-Path $raiz 'tools\gerar-video-teste.ps1') -Raiz $dist }
if ($AcervoTeste) { & (Join-Path $raiz 'tools\gerar-acervo-teste.ps1') -Raiz $dist }

$tam = (Get-ChildItem $dist -Recurse -File -Exclude *.mp4 | Measure-Object Length -Sum).Sum
Write-Host ("`nPronto: {0}  ({1:N0} MB sem vídeos)" -f $dist, ($tam / 1MB))
