param(
    [Parameter(Mandatory = $true)]
    [string]$BuildDirectory,
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$Repository = 'CTLBs/zombombo-game-builds'
)

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') {
    throw 'Sürüm etiketi geçersiz. Örnek: v1.0.0'
}
$source = (Resolve-Path -LiteralPath $BuildDirectory).Path
if (-not (Test-Path -LiteralPath $source -PathType Container)) {
    throw 'BuildDirectory bir klasör olmalı.'
}
if (-not (Get-ChildItem -LiteralPath $source -Force | Select-Object -First 1)) {
    throw 'Build klasörü boş.'
}
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'GitHub CLI (gh) kurulu olmalı.'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$stage = Join-Path $tempRoot ('ZombomboBuild-' + [Guid]::NewGuid().ToString('N'))
$stageFull = [IO.Path]::GetFullPath($stage)
if (-not $stageFull.StartsWith($tempRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Geçici çalışma klasörü beklenen konumda değil.'
}
New-Item -ItemType Directory -Path $stageFull | Out-Null
try {
    $zip = Join-Path $stageFull 'game.zip'
    $checksum = Join-Path $stageFull 'game.zip.sha256'
    [IO.Compression.ZipFile]::CreateFromDirectory($source, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($checksum, "$hash  game.zip`n")
    gh release create $Version $zip $checksum --repo $Repository --title $Version --notes "Game build $Version" --latest
    if ($LASTEXITCODE -ne 0) { throw 'GitHub Release oluşturulamadı.' }
    Write-Host "Yayınlandı: https://github.com/$Repository/releases/tag/$Version"
}
finally {
    if (Test-Path -LiteralPath $stageFull) {
        Remove-Item -LiteralPath $stageFull -Recurse -Force
    }
}
