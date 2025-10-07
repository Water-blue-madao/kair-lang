namespace Kairc.Lexer;

public class Token
{
    public TokenType Type { get; }
    public string Lexeme { get; }
    public int Line { get; }
    public int Column { get; }
    public object? Value { get; }

    public Token(TokenType type, string lexeme, int line, int column, object? value = null)
    {
        Type = type;
        Lexeme = lexeme;
        Line = line;
        Column = column;
        Value = value;
    }

    public override string ToString()
    {
        if (Value != null)
            return $"{Type}({Value}) at {Line}:{Column}";
        return $"{Type}('{Lexeme}') at {Line}:{Column}";
    }
}

