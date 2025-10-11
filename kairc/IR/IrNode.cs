namespace Kairc.IR;

/// <summary>
/// 内部IRの基底ノード
/// Asm IRの全構文を表現可能 + コンパイラ拡張
/// </summary>
public abstract class IrNode
{
    public int Line { get; set; }
    public int Column { get; set; }
    public string? SourceText { get; set; }  // 元のKIRソースコード行
}

// ========== プログラム構造 ==========

public class IrProgram : IrNode
{
    public List<DataInitialization> DataInitializations { get; set; } = new();
    public List<Statement> Statements { get; set; } = new();
}

// ========== データ初期化 ==========

/// <summary>
/// トップレベルでのデータセクション初期化
/// [data + offset] = value または [const + offset] = value
/// </summary>
public class DataInitialization : IrNode
{
    public BaseRegister Section { get; set; }  // Data または Const
    public long Offset { get; set; }
    public long Value { get; set; }  // 静的な値のみ
}

// ========== ステートメント ==========

public abstract class Statement : IrNode { }

public class Label : Statement
{
    public string Name { get; set; } = "";
}

public class Assignment : Statement
{
    public Expression Destination { get; set; } = null!;  // MemoryAccess または BaseOffsetAccess
    public Expression Source { get; set; } = null!;
}

public class ConditionalAssignment : Statement
{
    public Expression Destination { get; set; } = null!;  // MemoryAccess または BaseOffsetAccess
    public Expression Source { get; set; } = null!;
    public Condition Condition { get; set; } = null!;
}

public class TernaryAssignment : Statement
{
    public Expression Destination { get; set; } = null!;  // MemoryAccess または BaseOffsetAccess
    public Condition Condition { get; set; } = null!;
    public Expression TrueValue { get; set; } = null!;
    public Expression FalseValue { get; set; } = null!;
}

public class CompoundAssignment : Statement
{
    public Expression Destination { get; set; } = null!;  // MemoryAccess または BaseOffsetAccess
    public BinaryOperator Operator { get; set; }
    public Expression Source { get; set; } = null!;
}

public class Goto : Statement
{
    public string Target { get; set; } = "";
}

public class ConditionalGoto : Statement
{
    public string Target { get; set; } = "";
    public Condition Condition { get; set; } = null!;
}

public class Pass : Statement
{
    public int Count { get; set; } = 1;  // pass * count
}

public class StackPointerUpdate : Statement
{
    public BinaryOperator Operator { get; set; }
    public Expression Value { get; set; } = null!;
}

public class Align : Statement
{
    public int Alignment { get; set; }  // 16 または 8
}

public class Syscall : Statement
{
    public Expression? Destination { get; set; }  // null の場合は戻り値なし (MemoryAccess または BaseOffsetAccess)
    public string FunctionName { get; set; } = "";
    public List<Expression> Arguments { get; set; } = new();
}

// ========== 式 ==========

public abstract class Expression : IrNode { }

public class NumberLiteral : Expression
{
    public long Value { get; set; }
    public bool IsLong { get; set; }  // @long アノテーション
}

public class MemoryAccess : Expression
{
    public MemoryType Type { get; set; }
    public Expression Address { get; set; } = null!;

    // 最適化用（将来実装）
    public string? AllocatedRegister { get; set; }
}

public class StackPointer : Expression { }  // sp

/// <summary>
/// データベースアドレス (data, const) - アドレス値として使用
/// 例: s[8] = data; s[8] += 32
/// </summary>
public class DataBaseAddress : Expression
{
    public BaseRegister Section { get; set; }  // Data または Const
}

/// <summary>
/// 新しい統一メモリアクセス構文: [base + offset]
/// base: sp, data, const
/// offset: 静的な数値のみ（動的オフセットは内部的にサポートするが構文としては認めない）
/// </summary>
public class BaseOffsetAccess : Expression
{
    public BaseRegister Base { get; set; }
    public long Offset { get; set; }  // 静的オフセット
}

public class BinaryOperation : Expression
{
    public Expression Left { get; set; } = null!;
    public BinaryOperator Operator { get; set; }
    public Expression Right { get; set; } = null!;
}

public class UnaryOperation : Expression
{
    public UnaryOperator Operator { get; set; }
    public Expression Operand { get; set; } = null!;
}

// ========== 条件式 ==========

public class Condition : IrNode
{
    public Expression Left { get; set; } = null!;
    public ComparisonOperator Operator { get; set; }
    public Expression Right { get; set; } = null!;
}

// ========== 列挙型 ==========

// DEPRECATED: メモリサイズ指定 (s8, s16, s32, mem8, etc.) は廃止予定
// 別の構文で対応する予定のため、サイズ付きバリアントは将来削除される
public enum MemoryType
{
    // ピュアメモリ
    Mem, Mem8, Mem16, Mem32, Mem64,
    Mem8s, Mem16s, Mem32s,

    // スタック
    S, S8, S16, S32, S64,
    S8s, S16s, S32s,

    // データセクション
    D, D8, D16, D32, D64,
    D8s, D16s, D32s,

    // 定数セクション
    C, C8, C16, C32, C64,
    C8s, C16s, C32s,
}

public enum BinaryOperator
{
    Add, Sub, Mul,
    DivS, DivU,
    ModS, ModU,
    And, Or, Xor,
    LeftShift,
    RightShiftS, RightShiftU,
}

public enum UnaryOperator
{
    Negate,  // -
    Not,     // ~
}

public enum ComparisonOperator
{
    Equal, NotEqual,
    LessS, LessU,
    LessEqualS, LessEqualU,
    GreaterS, GreaterU,
    GreaterEqualS, GreaterEqualU,
}

public enum BaseRegister
{
    Sp,      // スタックポインタ
    Data,    // データセクション (_data_base)
    Const,   // 読み取り専用 (_rodata_base)
}

