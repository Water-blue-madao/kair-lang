using System.Text;
using Kairc.IR;

namespace Kairc.CodeGen;

public class LlvmCodeGenerator
{
    private readonly IrProgram _program;
    private readonly StringBuilder _output = new();
    private int _labelCounter = 0;
    private readonly bool _emitComments;

    public LlvmCodeGenerator(IrProgram program, bool emitComments = false)
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
        Emit(".intel_syntax noprefix");
        Emit("");
        Emit(".globl Start");
        Emit("");
    }

    private void EmitDataSection()
    {
        var dataInits = _program.DataInitializations.Where(d => d.Section == BaseRegister.Data).OrderBy(d => d.Offset).ToList();
        var constInits = _program.DataInitializations.Where(d => d.Section == BaseRegister.Const).OrderBy(d => d.Offset).ToList();

        // データセクション
        Emit(".section .data");
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
                    Emit($"    .zero {padding}  # padding");
                    currentOffset = init.Offset;
                }

                Emit($"    .quad {init.Value}  # offset {init.Offset}");
                currentOffset += 8;  // dq = 8 バイト
            }
            Emit("");
        }
        else
        {
            Emit("_data_base:");
            Emit("");
        }

        // 読み取り専用データセクション
        Emit(".section .rodata");
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
                    Emit($"    .zero {padding}  # padding");
                    currentOffset = init.Offset;
                }

                Emit($"    .quad {init.Value}  # offset {init.Offset}");
                currentOffset += 8;  // dq = 8 バイト
            }
            Emit("");
        }
        else
        {
            Emit("_rodata_base:");
            Emit("");
        }
    }

    private void EmitTextSection()
    {
        Emit(".section .text");
        Emit("Start:");
        Emit("    # Initialize stack");
        Emit("    # IMPORTANT: Windows x64 calling convention");
        Emit("    # Entry: RSP % 16 == 0 (OS guarantees)");
        Emit("    mov rbp, rsp    # Save base pointer");
        Emit("");

        foreach (var stmt in _program.Statements)
        {
            EmitStatement(stmt);
        }

        // ENDラベル - 終了コードは s[0]
        Emit("");
        Emit("_END:");
        Emit("    # Exit program (Windows) - exit code from s[0]");
        Emit("    mov rcx, [rsp]      # Load exit code from s[0]");
        Emit("    # IMPORTANT: Align RSP to 16-byte boundary before CALL");
        Emit("    mov rax, rsp");
        Emit("    and rax, 15         # rsp % 16");
        Emit("    sub rsp, rax        # Adjust RSP so RSP % 16 == 0");
        Emit("    sub rsp, 32         # Shadow space (maintains RSP % 16 == 0)");
        Emit("    call ExitProcess    # Direct call (RSP % 16 == 0 required)");
    }

    private void EmitStatement(Statement stmt)
    {
        // コメント出力（オプション）
        if (_emitComments && stmt.SourceText != null)
        {
            Emit($"    # {stmt.SourceText}");
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
        // ソース → rax
        EmitLoadExpression(assign.Source, "rax");

        // rax → 代入先
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
                throw new NotImplementedException($"{dest.GetType().Name} への保存は未対応です");
        }
    }

    /// <summary>
    /// 新しい統一メモリアクセス構文へのストア: [base + offset]
    /// レジスタを消費せず、直接アドレッシング
    /// </summary>
    private void EmitStoreToBaseOffset(BaseOffsetAccess access, string sourceReg)
    {
        if (access.Base == BaseRegister.Sp)
        {
            // Stack: direct access
            Emit($"    mov [rsp + {access.Offset}], {sourceReg}");
        }
        else
        {
            // Data/Const: RIP-relative direct memory access
            string baseLabel = access.Base switch
            {
                BaseRegister.Data => "_data_base",
                BaseRegister.Const => "_rodata_base",
                _ => throw new NotImplementedException($"ベースレジスタ {access.Base} は未対応です")
            };

            // Direct RIP-relative memory access (LLVM MC supports this)
            Emit($"    mov [rip + {baseLabel} + {access.Offset}], {sourceReg}");
        }
    }

    private void EmitConditionalAssignment(ConditionalAssignment condAssign)
    {
        // Optimized: Use cmov to avoid branching
        // Strategy: Load new value to rax, then cmov old value if condition is false

        // Load new value (置換値) → rax
        EmitLoadExpression(condAssign.Source, "rax");

        // Load old value (元の値) → rbx
        EmitLoadExpression(condAssign.Destination, "rbx");

        // Compare for condition (left and right need temp registers)
        EmitLoadExpression(condAssign.Condition.Left, "r12");
        EmitLoadExpression(condAssign.Condition.Right, "r13");
        Emit("    cmp r12, r13");

        // If condition is FALSE, restore old value (反対条件で元値へcmov)
        string invertedCmov = GetCmovInstruction(condAssign.Condition.Operator, invert: true);
        Emit($"    {invertedCmov} rax, rbx  # Keep old value if condition is false");

        // Store result
        EmitStoreToDestination(condAssign.Destination, "rax");
    }

    private void EmitTernaryAssignment(TernaryAssignment ternary)
    {
        // Optimized: Use cmov to avoid branching
        // Strategy: Load true value to rax, false value to rbx, then cmov false if condition is false

        // Load true value → rax
        EmitLoadExpression(ternary.TrueValue, "rax");

        // Load false value → rbx
        EmitLoadExpression(ternary.FalseValue, "rbx");

        // Compare for condition
        EmitLoadExpression(ternary.Condition.Left, "r12");
        EmitLoadExpression(ternary.Condition.Right, "r13");
        Emit("    cmp r12, r13");

        // If condition is FALSE, select false value (反対条件でfalse値を選択)
        string invertedCmov = GetCmovInstruction(ternary.Condition.Operator, invert: true);
        Emit($"    {invertedCmov} rax, rbx  # Select false value if condition is false");

        // Store result
        EmitStoreToDestination(ternary.Destination, "rax");
    }

    private void EmitCompoundAssignment(CompoundAssignment compound)
    {
        // 代入先 → rax
        EmitLoadExpression(compound.Destination, "rax");

        // シフト演算は rcx を使う必要があるため特別扱い
        if (compound.Operator == BinaryOperator.LeftShift ||
            compound.Operator == BinaryOperator.RightShiftS ||
            compound.Operator == BinaryOperator.RightShiftU)
        {
            // ソース → rcx
            EmitLoadExpression(compound.Source, "rcx");
            // rax op= rcx
            EmitBinaryOperation(compound.Operator, "rax", "rcx");
        }
        else if (compound.Operator == BinaryOperator.DivS ||
                 compound.Operator == BinaryOperator.DivU ||
                 compound.Operator == BinaryOperator.ModS ||
                 compound.Operator == BinaryOperator.ModU)
        {
            // Division requires special handling
            EmitLoadExpression(compound.Source, "rbx");
            EmitBinaryOperation(compound.Operator, "rax", "rbx");
        }
        else
        {
            // Try to use memory operand for operations that support it
            if (compound.Source is BaseOffsetAccess sourceAccess)
            {
                string memOperand;
                if (sourceAccess.Base == BaseRegister.Sp)
                {
                    memOperand = $"[rsp + {sourceAccess.Offset}]";
                }
                else
                {
                    string baseLabel = sourceAccess.Base == BaseRegister.Data ? "_data_base" : "_rodata_base";
                    memOperand = $"[rip + {baseLabel} + {sourceAccess.Offset}]";
                }

                // Emit operation with memory operand
                switch (compound.Operator)
                {
                    case BinaryOperator.Add:
                        Emit($"    add rax, {memOperand}");
                        break;
                    case BinaryOperator.Sub:
                        Emit($"    sub rax, {memOperand}");
                        break;
                    case BinaryOperator.Mul:
                        Emit($"    imul rax, {memOperand}");
                        break;
                    case BinaryOperator.And:
                        Emit($"    and rax, {memOperand}");
                        break;
                    case BinaryOperator.Or:
                        Emit($"    or rax, {memOperand}");
                        break;
                    case BinaryOperator.Xor:
                        Emit($"    xor rax, {memOperand}");
                        break;
                    default:
                        // Fallback to register operand
                        EmitLoadExpression(compound.Source, "rbx");
                        EmitBinaryOperation(compound.Operator, "rax", "rbx");
                        break;
                }
            }
            else
            {
                // ソース → rbx
                EmitLoadExpression(compound.Source, "rbx");
                // rax op= rbx
                EmitBinaryOperation(compound.Operator, "rax", "rbx");
            }
        }

        // rax → 代入先
        EmitStoreToDestination(compound.Destination, "rax");
    }

    private void EmitStackPointerUpdate(StackPointerUpdate spUpdate)
    {
        // KIR: sp -= N は「N バイトを確保」(スタックを下方向に拡張)
        // ASM: sub rsp, N (アセンブリと同じ記法)

        // sp → rax
        Emit("    mov rax, rsp");

        // 値 → rbx
        EmitLoadExpression(spUpdate.Value, "rbx");

        // そのまま演算
        EmitBinaryOperation(spUpdate.Operator, "rax", "rbx");

        // rax → sp (RSP)
        Emit("    mov rsp, rax");
    }

    private void EmitConditionalGoto(ConditionalGoto condGoto)
    {
        EmitConditionJump(condGoto.Condition, $"_{condGoto.Target}", invert: false);
    }

    private void EmitConditionJump(Condition condition, string targetLabel, bool invert)
    {
        // 左辺 → rax
        EmitLoadExpression(condition.Left, "rax");

        // 右辺 → rbx
        EmitLoadExpression(condition.Right, "rbx");

        // 比較
        Emit("    cmp rax, rbx");

        // ジャンプ命令
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
            _ => throw new NotImplementedException($"比較演算子 {op} は未対応です")
        };
    }

    private string GetCmovInstruction(ComparisonOperator op, bool invert)
    {
        // cmov instruction mapping (same logic as jump instructions)
        return (op, invert) switch
        {
            (ComparisonOperator.Equal, false) => "cmove",
            (ComparisonOperator.Equal, true) => "cmovne",
            (ComparisonOperator.NotEqual, false) => "cmovne",
            (ComparisonOperator.NotEqual, true) => "cmove",
            (ComparisonOperator.LessS, false) => "cmovl",
            (ComparisonOperator.LessS, true) => "cmovge",
            (ComparisonOperator.LessU, false) => "cmovb",
            (ComparisonOperator.LessU, true) => "cmovae",
            (ComparisonOperator.LessEqualS, false) => "cmovle",
            (ComparisonOperator.LessEqualS, true) => "cmovg",
            (ComparisonOperator.LessEqualU, false) => "cmovbe",
            (ComparisonOperator.LessEqualU, true) => "cmova",
            (ComparisonOperator.GreaterS, false) => "cmovg",
            (ComparisonOperator.GreaterS, true) => "cmovle",
            (ComparisonOperator.GreaterU, false) => "cmova",
            (ComparisonOperator.GreaterU, true) => "cmovbe",
            (ComparisonOperator.GreaterEqualS, false) => "cmovge",
            (ComparisonOperator.GreaterEqualS, true) => "cmovl",
            (ComparisonOperator.GreaterEqualU, false) => "cmovae",
            (ComparisonOperator.GreaterEqualU, true) => "cmovb",
            _ => throw new NotImplementedException($"比較演算子 {op} は未対応です")
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
                Emit($"    lea {destReg}, [rip + {baseName}]");
                break;

            case BaseOffsetAccess baseOffset:
                EmitLoadFromBaseOffset(baseOffset, destReg);
                break;

            case MemoryAccess mem:
                EmitLoadFromMemory(mem, destReg);
                break;

            case BinaryOperation binOp:
                // 重要: push/pop を使わない (RSP を変更してはならない)
                // シフト演算は RCX を使う必要があるため特別扱い
                if (binOp.Operator == BinaryOperator.LeftShift ||
                    binOp.Operator == BinaryOperator.RightShiftS ||
                    binOp.Operator == BinaryOperator.RightShiftU)
                {
                    EmitLoadExpression(binOp.Left, destReg);
                    Emit($"    mov rbx, {destReg}");  // Save left operand to rbx
                    EmitLoadExpression(binOp.Right, "rcx");
                    Emit($"    mov {destReg}, rbx");  // Restore left operand
                    EmitBinaryOperation(binOp.Operator, destReg, "rcx");
                }
                else if (binOp.Operator == BinaryOperator.DivS ||
                         binOp.Operator == BinaryOperator.DivU ||
                         binOp.Operator == BinaryOperator.ModS ||
                         binOp.Operator == BinaryOperator.ModU)
                {
                    // Division requires rax/rdx, load dividend to rax, divisor to rbx
                    EmitLoadExpression(binOp.Left, destReg);  // Load left (dividend) to rax
                    Emit($"    mov r12, {destReg}");  // Save dividend to r12
                    EmitLoadExpression(binOp.Right, "rbx");  // Load divisor to rbx
                    Emit($"    mov {destReg}, r12");  // Restore dividend to rax
                    EmitBinaryOperation(binOp.Operator, destReg, "rbx");
                }
                else
                {
                    // Simplified: load left → operation with right (memory operand allowed)
                    EmitLoadExpression(binOp.Left, destReg);

                    // For operations that support memory operands (add, sub, imul, and, or, xor)
                    // Check if right operand is a direct memory access
                    if (binOp.Right is BaseOffsetAccess rightAccess)
                    {
                        // Generate memory operand directly
                        string memOperand;
                        if (rightAccess.Base == BaseRegister.Sp)
                        {
                            memOperand = $"[rsp + {rightAccess.Offset}]";
                        }
                        else
                        {
                            string baseLabel = rightAccess.Base == BaseRegister.Data ? "_data_base" : "_rodata_base";
                            memOperand = $"[rip + {baseLabel} + {rightAccess.Offset}]";
                        }

                        // Emit operation with memory operand
                        switch (binOp.Operator)
                        {
                            case BinaryOperator.Add:
                                Emit($"    add {destReg}, {memOperand}");
                                break;
                            case BinaryOperator.Sub:
                                Emit($"    sub {destReg}, {memOperand}");
                                break;
                            case BinaryOperator.Mul:
                                Emit($"    imul {destReg}, {memOperand}");
                                break;
                            case BinaryOperator.And:
                                Emit($"    and {destReg}, {memOperand}");
                                break;
                            case BinaryOperator.Or:
                                Emit($"    or {destReg}, {memOperand}");
                                break;
                            case BinaryOperator.Xor:
                                Emit($"    xor {destReg}, {memOperand}");
                                break;
                            default:
                                // Fallback to register operand
                                EmitLoadExpression(binOp.Right, "rbx");
                                EmitBinaryOperation(binOp.Operator, destReg, "rbx");
                                break;
                        }
                    }
                    else
                    {
                        // Other expressions: load to rbx and operate
                        EmitLoadExpression(binOp.Right, "rbx");
                        EmitBinaryOperation(binOp.Operator, destReg, "rbx");
                    }
                }
                break;

            case UnaryOperation unOp:
                EmitLoadExpression(unOp.Operand, destReg);
                EmitUnaryOperation(unOp.Operator, destReg);
                break;

            default:
                throw new NotImplementedException($"式種類 {expr.GetType().Name} は未対応です");
        }
    }

    /// <summary>
    /// 新しい統一メモリアクセス構文からロード: [base + offset]
    /// レジスタを消費せず、直接アドレッシング
    /// </summary>
    private void EmitLoadFromBaseOffset(BaseOffsetAccess access, string destReg)
    {
        if (access.Base == BaseRegister.Sp)
        {
            // Stack: direct access
            Emit($"    mov {destReg}, [rsp + {access.Offset}]");
        }
        else
        {
            // Data/Const: RIP-relative direct memory access
            string baseLabel = access.Base switch
            {
                BaseRegister.Data => "_data_base",
                BaseRegister.Const => "_rodata_base",
                _ => throw new NotImplementedException($"ベースレジスタ {access.Base} は未対応です")
            };

            // Direct RIP-relative memory access (LLVM MC supports this)
            Emit($"    mov {destReg}, [rip + {baseLabel} + {access.Offset}]");
        }
    }

    private void EmitLoadFromMemory(MemoryAccess mem, string destReg)
    {
        // 完全に単純化: アドレス計算 → ロード (各ステップで完結)
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
        // スタックを使わずに sourceReg を退避 (push は RSP を変更してしまう)
        Emit($"    mov rbx, {sourceReg}  # Save value");
        var address = CalculateAddress(mem);
        Emit($"    mov {sourceReg}, rbx  # Restore value");

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
        // 完全に単純化: アドレス計算専用レジスタ rdi を使用
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

            _ => throw new NotImplementedException($"メモリ種別 {mem.Type} は未対応です")
        };

        // アドレス計算を rdi で行う (専用レジスタとして利用)
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
                // sourceReg は rcx であることが前提 (EmitLoadExpression で保証)
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
                throw new NotImplementedException($"二項演算子 {op} は未対応です");
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
                throw new NotImplementedException($"単項演算子 {op} は未対応です");
        }
    }

    // DEPRECATED: メモリサイズ指定 (s8, s16, s32, mem8, etc.) は廃止予定
    // 別の構文で対応する予定のため、この機能は将来削除される
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

            _ => throw new NotImplementedException($"メモリ種別 {type} は未対応です")
        };
    }

    // DEPRECATED: メモリサイズ指定は廃止予定
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

        throw new NotImplementedException("データセクションでは定数リテラルのみをサポートしています");
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
        Emit($"    # align {align.Alignment}");
        Emit($"    mov rax, rsp");
        Emit($"    and rax, {align.Alignment - 1}  # rsp % {align.Alignment}");
        Emit($"    sub rsp, rax  # align to {align.Alignment}-byte boundary");
    }

    private void EmitSyscallArgument(Expression arg, string destReg)
    {
        // システムコールの引数では、d[] と c[] は値ではなくアドレスを読み込む必要がある
        if (arg is MemoryAccess mem && (
            mem.Type == MemoryType.D || mem.Type == MemoryType.D8 || mem.Type == MemoryType.D16 ||
            mem.Type == MemoryType.D32 || mem.Type == MemoryType.D64 ||
            mem.Type == MemoryType.D8s || mem.Type == MemoryType.D16s || mem.Type == MemoryType.D32s ||
            mem.Type == MemoryType.C || mem.Type == MemoryType.C8 || mem.Type == MemoryType.C16 ||
            mem.Type == MemoryType.C32 || mem.Type == MemoryType.C64 ||
            mem.Type == MemoryType.C8s || mem.Type == MemoryType.C16s || mem.Type == MemoryType.C32s))
        {
            // データ/定数セクションのアドレスをロード
            EmitLoadExpression(mem.Address, "rdi");
            var baseLabel = mem.Type.ToString().StartsWith("D") ? "_data_base" : "_rodata_base";
            Emit($"    lea rsi, [{baseLabel}]");
            Emit($"    add rdi, rsi");
            Emit($"    mov {destReg}, rdi  # Load address of {mem.Type}[]");
        }
        else
        {
            // 通常の値読み込み
            EmitLoadExpression(arg, destReg);
        }
    }

    private void EmitSyscall(Syscall syscall)
    {
        Emit($"    # syscall {syscall.FunctionName}");

        // Windows x64 呼び出し規約:
        // 引数: RCX, RDX, R8, R9, それ以降は [RSP+32], [RSP+40], ...
        // シャドウスペース: 最低 32 バイト
        // CALL の前に RSP は16バイト境界に整列されている必要がある

        var argRegs = new[] { "rcx", "rdx", "r8", "r9" };

        // スタック領域の合計を計算
        // シャドウスペース (32) + スタック引数 (引数5以降は1つにつき8バイト)
        // 重要: RSP % 16 == 0 を維持するため16の倍数にする必要がある
        int stackArgCount = Math.Max(0, syscall.Arguments.Count - 4);
        int shadowAndArgs = 32 + (stackArgCount * 8);
        // 次の16の倍数に切り上げ
        int stackSpace = ((shadowAndArgs + 15) / 16) * 16;

        // 引数をレジスタにロード (先頭4つ)
        for (int i = 0; i < Math.Min(syscall.Arguments.Count, 4); i++)
        {
            EmitSyscallArgument(syscall.Arguments[i], argRegs[i]);
        }

        // スタックを確保する前に RSP % 16 == 0 を保証
        Emit($"    mov rax, rsp");
        Emit($"    and rax, 15");
        Emit($"    sub rsp, rax  # Align RSP to 16-byte boundary");

        // シャドウスペースおよびスタック引数分の領域を確保
        Emit($"    sub rsp, {stackSpace}  # Shadow space + args (RSP % 16 == 0 maintained)");

        // スタック引数 (5番目以降) をロード
        if (stackArgCount > 0)
        {
            for (int i = 0; i < stackArgCount; i++)
            {
                EmitSyscallArgument(syscall.Arguments[i + 4], "rax");
                Emit($"    mov [rsp + {32 + i * 8}], rax  # stack arg {i + 5}");
            }
        }

        // 関数を呼び出す (GoLink のインポートスタブ - 間接呼び出しではなく直接呼び出す)
        Emit($"    call {syscall.FunctionName}");

        // 戻らない関数 (ExitProcess など) かどうかを確認
        bool isNoReturn = syscall.FunctionName == "ExitProcess";

        if (!isNoReturn)
        {
            // スタックを解放
            Emit($"    add rsp, {stackSpace}");

            // 戻り値が必要なら保存
            if (syscall.Destination != null)
            {
                EmitStoreToDestination(syscall.Destination, "rax");
            }
        }
    }
}

