using Kairc.IR;
using Kairc.Lexer;

namespace Kairc.Parser;

public class Parser
{
    private readonly List<Token> _tokens;
    private readonly string[] _sourceLines;
    private int _current = 0;

    public Parser(List<Token> tokens, string[]? sourceLines = null)
    {
        _tokens = tokens;
        _sourceLines = sourceLines ?? Array.Empty<string>();
    }

    public IrProgram Parse()
    {
        var program = new IrProgram();

        // データ初期化セクション（トップレベル制約）
        ConsumeNewlines();  // 最初の空行やコメントをスキップ
        while (!IsAtEnd() && IsDataInitialization())
        {
            var dataInit = ParseDataInitialization();
            if (dataInit != null)
                program.DataInitializations.Add(dataInit);

            ConsumeNewlines();
        }

        // コードセクション（通常のステートメント）
        while (!IsAtEnd())
        {
            ConsumeNewlines();
            if (IsAtEnd())
                break;

            var stmt = ParseStatement();
            if (stmt != null)
                program.Statements.Add(stmt);

            ConsumeNewlines();
        }

        return program;
    }

    // ========== データ初期化 ==========

    private bool IsDataInitialization()
    {
        // [data + ...] = ... または [const + ...] = ... かチェック
        if (!Check(TokenType.LeftBracket))
            return false;

        // 先読みして判定（消費しない）
        int savedPos = _current;
        try
        {
            Advance(); // [
            if (!Check(TokenType.Data) && !Check(TokenType.Const))
                return false;

            Advance(); // data/const
            if (!Check(TokenType.Plus))
                return false;

            Advance(); // +
            if (!Check(TokenType.Number))
                return false;

            Advance(); // number
            if (!Check(TokenType.RightBracket))
                return false;

            Advance(); // ]
            return Check(TokenType.Assign);
        }
        finally
        {
            _current = savedPos;
        }
    }

    private DataInitialization? ParseDataInitialization()
    {
        // [data/const + offset] = value
        Consume(TokenType.LeftBracket, "Expected '['");

        BaseRegister section;
        if (Match(TokenType.Data))
            section = BaseRegister.Data;
        else if (Match(TokenType.Const))
            section = BaseRegister.Const;
        else
            throw Error("Expected 'data' or 'const' in data initialization");

        Consume(TokenType.Plus, "Expected '+'");
        long offset = ParseNumberValue();
        Consume(TokenType.RightBracket, "Expected ']'");
        Consume(TokenType.Assign, "Expected '='");

        // 値は静的な数値リテラルのみ
        if (!Check(TokenType.Number))
            throw Error("Data initialization value must be a static number literal");

        long value = ParseNumberValue();

        return new DataInitialization
        {
            Section = section,
            Offset = offset,
            Value = value
        };
    }

    // ========== ステートメント ==========

