using System.Text;
using Kairc.IR;

namespace Kairc.CodeGen;

public class NasmCodeGenerator
{
    private readonly IrProgram _program;
    private readonly StringBuilder _output = new();
    private int _labelCounter = 0;
    private readonly bool _emitComments;

    public NasmCodeGenerator(IrProgram program, bool emitComments = false)
    {
        _program = program;
        _emitComments = emitComments;
    }

    public string Generate()
    {
        EmitHeader();
        EmitDataSection();
        EmitTextSection();
        return _output.ToString();
    }

    private void EmitHeader()
    {
        Emit("bits 64");
        Emit("default rel");
        Emit("");
        Emit("extern ExitProcess");
        Emit("extern GetStdHandle");
        Emit("extern WriteFile");
        Emit("");
        Emit("global Start  ; CRITICAL: Must be 'Start' (not '_start') for GoLink");
        Emit("");
    }

    private void EmitDataSection()
    {
        var dataInits = _program.DataInitializations.Where(d => d.Section == BaseRegister.Data).OrderBy(d => d.Offset).ToList();
        var constInits = _program.DataInitializations.Where(d => d.Section == BaseRegister.Const).OrderBy(d => d.Offset).ToList();

        // データセクション
        Emit("section .data");
        if (dataInits.Any())
        {
            Emit("_data_base:");
            long currentOffset = 0;
            foreach (var init in dataInits)
            {
                // パディングが必要な場合
                if (init.Offset > currentOffset)
                {
                    long padding = init.Offset - currentOffset;
                    Emit($"    resb {padding}  ; padding");
                    currentOffset = init.Offset;
                }

                Emit($"    dq {init.Value}  ; offset {init.Offset}");
                currentOffset += 8;  // dq = 8 bytes
            }
            Emit("");
        }
        else
        {
            Emit("    _data_base: resb 4096  ; reserve space for data");
            Emit("");
        }

        // 読み取り専用データセクション
        Emit("section .rodata");
        if (constInits.Any())
        {
            Emit("_rodata_base:");
            long currentOffset = 0;
            foreach (var init in constInits)
            {
                // パディングが必要な場合
                if (init.Offset > currentOffset)
                {
                    long padding = init.Offset - currentOffset;
                    Emit($"    resb {padding}  ; padding");
                    currentOffset = init.Offset;
                }

                Emit($"    dq {init.Value}  ; offset {init.Offset}");
                currentOffset += 8;  // dq = 8 bytes
            }
            Emit("");
        }
        else
        {
            Emit("    _rodata_base: resb 4096  ; reserve space for const data");
            Emit("");
        }
    }

    private void EmitTextSection()
    {
        Emit("section .text");
        Emit("Start:");
        Emit("    ; Initialize stack");
        Emit("    ; CRITICAL: Windows x64 calling convention");
        Emit("    ; At entry: RSP % 16 == 0 (aligned by OS)");
        Emit("    ; No initial alignment needed - syscalls will handle it");
        Emit("    mov rbp, rsp    ; save base pointer");
        Emit("");

        foreach (var stmt in _program.Statements)
        {
            EmitStatement(stmt);
        }

        // ENDラベル - exit code is at s[0]
        Emit("");
        Emit("_END:");
        Emit("    ; Exit program (Windows) - exit code at s[0]");
        Emit("    mov rcx, [rsp]      ; load exit code from s[0]");
        Emit("    ; CRITICAL: Align RSP to 16-byte boundary for call");
        Emit("    mov rax, rsp");
        Emit("    and rax, 15         ; rsp % 16");
        Emit("    sub rsp, rax        ; align RSP so RSP % 16 == 0");
        Emit("    sub rsp, 32         ; shadow space (keeps RSP % 16 == 0)");
        Emit("    call ExitProcess    ; Direct call (RSP % 16 == 0 required)");
    }

