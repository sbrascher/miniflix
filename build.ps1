<#
  Gera a pasta portátil em dist\MiniFlix (pronta para copiar para um pendrive).

  Uso:  .\build.ps1                  # compila o CSS (se houver tools\tailwindcss.exe) + publish Release
        .\build.ps1 -SoCss           # só recompila src\ui\css\app.css
        .\build.ps1 -VideoTeste      # + gera um vídeo de teste grande em dist\MiniFlix\videos
        .\build.ps1 -AcervoTeste     # + gera um acervo de exemplo (vídeos curtos com capa e metadados)
#>
param([switch] $SoCss, [switch] $VideoTeste, [switch] $AcervoTeste)
$ErrorActionPreference = 'Stop'
$raiz = $PSScriptRoot
$dist = Join-Path $raiz 'dist\MiniFlix'

# ---- CSS (Tailwind standalone, sem Node) ----
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
if ($SoCss) { return }

# ---- Publish ----
$exeDist = Join-Path $dist 'MiniFlix.exe'
$aberto = Get-Process MiniFlix -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exeDist }
if ($aberto) { throw "O MiniFlix está aberto a partir de $dist. Feche-o e rode o build novamente." }

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

# README.txt para o usuário final (versão do .csproj + data), em UTF-8 com BOM e CRLF (Bloco de Notas).
$versao = ([xml](Get-Content (Join-Path $raiz 'src\MiniFlix\MiniFlix.csproj'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$leiame = [IO.File]::ReadAllText((Join-Path $raiz 'pacote\README.txt'))
$leiame = $leiame.Replace('{{VERSAO}}', $versao).Replace('{{DATA}}', (Get-Date -Format 'dd/MM/yyyy'))
$leiame = $leiame -replace "`r?`n", "`r`n"
[IO.File]::WriteAllText((Join-Path $dist 'README.txt'), $leiame, (New-Object System.Text.UTF8Encoding($true)))
if (-not (Test-Path (Join-Path $dist 'bin\licencas'))) {
  Write-Warning 'bin\licencas não encontrado: rode .\tools\baixar-dependencias.ps1 -SoLicencas antes de distribuir.'
}

if ($VideoTeste)  { & (Join-Path $raiz 'tools\gerar-video-teste.ps1') -Raiz $dist }
if ($AcervoTeste) { & (Join-Path $raiz 'tools\gerar-acervo-teste.ps1') -Raiz $dist }

$tam = (Get-ChildItem $dist -Recurse -File -Exclude *.mp4 | Measure-Object Length -Sum).Sum
Write-Host ("`nPronto: {0}  ({1:N0} MB sem vídeos)" -f $dist, ($tam / 1MB))