    private Statement? ParseStatement()
    {
        // ソース行を記録
        var lineNumber = _current < _tokens.Count ? _tokens[_current].Line : 0;
        var sourceLine = (lineNumber > 0 && lineNumber <= _sourceLines.Length)
            ? _sourceLines[lineNumber - 1].Trim()
            : null;

        // ラベル
        if (Check(TokenType.Hash))
        {
            Advance(); // #
            var name = Consume(TokenType.Identifier, "Expected label name").Lexeme;
            return new Label { Name = name, SourceText = sourceLine };
        }

        // goto
        if (Match(TokenType.Goto))
        {
            string target;
            if (Match(TokenType.End))
            {
                target = "END";
            }
            else
            {
                target = Consume(TokenType.Identifier, "Expected label name or END").Lexeme;
            }

            // 条件付き goto
            if (Match(TokenType.If))
            {
                var condition = ParseCondition();
                return new ConditionalGoto { Target = target, Condition = condition, SourceText = sourceLine };
            }

            return new Goto { Target = target, SourceText = sourceLine };
        }

        // pass
        if (Match(TokenType.Pass))
        {
            if (Match(TokenType.Star))
            {
                var count = (int)ParseNumberValue();
                return new Pass { Count = count, SourceText = sourceLine };
            }
            return new Pass { Count = 1, SourceText = sourceLine };
        }

        // align
        if (Match(TokenType.Align))
        {
            var alignment = (int)ParseNumberValue();
            if (alignment != 8 && alignment != 16)
                throw Error("Alignment must be 8 or 16");
            return new Align { Alignment = alignment, SourceText = sourceLine };
        }

        // syscall
        if (Match(TokenType.Syscall))
        {
            var functionName = Consume(TokenType.Identifier, "Expected function name").Lexeme;
            var args = new List<Expression>();

            // Parse arguments: syscall FuncName, arg1, arg2, ...
            if (Match(TokenType.Comma))
            {
                args.Add(ParseExpression());
                while (Match(TokenType.Comma))
                {
                    args.Add(ParseExpression());
                }
            }

            return new Syscall
            {
                Destination = null,
                FunctionName = functionName,
                Arguments = args,
                SourceText = sourceLine
            };
        }

        // 代入系（新構文 [base + offset] と糖衣構文 s[]/d[]/c[] をサポート）
        if (Check(TokenType.LeftBracket) || IsMemoryAccess())
        {
            Expression dest;
            if (Check(TokenType.LeftBracket))
                dest = ParseBaseOffsetAccess();
            else
                dest = ParseMemoryAccess();  // s[]/d[]/c[] は内部で [sp/data/const + offset] にデシュガーされる

            // 複合代入 (+=, -=, etc.)
            if (IsCompoundAssignOperator())
            {
                var op = ParseCompoundAssignOperator();
                var source = ParseExpression();
                return new CompoundAssignment
                {
                    Destination = dest,
                    Operator = op,
                    Source = source,
                    SourceText = sourceLine
                };
            }

            // 通常の代入
            Consume(TokenType.Assign, "Expected '=' or compound assignment operator");

            // syscall with return value
            if (Match(TokenType.Syscall))
            {
                var functionName = Consume(TokenType.Identifier, "Expected function name").Lexeme;
                var args = new List<Expression>();

                // Parse arguments: s[0] = syscall FuncName, arg1, arg2, ...
                if (Match(TokenType.Comma))
                {
                    args.Add(ParseExpression());
                    while (Match(TokenType.Comma))
                    {
                        args.Add(ParseExpression());
                    }
                }

                return new Syscall
                {
                    Destination = dest,
                    FunctionName = functionName,
                    Arguments = args,
                    SourceText = sourceLine
                };
            }

            // 三項演算子
            if (Match(TokenType.LeftParen))
            {
                var condition = ParseCondition();
                Consume(TokenType.RightParen, "Expected ')'");
                Consume(TokenType.Question, "Expected '?'");
                var trueValue = ParseExpression();
                Consume(TokenType.Colon, "Expected ':'");
                var falseValue = ParseExpression();

                return new TernaryAssignment
                {
                    Destination = dest,
                    Condition = condition,
                    TrueValue = trueValue,
                    FalseValue = falseValue,
                    SourceText = sourceLine
                };
            }

            var expr = ParseExpression();

            // 条件付き代入
            if (Match(TokenType.If))
            {
                var condition = ParseCondition();
                return new ConditionalAssignment
                {
                    Destination = dest,
                    Source = expr,
                    Condition = condition,
                    SourceText = sourceLine
                };
            }

            return new Assignment
            {
                Destination = dest,
                Source = expr,
                SourceText = sourceLine
            };
        }

        // スタックポインタへの代入 (sp += value など)
        if (Match(TokenType.Sp))
        {
            if (IsCompoundAssignOperator())
            {
                var op = ParseCompoundAssignOperator();
                var source = ParseExpression();

                return new StackPointerUpdate
                {
                    Operator = op,
                    Value = source,
                    SourceText = sourceLine
                };
            }
        }

        var unexpected = Peek();
        throw Error($"Unexpected token: {unexpected.Type} ('{unexpected.Lexeme}')");
    }

    // ========== 式 ==========

    private Expression ParseExpression()
    {
        return ParseBinaryExpression();
    }

    private Expression ParseBinaryExpression()
    {
        var left = ParseUnaryExpression();

        while (IsBinaryOperator())
        {
            var op = ParseBinaryOperator();
            var right = ParseUnaryExpression();
            left = new BinaryOperation
            {
                Left = left,
                Operator = op,
                Right = right
            };
        }

        return left;
    }

