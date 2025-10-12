# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**KAIR (Kernel Assembly IR)** is a cross-platform assembly abstraction layer compiler. It compiles `.kir` files (a minimal intermediate representation language) to native x64 assembly, then to executables via **LLVM toolchain** (llvm-mc + lld-link).

**Current Status**: Windows x64 fully supported with naive implementation. All operations follow load→compute→store pattern for maximum reliability.

## Build and Test Commands

### Building the Compiler
```bash
dotnet build KAIR/kairc/kairc.csproj -c Debug -r win-x64 --self-contained false
```

### Compiling KIR Programs
```powershell
# Build only (default)
./build.ps1 samples/fibonacci.kir

# Build and run with exit code display
./build.ps1 samples/fibonacci.kir -Run

# IMPORTANT: Artifacts always go to workspace/ directory
# - samples/ contains ONLY .kir source files
# - workspace/ contains all build artifacts (.s, .obj, .exe)
# - Build script automatically cleans old artifacts (avoids stale cache issues)

# Manual steps (for debugging)
dotnet run --project kairc/kairc.csproj -- input.kir -o output.s --emit-comments
tools/llvm/bin/llvm-mc.exe --triple=x86_64-pc-windows-msvc --output-asm-variant=1 --filetype=obj output.s -o output.obj
tools/llvm/bin/lld-link.exe output.obj tools/llvm/lib/kernel32.lib /subsystem:console /entry:Start /out:output.exe
```

### Running Tests
```powershell
# Simple one-liner with -Run flag
./build.ps1 workspace/test-feature.kir -Run  # Builds, runs, and shows exit code

# Test all samples
./workspace/test-all.ps1
```

## Architecture

### Compilation Pipeline
```
.kir → Lexer → Parser → IR → CodeGen → .s (LLVM MC) → llvm-mc → .obj → lld-link → .exe
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

**CodeGen** (`kairc/CodeGen/LlvmCodeGenerator.cs`)
- Generates LLVM MC assembly (Intel syntax with `.intel_syntax noprefix`)
- **Completely naive**: Each operation is load→compute→store with no optimization
- **Critical constraints**:
  - NEVER use `push`/`pop` for temporary storage (changes RSP, breaks `[rsp + offset]` addressing)
  - Use `r8` register for temporary values instead
  - Shift operations must use `rcx` (hardcoded by x64 ISA to use `cl`)
  - All syscalls require 16-byte stack alignment before `call`
  - **RIP-relative addressing REQUIRED** for data/const section access (see below)

### Key Design Decisions

**Native RSP Usage**
- `sp` in KIR directly maps to native RSP (not a virtual register)
- `s[offset]` compiles to `[rsp + offset]`
- Internal implementation MUST NOT modify RSP except for explicit `sp += / sp -=` operations
- This is why `push`/`pop` are forbidden in code generation
- **Stack allocation**: `sp -= N` decreases RSP (stack grows downward)

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

### LLVM Toolchain Specifics

**RIP-Relative Addressing (CRITICAL)**
- LLVM MC on Windows x64 requires RIP-relative addressing for data/const sections
- Direct addressing like `mov rax, [_data_base + 0]` will **crash** (0xC0000005)
- Must use: `lea r10, [rip + _data_base]; mov rax, [r10 + 0]`
- Stack (`rsp`) can use direct addressing: `mov rax, [rsp + 0]` is OK

**lld-link**
- Windows PE/COFF linker from LLVM
- Syntax: `/subsystem:console /entry:Start /out:output.exe`
- Requires import libraries (.lib) unlike GoLink which could link DLLs directly
- Correctly sets subsystem (no FixSubsystem needed)

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

**Stack Operations**:
```kir
sp -= 40         // Allocate 40 bytes on stack (decreases RSP)
sp += 16         // Deallocate 16 bytes (increases RSP)
```

**Important for Assembly Beginners**: Stack grows downward (toward lower addresses) in x64/ARM64. To allocate stack space, you **decrease** sp. KIR uses the same notation as assembly: `sp -= N` compiles to `sub rsp, N`.

**Alignment** (IMPORTANT for syscalls):
```kir
align 16         // Align stack to 16-byte boundary (required before syscalls on Windows x64)
align 8          // Align stack to 8-byte boundary
```

**Data Initialization** (top-level only):
```kir
[data + 0] = 0x48656C6C6F  // "Hello" in little-endian
[const + 8] = 100
```

**Operations**: `+`, `-`, `*`, `/s`, `/u`, `%s`, `%u`, `&`, `|`, `^`, `<<`, `>>s`, `>>u`, `-`, `~`

**Compound Assignment**: `+=`, `-=`, `*=`, `/s=`, `&=`, `<<=`, `>>s=`, etc.

**Conditional Assignment** (implemented but not in samples):
```kir
s[0] = 10 if s[8] >s 0                  // Assign only if condition is true
```

**Ternary Operator** (implemented but not in samples):
```kir
s[0] = (s[8] >s 0) ? 100 : 200         // Conditional value selection
```

**Syntax Constraints** (CRITICAL):
- **Each statement represents exactly one operation** - KIR syntax maps almost 1:1 to assembly instructions
- Expression composition is not supported - cannot combine operations or use computation results directly
- Examples of what's NOT allowed:
  ```kir
  s[0] = (s[8] + 10) * 2                      // NO: cannot combine operations
  syscall ExitProcess, s[0] + 1               // NO: cannot compute in arguments
  syscall WriteFile, h, data + 8, len, ptr, 0 // NO: data + 8 not allowed
  s[0] = ((s[8] > 0) ? 10 : 20) + 5          // NO: cannot use ternary result directly
  ```
- Correct approach (store intermediate results explicitly):
  ```kir
  s[0] = s[8] + 10
  s[0] *= 2

  s[0] += 1
  syscall ExitProcess, s[0]

  s[16] = data
  s[16] += 8
  syscall WriteFile, h, s[16], len, ptr, 0

  s[0] = (s[8] > 0) ? 10 : 20
  s[0] += 5
  ```

**Control Flow**: `goto label`, `goto label if condition`

**Syscalls**:
```kir
align 16
syscall ExitProcess, 0
align 16
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



## Important Lessons Learned

### Build Artifact Management
**CRITICAL: Always clean artifacts before testing**
- Old build artifacts can cause false test results
- `build.ps1` automatically cleans `.s`, `.obj`, `.exe` before building
- This prevents issues with stale object files that worked with old code

### Testing Methodology
When debugging assembly issues:
1. Start with minimal working case (e.g., `exit(42)`)
2. Gradually add complexity to isolate the problem
3. Compare working vs non-working assembly side-by-side
4. Test in a clean environment (remove all artifacts first)

### LLVM MC vs NASM Differences
- **Comment syntax**: NASM uses `;`, LLVM MC uses `#`
- **Data section access**: NASM allows direct `[symbol]`, LLVM MC requires RIP-relative
- **Directives**: NASM `bits 64`/`section .data`, LLVM MC `.intel_syntax`/`.section .data`
- **Assembler**: NASM is x86-specific, LLVM MC is cross-platform (x64/ARM64/etc)

### Debugging Checklist
When a program crashes (0xC0000005):
1. Check if data/const section is accessed with RIP-relative addressing
2. Verify stack alignment before `call` instructions
3. Ensure no `push`/`pop` between stack-relative accesses
4. Confirm artifacts are fresh (not cached from previous build)
5. Test with minimal reproduction case

