namespace Kairc.Lexer;

public class Lexer
{
    private readonly string _source;
    private int _position;
    private int _line = 1;
    private int _column = 1;

    private static readonly Dictionary<string, TokenType> _keywords = new()
    {
        { "goto", TokenType.Goto },
        { "if", TokenType.If },
        { "pass", TokenType.Pass },
        { "sp", TokenType.Sp },
        { "data", TokenType.Data },
        { "const", TokenType.Const },
        { "END", TokenType.End },
        { "align", TokenType.Align },
        { "syscall", TokenType.Syscall },
    };

    public Lexer(string source)
    {
        _source = source;
    }

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();

        while (!IsAtEnd())
        {
            SkipWhitespaceExceptNewline();

            if (IsAtEnd())
                break;

            var token = NextToken();
            if (token != null)
                tokens.Add(token);
        }

        tokens.Add(new Token(TokenType.Eof, "", _line, _column));
        return tokens;
    }

    private Token? NextToken()
    {
        var startLine = _line;
        var startColumn = _column;
        var c = Advance();

        switch (c)
        {
            case '\n':
                return new Token(TokenType.Newline, "\\n", startLine, startColumn);

            case '#':
                return new Token(TokenType.Hash, "#", startLine, startColumn);

            case '[':
                return new Token(TokenType.LeftBracket, "[", startLine, startColumn);

            case ']':
                return new Token(TokenType.RightBracket, "]", startLine, startColumn);

            case '(':
                return new Token(TokenType.LeftParen, "(", startLine, startColumn);

            case ')':
                return new Token(TokenType.RightParen, ")", startLine, startColumn);

            case '?':
                return new Token(TokenType.Question, "?", startLine, startColumn);

            case ':':
                return new Token(TokenType.Colon, ":", startLine, startColumn);

            case ',':
                return new Token(TokenType.Comma, ",", startLine, startColumn);

            case '~':
                return new Token(TokenType.Tilde, "~", startLine, startColumn);

            case '+':
                if (Match('='))
                    return new Token(TokenType.PlusAssign, "+=", startLine, startColumn);
                return new Token(TokenType.Plus, "+", startLine, startColumn);

            case '-':
                if (Match('='))
                    return new Token(TokenType.MinusAssign, "-=", startLine, startColumn);
                return new Token(TokenType.Minus, "-", startLine, startColumn);

            case '*':
                if (Match('='))
                    return new Token(TokenType.StarAssign, "*=", startLine, startColumn);
                return new Token(TokenType.Star, "*", startLine, startColumn);

            case '&':
                if (Match('='))
                    return new Token(TokenType.AmpersandAssign, "&=", startLine, startColumn);
                return new Token(TokenType.Ampersand, "&", startLine, startColumn);

            case '|':
                if (Match('='))
                    return new Token(TokenType.PipeAssign, "|=", startLine, startColumn);
                return new Token(TokenType.Pipe, "|", startLine, startColumn);

            case '^':
                if (Match('='))
                    return new Token(TokenType.CaretAssign, "^=", startLine, startColumn);
                return new Token(TokenType.Caret, "^", startLine, startColumn);

            case '/':
                if (Match('/'))
                {
                    // 単行コメント
                    SkipLineComment();
                    return null;
                }
                if (Match('*'))
                {
                    // 複数行コメント
                    SkipBlockComment();
                    return null;
                }
                if (Match('s'))
                {
                    if (Match('='))
                        return new Token(TokenType.DivSAssign, "/s=", startLine, startColumn);
                    return new Token(TokenType.DivS, "/s", startLine, startColumn);
                }
                if (Match('u'))
                {
                    if (Match('='))
                        return new Token(TokenType.DivUAssign, "/u=", startLine, startColumn);
                    return new Token(TokenType.DivU, "/u", startLine, startColumn);
                }
                return new Token(TokenType.Slash, "/", startLine, startColumn);

            case '%':
                if (Match('s'))
                {
                    if (Match('='))
                        return new Token(TokenType.ModSAssign, "%s=", startLine, startColumn);
                    return new Token(TokenType.ModS, "%s", startLine, startColumn);
                }
                if (Match('u'))
                {
                    if (Match('='))
                        return new Token(TokenType.ModUAssign, "%u=", startLine, startColumn);
                    return new Token(TokenType.ModU, "%u", startLine, startColumn);
                }
                return new Token(TokenType.Percent, "%", startLine, startColumn);

            case '<':
                if (Match('<'))
                {
                    if (Match('='))
                        return new Token(TokenType.LeftShiftAssign, "<<=", startLine, startColumn);
                    return new Token(TokenType.LeftShift, "<<", startLine, startColumn);
                }
                if (Match('='))
                {
                    if (Match('s'))
                        return new Token(TokenType.LessEqualS, "<=s", startLine, startColumn);
                    if (Match('u'))
                        return new Token(TokenType.LessEqualU, "<=u", startLine, startColumn);
                    throw new Exception($"{startLine}:{startColumn} で無効なトークンです");
                }
                if (Match('s'))
                    return new Token(TokenType.LessS, "<s", startLine, startColumn);
                if (Match('u'))
                    return new Token(TokenType.LessU, "<u", startLine, startColumn);
                throw new Exception($"'<': {startLine}:{startColumn} で無効なトークンです");

            case '>':
                if (Match('>'))
                {
                    if (Match('s'))
                    {
                        if (Match('='))
                            return new Token(TokenType.RightShiftSAssign, ">>s=", startLine, startColumn);
                        return new Token(TokenType.RightShiftS, ">>s", startLine, startColumn);
                    }
                    if (Match('u'))
                    {
                        if (Match('='))
                            return new Token(TokenType.RightShiftUAssign, ">>u=", startLine, startColumn);
                        return new Token(TokenType.RightShiftU, ">>u", startLine, startColumn);
                    }
                    return new Token(TokenType.RightShift, ">>", startLine, startColumn);
                }
                if (Match('='))
                {
                    if (Match('s'))
                        return new Token(TokenType.GreaterEqualS, ">=s", startLine, startColumn);
                    if (Match('u'))
                        return new Token(TokenType.GreaterEqualU, ">=u", startLine, startColumn);
                    throw new Exception($"{startLine}:{startColumn} で無効なトークンです");
                }
                if (Match('s'))
                    return new Token(TokenType.GreaterS, ">s", startLine, startColumn);
                if (Match('u'))
                    return new Token(TokenType.GreaterU, ">u", startLine, startColumn);
                throw new Exception($"'>': {startLine}:{startColumn} で無効なトークンです");

            case '=':
                if (Match('='))
                    return new Token(TokenType.Equal, "==", startLine, startColumn);
                return new Token(TokenType.Assign, "=", startLine, startColumn);

            case '!':
                if (Match('='))
                    return new Token(TokenType.NotEqual, "!=", startLine, startColumn);
                throw new Exception($"'!': {startLine}:{startColumn} で無効なトークンです");

            case '@':
                if (MatchWord("long"))
                    return new Token(TokenType.AtLong, "@long", startLine, startColumn);
                throw new Exception($"{startLine}:{startColumn} で無効なアノテーションです");

            default:
                if (char.IsDigit(c))
                {
                    _position--; // 戻す
                    _column--;
                    return ScanNumber(startLine, startColumn);
                }
                if (IsIdentifierStart(c))
                {
                    _position--; // 戻す
                    _column--;
                    return ScanIdentifier(startLine, startColumn);
                }
                throw new Exception($"予期しない文字 '{c}' (位置: {startLine}:{startColumn})");
        }
    }

    private Token ScanNumber(int startLine, int startColumn)
    {
        var start = _position;

        // 16進数
        if (Peek() == '0' && (PeekNext() == 'x' || PeekNext() == 'X'))
        {
            Advance(); // '0'
            Advance(); // 'x'

            while (IsHexDigit(Peek()))
                Advance();

            var hexStr = _source.Substring(start, _position - start);
            var value = Convert.ToInt64(hexStr, 16);
            return new Token(TokenType.Number, hexStr, startLine, startColumn, value);
        }

        // 10進数
        while (char.IsDigit(Peek()))
            Advance();

        var numStr = _source.Substring(start, _position - start);
        var numValue = long.Parse(numStr);
        return new Token(TokenType.Number, numStr, startLine, startColumn, numValue);
    }

    private Token ScanIdentifier(int startLine, int startColumn)
    {
        var start = _position;

        while (IsIdentifierPart(Peek()))
            Advance();

        var text = _source.Substring(start, _position - start);

        if (_keywords.TryGetValue(text, out var type))
            return new Token(type, text, startLine, startColumn);

        return new Token(TokenType.Identifier, text, startLine, startColumn);
    }

    private void SkipLineComment()
    {
        while (Peek() != '\n' && !IsAtEnd())
            Advance();
    }

    private void SkipBlockComment()
    {
        while (!IsAtEnd())
        {
            if (Peek() == '*' && PeekNext() == '/')
            {
                Advance(); // '*'
                Advance(); // '/'
                break;
            }
            Advance();
        }
    }

    private void SkipWhitespaceExceptNewline()
    {
        while (!IsAtEnd())
        {
            var c = Peek();
            if (c == ' ' || c == '\t' || c == '\r')
                Advance();
            else
                break;
        }
    }

    private bool MatchWord(string word)
    {
        for (int i = 0; i < word.Length; i++)
        {
            if (_position + i >= _source.Length || _source[_position + i] != word[i])
                return false;
        }

        _position += word.Length;
        _column += word.Length;
        return true;
    }

    private bool Match(char expected)
    {
        if (IsAtEnd() || _source[_position] != expected)
            return false;

        _position++;
        _column++;
        return true;
    }

    private char Advance()
    {
        var c = _source[_position++];
        if (c == '\n')
        {
            _line++;
            _column = 1;
        }
        else
        {
            _column++;
        }
        return c;
    }

    private char Peek()
    {
        if (IsAtEnd())
            return '\0';
        return _source[_position];
    }

    private char PeekNext()
    {
        if (_position + 1 >= _source.Length)
            return '\0';
        return _source[_position + 1];
    }

    private bool IsAtEnd() => _position >= _source.Length;

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';
    private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';
    private static bool IsHexDigit(char c) => char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
}