    private Expression ParseUnaryExpression()
    {
        if (Match(TokenType.Minus))
        {
            var operand = ParsePrimaryExpression();
            return new UnaryOperation
            {
                Operator = UnaryOperator.Negate,
                Operand = operand
            };
        }

        if (Match(TokenType.Tilde))
        {
            var operand = ParsePrimaryExpression();
            return new UnaryOperation
            {
                Operator = UnaryOperator.Not,
                Operand = operand
            };
        }

        return ParsePrimaryExpression();
    }

    private Expression ParsePrimaryExpression()
    {
        // 数値リテラル
        if (Check(TokenType.Number))
        {
            var isLong = false;
            if (Match(TokenType.AtLong))
                isLong = true;

            var value = ParseNumberValue();
            return new NumberLiteral { Value = value, IsLong = isLong };
        }

        // @long number
        if (Match(TokenType.AtLong))
        {
            var value = ParseNumberValue();
            return new NumberLiteral { Value = value, IsLong = true };
        }

        // スタックポインタ
        if (Match(TokenType.Sp))
        {
            return new StackPointer();
        }

        // データベースレジスタ (data, const) - アドレス値として使用
        if (Match(TokenType.Data))
        {
            return new DataBaseAddress { Section = BaseRegister.Data };
        }
        if (Match(TokenType.Const))
        {
            return new DataBaseAddress { Section = BaseRegister.Const };
        }

        // 新しい統一メモリアクセス: [base + offset]
        if (Check(TokenType.LeftBracket))
        {
            return ParseBaseOffsetAccess();
        }

        // 旧メモリアクセス (s[], d[], c[] など)
        if (IsMemoryAccess())
        {
            return ParseMemoryAccess();
        }

        // 括弧
        if (Match(TokenType.LeftParen))
        {
            var expr = ParseExpression();
            Consume(TokenType.RightParen, "Expected ')'");
            return expr;
        }

        throw Error($"Expected expression, got {Peek().Type}");
    }

    /// <summary>
    /// 新しい統一メモリアクセス構文: [base + offset]
    /// base: sp, data, const
    /// offset: 静的な数値のみ
    /// </summary>
    private BaseOffsetAccess ParseBaseOffsetAccess()
    {
        Consume(TokenType.LeftBracket, "Expected '['");

        // base を読む (sp, data, const)
        BaseRegister baseReg;
        if (Match(TokenType.Sp))
            baseReg = BaseRegister.Sp;
        else if (Match(TokenType.Data))
            baseReg = BaseRegister.Data;
        else if (Match(TokenType.Const))
            baseReg = BaseRegister.Const;
        else
            throw Error("Expected base register (sp, data, or const)");

        // + を読む
        Consume(TokenType.Plus, "Expected '+' after base register");

        // offset を読む (静的な数値のみ)
        if (!Check(TokenType.Number))
            throw Error("Expected static number offset (dynamic offsets not supported in this syntax)");

        long offset = ParseNumberValue();

        Consume(TokenType.RightBracket, "Expected ']'");

        return new BaseOffsetAccess
        {
            Base = baseReg,
            Offset = offset
        };
    }

    private Expression ParseMemoryAccess()
    {
        var type = ParseMemoryType();
        Consume(TokenType.LeftBracket, "Expected '['");
        var address = ParseExpression();
        Consume(TokenType.RightBracket, "Expected ']'");

        // シンタックスシュガー: s[x] -> [sp + x], d[x] -> [data + x], c[x] -> [const + x]
        // アドレスが静的な数値の場合のみデシュガー可能
        if (address is NumberLiteral num && !num.IsLong)
        {
            // s[] -> [sp + offset]
            if (type >= MemoryType.S && type <= MemoryType.S32s)
            {
                return new BaseOffsetAccess
                {
                    Base = BaseRegister.Sp,
                    Offset = num.Value
                };
            }
            // d[] -> [data + offset]
            else if (type >= MemoryType.D && type <= MemoryType.D32s)
            {
                return new BaseOffsetAccess
                {
                    Base = BaseRegister.Data,
                    Offset = num.Value
                };
            }
            // c[] -> [const + offset]
            else if (type >= MemoryType.C && type <= MemoryType.C32s)
            {
                return new BaseOffsetAccess
                {
                    Base = BaseRegister.Const,
                    Offset = num.Value
                };
            }
        }

        // デシュガーできない場合は旧形式のまま
        return new MemoryAccess
        {
            Type = type,
            Address = address
        };
    }

