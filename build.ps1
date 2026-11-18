<#
  Gera a pasta portátil em dist\MiniFlix (pronta para copiar para um pendrive).

  Uso:  .\build.ps1                 # publish Release
        .\build.ps1 -VideoTeste     # + gera um vídeo de teste grande em dist\MiniFlix\videos
#>
param([switch] $VideoTeste)
$ErrorActionPreference = 'Stop'
$raiz = $PSScriptRoot
$dist = Join-Path $raiz 'dist\MiniFlix'

dotnet publish (Join-Path $raiz 'src\MiniFlix\MiniFlix.csproj') -c Release -r win-x64 --self-contained true -o $dist
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish falhou' }

# Binários de terceiros (yt-dlp, ffmpeg, deno, webview2) ficam em vendor\bin e são copiados para \bin
$vendorBin = Join-Path $raiz 'vendor\bin'
if (Test-Path $vendorBin) {
  Write-Host 'Copiando vendor\bin -> bin ...'
  robocopy $vendorBin (Join-Path $dist 'bin') /E /NFL /NDL /NJH /NJS /NP | Out-Null
  if ($LASTEXITCODE -ge 8) { throw "robocopy falhou ($LASTEXITCODE)" }
} else {
  Write-Warning 'vendor\bin não encontrado: o app usará o WebView2 instalado no sistema.'
}

foreach ($d in 'videos', 'data') { New-Item -ItemType Directory -Force (Join-Path $dist $d) | Out-Null }

if ($VideoTeste) { & (Join-Path $raiz 'tools\gerar-video-teste.ps1') -Raiz $dist }

$tam = (Get-ChildItem $dist -Recurse -File -Exclude *.mp4 | Measure-Object Length -Sum).Sum
Write-Host ("`nPronto: {0}  ({1:N0} MB sem vídeos)" -f $dist, ($tam / 1MB))
