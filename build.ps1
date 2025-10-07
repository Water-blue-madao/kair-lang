# KAIR Build Script
param(
    [string]$InputFile,
    [string]$OutputExe = ""
)

if (-not $InputFile) {
    Write-Host "Usage: .\build.ps1 <input.kir> [output.exe]"
    exit 1
}

# Auto-generate output filename if not provided
if (-not $OutputExe) {
    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($InputFile)
    $dirName = [System.IO.Path]::GetDirectoryName($InputFile)
    if (-not $dirName) { $dirName = "." }
    $OutputExe = Join-Path $dirName "$baseName.exe"
}

$asmFile = [System.IO.Path]::ChangeExtension($OutputExe, ".asm")
$objFile = [System.IO.Path]::ChangeExtension($OutputExe, ".obj")

# Resolve absolute paths relative to script location
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $scriptDir 'kairc/kairc.csproj'
$toolsPath = Join-Path $scriptDir 'tools'
$asmFile = Join-Path $scriptDir $asmFile
$objFile = Join-Path $scriptDir $objFile
$OutputExe = Join-Path $scriptDir $OutputExe
$inputAbsolute = Join-Path $scriptDir $InputFile

# Clean all related artifacts before building (avoid cache issues)
Write-Host "Cleaning old artifacts..."
if (Test-Path $asmFile) { Remove-Item $asmFile -Force }
if (Test-Path $objFile) { Remove-Item $objFile -Force }
if (Test-Path $OutputExe) { Remove-Item $OutputExe -Force }

# Step 1: Compile KIR to ASM
Write-Host "Compiling $InputFile to $asmFile..."
dotnet run --project $projectPath -- $inputAbsolute -o $asmFile --emit-comments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Step 2: Assemble ASM to OBJ
Write-Host "Assembling $asmFile to $objFile..."
& (Join-Path $toolsPath 'nasm\nasm.exe') -f win64 $asmFile -o $objFile
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Step 3: Link OBJ to EXE
Write-Host "Linking $objFile to $OutputExe..."
& (Join-Path $toolsPath 'golink\GoLink.exe') /console kernel32.dll $objFile /fo $OutputExe
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Step 4: Fix subsystem to Console (CRITICAL: Required for Windows execution)
Write-Host "Fixing subsystem to Console..."
$fixSubsystemProject = Join-Path $toolsPath 'subsystem\FixSubsystem.csproj'
if (Test-Path $fixSubsystemProject) {
    dotnet run --project $fixSubsystemProject -- $OutputExe
    if ($LASTEXITCODE -ne 0) {
        Write-Host "WARNING: Failed to fix subsystem"
    }
} else {
    Write-Host "WARNING: FixSubsystem project not found at $fixSubsystemProject"
    Write-Host "The executable may not run properly on Windows!"
}

Write-Host ""
Write-Host "Build complete!"
Write-Host "  ASM: $asmFile (kept for debugging)"
Write-Host "  EXE: $OutputExe"