    // ========== 条件式 ==========

    private Condition ParseCondition()
    {
        var left = ParseExpression();
        var op = ParseComparisonOperator();
        var right = ParseExpression();

        return new Condition
        {
            Left = left,
            Operator = op,
            Right = right
        };
    }

    // ========== 演算子パース ==========

    private MemoryType ParseMemoryType()
    {
        var token = Advance();
        return token.Type switch
        {
            TokenType.Mem => MemoryType.Mem,
            TokenType.Mem8 => MemoryType.Mem8,
            TokenType.Mem16 => MemoryType.Mem16,
            TokenType.Mem32 => MemoryType.Mem32,
            TokenType.Mem64 => MemoryType.Mem64,
            TokenType.Mem8s => MemoryType.Mem8s,
            TokenType.Mem16s => MemoryType.Mem16s,
            TokenType.Mem32s => MemoryType.Mem32s,

            TokenType.S => MemoryType.S,
            TokenType.S8 => MemoryType.S8,
            TokenType.S16 => MemoryType.S16,
            TokenType.S32 => MemoryType.S32,
            TokenType.S64 => MemoryType.S64,
            TokenType.S8s => MemoryType.S8s,
            TokenType.S16s => MemoryType.S16s,
            TokenType.S32s => MemoryType.S32s,

            TokenType.D => MemoryType.D,
            TokenType.D8 => MemoryType.D8,
            TokenType.D16 => MemoryType.D16,
            TokenType.D32 => MemoryType.D32,
            TokenType.D64 => MemoryType.D64,
            TokenType.D8s => MemoryType.D8s,
            TokenType.D16s => MemoryType.D16s,
            TokenType.D32s => MemoryType.D32s,

            TokenType.C => MemoryType.C,
            TokenType.C8 => MemoryType.C8,
            TokenType.C16 => MemoryType.C16,
            TokenType.C32 => MemoryType.C32,
            TokenType.C64 => MemoryType.C64,
            TokenType.C8s => MemoryType.C8s,
            TokenType.C16s => MemoryType.C16s,
            TokenType.C32s => MemoryType.C32s,

            _ => throw Error($"Expected memory type, got {token.Type}")
        };
    }

    private BinaryOperator ParseBinaryOperator()
    {
        var token = Advance();
        return token.Type switch
        {
            TokenType.Plus => BinaryOperator.Add,
            TokenType.Minus => BinaryOperator.Sub,
            TokenType.Star => BinaryOperator.Mul,
            TokenType.DivS => BinaryOperator.DivS,
            TokenType.DivU => BinaryOperator.DivU,
            TokenType.ModS => BinaryOperator.ModS,
            TokenType.ModU => BinaryOperator.ModU,
            TokenType.Ampersand => BinaryOperator.And,
            TokenType.Pipe => BinaryOperator.Or,
            TokenType.Caret => BinaryOperator.Xor,
            TokenType.LeftShift => BinaryOperator.LeftShift,
            TokenType.RightShiftS => BinaryOperator.RightShiftS,
            TokenType.RightShiftU => BinaryOperator.RightShiftU,
            _ => throw Error($"Expected binary operator, got {token.Type}")
        };
    }

    private BinaryOperator ParseCompoundAssignOperator()
    {
        var token = Advance();
        return token.Type switch
        {
            TokenType.PlusAssign => BinaryOperator.Add,
            TokenType.MinusAssign => BinaryOperator.Sub,
            TokenType.StarAssign => BinaryOperator.Mul,
            TokenType.DivSAssign => BinaryOperator.DivS,
            TokenType.DivUAssign => BinaryOperator.DivU,
            TokenType.ModSAssign => BinaryOperator.ModS,
            TokenType.ModUAssign => BinaryOperator.ModU,
            TokenType.AmpersandAssign => BinaryOperator.And,
            TokenType.PipeAssign => BinaryOperator.Or,
            TokenType.CaretAssign => BinaryOperator.Xor,
            TokenType.LeftShiftAssign => BinaryOperator.LeftShift,
            TokenType.RightShiftSAssign => BinaryOperator.RightShiftS,
            TokenType.RightShiftUAssign => BinaryOperator.RightShiftU,
            _ => throw Error($"Expected compound assignment operator, got {token.Type}")
        };
    }

