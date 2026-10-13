param([switch]$SkipRestore)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
$previousTelemetry = $env:VSCMD_SKIP_SENDTELEMETRY
try {
    # VS 2022 environment detection can block on its telemetry helper in unattended builds.
    $env:VSCMD_SKIP_SENDTELEMETRY = '1'
    New-Item -ItemType Directory -Force 'artifacts' | Out-Null
    if (-not $SkipRestore) {
        dotnet restore SourceLore.slnx --locked-mode --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed' }
    }
    $output = Join-Path $root 'artifacts\publish\win-x64'
    dotnet publish src/SourceLore.App/SourceLore.App.csproj -c Release -r win-x64 --no-restore -o $output --nologo 2>&1 |
        Tee-Object -FilePath 'artifacts\publish.log'
    if ($LASTEXITCODE -ne 0) { throw 'NativeAOT publish failed' }
    if (Select-String -Path 'artifacts\publish.log' -Pattern '(?i)warning IL\d+' -Quiet) {
        throw 'Review trimming/AOT diagnostics before distributing this build'
    }
    & (Join-Path $PSScriptRoot 'Collect-Notices.ps1') -OutputDirectory $output
    $releaseDocs = Join-Path $output 'docs'
    New-Item -ItemType Directory -Force $releaseDocs | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $root 'docs') -File -Filter '*.md' |
        ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $releaseDocs }
    Copy-Item -LiteralPath (Join-Path $root 'docs\release-candidate.md') -Destination (Join-Path $output 'README.md')
    foreach ($required in 'SourceLore.App.exe','lorelib.dll','libSkiaSharp.dll','libHarfBuzzSharp.dll','av_libglesv2.dll','lore.THIRD-PARTY-NOTICES.txt') {
        if (-not (Test-Path -LiteralPath (Join-Path $output $required))) { throw "Missing release file: $required" }
    }
    Get-ChildItem -LiteralPath $output -File -Recurse | Where-Object Name -ne 'manifest.sha256' |
        Sort-Object FullName | ForEach-Object {
            $relative = [IO.Path]::GetRelativePath($output, $_.FullName)
            "$( (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash )  $relative"
        } | Set-Content -LiteralPath (Join-Path $output 'manifest.sha256') -Encoding utf8
    Write-Output "Release directory: $output"
}
finally {
    $env:VSCMD_SKIP_SENDTELEMETRY = $previousTelemetry
    Pop-Location
}
