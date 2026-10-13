param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$supplementRoot = Join-Path $root 'docs\third-party-licenses'
$supplements = Get-Content -LiteralPath (Join-Path $supplementRoot 'sources.json') -Raw | ConvertFrom-Json
$assets = Get-Content (Join-Path $root 'src\SourceLore.App\obj\project.assets.json') -Raw | ConvertFrom-Json
$notices = Join-Path $OutputDirectory 'licenses'
New-Item -ItemType Directory -Force $notices | Out-Null
$index = [Collections.Generic.List[string]]::new()
$index.Add('# Third-party packages')
$index.Add('Generated from the exact restored package graph. See the included package metadata, license texts, and lore.THIRD-PARTY-NOTICES.txt.')
foreach ($library in $assets.libraries.PSObject.Properties | Sort-Object Name) {
    if ($library.Value.type -ne 'package') { continue }
    $directory = $null
    foreach ($packageFolder in $assets.packageFolders.PSObject.Properties.Name) {
        $candidate = Join-Path $packageFolder $library.Value.path
        if (Test-Path -LiteralPath $candidate) { $directory = $candidate; break }
    }
    if (-not $directory) { throw "Restored package missing: $($library.Name)" }
    $destination = Join-Path $notices ($library.Name.Replace('/', '-'))
    New-Item -ItemType Directory -Force $destination | Out-Null
    $nuspecFile = Get-ChildItem -LiteralPath $directory -Filter '*.nuspec' | Select-Object -First 1
    [xml]$nuspec = Get-Content -LiteralPath $nuspecFile.FullName
    Copy-Item -LiteralPath $nuspecFile.FullName -Destination $destination
    $license = $nuspec.package.metadata.license
    $index.Add("- $($library.Name): $($license.'#text') — https://www.nuget.org/packages/$($library.Name)")
    if ($license.type -eq 'file') {
        $source = [IO.Path]::GetFullPath((Join-Path $directory $license.'#text'))
        if (-not $source.StartsWith([IO.Path]::GetFullPath($directory) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Package license path escapes package directory'
        }
        Copy-Item -LiteralPath $source -Destination $destination
    }
    Get-ChildItem -LiteralPath $directory -File -Recurse |
        Where-Object { $_.Name -match '(?i)^(license|copying|notice|third.party)' -and $_.Extension -notin '.dll','.pdb' } |
        ForEach-Object {
            $relative = [IO.Path]::GetRelativePath($directory, $_.FullName)
            $target = Join-Path $destination $relative
            New-Item -ItemType Directory -Force (Split-Path $target -Parent) | Out-Null
            Copy-Item -LiteralPath $_.FullName -Destination $target
        }
    $licenseTexts = @(Get-ChildItem -LiteralPath $destination -File -Recurse | Where-Object Extension -ne '.nuspec')
    if ($licenseTexts.Count -eq 0) {
        $supplement = $supplements.PSObject.Properties[[string]$nuspec.package.metadata.id].Value
        if (-not $supplement -or $supplement.commit -ne $nuspec.package.metadata.repository.commit) {
            throw "Missing matching full upstream license text for $($library.Name)"
        }
        $source = Join-Path $supplementRoot $supplement.file
        if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $supplement.sha256) {
            throw "Pinned license text hash changed: $($library.Name)"
        }
        Copy-Item -LiteralPath $source -Destination (Join-Path $destination 'UPSTREAM-LICENSE.txt')
        $index.Add("  Full text from package source commit: $($supplement.url)")
    }
}
$index | Set-Content -LiteralPath (Join-Path $OutputDirectory 'THIRD-PARTY-PACKAGES.md') -Encoding utf8
