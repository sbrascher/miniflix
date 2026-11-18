<#
  Baixa o Tailwind CSS CLI standalone (sem Node) para tools\tailwindcss.exe.
  Só é necessário em desenvolvimento: o CSS compilado (src\ui\css\app.css) é versionado.
#>
param([string] $Versao = 'v4.3.3')
$ErrorActionPreference = 'Stop'
$destino = Join-Path $PSScriptRoot 'tailwindcss.exe'
$url = "https://github.com/tailwindlabs/tailwindcss/releases/download/$Versao/tailwindcss-windows-x64.exe"

Write-Host "Baixando Tailwind CLI $Versao ..."
Invoke-WebRequest $url -OutFile $destino
Write-Host "Pronto: $destino"
& $destino --help | Select-Object -First 1