    private void EmitStatement(Statement stmt)
    {
        // コメント出力（オプション）
        if (_emitComments && stmt.SourceText != null)
        {
            Emit($"    ; {stmt.SourceText}");
        }

        switch (stmt)
        {
            case Label label:
                Emit($"_{label.Name}:");
                break;

            case Assignment assign:
                EmitAssignment(assign);
                break;

            case ConditionalAssignment condAssign:
                EmitConditionalAssignment(condAssign);
                break;

            case TernaryAssignment ternary:
                EmitTernaryAssignment(ternary);
                break;

            case CompoundAssignment compound:
                EmitCompoundAssignment(compound);
                break;

            case Goto gotoStmt:
                Emit($"    jmp _{gotoStmt.Target}");
                break;

            case ConditionalGoto condGoto:
                EmitConditionalGoto(condGoto);
                break;

            case Pass pass:
                for (int i = 0; i < pass.Count; i++)
                    Emit("    nop");
                break;

            case StackPointerUpdate spUpdate:
                EmitStackPointerUpdate(spUpdate);
                break;

            case Align align:
                EmitAlign(align);
                break;

            case Syscall syscall:
                EmitSyscall(syscall);
                break;
        }
    }

    private void EmitAssignment(Assignment assign)
    {
        // source -> rax
        EmitLoadExpression(assign.Source, "rax");

        // rax -> destination
        EmitStoreToDestination(assign.Destination, "rax");
    }

    private void EmitStoreToDestination(Expression dest, string sourceReg)
    {
        switch (dest)
        {
            case BaseOffsetAccess baseOffset:
                EmitStoreToBaseOffset(baseOffset, sourceReg);
                break;
            case MemoryAccess mem:
                EmitStoreToMemory(mem, sourceReg);
                break;
            default:
                throw new NotImplementedException($"Cannot store to {dest.GetType().Name}");
        }
    }

    /// <summary>
    /// 新しい統一メモリアクセス構文へのストア: [base + offset]
    /// レジスタを消費せず、直接アドレッシング
    /// </summary>
    private void EmitStoreToBaseOffset(BaseOffsetAccess access, string sourceReg)
    {
        string baseLabel = access.Base switch
        {
            BaseRegister.Sp => "rsp",
            BaseRegister.Data => "_data_base",
            BaseRegister.Const => "_rodata_base",
            _ => throw new NotImplementedException($"Base register {access.Base}")
        };

        // 静的オフセット：直接 [base + offset] でアクセス（レジスタ不要）
        Emit($"    mov [{baseLabel} + {access.Offset}], {sourceReg}");
    }

    private void EmitConditionalAssignment(ConditionalAssignment condAssign)
    {
        var skipLabel = GenerateLabel("skip_assign");

        // 条件評価（反対条件でスキップ）
        EmitConditionJump(condAssign.Condition, skipLabel, invert: true);

        // source -> rax
        EmitLoadExpression(condAssign.Source, "rax");

        // rax -> destination
        EmitStoreToDestination(condAssign.Destination, "rax");

        Emit($"{skipLabel}:");
    }

    private void EmitTernaryAssignment(TernaryAssignment ternary)
    {
        var falseLabel = GenerateLabel("ternary_false");
        var endLabel = GenerateLabel("ternary_end");

        // 条件評価
        EmitConditionJump(ternary.Condition, falseLabel, invert: true);

        // true value -> rax
        EmitLoadExpression(ternary.TrueValue, "rax");
        Emit($"    jmp {endLabel}");

        Emit($"{falseLabel}:");
        // false value -> rax
        EmitLoadExpression(ternary.FalseValue, "rax");

        Emit($"{endLabel}:");
        // rax -> destination
        EmitStoreToDestination(ternary.Destination, "rax");
    }

    private void EmitCompoundAssignment(CompoundAssignment compound)
    {
        // destination -> rax
        EmitLoadExpression(compound.Destination, "rax");

        // source -> rbx
        EmitLoadExpression(compound.Source, "rbx");

        // rax op= rbx
        EmitBinaryOperation(compound.Operator, "rax", "rbx");

        // rax -> destination
        EmitStoreToDestination(compound.Destination, "rax");
    }

    private void EmitStackPointerUpdate(StackPointerUpdate spUpdate)
    {
        // KIR: sp += N means "allocate N bytes" (grow stack downward)
        // ASM: sub rsp, N (stack grows down, but s[] offsets are positive upward)

        // sp -> rax
        Emit("    mov rax, rsp");

        // value -> rbx
        EmitLoadExpression(spUpdate.Value, "rbx");

        // INVERT operator: sp += N → sub rsp, N
        var invertedOp = spUpdate.Operator switch
        {
            BinaryOperator.Add => BinaryOperator.Sub,
            BinaryOperator.Sub => BinaryOperator.Add,
            _ => spUpdate.Operator
        };
        EmitBinaryOperation(invertedOp, "rax", "rbx");

        // rax -> sp (RSP)
        Emit("    mov rsp, rax");
    }

