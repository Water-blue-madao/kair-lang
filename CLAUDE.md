# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**KAIR (Kernel Assembly IR)** is a cross-platform assembly abstraction layer compiler. It compiles `.kir` files (a minimal intermediate representation language) to native x64 assembly, then to executables via NASM and GoLink.

**Current Status**: Windows x64 fully supported with naive implementation. All operations follow load→compute→store pattern for maximum reliability.

## Build and Test Commands

### Building the Compiler
```bash
dotnet build KAIR/kairc/kairc.csproj -c Debug -r win-x64 --self-contained false
```

### Compiling KIR Programs
```powershell
# One-command build (recommended) - ONLY in workspace/
./build.ps1 workspace/test-feature.kir

# IMPORTANT: build.ps1 enforces workspace/ directory
# - samples/ contains final .kir files only (no .exe/.obj/.asm)
# - workspace/ is for testing (artifacts allowed, gitignored)
# - Build script cleans all artifacts before building (avoids cache issues)
# - ASM files are kept for debugging

# Manual steps (for debugging)
dotnet run --project KAIR/kairc/kairc.csproj -- input.kir -o output.asm --emit-comments
tools/nasm/nasm.exe -f win64 output.asm -o output.obj
tools/golink/GoLink.exe /console kernel32.dll output.obj /fo output.exe
dotnet run --project tools/subsystem/FixSubsystem.csproj -- output.exe  # CRITICAL!
```

### Running Tests
```powershell
# Build and test in one line (PowerShell uses ';' not '&&')
./build.ps1 workspace/test-feature.kir ; ./workspace/test-feature.exe ; echo "Exit code: $LASTEXITCODE"

# Examples:
./build.ps1 workspace/test-operators.kir ; ./workspace/test-operators.exe ; echo "Exit code: $LASTEXITCODE"  # Expected: 31
```

**CRITICAL**: Always run `FixSubsystem` on executables produced by GoLink. Without it, programs won't return exit codes correctly in PowerShell (GoLink produces GUI subsystem by default despite `/console` flag).

## Architecture

### Compilation Pipeline
```
.kir → Lexer → Parser → IR → CodeGen → .asm → NASM → .obj → GoLink → FixSubsystem → .exe
```

### Core Components

**Lexer** (`KAIR/kairc/Lexer/`)
- Tokenizes KIR source into tokens
- Handles comments (`//`, `/* */`), keywords, operators, literals
- TokenType.cs defines all token types

**Parser** (`KAIR/kairc/Parser/Parser.cs`)
- Recursive descent parser
- Produces IR tree (IrNode)
- Key parsing patterns:
  - **Top-level constraint**: Data initialization (`[data/const + offset] = value`) only at start
  - **Syntax sugar desugaring**: `s[x]` → `[sp + x]`, `d[x]` → `[data + x]`, `c[x]` → `[const + x]`
  - Statement parsing uses explicit lookahead (`Check()` / `Peek()`)

**IR** (`KAIR/kairc/IR/IrNode.cs`)
- Defines IR node types (statements, expressions)
- Key nodes:
  - `BaseOffsetAccess`: Unified memory access `[base + offset]`
  - `DataBaseAddress`: Base address value (data, const)
  - `DataInitialization`: Top-level data init
  - `BinaryOperation`, `UnaryOperation`: Arithmetic/logical ops
  - `Syscall`: Windows API calls with alignment

**CodeGen** (`KAIR/kairc/CodeGen/NasmCodeGenerator.cs`)
- Generates NASM x64 assembly
- **Completely naive**: Each operation is load→compute→store with no optimization
- **Critical constraints**:
  - NEVER use `push`/`pop` for temporary storage (changes RSP, breaks `[rsp + offset]` addressing)
  - Use `r8` register for temporary values instead
  - Shift operations must use `rcx` (hardcoded by x64 ISA to use `cl`)
  - All syscalls require 16-byte stack alignment before `call`

### Key Design Decisions

**Native RSP Usage**
- `sp` in KIR directly maps to native RSP (not a virtual register)
- `s[offset]` compiles to `[rsp + offset]`
- Internal implementation MUST NOT modify RSP except for explicit `sp +=` operations
- This is why `push`/`pop` are forbidden in code generation

**Memory Access Unification**
- Single syntax: `[base + offset]` where base ∈ {sp, data, const}
- Static offsets only (no dynamic offset expressions like `[sp + x + y]`)
- Syntax sugar (`s[]`, `d[]`, `c[]`) desugars during parsing

