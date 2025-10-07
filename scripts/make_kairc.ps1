# Regenerates KAIR/ so it mirrors the repository with the kairc project renamed from kairc
$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$root = (Resolve-Path (Join-Path $scriptRoot '..')).Path
$destRoot = Join-Path $root 'KAIR'

Write-Host "Workspace root: $root"
Write-Host "Destination root: $destRoot"

if (Test-Path $destRoot) {
    Write-Host 'Removing existing KAIR directory...'
    Remove-Item -LiteralPath $destRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $destRoot -Force | Out-Null

Write-Host 'Copying repository into KAIR (excluding build artifacts)...'
$robocopyArgs = @($root, $destRoot, '/MIR', '/R:2', '/W:2', '/NFL', '/NDL', '/XD', '.git', 'KAIR', 'bin', 'obj', '.vs')
robocopy @robocopyArgs | Out-Null

# Clean any bin/obj folders that might still be present
Get-ChildItem -Path $destRoot -Directory -Recurse | Where-Object { $_.Name -in @('bin', 'obj') } | ForEach-Object {
    Remove-Item -LiteralPath $_.FullName -Recurse -Force
}

$asmDir = Join-Path $destRoot 'kairc'
if (-not (Test-Path $asmDir)) {
    throw "Expected directory not found: $asmDir"
}

Write-Host 'Renaming kairc folder to kairc...'
Rename-Item -LiteralPath $asmDir -NewName 'kairc'
$projectDir = Join-Path $destRoot 'kairc'

$oldProj = Join-Path $projectDir 'kairc.csproj'
$projPath = Join-Path $projectDir 'kairc.csproj'
if (Test-Path $oldProj) {
    Write-Host 'Renaming project file to kairc.csproj...'
    Rename-Item -LiteralPath $oldProj -NewName 'kairc.csproj'
} elseif (-not (Test-Path $projPath)) {
    throw "Project file not found in $projectDir"
}

[xml]$projXml = Get-Content -LiteralPath $projPath -Raw
$propertyGroup = $projXml.Project.PropertyGroup | Select-Object -First 1
if (-not $propertyGroup) {
    throw "PropertyGroup not found inside $projPath"
}
$propertyGroup.AssemblyName = 'kairc'
if ($propertyGroup.RootNamespace) {
    $propertyGroup.RootNamespace = 'Kairc'
} else {
    $rootNs = $projXml.CreateElement('RootNamespace')
    $rootNs.InnerText = 'Kairc'
    [void]$propertyGroup.AppendChild($rootNs)
}
$projXml.Save($projPath)

Write-Host 'Updating C# namespaces and usings...'
Get-ChildItem -Path $projectDir -Recurse -Include *.cs -File | Where-Object { $_.FullName -notmatch '\\obj\\' -and $_.FullName -notmatch '\\bin\\' } | ForEach-Object {
    $path = $_.FullName
    $text = Get-Content -LiteralPath $path -Raw
    $updated = [regex]::Replace($text, 'namespace\s+kairc(\S*)', 'namespace Kairc$1')
    $updated = [regex]::Replace($updated, 'using\s+kairc(\S*)', 'using Kairc$1')
    $updated = $updated -replace 'kairc\\.', 'Kairc.'
    if ($updated -ne $text) {
        Set-Content -LiteralPath $path -Value $updated -Encoding UTF8
    }
}

Write-Host 'Updating scripts and documentation references...'
$includePatterns = '*.ps1','*.md','*.json','*.txt','*.csproj'
Get-ChildItem -Path $destRoot -Recurse -File -Include $includePatterns | Where-Object { $_.FullName -notmatch '\\obj\\' -and $_.FullName -notmatch '\\bin\\' } | ForEach-Object {
    $path = $_.FullName
    $text = Get-Content -LiteralPath $path -Raw
    $updated = $text -replace 'kairc/kairc\\.csproj', 'kairc/kairc.csproj'
    $updated = $updated -replace 'kairc\\\kairc\\.csproj', 'kairc\\kairc.csproj'
    $updated = $updated -replace 'kairc/', 'kairc/'
    $updated = $updated -replace 'kairc\\\', 'kairc\\'
    $updated = $updated -replace 'kairc', 'kairc'
    $updated = $updated -replace 'KAIR', 'KAIR'
    if ($updated -ne $text) {
        Set-Content -LiteralPath $path -Value $updated -Encoding UTF8
    }
}

Write-Host 'Building kairc project inside KAIR...'
$buildPath = Join-Path $projectDir 'kairc.csproj'
& dotnet build $buildPath -c Debug | Out-Host

Write-Host 'KAIR directory regeneration complete.'