    private ComparisonOperator ParseComparisonOperator()
    {
        var token = Advance();
        return token.Type switch
        {
            TokenType.Equal => ComparisonOperator.Equal,
            TokenType.NotEqual => ComparisonOperator.NotEqual,
            TokenType.LessS => ComparisonOperator.LessS,
            TokenType.LessU => ComparisonOperator.LessU,
            TokenType.LessEqualS => ComparisonOperator.LessEqualS,
            TokenType.LessEqualU => ComparisonOperator.LessEqualU,
            TokenType.GreaterS => ComparisonOperator.GreaterS,
            TokenType.GreaterU => ComparisonOperator.GreaterU,
            TokenType.GreaterEqualS => ComparisonOperator.GreaterEqualS,
            TokenType.GreaterEqualU => ComparisonOperator.GreaterEqualU,
            _ => throw Error($"Expected comparison operator, got {token.Type}")
        };
    }

    // ========== ヘルパー ==========

    private long ParseNumberValue()
    {
        var token = Consume(TokenType.Number, "Expected number");
        return (long)token.Value!;
    }

    private bool IsMemoryAccess()
    {
        return Check(TokenType.Mem) || Check(TokenType.Mem8) || Check(TokenType.Mem16) ||
               Check(TokenType.Mem32) || Check(TokenType.Mem64) ||
               Check(TokenType.Mem8s) || Check(TokenType.Mem16s) || Check(TokenType.Mem32s) ||
               Check(TokenType.S) || Check(TokenType.S8) || Check(TokenType.S16) ||
               Check(TokenType.S32) || Check(TokenType.S64) ||
               Check(TokenType.S8s) || Check(TokenType.S16s) || Check(TokenType.S32s) ||
               Check(TokenType.D) || Check(TokenType.D8) || Check(TokenType.D16) ||
               Check(TokenType.D32) || Check(TokenType.D64) ||
               Check(TokenType.D8s) || Check(TokenType.D16s) || Check(TokenType.D32s) ||
               Check(TokenType.C) || Check(TokenType.C8) || Check(TokenType.C16) ||
               Check(TokenType.C32) || Check(TokenType.C64) ||
               Check(TokenType.C8s) || Check(TokenType.C16s) || Check(TokenType.C32s);
    }

    private bool IsBinaryOperator()
    {
        return Check(TokenType.Plus) || Check(TokenType.Minus) || Check(TokenType.Star) ||
               Check(TokenType.DivS) || Check(TokenType.DivU) ||
               Check(TokenType.ModS) || Check(TokenType.ModU) ||
               Check(TokenType.Ampersand) || Check(TokenType.Pipe) || Check(TokenType.Caret) ||
               Check(TokenType.LeftShift) || Check(TokenType.RightShiftS) || Check(TokenType.RightShiftU);
    }

    private bool IsCompoundAssignOperator()
    {
        return Check(TokenType.PlusAssign) || Check(TokenType.MinusAssign) || Check(TokenType.StarAssign) ||
               Check(TokenType.DivSAssign) || Check(TokenType.DivUAssign) ||
               Check(TokenType.ModSAssign) || Check(TokenType.ModUAssign) ||
               Check(TokenType.AmpersandAssign) || Check(TokenType.PipeAssign) || Check(TokenType.CaretAssign) ||
               Check(TokenType.LeftShiftAssign) || Check(TokenType.RightShiftSAssign) || Check(TokenType.RightShiftUAssign);
    }

    private void ConsumeNewlines()
    {
        while (Match(TokenType.Newline)) { }
    }

    private bool Match(TokenType type)
    {
        if (Check(type))
        {
            Advance();
            return true;
        }
        return false;
    }

    private bool Check(TokenType type)
    {
        if (IsAtEnd())
            return false;
        return Peek().Type == type;
    }

    private Token Advance()
    {
        if (!IsAtEnd())
            _current++;
        return Previous();
    }

    private Token Peek() => _tokens[_current];
    private Token Previous() => _tokens[_current - 1];
    private bool IsAtEnd() => Peek().Type == TokenType.Eof;

    private Token Consume(TokenType type, string message)
    {
        if (Check(type))
            return Advance();

        throw Error(message);
    }

    private Exception Error(string message)
    {
        var token = Peek();
        return new Exception($"Parse error at {token.Line}:{token.Column}: {message}");
    }
}