    private void EmitConditionalGoto(ConditionalGoto condGoto)
    {
        EmitConditionJump(condGoto.Condition, $"_{condGoto.Target}", invert: false);
    }

    private void EmitConditionJump(Condition condition, string targetLabel, bool invert)
    {
        // left -> rax
        EmitLoadExpression(condition.Left, "rax");

        // right -> rbx
        EmitLoadExpression(condition.Right, "rbx");

        // compare
        Emit("    cmp rax, rbx");

        // jump
        var jumpInstr = GetJumpInstruction(condition.Operator, invert);
        Emit($"    {jumpInstr} {targetLabel}");
    }

    private string GetJumpInstruction(ComparisonOperator op, bool invert)
    {
        return (op, invert) switch
        {
            (ComparisonOperator.Equal, false) => "je",
            (ComparisonOperator.Equal, true) => "jne",
            (ComparisonOperator.NotEqual, false) => "jne",
            (ComparisonOperator.NotEqual, true) => "je",
            (ComparisonOperator.LessS, false) => "jl",
            (ComparisonOperator.LessS, true) => "jge",
            (ComparisonOperator.LessU, false) => "jb",
            (ComparisonOperator.LessU, true) => "jae",
            (ComparisonOperator.LessEqualS, false) => "jle",
            (ComparisonOperator.LessEqualS, true) => "jg",
            (ComparisonOperator.LessEqualU, false) => "jbe",
            (ComparisonOperator.LessEqualU, true) => "ja",
            (ComparisonOperator.GreaterS, false) => "jg",
            (ComparisonOperator.GreaterS, true) => "jle",
            (ComparisonOperator.GreaterU, false) => "ja",
            (ComparisonOperator.GreaterU, true) => "jbe",
            (ComparisonOperator.GreaterEqualS, false) => "jge",
            (ComparisonOperator.GreaterEqualS, true) => "jl",
            (ComparisonOperator.GreaterEqualU, false) => "jae",
            (ComparisonOperator.GreaterEqualU, true) => "jb",
            _ => throw new NotImplementedException($"Comparison operator {op}")
        };
    }

    private void EmitLoadExpression(Expression expr, string destReg)
    {
        switch (expr)
        {
            case NumberLiteral num:
                Emit($"    mov {destReg}, {num.Value}");
                break;

            case StackPointer:
                Emit($"    mov {destReg}, rsp");
                break;

            case DataBaseAddress dataBase:
                // データセクションのベースアドレスを取得
                var baseName = dataBase.Section == BaseRegister.Data ? "_data_base" : "_rodata_base";
                Emit($"    lea {destReg}, [rel {baseName}]");
                break;

            case BaseOffsetAccess baseOffset:
                EmitLoadFromBaseOffset(baseOffset, destReg);
                break;

            case MemoryAccess mem:
                EmitLoadFromMemory(mem, destReg);
                break;

            case BinaryOperation binOp:
                // CRITICAL: push/pop を使わない（RSP を変更してはいけない）
                // シフト演算は RCX を使う必要があるため特別扱い
                if (binOp.Operator == BinaryOperator.LeftShift ||
                    binOp.Operator == BinaryOperator.RightShiftS ||
                    binOp.Operator == BinaryOperator.RightShiftU)
                {
                    EmitLoadExpression(binOp.Left, destReg);
                    Emit($"    mov r8, {destReg}");
                    EmitLoadExpression(binOp.Right, "rcx");
                    Emit($"    mov {destReg}, r8");
                    EmitBinaryOperation(binOp.Operator, destReg, "rcx");
                }
                else
                {
                    // 通常の演算：左辺を r8 に退避、右辺を直接 rbx にロード
                    EmitLoadExpression(binOp.Left, destReg);
                    Emit($"    mov r8, {destReg}");
                    EmitLoadExpression(binOp.Right, "rbx");
                    Emit($"    mov {destReg}, r8");
                    EmitBinaryOperation(binOp.Operator, destReg, "rbx");
                }
                break;

            case UnaryOperation unOp:
                EmitLoadExpression(unOp.Operand, destReg);
                EmitUnaryOperation(unOp.Operator, destReg);
                break;

            default:
                throw new NotImplementedException($"Expression type {expr.GetType().Name}");
        }
    }

