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

