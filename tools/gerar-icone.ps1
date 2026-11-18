<#
  Gera o ícone do Windows (.ico com vários tamanhos) a partir dos SVGs em assets\icone:
    - miniflix.svg          → 48, 64, 128, 256 px (versão detalhada)
    - miniflix-pequeno.svg  → 16, 20, 24, 32 px   (versão simplificada, legível em tamanho pequeno)

  Saídas:
    src\MiniFlix\miniflix.ico      ícone do executável e da janela
    src\ui\img\logo.svg            logotipo do cabeçalho da interface
    assets\icone\miniflix-256.png  imagem para o README

  Renderiza com o Microsoft Edge em modo headless (fundo transparente) e reduz com
  reamostragem bicúbica de alta qualidade. Cada tamanho vai no .ico como PNG (Windows Vista+).

  Uso:  .\tools\gerar-icone.ps1
#>
$ErrorActionPreference = 'Stop'
$raiz = Split-Path $PSScriptRoot -Parent
$pastaIcone = Join-Path $raiz 'assets\icone'
Add-Type -AssemblyName System.Drawing

$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
          "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge não encontrado (usado para renderizar o SVG).' }

$tmp = Join-Path ([IO.Path]::GetTempPath()) ("miniflix-icone-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $tmp | Out-Null

# Renderiza um SVG em PNG quadrado com fundo transparente.
function Renderizar($svg, $lado) {
  $png = Join-Path $tmp ("{0}-{1}.png" -f [IO.Path]::GetFileNameWithoutExtension($svg), $lado)
  $url = 'file:///' + ((Resolve-Path $svg).Path -replace '\\', '/')
  & $edge --headless=new --disable-gpu --hide-scrollbars --default-background-color=00000000 `
    --user-data-dir="$(Join-Path $tmp 'perfil')" --window-size="$lado,$lado" --screenshot="$png" $url 2>$null | Out-Null
  for ($i = 0; $i -lt 50 -and -not (Test-Path $png); $i++) { Start-Sleep -Milliseconds 100 }
  if (-not (Test-Path $png)) { throw "Falha ao renderizar $svg" }
  return $png
}

# Reduz uma imagem grande para o tamanho final (qualidade máxima) e devolve os bytes PNG.
function Reduzir($origemPng, $lado) {
  $origem = [Drawing.Image]::FromFile($origemPng)
  try {
    $bmp = New-Object Drawing.Bitmap $lado, $lado, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
    $g.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $atributos = New-Object Drawing.Imaging.ImageAttributes
    $atributos.SetWrapMode([Drawing.Drawing2D.WrapMode]::TileFlipXY)   # evita borda clara no contorno
    $g.DrawImage($origem, (New-Object Drawing.Rectangle 0, 0, $lado, $lado), 0, 0, $origem.Width, $origem.Height,
                 [Drawing.GraphicsUnit]::Pixel, $atributos)
    $g.Dispose()
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
  } finally { $origem.Dispose() }
}

# Monta o .ico: cabeçalho + diretório (16 bytes por imagem) + dados PNG.
function Escrever-Ico($destino, $imagens) {
  $ms = New-Object IO.MemoryStream
  $w = New-Object IO.BinaryWriter $ms
  $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$imagens.Count)
  $deslocamento = 6 + 16 * $imagens.Count
  foreach ($img in $imagens) {
    $lado = if ($img.Lado -ge 256) { 0 } else { $img.Lado }   # 0 significa 256
    $w.Write([byte]$lado); $w.Write([byte]$lado); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$img.Bytes.Length); $w.Write([uint32]$deslocamento)
    $deslocamento += $img.Bytes.Length
  }
  foreach ($img in $imagens) { $w.Write($img.Bytes) }
  $w.Flush()
  [IO.File]::WriteAllBytes($destino, $ms.ToArray())
}

try {
  $svgGrande = Join-Path $pastaIcone 'miniflix.svg'
  $svgPequeno = Join-Path $pastaIcone 'miniflix-pequeno.svg'
  $grande = Renderizar $svgGrande 1024
  $pequeno = Renderizar $svgPequeno 512

  $imagens = @()
  foreach ($lado in 16, 20, 24, 32) { $imagens += @{ Lado = $lado; Bytes = (Reduzir $pequeno $lado) } }
  foreach ($lado in 48, 64, 128, 256) { $imagens += @{ Lado = $lado; Bytes = (Reduzir $grande $lado) } }

  $ico = Join-Path $raiz 'src\MiniFlix\miniflix.ico'
  Escrever-Ico $ico $imagens
  [IO.File]::WriteAllBytes((Join-Path $pastaIcone 'miniflix-256.png'), ($imagens | Where-Object { $_.Lado -eq 256 }).Bytes)

  $pastaImg = Join-Path $raiz 'src\ui\img'
  New-Item -ItemType Directory -Force $pastaImg | Out-Null
  Copy-Item $svgPequeno (Join-Path $pastaImg 'logo.svg') -Force

  Write-Host ("Ícone gerado: {0} ({1} tamanhos, {2:N0} KB)" -f $ico, $imagens.Count, ((Get-Item $ico).Length / 1KB))
}
finally {
  Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