    /// <summary>
    /// 新しい統一メモリアクセス構文からロード: [base + offset]
    /// レジスタを消費せず、直接アドレッシング
    /// </summary>
    private void EmitLoadFromBaseOffset(BaseOffsetAccess access, string destReg)
    {
        string baseLabel = access.Base switch
        {
            BaseRegister.Sp => "rsp",
            BaseRegister.Data => "_data_base",
            BaseRegister.Const => "_rodata_base",
            _ => throw new NotImplementedException($"Base register {access.Base}")
        };

        // 静的オフセット：直接 [base + offset] でアクセス（レジスタ不要）
        Emit($"    mov {destReg}, [{baseLabel} + {access.Offset}]");
    }

    private void EmitLoadFromMemory(MemoryAccess mem, string destReg)
    {
        // 完全ナイーブ：アドレス計算→load（すべてステップごとに完結）
        var address = CalculateAddress(mem);
        var size = GetMemorySize(mem.Type);
        var isSigned = IsSignedMemoryType(mem.Type);

        switch (size)
        {
            case 8:
                Emit($"    mov {destReg}, [{address}]");
                break;
            case 4:
                if (isSigned)
                    Emit($"    movsxd {destReg}, dword [{address}]");
                else
                    Emit($"    mov {GetRegisterSize(destReg, 4)}, dword [{address}]");
                break;
            case 2:
                if (isSigned)
                    Emit($"    movsx {destReg}, word [{address}]");
                else
                    Emit($"    movzx {destReg}, word [{address}]");
                break;
            case 1:
                if (isSigned)
                    Emit($"    movsx {destReg}, byte [{address}]");
                else
                    Emit($"    movzx {destReg}, byte [{address}]");
                break;
        }
    }

    private void EmitStoreToMemory(MemoryAccess mem, string sourceReg)
    {
        // Save sourceReg without using stack (push changes RSP!)
        Emit($"    mov rbx, {sourceReg}  ; save value");
        var address = CalculateAddress(mem);
        Emit($"    mov {sourceReg}, rbx  ; restore value");

        var size = GetMemorySize(mem.Type);

        switch (size)
        {
            case 8:
                Emit($"    mov [{address}], {sourceReg}");
                break;
            case 4:
                Emit($"    mov dword [{address}], {GetRegisterSize(sourceReg, 4)}");
                break;
            case 2:
                Emit($"    mov word [{address}], {GetRegisterSize(sourceReg, 2)}");
                break;
            case 1:
                Emit($"    mov byte [{address}], {GetRegisterSize(sourceReg, 1)}");
                break;
        }
    }

    private string CalculateAddress(MemoryAccess mem)
    {
        // 完全ナイーブ：アドレス計算専用レジスタrdiを使用
        var baseReg = mem.Type switch
        {
            MemoryType.Mem or MemoryType.Mem8 or MemoryType.Mem16 or MemoryType.Mem32 or MemoryType.Mem64 or
            MemoryType.Mem8s or MemoryType.Mem16s or MemoryType.Mem32s => null,

            MemoryType.S or MemoryType.S8 or MemoryType.S16 or MemoryType.S32 or MemoryType.S64 or
            MemoryType.S8s or MemoryType.S16s or MemoryType.S32s => "rsp",

            MemoryType.D or MemoryType.D8 or MemoryType.D16 or MemoryType.D32 or MemoryType.D64 or
            MemoryType.D8s or MemoryType.D16s or MemoryType.D32s => "_data_base",

            MemoryType.C or MemoryType.C8 or MemoryType.C16 or MemoryType.C32 or MemoryType.C64 or
            MemoryType.C8s or MemoryType.C16s or MemoryType.C32s => "_rodata_base",

            _ => throw new NotImplementedException($"Memory type {mem.Type}")
        };

        // アドレス計算を rdi に（専用レジスタとして使う）
        EmitLoadExpression(mem.Address, "rdi");

        if (baseReg != null)
        {
            if (baseReg.StartsWith("_"))
            {
                // d[], c[] の場合
                Emit($"    lea rsi, [{baseReg}]");
                Emit($"    add rdi, rsi");
            }
            else
            {
                // s[] の場合
                Emit($"    add rdi, {baseReg}");
            }
        }

        return "rdi";
    }

