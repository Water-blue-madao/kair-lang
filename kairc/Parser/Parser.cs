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
            throw Error("データ初期化では 'data' か 'const' が必要です");

        Consume(TokenType.Plus, "PLUS が必要です");
        long offset = ParseNumberValue();
        Consume(TokenType.RightBracket, "']' が必要です");
        Consume(TokenType.Assign, "'=' が必要です");

        // 値は静的な数値リテラルのみ
        if (!Check(TokenType.Number))
            throw Error("データ初期化の値は静的な数値リテラルでなければなりません");

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
                target = Consume(TokenType.Identifier, "ラベル名または END が必要です").Lexeme;
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
                throw Error("アラインメントは 8 または 16 でなければなりません");
            return new Align { Alignment = alignment, SourceText = sourceLine };
        }

        // syscall
        if (Match(TokenType.Syscall))
        {
            var functionName = Consume(TokenType.Identifier, "関数名が必要です").Lexeme;
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

        // 代入系（[base + offset] 構文）
        if (Check(TokenType.LeftBracket))
        {
            Expression dest = ParseBaseOffsetAccess();

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
            Consume(TokenType.Assign, "'=' または複合代入演算子が必要です");

            // syscall with return value
            if (Match(TokenType.Syscall))
            {
                var functionName = Consume(TokenType.Identifier, "関数名が必要です").Lexeme;
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
                Consume(TokenType.RightParen, "')' が必要です");
                Consume(TokenType.Question, "'?' が必要です");
                var trueValue = ParseExpression();
                Consume(TokenType.Colon, "':' が必要です");
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
        throw Error($"予期しないトークン: {unexpected.Type} ('{unexpected.Lexeme}')");
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

        // 統一メモリアクセス: [base + offset]
        if (Check(TokenType.LeftBracket))
        {
            return ParseBaseOffsetAccess();
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
    /// 統一メモリアクセス構文: [base + offset]
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
            throw Error("ベースレジスタ (sp, data, const) が必要です");

        // + を読む
        Consume(TokenType.Plus, "ベースレジスタの後には '+' が必要です");

        // offset を読む (静的な数値のみ)
        if (!Check(TokenType.Number))
            throw Error("静的な数値オフセットが必要です (この構文では動的オフセットは非対応です)");

        long offset = ParseNumberValue();

        Consume(TokenType.RightBracket, "Expected ']'");

        return new BaseOffsetAccess
        {
            Base = baseReg,
            Offset = offset
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
            _ => throw Error($"二項演算子が必要ですが {token.Type} でした")
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
            _ => throw Error($"複合代入演算子が必要ですが {token.Type} でした")
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
            _ => throw Error($"比較演算子が必要ですが {token.Type} でした")
        };
    }

    // ========== ヘルパー ==========

    private long ParseNumberValue()
    {
        var token = Consume(TokenType.Number, "数値が必要です");
        return (long)token.Value!;
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
        return new Exception($"構文エラー {token.Line}:{token.Column}: {message}");
    }
}

