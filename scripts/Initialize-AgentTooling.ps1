[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$canonicalDirectory = Join-Path $repositoryRoot '.agents'
$isWindowsPlatform = $env:OS -eq 'Windows_NT'

if (-not (Test-Path -LiteralPath $canonicalDirectory -PathType Container)) {
    throw "Canonical agent directory not found: $canonicalDirectory"
}

foreach ($aliasName in @('.codex', '.claude')) {
    $aliasPath = Join-Path $repositoryRoot $aliasName

    if (Test-Path -LiteralPath $aliasPath) {
        $item = Get-Item -LiteralPath $aliasPath -Force
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -eq 0) {
            throw "Refusing to replace existing non-link directory: $aliasPath"
        }

        $pointsToCanonicalDirectory = $false
        foreach ($target in @($item.Target)) {
            if ([string]::IsNullOrWhiteSpace($target)) {
                continue
            }

            $resolvedTarget = if ([System.IO.Path]::IsPathRooted($target)) {
                [System.IO.Path]::GetFullPath($target)
            }
            else {
                [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $target))
            }

            if ($resolvedTarget -eq $canonicalDirectory) {
                $pointsToCanonicalDirectory = $true
                break
            }
        }

        if (-not $pointsToCanonicalDirectory) {
            throw "Refusing to replace agent alias that points elsewhere: $aliasPath"
        }

        Write-Host "$aliasName already points to .agents"
        continue
    }

    $itemType = if ($isWindowsPlatform) { 'Junction' } else { 'SymbolicLink' }
    New-Item -ItemType $itemType -Path $aliasPath -Target $canonicalDirectory | Out-Null
    Write-Host "Created $aliasName -> .agents ($itemType)"
}