    private void EmitBinaryOperation(BinaryOperator op, string destReg, string sourceReg)
    {
        switch (op)
        {
            case BinaryOperator.Add:
                Emit($"    add {destReg}, {sourceReg}");
                break;
            case BinaryOperator.Sub:
                Emit($"    sub {destReg}, {sourceReg}");
                break;
            case BinaryOperator.Mul:
                Emit($"    imul {destReg}, {sourceReg}");
                break;
            case BinaryOperator.DivS:
                Emit($"    cqo");
                Emit($"    idiv {sourceReg}");
                break;
            case BinaryOperator.DivU:
                Emit($"    xor rdx, rdx");
                Emit($"    div {sourceReg}");
                break;
            case BinaryOperator.ModS:
                Emit($"    cqo");
                Emit($"    idiv {sourceReg}");
                Emit($"    mov {destReg}, rdx");
                break;
            case BinaryOperator.ModU:
                Emit($"    xor rdx, rdx");
                Emit($"    div {sourceReg}");
                Emit($"    mov {destReg}, rdx");
                break;
            case BinaryOperator.And:
                Emit($"    and {destReg}, {sourceReg}");
                break;
            case BinaryOperator.Or:
                Emit($"    or {destReg}, {sourceReg}");
                break;
            case BinaryOperator.Xor:
                Emit($"    xor {destReg}, {sourceReg}");
                break;
            case BinaryOperator.LeftShift:
                // sourceReg は rcx であることが前提（EmitLoadExpression で保証）
                Emit($"    shl {destReg}, cl");
                break;
            case BinaryOperator.RightShiftS:
                // sourceReg は rcx であることが前提
                Emit($"    sar {destReg}, cl");
                break;
            case BinaryOperator.RightShiftU:
                // sourceReg は rcx であることが前提
                Emit($"    shr {destReg}, cl");
                break;
            default:
                throw new NotImplementedException($"Binary operator {op}");
        }
    }

    private void EmitUnaryOperation(UnaryOperator op, string reg)
    {
        switch (op)
        {
            case UnaryOperator.Negate:
                Emit($"    neg {reg}");
                break;
            case UnaryOperator.Not:
                Emit($"    not {reg}");
                break;
            default:
                throw new NotImplementedException($"Unary operator {op}");
        }
    }

    private int GetMemorySize(MemoryType type)
    {
        return type switch
        {
            MemoryType.Mem or MemoryType.Mem64 or MemoryType.S or MemoryType.S64 or
            MemoryType.D or MemoryType.D64 or MemoryType.C or MemoryType.C64 => 8,

            MemoryType.Mem32 or MemoryType.Mem32s or MemoryType.S32 or MemoryType.S32s or
            MemoryType.D32 or MemoryType.D32s or MemoryType.C32 or MemoryType.C32s => 4,

            MemoryType.Mem16 or MemoryType.Mem16s or MemoryType.S16 or MemoryType.S16s or
            MemoryType.D16 or MemoryType.D16s or MemoryType.C16 or MemoryType.C16s => 2,

            MemoryType.Mem8 or MemoryType.Mem8s or MemoryType.S8 or MemoryType.S8s or
            MemoryType.D8 or MemoryType.D8s or MemoryType.C8 or MemoryType.C8s => 1,

            _ => throw new NotImplementedException($"Memory type {type}")
        };
    }

    private bool IsSignedMemoryType(MemoryType type)
    {
        return type.ToString().EndsWith("s");
    }

    private string GetRegisterSize(string reg, int size)
    {
        // raxの場合: 8=rax, 4=eax, 2=ax, 1=al
        var baseReg = reg.TrimStart('r');
        return size switch
        {
            8 => reg,
            4 => "e" + baseReg,
            2 => baseReg.Length > 1 ? baseReg.Substring(baseReg.Length - 2) : baseReg,
            1 => baseReg.Substring(baseReg.Length - 1) + "l",
            _ => reg
        };
    }

    private long EvaluateConstantExpression(Expression expr)
    {
        if (expr is NumberLiteral num)
            return num.Value;

        throw new NotImplementedException("Only constant literals are supported in data section");
    }

