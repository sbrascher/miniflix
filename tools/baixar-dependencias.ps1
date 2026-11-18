<#
  Baixa as ferramentas externas para vendor\bin (o build.ps1 as copia para a pasta bin do app portátil):
    - yt-dlp.exe              download de vídeos (precisa ser atualizado com frequência)
    - deno.exe                runtime JavaScript exigido pelo yt-dlp para o YouTube
    - ffmpeg.exe, ffprobe.exe conversão e análise de vídeos (build GPL do BtbN)

    - webview2\               WebView2 Fixed Version Runtime x64 (com -WebView2 ou -SoWebView2)

  O WebView2 Fixed Version não tem link estável: a página oficial monta os links por
  JavaScript. O script renderiza a página com o Edge em modo headless, pega o .cab x64
  mais recente (domínio de downloads da Microsoft), confere a assinatura digital da
  Microsoft e extrai em vendor\bin\webview2.

  Também salva os textos de licença em vendor\bin\licencas (o FFmpeg é GPL: a licença
  precisa acompanhar os binários distribuídos).

  Uso:  .\tools\baixar-dependencias.ps1 [-WebView2] [-SoWebView2] [-SoYtDlp] [-SoLicencas]
#>
param(
  [string] $VersaoYtDlp  = '2026.10.26',
  [string] $VersaoDeno   = 'v2.9.8',
  [string] $RamoFfmpeg   = '8.1',
  [switch] $WebView2,
  [switch] $SoWebView2,
  [switch] $ManterTodosIdiomas,   # por padrão o WebView2 fica só com pt-BR e en-US (economiza ~130 MB)
  [switch] $SoYtDlp,
  [switch] $SoLicencas
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest fica muito mais rápido sem a barra
$raiz = Split-Path $PSScriptRoot -Parent
$bin = Join-Path $raiz 'vendor\bin'
$tmp = Join-Path $raiz 'vendor\_download'
New-Item -ItemType Directory -Force $bin, $tmp | Out-Null

function Baixar($url, $destino) {
  Write-Host "  $url"
  Invoke-WebRequest $url -OutFile $destino -UseBasicParsing
}

function BaixarLicencas {
  $dir = Join-Path $bin 'licencas'
  New-Item -ItemType Directory -Force $dir | Out-Null
  Write-Host 'Licenças'
  Baixar "https://raw.githubusercontent.com/FFmpeg/FFmpeg/n$RamoFfmpeg/COPYING.GPLv3" (Join-Path $dir 'FFmpeg-GPLv3.txt')
  Baixar "https://raw.githubusercontent.com/yt-dlp/yt-dlp/$VersaoYtDlp/LICENSE" (Join-Path $dir 'yt-dlp-Unlicense.txt')
  Baixar "https://raw.githubusercontent.com/denoland/deno/$VersaoDeno/LICENSE.md" (Join-Path $dir 'Deno-MIT.txt')
  @"
Licenças dos componentes de terceiros distribuídos na pasta bin do MiniFlix.

FFmpeg $RamoFfmpeg (GPL v3) - https://ffmpeg.org
  Compilação usada: https://github.com/BtbN/FFmpeg-Builds (build gpl-shared)
  O código-fonte correspondente está disponível em https://github.com/FFmpeg/FFmpeg
  (tag n$RamoFfmpeg) e os scripts de compilação em https://github.com/BtbN/FFmpeg-Builds.
yt-dlp $VersaoYtDlp (Unlicense) - https://github.com/yt-dlp/yt-dlp
Deno $VersaoDeno (MIT) - https://deno.com
Microsoft Edge WebView2 Runtime (licença Microsoft) - https://developer.microsoft.com/microsoft-edge/webview2/
"@ | Set-Content (Join-Path $dir 'LEIA-ME.txt') -Encoding utf8
}

# Traduções da interface do Chromium (menus de contexto, diálogos). O app é em português e o
# Chromium usa en-US como alternativa, então os outros 80+ idiomas são peso morto no pendrive.
function ReduzirIdiomasWebView2($pasta) {
  $locales = Join-Path $pasta 'Locales'
  if (-not (Test-Path $locales)) { return }
  $antes = (Get-ChildItem $locales -File | Measure-Object Length -Sum).Sum
  Get-ChildItem $locales -File | Where-Object { $_.BaseName -notin 'pt-BR', 'en-US' } | Remove-Item
  $depois = (Get-ChildItem $locales -File | Measure-Object Length -Sum).Sum
  Write-Host ("  idiomas: mantidos pt-BR e en-US ({0:N0} MB economizados)" -f (($antes - $depois) / 1MB))
}

function BaixarWebView2 {
  Write-Host 'WebView2 Fixed Version Runtime (x64)'
  $edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
            "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
  if (-not $edge) { throw 'Microsoft Edge não encontrado (necessário para ler a página de download do WebView2).' }

  # 1. Renderiza a página oficial e extrai os links dos .cab
  # Os links vêm escapados no JSON da página (barra = contrabarra + "u002F"). A sequência é
  # montada a partir do código do caractere para não depender de escapes no próprio script.
  $barraEscapada = [string][char]92 + 'u002F'

  # Por garantia, até 3 tentativas (a renderização depende de rede e do tempo da página).
  $perfil = Join-Path $tmp 'edge-perfil'
  $links = $null
  for ($tentativa = 1; $tentativa -le 3 -and -not $links; $tentativa++) {
    $dom = & $edge --headless=new --disable-gpu --no-first-run --user-data-dir="$perfil" --virtual-time-budget=25000 `
             --dump-dom 'https://developer.microsoft.com/en-us/microsoft-edge/webview2/' 2>$null | Out-String
    $links = [regex]::Matches($dom.Replace($barraEscapada, '/'),
               'https://msedge\.sf\.dl\.delivery\.mp\.microsoft\.com/filestreamingservice/files/[0-9a-f-]+/Microsoft\.WebView2\.FixedVersionRuntime\.([\d.]+)\.x64\.cab') |
             ForEach-Object { [pscustomobject]@{ Url = $_.Value; Versao = [version]$_.Groups[1].Value } } |
             Sort-Object Versao -Descending
    if (-not $links) { Write-Host "  tentativa $tentativa sem links (página com $($dom.Length) caracteres), repetindo..." }
  }
  if (-not $links) { throw 'Não encontrei o link do WebView2 na página. Baixe manualmente em https://developer.microsoft.com/microsoft-edge/webview2/ e extraia em vendor\bin\webview2.' }
  $escolhido = $links[0]
  Write-Host "  versão $($escolhido.Versao)"

  # 2. Baixa e confere a assinatura digital (tem que ser da Microsoft)
  $cab = Join-Path $tmp 'webview2.cab'
  Baixar $escolhido.Url $cab
  $assinatura = Get-AuthenticodeSignature $cab
  if ($assinatura.Status -ne 'Valid' -or $assinatura.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
    throw "Assinatura inválida no pacote do WebView2 ($($assinatura.Status), $($assinatura.SignerCertificate.Subject))."
  }
  Write-Host "  assinatura OK: $($assinatura.SignerCertificate.Subject -replace ',.*', '')"

  # 3. Extrai (o .cab contém uma pasta Microsoft.WebView2.FixedVersionRuntime.<versão>.x64)
  $extraido = Join-Path $tmp 'webview2'
  New-Item -ItemType Directory -Force $extraido | Out-Null
  & expand.exe $cab -F:* $extraido | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "expand.exe falhou ($LASTEXITCODE)" }
  $exe = Get-ChildItem $extraido -Recurse -Filter msedgewebview2.exe | Select-Object -First 1
  if (-not $exe) { throw 'msedgewebview2.exe não encontrado no pacote extraído.' }

  # Troca o conteúdo (não a pasta em si): funciona mesmo com a pasta aberta no Explorer/terminal.
  $destino = Join-Path $bin 'webview2'
  New-Item -ItemType Directory -Force $destino | Out-Null
  Get-ChildItem $destino -Force | Remove-Item -Recurse -Force
  Get-ChildItem $exe.Directory.FullName -Force | Move-Item -Destination $destino
  Set-Content (Join-Path $destino 'VERSAO.txt') "$($escolhido.Versao)`r`n$($escolhido.Url)" -Encoding utf8
  if (-not $ManterTodosIdiomas) { ReduzirIdiomasWebView2 $destino }
  Write-Host ("  {0:N0} MB em vendor\bin\webview2" -f ((Get-ChildItem $destino -Recurse -File | Measure-Object Length -Sum).Sum / 1MB))
}

try {
  if ($SoWebView2) { BaixarWebView2; return }
  BaixarLicencas
  if ($SoLicencas) { return }
  if ($WebView2) { BaixarWebView2 }

  Write-Host "yt-dlp $VersaoYtDlp"
  Baixar "https://github.com/yt-dlp/yt-dlp/releases/download/$VersaoYtDlp/yt-dlp.exe" (Join-Path $bin 'yt-dlp.exe')
  if ($SoYtDlp) { return }

  Write-Host "deno $VersaoDeno"
  $zip = Join-Path $tmp 'deno.zip'
  Baixar "https://github.com/denoland/deno/releases/download/$VersaoDeno/deno-x86_64-pc-windows-msvc.zip" $zip
  Expand-Archive $zip -DestinationPath $tmp -Force
  Move-Item (Join-Path $tmp 'deno.exe') (Join-Path $bin 'deno.exe') -Force

  # Build "shared": ffmpeg.exe e ffprobe.exe pequenos + DLLs comuns (~1/3 do tamanho dos estáticos).
  Write-Host "ffmpeg $RamoFfmpeg (BtbN, GPL, shared)"
  $nome = "ffmpeg-n$RamoFfmpeg-latest-win64-gpl-shared-$RamoFfmpeg"
  $zip = Join-Path $tmp 'ffmpeg.zip'
  Baixar "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/$nome.zip" $zip
  Expand-Archive $zip -DestinationPath $tmp -Force
  Get-ChildItem (Join-Path $bin '*.dll') -ErrorAction SilentlyContinue | Remove-Item
  Get-ChildItem (Join-Path $tmp "$nome\bin") -File |
    Where-Object { $_.Name -ne 'ffplay.exe' } |
    Move-Item -Destination $bin -Force
}
finally {
  Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "`nConteúdo de vendor\bin:"
Get-ChildItem $bin -File | ForEach-Object { '  {0,-14} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }
if (-not (Test-Path (Join-Path $bin 'webview2'))) {
  Write-Host "`nLembrete: o WebView2 Fixed Runtime (vendor\bin\webview2) precisa ser baixado manualmente." -ForegroundColor Yellow
}