**Complete Naive Implementation**
- Binary operations: `mov rax, [left]; mov r8, rax; mov rbx, [right]; mov rax, r8; op rax, rbx`
- No register allocation, no CSE, no dead code elimination
- Reduces bugs, simplifies debugging

## Critical Windows x64 Knowledge

### Stack Alignment (MOST IMPORTANT)
- Windows x64 calling convention requires `RSP % 16 == 0` before `call`
- Entry point guarantees `RSP % 16 == 0` (OS ensures this)
- Dynamic alignment before syscalls:
  ```asm
  mov rax, rsp
  and rax, 15         ; RSP % 16
  sub rsp, rax        ; Align to 16-byte boundary
  sub rsp, 32         ; Shadow space (maintains alignment)
  call Function       ; RSP % 16 == 0 here
  ```

### GoLink Quirks
- `/console` flag doesn't work (bug in GoLink 1.0.4.6)
- Always produces GUI subsystem (2), must fix to Console (3) with FixSubsystem
- Import stubs are code, not pointers: use `call ExitProcess` NOT `call [ExitProcess]`
- `section .data` required even if empty (else Import Directory missing)

### Register Usage
- Volatile (caller-saved): RAX, RCX, RDX, R8-R11
- Non-volatile (callee-saved): RBX, RBP, RDI, RSI, RSP, R12-R15
- Argument passing: RCX, RDX, R8, R9 (first 4 integer args), rest on stack at `[rsp+32]`, `[rsp+40]`, ...

## Common Modifications

### Adding New Operators
1. Add to `BinaryOperator` enum in `IR/IrNode.cs`
2. Add token type in `Lexer/TokenType.cs`
3. Add lexer mapping in `Lexer/Lexer.cs`
4. Add parser case in `Parser/Parser.cs::ParseBinaryOperator()`
5. Add code generation in `CodeGen/NasmCodeGenerator.cs::EmitBinaryOperation()`

### Adding New IR Nodes
1. Define node class in `IR/IrNode.cs` (inherit from `Statement` or `Expression`)
2. Add parsing logic in `Parser/Parser.cs`
3. Add code generation case in `NasmCodeGenerator.cs::EmitStatement()` or `EmitLoadExpression()`

### Modifying Code Generation
- Always check that changes don't introduce `push`/`pop` (breaks RSP-relative addressing)
- Test with `--emit-comments` flag to verify generated assembly
- Use dedicated registers: `rax` (primary), `r8` (temp), `rbx` (secondary operand), `rcx` (shift amount)

## File Organization

```
KAIR/kairc/
├── Lexer/           # Tokenization
├── Parser/          # Syntax analysis → IR
├── IR/              # IR node definitions
├── CodeGen/         # IR → NASM assembly
├── ProcessHelper.cs # External tool execution (shared by NASM/GoLink)
├── Compiler.cs      # Orchestrates pipeline
└── Program.cs       # Entry point, CLI

workspace/           # Test files (.kir only, binaries gitignored)
samples/             # Example programs
tools/               # NASM, GoLink binaries
build.ps1            # One-command build script

文法メモ.md               # Language spec (changes frequently)
命令対応表.md             # Instruction mapping (x64/ARM64 naive implementation)
CLAUDE.md                 # This file - comprehensive development guide for AI assistants
```

## Testing Strategy

**Build script automatically cleans artifacts** (avoids cache issues):
```powershell
./build.ps1 workspace/test-feature.kir
# Automatically removes old .asm/.obj/.exe before building
# ASM file is kept for debugging
```

Exit code verification in PowerShell:
```powershell
./workspace/test-feature.exe ; echo "Exit code: $LASTEXITCODE"  # NOT $? (that's a boolean)
# Note: PowerShell uses ';' as statement separator, NOT '&&'
```

**CRITICAL Testing Location:**
- ALWAYS test in `workspace/` directory (use `./build.ps1 workspace/file.kir`)
- NEVER build in `samples/` directory (build.ps1 will reject it)
- `samples/` contains final .kir files only, no artifacts (.exe/.obj/.asm are gitignored)

## AI Assistant Execution Testing Policy

**Environment Context:**
- **AI Environment**: WSL (Linux) - cannot execute Windows x64 binaries
- **User Environment**: Windows x64 - actual execution platform

**Testing Workflow:**
1. AI prepares test command as a single PowerShell one-liner
2. AI passes command to user and waits for results
3. User executes on Windows and reports back
4. AI analyzes results and proceeds

