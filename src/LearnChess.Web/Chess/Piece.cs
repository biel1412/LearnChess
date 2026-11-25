namespace LearnChess.Web.Chess;

/// <summary>
/// Codificação das peças: os 3 bits baixos são o tipo e o bit 8 indica as pretas.
/// </summary>
public static class Piece
{
    public const int None = 0;
    public const int Pawn = 1;
    public const int Knight = 2;
    public const int Bishop = 3;
    public const int Rook = 4;
    public const int Queen = 5;
    public const int King = 6;

    public const int WhiteBit = 0;
    public const int BlackBit = 8;

    public static int Type(int piece) => piece & 7;

    /// <summary>0 = brancas, 1 = pretas.</summary>
    public static int Side(int piece) => (piece >> 3) & 1;

    public static int Make(int type, int side) => type | (side << 3);

    public static char ToFenChar(int piece)
    {
        char c = Type(piece) switch
        {
            Pawn => 'p',
            Knight => 'n',
            Bishop => 'b',
            Rook => 'r',
            Queen => 'q',
            King => 'k',
            _ => '?'
        };
        return Side(piece) == 0 ? char.ToUpperInvariant(c) : c;
    }

    public static int FromFenChar(char c)
    {
        int side = char.IsUpper(c) ? 0 : 1;
        int type = char.ToLowerInvariant(c) switch
        {
            'p' => Pawn,
            'n' => Knight,
            'b' => Bishop,
            'r' => Rook,
            'q' => Queen,
            'k' => King,
            _ => throw new FormatException($"Peça inválida na FEN: '{c}'")
        };
        return Make(type, side);
    }

    /// <summary>Letra usada na notação algébrica (inglês, padrão PGN).</summary>
    public static string SanLetter(int type) => type switch
    {
        Knight => "N",
        Bishop => "B",
        Rook => "R",
        Queen => "Q",
        King => "K",
        _ => ""
    };
}

public static class Square
{
    public static int File(int sq) => sq & 7;
    public static int Rank(int sq) => sq >> 3;
    public static int Make(int file, int rank) => rank * 8 + file;

    public static string Name(int sq) => $"{(char)('a' + File(sq))}{(char)('1' + Rank(sq))}";

    public static int Parse(string name)
    {
        if (name is null || name.Length != 2)
            throw new FormatException($"Casa inválida: '{name}'");
        int file = name[0] - 'a';
        int rank = name[1] - '1';
        if (file is < 0 or > 7 || rank is < 0 or > 7)
            throw new FormatException($"Casa inválida: '{name}'");
        return Make(file, rank);
    }
}
