<#
  Gera um MP4 sintético grande (H.264 + AAC, faststart) para testar reprodução e seek.
  O padrão testsrc mostra um contador de tempo na imagem, o que facilita conferir o seek visualmente.

  Uso:  .\tools\gerar-video-teste.ps1 -Raiz .\dist\MiniFlix -Minutos 7 -MbpsVideo 25
#>
param(
  [Parameter(Mandatory)] [string] $Raiz,
  [int] $Minutos = 7,
  [int] $MbpsVideo = 25
)
$ErrorActionPreference = 'Stop'

$ffmpeg = Join-Path $Raiz 'bin\ffmpeg.exe'
if (-not (Test-Path $ffmpeg)) { $ffmpeg = (Get-Command ffmpeg -ErrorAction Stop).Source }

$destDir = Join-Path $Raiz 'videos\teste-grande'
New-Item -ItemType Directory -Force $destDir | Out-Null
$saida = Join-Path $destDir 'teste-grande.mp4'
$seg = $Minutos * 60

Write-Host "Gerando $saida ($Minutos min, ~$([math]::Round($MbpsVideo * $seg / 8 / 1024, 2)) GB)..."
& $ffmpeg -hide_banner -loglevel warning -stats -y `
  -f lavfi -i "testsrc=size=1280x720:rate=30:duration=$seg" `
  -f lavfi -i "sine=frequency=440:beep_factor=4:duration=$seg" `
  -vf "noise=alls=30:allf=t" `
  -c:v libx264 -preset ultrafast -pix_fmt yuv420p -b:v "${MbpsVideo}M" -maxrate "${MbpsVideo}M" -bufsize "$($MbpsVideo*2)M" `
  -c:a aac -b:a 128k -movflags +faststart `
  $saida
if ($LASTEXITCODE -ne 0) { throw "ffmpeg falhou ($LASTEXITCODE)" }
Write-Host ("Pronto: {0:N2} GB" -f ((Get-Item $saida).Length / 1GB))
