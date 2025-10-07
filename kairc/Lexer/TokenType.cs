namespace Kairc.Lexer;

public enum TokenType
{
    // リテラル
    Number,           // 123, 0xFF
    Identifier,       // label, loop, etc.

    // キーワード
    Goto,             // goto
    If,               // if
    Pass,             // pass
    Sp,               // sp
    Data,             // data (ベースレジスタ)
    Const,            // const (ベースレジスタ)
    End,              // END
    Align,            // align
    Syscall,          // syscall

    // メモリアクセス
    Mem,              // mem
    Mem8,             // mem8
    Mem16,            // mem16
    Mem32,            // mem32
    Mem64,            // mem64
    Mem8s,            // mem8s
    Mem16s,           // mem16s
    Mem32s,           // mem32s

    S,                // s (stack)
    S8,               // s8
    S16,              // s16
    S32,              // s32
    S64,              // s64
    S8s,              // s8s
    S16s,             // s16s
    S32s,             // s32s

    D,                // d (data)
    D8,               // d8
    D16,              // d16
    D32,              // d32
    D64,              // d64
    D8s,              // d8s
    D16s,             // d16s
    D32s,             // d32s

    C,                // c (const/rodata)
    C8,               // c8
    C16,              // c16
    C32,              // c32
    C64,              // c64
    C8s,              // c8s
    C16s,             // c16s
    C32s,             // c32s

    // 演算子
    Plus,             // +
    Minus,            // -
    Star,             // *
    Slash,            // /
    Percent,          // %
    Ampersand,        // &
    Pipe,             // |
    Caret,            // ^
    Tilde,            // ~
    LeftShift,        // <<
    RightShift,       // >>

    // 符号付き演算子
    DivS,             // /s
    DivU,             // /u
    ModS,             // %s
    ModU,             // %u
    RightShiftS,      // >>s
    RightShiftU,      // >>u

    // 比較演算子
    Equal,            // ==
    NotEqual,         // !=
    LessS,            // <s
    LessU,            // <u
    LessEqualS,       // <=s
    LessEqualU,       // <=u
    GreaterS,         // >s
    GreaterU,         // >u
    GreaterEqualS,    // >=s
    GreaterEqualU,    // >=u

    // 代入演算子
    Assign,           // =
    PlusAssign,       // +=
    MinusAssign,      // -=
    StarAssign,       // *=
    DivSAssign,       // /s=
    DivUAssign,       // /u=
    ModSAssign,       // %s=
    ModUAssign,       // %u=
    AmpersandAssign,  // &=
    PipeAssign,       // |=
    CaretAssign,      // ^=
    LeftShiftAssign,  // <<=
    RightShiftSAssign,// >>s=
    RightShiftUAssign,// >>u=

    // 区切り文字
    LeftBracket,      // [
    RightBracket,     // ]
    LeftParen,        // (
    RightParen,       // )
    Question,         // ?
    Colon,            // :
    Hash,             // #
    Comma,            // ,

    // アノテーション
    AtLong,           // @long

    // 特殊
    Newline,          // \n
    Eof,              // End of file
}

