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
        Emit("global Start  ; 重要: GoLinkでは 'Start' (先頭に '_' なし) である必要がある");
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
                    Emit($"    resb {padding}  ; パディング");
                    currentOffset = init.Offset;
                }

                Emit($"    dq {init.Value}  ; オフセット {init.Offset}");
                currentOffset += 8;  // dq = 8 バイト
            }
            Emit("");
        }
        else
        {
            Emit("    _data_base: resb 4096  ; データ領域として確保");
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
                    Emit($"    resb {padding}  ; パディング");
                    currentOffset = init.Offset;
                }

                Emit($"    dq {init.Value}  ; オフセット {init.Offset}");
                currentOffset += 8;  // dq = 8 バイト
            }
            Emit("");
        }
        else
        {
            Emit("    _rodata_base: resb 4096  ; 定数データ領域として確保");
            Emit("");
        }
    }

    private void EmitTextSection()
    {
        Emit("section .text");
        Emit("Start:");
        Emit("    ; スタックを初期化");
        Emit("    ; 重要: Windows x64 呼び出し規約");
        Emit("    ; エントリ時: RSP % 16 == 0 (OS が整列)");
        Emit("    ; 初期整列は不要 - syscall 側で対応");
        Emit("    mov rbp, rsp    ; ベースポインタを保存");
        Emit("");

        foreach (var stmt in _program.Statements)
        {
            EmitStatement(stmt);
        }

        // ENDラベル - 終了コードは s[0]
        Emit("");
        Emit("_END:");
        Emit("    ; プログラムを終了 (Windows) - 終了コードは s[0]");
        Emit("    mov rcx, [rsp]      ; s[0] から終了コードを読み出す");
        Emit("    ; 重要: CALL の前に RSP を16バイト境界に整列");
        Emit("    mov rax, rsp");
        Emit("    and rax, 15         ; rsp % 16");
        Emit("    sub rsp, rax        ; RSP を調整し RSP % 16 == 0 にする");
        Emit("    sub rsp, 32         ; シャドウスペース (RSP % 16 == 0 を維持)");
        Emit("    call ExitProcess    ; 直接呼び出し (RSP % 16 == 0 が必須)");
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
        string baseLabel = access.Base switch
        {
            BaseRegister.Sp => "rsp",
            BaseRegister.Data => "_data_base",
            BaseRegister.Const => "_rodata_base",
            _ => throw new NotImplementedException($"ベースレジスタ {access.Base} は未対応です")
        };

        // 静的オフセット: 直接 [base + offset] でアクセス（レジスタ不要）
        Emit($"    mov [{baseLabel} + {access.Offset}], {sourceReg}");
    }

    private void EmitConditionalAssignment(ConditionalAssignment condAssign)
    {
        var skipLabel = GenerateLabel("skip_assign");

        // 条件を評価し、成り立たない場合はスキップ
        EmitConditionJump(condAssign.Condition, skipLabel, invert: true);

        // ソース → rax
        EmitLoadExpression(condAssign.Source, "rax");

        // rax → 代入先
        EmitStoreToDestination(condAssign.Destination, "rax");

        Emit($"{skipLabel}:");
    }

    private void EmitTernaryAssignment(TernaryAssignment ternary)
    {
        var falseLabel = GenerateLabel("ternary_false");
        var endLabel = GenerateLabel("ternary_end");

        // 条件を評価
        EmitConditionJump(ternary.Condition, falseLabel, invert: true);

        // 真の場合の値 → rax
        EmitLoadExpression(ternary.TrueValue, "rax");
        Emit($"    jmp {endLabel}");

        Emit($"{falseLabel}:");
        // 偽の場合の値 → rax
        EmitLoadExpression(ternary.FalseValue, "rax");

        Emit($"{endLabel}:");
        // rax → 代入先
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
        else
        {
            // ソース → rbx
            EmitLoadExpression(compound.Source, "rbx");
            // rax op= rbx
            EmitBinaryOperation(compound.Operator, "rax", "rbx");
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
                // 重要: push/pop を使わない (RSP を変更してはならない)
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
                throw new NotImplementedException($"式種類 {expr.GetType().Name} は未対応です");
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
            _ => throw new NotImplementedException($"ベースレジスタ {access.Base} は未対応です")
        };

        // 静的オフセット: 直接 [base + offset] でアクセス（レジスタ不要）
        Emit($"    mov {destReg}, [{baseLabel} + {access.Offset}]");
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
        Emit($"    mov rbx, {sourceReg}  ; 値を退避");
        var address = CalculateAddress(mem);
        Emit($"    mov {sourceReg}, rbx  ; 値を戻す");

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
        Emit($"    ; align {align.Alignment}");
        Emit($"    mov rax, rsp");
        Emit($"    and rax, {align.Alignment - 1}  ; rsp % {align.Alignment}");
        Emit($"    sub rsp, rax  ; align to {align.Alignment}-byte boundary");
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
            Emit($"    mov {destReg}, rdi  ; {mem.Type}[] のアドレスを読み込む");
        }
        else
        {
            // 通常の値読み込み
            EmitLoadExpression(arg, destReg);
        }
    }

    private void EmitSyscall(Syscall syscall)
    {
        Emit($"    ; システムコール {syscall.FunctionName}");

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
        Emit($"    sub rsp, rax        ; RSP を16バイト境界に整列");

        // シャドウスペースおよびスタック引数分の領域を確保
        Emit($"    sub rsp, {stackSpace}  ; シャドウスペース + 引数 (RSP % 16 == 0 を維持)");

        // スタック引数 (5番目以降) をロード
        if (stackArgCount > 0)
        {
            for (int i = 0; i < stackArgCount; i++)
            {
                EmitSyscallArgument(syscall.Arguments[i + 4], "rax");
                Emit($"    mov [rsp + {32 + i * 8}], rax  ; スタック引数 {i + 5}");
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