**Example:**
```
AI: Please run this command and report the results:
    ./build.ps1 workspace/test-feature.kir ; ./workspace/test-feature.exe ; echo "Exit code: $LASTEXITCODE"
User: [reports output and exit code]
AI: [analyzes and continues work]
```

## Important Context Files

- **文法メモ.md**: KIR language specification (updated frequently, check git log).
- **命令対応表.md**: Naive implementation patterns for each operation on x64/ARM64.

## Language Specification Summary

**Memory Access**: `[sp + offset]`, `[data + offset]`, `[const + offset]` (sugar: `s[]`, `d[]`, `c[]`)

**Base Address Access** (NEW):
```kir
s[8] = data         // Load address of data section base
s[8] += 32          // Calculate offset address
syscall WriteFile, handle, data, length, s[8], 0  // Pass address directly
```
- `data` and `const` can be used as address values
- Compiles to `lea reg, [rel _data_base]` or `lea reg, [rel _rodata_base]`
- **CRITICAL**: `d[0]` loads the **value** at data+0, `data` loads the **address** of data section

**Data Initialization** (top-level only):
```kir
[data + 0] = 0x48656C6C6F  // "Hello" in little-endian
[const + 8] = 100
```

**Operations**: `+`, `-`, `*`, `/s`, `/u`, `%s`, `%u`, `&`, `|`, `^`, `<<`, `>>s`, `>>u`, `-`, `~`

**Control Flow**: `goto label`, `goto label if condition`

**Syscalls**:
```kir
syscall ExitProcess, 0
s[0] = syscall GetStdHandle, -11
```

**Comments**: `// single line`, `/* multi-line */`

## Sample KIR File Standards

All sample `.kir` files must follow this format to maintain consistency and clarity:

### File Header Format
```kir
//==============================================================================
// ファイル名: sample-name.kir
//==============================================================================
// 【目的】
// このサンプルの狙いを簡潔に記述（1-2行）
//
// 【解説】
// 実装の詳細、使用している機能、注意点など
//
// 【期待される結果】
// Exit code: 42
// または具体的な出力内容
//==============================================================================

// コードはここから
```

### Sample Development Workflow

**CRITICAL**: Samples are developed in `workspace/`, then moved to `samples/`. This keeps `samples/` clean and prevents build artifacts from being committed.

1. **Create in workspace/**
   ```bash
   # Create new sample in workspace
   vim workspace/test-feature.kir
   ```

2. **Test in workspace/**
   ```powershell
   # Build and test (artifacts stay in workspace/)
   ./build.ps1 workspace/test-feature.kir ; ./workspace/test-feature.exe ; echo "Exit code: $LASTEXITCODE"
   ```

3. **Verify and document**
   - Confirm expected behavior
   - Add comprehensive header comments
   - Document any known issues or limitations

4. **Move to samples/**
   ```bash
   # Move ONLY the .kir file (never .exe/.obj/.asm)
   git mv workspace/test-feature.kir samples/test-feature.kir
   git add samples/test-feature.kir
   ```

5. **Clean workspace/**
   ```bash
   # Remove test artifacts
   rm -f workspace/*.exe workspace/*.obj workspace/*.asm
   ```

### Sample Categories

**Basic Samples** (in `samples/`):
- `test-minimal.kir`: Minimal program structure (exit code only)
- `test-operators.kir`: Comprehensive operator testing
- `loop.kir`: Control flow examples
- `hello-syscall.kir`: System call usage

**Development Samples** (in `workspace/`):
- Temporary test files
- Work-in-progress features
- Debug/diagnostic code

**Never commit to samples/**:
- `.exe`, `.obj`, `.asm` files (covered by `.gitignore`)
- Incomplete or undocumented samples
- Files without proper header comments

## Common Pitfalls

1. **Forgetting FixSubsystem**: Executables won't return exit codes properly
2. **Using `push`/`pop` in codegen**: Breaks `[rsp + offset]` addressing
3. **Not aligning stack before syscalls**: Causes 0xC0000005 (ACCESS_VIOLATION)
4. **Using `call [Function]` instead of `call Function`**: GoLink import stubs are code, not pointers
5. **Stale build artifacts**: Build script now auto-cleans before building
6. **Committing workspace/ artifacts**: Only `.kir` files should be in git
7. **Confusing `d[0]` vs `data`**: `d[0]` = value at data+0, `data` = address of data section base


