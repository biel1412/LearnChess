namespace LearnChess.Web.Chess;

/// <summary>Conversão de lances para notação algébrica padrão (SAN), ex.: e4, Nf3, exd5, O-O, e8=Q+, Qh7#.</summary>
public static class Notation
{
    public static string ToSan(Board board, Move move) => ToSan(board, move, board.GetLegalMoves());

    public static string ToSan(Board board, Move move, IReadOnlyList<Move> legalMoves)
    {
        string san;
        int piece = board.Squares[move.From];
        int type = Piece.Type(piece);

        if (move.Flag == Move.FlagCastle)
        {
            san = move.To > move.From ? "O-O" : "O-O-O";
        }
        else
        {
            bool isCapture = board.Squares[move.To] != Piece.None || move.Flag == Move.FlagEnPassant;
            string prefix;

            if (type == Piece.Pawn)
            {
                prefix = isCapture ? ((char)('a' + Square.File(move.From))).ToString() : "";
            }
            else
            {
                prefix = Piece.SanLetter(type) + Disambiguation(board, move, type, legalMoves);
            }

            san = prefix + (isCapture ? "x" : "") + Square.Name(move.To);
            if (move.Promotion != 0)
                san += "=" + Piece.SanLetter(move.Promotion);
        }

        board.MakeMove(move);
        if (board.InCheck())
            san += board.GetLegalMoves().Count == 0 ? "#" : "+";
        board.UnmakeMove();
        return san;
    }

    private static string Disambiguation(Board board, Move move, int type, IReadOnlyList<Move> legalMoves)
    {
        bool ambiguous = false, sameFile = false, sameRank = false;
        foreach (var other in legalMoves)
        {
            if (other.To != move.To || other.From == move.From) continue;
            if (Piece.Type(board.Squares[other.From]) != type) continue;
            ambiguous = true;
            if (Square.File(other.From) == Square.File(move.From)) sameFile = true;
            if (Square.Rank(other.From) == Square.Rank(move.From)) sameRank = true;
        }
        if (!ambiguous) return "";
        string file = ((char)('a' + Square.File(move.From))).ToString();
        string rank = ((char)('1' + Square.Rank(move.From))).ToString();
        if (!sameFile) return file;
        if (!sameRank) return rank;
        return file + rank;
    }

    /// <summary>Encontra o lance legal correspondente a uma string UCI (ex.: "e2e4", "e7e8q").</summary>
    public static Move? ParseUci(Board board, string uci, IReadOnlyList<Move>? legalMoves = null)
    {
        if (string.IsNullOrWhiteSpace(uci) || uci.Length is < 4 or > 5) return null;
        int from, to;
        try
        {
            from = Square.Parse(uci[..2]);
            to = Square.Parse(uci.Substring(2, 2));
        }
        catch (FormatException)
        {
            return null;
        }
        int promo = 0;
        if (uci.Length == 5)
        {
            promo = char.ToLowerInvariant(uci[4]) switch
            {
                'q' => Piece.Queen,
                'r' => Piece.Rook,
                'b' => Piece.Bishop,
                'n' => Piece.Knight,
                _ => -1
            };
            if (promo < 0) return null;
        }
        foreach (var m in legalMoves ?? board.GetLegalMoves())
            if (m.From == from && m.To == to && m.Promotion == promo)
                return m;
        return null;
    }
}
