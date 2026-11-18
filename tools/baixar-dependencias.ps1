<#
  Baixa as ferramentas externas para vendor\bin (o build.ps1 as copia para a pasta bin do app portátil):
    - yt-dlp.exe              download de vídeos (precisa ser atualizado com frequência)
    - deno.exe                runtime JavaScript exigido pelo yt-dlp para o YouTube
    - ffmpeg.exe, ffprobe.exe conversão e análise de vídeos (build GPL do BtbN)

  O WebView2 Fixed Version Runtime não tem link estável: baixe manualmente em
  https://developer.microsoft.com/microsoft-edge/webview2/ e extraia em vendor\bin\webview2.

  Uso:  .\tools\baixar-dependencias.ps1 [-SoYtDlp]
#>
param(
  [string] $VersaoYtDlp  = '2026.10.26',
  [string] $VersaoDeno   = 'v2.9.8',
  [string] $RamoFfmpeg   = '8.1',
  [switch] $SoYtDlp
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

try {
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
