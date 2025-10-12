# KAIR Build Script - LLVM Toolchain
param(
    [string]$InputFile,
    [string]$OutputExe = "",
    [switch]$Run,        # -Run: ビルド後に実行して終了コードを表示
    [switch]$BuildOnly   # -BuildOnly: ビルドのみ（デフォルト動作と同じ）
)

if (-not $InputFile) {
    Write-Host "Usage: .\build.ps1 <input.kir> [-Run] [-BuildOnly]"
    Write-Host ""
    Write-Host "Options:"
    Write-Host "  -Run       : Build and run, then display exit code"
    Write-Host "  -BuildOnly : Build only (default behavior)"
    Write-Host ""
    Write-Host "Examples:"
    Write-Host "  .\build.ps1 workspace/test.kir           # Build only"
    Write-Host "  .\build.ps1 workspace/test.kir -Run      # Build and run"
    exit 1
}

# CRITICAL: All artifacts go to workspace/ directory (never in samples/)
$baseName = [System.IO.Path]::GetFileNameWithoutExtension($InputFile)

# Output always goes to workspace/
if (-not $OutputExe) {
    $OutputExe = "workspace/$baseName.exe"
}

$asmFile = "workspace/$baseName.s"    # LLVM MC uses .s extension
$objFile = "workspace/$baseName.obj"

# Resolve absolute paths relative to script location
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $scriptDir 'kairc/kairc.csproj'
$llvmPath = Join-Path $scriptDir 'tools/llvm'
$asmFile = Join-Path $scriptDir $asmFile
$objFile = Join-Path $scriptDir $objFile
$OutputExe = Join-Path $scriptDir $OutputExe
$inputAbsolute = Join-Path $scriptDir $InputFile

# CRITICAL: Clean all related artifacts before building (avoid stale cache issues)
Write-Host "Cleaning old artifacts..."
if (Test-Path $asmFile) { Remove-Item $asmFile -Force }
if (Test-Path $objFile) { Remove-Item $objFile -Force }
if (Test-Path $OutputExe) { Remove-Item $OutputExe -Force }

# Step 1: Compile KIR to LLVM MC Assembly
Write-Host "Compiling $InputFile to $asmFile..."
dotnet run --project $projectPath -- $inputAbsolute -o $asmFile --emit-comments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Step 2: Assemble with llvm-mc
Write-Host "Assembling $asmFile to $objFile..."
$llvmMc = Join-Path $llvmPath 'bin/llvm-mc.exe'
& $llvmMc --triple=x86_64-pc-windows-msvc --output-asm-variant=1 --filetype=obj $asmFile -o $objFile
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Step 3: Link with lld-link
Write-Host "Linking $objFile to $OutputExe..."
$lldLink = Join-Path $llvmPath 'bin/lld-link.exe'
$kernel32Lib = Join-Path $llvmPath 'lib/kernel32.lib'
& $lldLink $objFile $kernel32Lib /subsystem:console /entry:Start /out:$OutputExe
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Build complete!"
Write-Host "  ASM: $asmFile (kept for debugging)"
Write-Host "  EXE: $OutputExe"

# Run if requested
if ($Run) {
    Write-Host ""
    Write-Host "Running $OutputExe..."
    Write-Host "----------------------------------------"
    & $OutputExe
    $exitCode = $LASTEXITCODE
    Write-Host "----------------------------------------"
    Write-Host "Exit code: $exitCode"
    exit $exitCode
}
