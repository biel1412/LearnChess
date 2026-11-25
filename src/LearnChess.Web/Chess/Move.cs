namespace LearnChess.Web.Chess;

/// <summary>
/// Lance compactado em um inteiro: origem (6 bits), destino (6 bits),
/// tipo da promoção (3 bits) e flag especial (2 bits).
/// </summary>
public readonly struct Move : IEquatable<Move>
{
    public const int FlagNone = 0;
    public const int FlagDoublePush = 1;
    public const int FlagEnPassant = 2;
    public const int FlagCastle = 3;

    public readonly int Value;

    public Move(int from, int to, int promotion = 0, int flag = FlagNone)
    {
        Value = from | (to << 6) | (promotion << 12) | (flag << 16);
    }

    private Move(int value) => Value = value;

    public static Move FromValue(int value) => new(value);

    public int From => Value & 63;
    public int To => (Value >> 6) & 63;
    public int Promotion => (Value >> 12) & 7;
    public int Flag => (Value >> 16) & 3;
    public bool IsNull => Value == 0;

    public string ToUci()
    {
        string s = Square.Name(From) + Square.Name(To);
        if (Promotion != 0)
            s += char.ToLowerInvariant(Piece.ToFenChar(Promotion));
        return s;
    }

    public bool Equals(Move other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is Move m && Equals(m);
    public override int GetHashCode() => Value;
    public static bool operator ==(Move a, Move b) => a.Value == b.Value;
    public static bool operator !=(Move a, Move b) => a.Value != b.Value;
    public override string ToString() => ToUci();
}