    private string GenerateLabel(string prefix)
    {
        return $"_{prefix}_{_labelCounter++}";
    }

    private void Emit(string line)
    {
        _output.AppendLine(line);
    }

    private void EmitAlign(Align align)
    {
        Emit($"    ; align {align.Alignment}");
        Emit($"    mov rax, rsp");
        Emit($"    and rax, {align.Alignment - 1}  ; rsp % {align.Alignment}");
        Emit($"    sub rsp, rax  ; align to {align.Alignment}-byte boundary");
    }

    private void EmitSyscallArgument(Expression arg, string destReg)
    {
        // For syscall arguments, d[] and c[] should load the ADDRESS, not the value
        if (arg is MemoryAccess mem && (
            mem.Type == MemoryType.D || mem.Type == MemoryType.D8 || mem.Type == MemoryType.D16 ||
            mem.Type == MemoryType.D32 || mem.Type == MemoryType.D64 ||
            mem.Type == MemoryType.D8s || mem.Type == MemoryType.D16s || mem.Type == MemoryType.D32s ||
            mem.Type == MemoryType.C || mem.Type == MemoryType.C8 || mem.Type == MemoryType.C16 ||
            mem.Type == MemoryType.C32 || mem.Type == MemoryType.C64 ||
            mem.Type == MemoryType.C8s || mem.Type == MemoryType.C16s || mem.Type == MemoryType.C32s))
        {
            // Load address of data/const section
            EmitLoadExpression(mem.Address, "rdi");
            var baseLabel = mem.Type.ToString().StartsWith("D") ? "_data_base" : "_rodata_base";
            Emit($"    lea rsi, [{baseLabel}]");
            Emit($"    add rdi, rsi");
            Emit($"    mov {destReg}, rdi  ; load address of {mem.Type}[]");
        }
        else
        {
            // Normal value load
            EmitLoadExpression(arg, destReg);
        }
    }

    private void EmitSyscall(Syscall syscall)
    {
        Emit($"    ; syscall {syscall.FunctionName}");

        // Windows x64 calling convention:
        // Args: RCX, RDX, R8, R9, then [RSP+32], [RSP+40], ...
        // Shadow space: 32 bytes minimum
        // RSP must be aligned to 16 bytes before CALL

        var argRegs = new[] { "rcx", "rdx", "r8", "r9" };

        // Calculate total stack space needed
        // Shadow space (32) + stack args (8 bytes each for args 5+)
        // CRITICAL: Must be multiple of 16 to keep RSP % 16 == 0
        int stackArgCount = Math.Max(0, syscall.Arguments.Count - 4);
        int shadowAndArgs = 32 + (stackArgCount * 8);
        // Round up to next multiple of 16
        int stackSpace = ((shadowAndArgs + 15) / 16) * 16;

        // Load arguments into registers (first 4)
        for (int i = 0; i < Math.Min(syscall.Arguments.Count, 4); i++)
        {
            EmitSyscallArgument(syscall.Arguments[i], argRegs[i]);
        }

        // Ensure RSP % 16 == 0 before allocating stack
        Emit($"    mov rax, rsp");
        Emit($"    and rax, 15");
        Emit($"    sub rsp, rax        ; align RSP to 16-byte boundary");

        // Allocate stack space for shadow space and stack arguments
        Emit($"    sub rsp, {stackSpace}  ; shadow + args (keeps RSP % 16 == 0)");

        // Load stack arguments (5+) if any
        if (stackArgCount > 0)
        {
            for (int i = 0; i < stackArgCount; i++)
            {
                EmitSyscallArgument(syscall.Arguments[i + 4], "rax");
                Emit($"    mov [rsp + {32 + i * 8}], rax  ; stack arg {i + 5}");
            }
        }

        // Call the function (GoLink import stub - direct call, not indirect)
        Emit($"    call {syscall.FunctionName}");

        // Check if this is a no-return function (like ExitProcess)
        bool isNoReturn = syscall.FunctionName == "ExitProcess";

        if (!isNoReturn)
        {
            // Clean up stack
            Emit($"    add rsp, {stackSpace}");

            // Store return value if needed
            if (syscall.Destination != null)
            {
                EmitStoreToDestination(syscall.Destination, "rax");
            }
        }
    }
}

