namespace LearnChess.Web.Chess;

/// <summary>
/// Avaliação estática: material + tabelas de posição (peça × casa).
/// Retorna centipeões do ponto de vista de quem joga.
/// </summary>
public static class Evaluation
{
    public static readonly int[] PieceValue = { 0, 100, 320, 330, 500, 900, 20000 };

    // Tabelas escritas do ponto de vista das brancas, com a 8ª fileira em cima.
    private static readonly int[] PawnTable =
    {
         0,  0,  0,  0,  0,  0,  0,  0,
        50, 50, 50, 50, 50, 50, 50, 50,
        10, 10, 20, 30, 30, 20, 10, 10,
         5,  5, 10, 25, 25, 10,  5,  5,
         0,  0,  0, 20, 20,  0,  0,  0,
         5, -5,-10,  0,  0,-10, -5,  5,
         5, 10, 10,-20,-20, 10, 10,  5,
         0,  0,  0,  0,  0,  0,  0,  0
    };

    private static readonly int[] KnightTable =
    {
        -50,-40,-30,-30,-30,-30,-40,-50,
        -40,-20,  0,  0,  0,  0,-20,-40,
        -30,  0, 10, 15, 15, 10,  0,-30,
        -30,  5, 15, 20, 20, 15,  5,-30,
        -30,  0, 15, 20, 20, 15,  0,-30,
        -30,  5, 10, 15, 15, 10,  5,-30,
        -40,-20,  0,  5,  5,  0,-20,-40,
        -50,-40,-30,-30,-30,-30,-40,-50
    };

    private static readonly int[] BishopTable =
    {
        -20,-10,-10,-10,-10,-10,-10,-20,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -10,  0,  5, 10, 10,  5,  0,-10,
        -10,  5,  5, 10, 10,  5,  5,-10,
        -10,  0, 10, 10, 10, 10,  0,-10,
        -10, 10, 10, 10, 10, 10, 10,-10,
        -10,  5,  0,  0,  0,  0,  5,-10,
        -20,-10,-10,-10,-10,-10,-10,-20
    };

    private static readonly int[] RookTable =
    {
          0,  0,  0,  0,  0,  0,  0,  0,
          5, 10, 10, 10, 10, 10, 10,  5,
         -5,  0,  0,  0,  0,  0,  0, -5,
         -5,  0,  0,  0,  0,  0,  0, -5,
         -5,  0,  0,  0,  0,  0,  0, -5,
         -5,  0,  0,  0,  0,  0,  0, -5,
         -5,  0,  0,  0,  0,  0,  0, -5,
          0,  0,  0,  5,  5,  0,  0,  0
    };

    private static readonly int[] QueenTable =
    {
        -20,-10,-10, -5, -5,-10,-10,-20,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -10,  0,  5,  5,  5,  5,  0,-10,
         -5,  0,  5,  5,  5,  5,  0, -5,
          0,  0,  5,  5,  5,  5,  0, -5,
        -10,  5,  5,  5,  5,  5,  0,-10,
        -10,  0,  5,  0,  0,  0,  0,-10,
        -20,-10,-10, -5, -5,-10,-10,-20
    };

    private static readonly int[] KingMiddleTable =
    {
        -30,-40,-40,-50,-50,-40,-40,-30,
        -30,-40,-40,-50,-50,-40,-40,-30,
        -30,-40,-40,-50,-50,-40,-40,-30,
        -30,-40,-40,-50,-50,-40,-40,-30,
        -20,-30,-30,-40,-40,-30,-30,-20,
        -10,-20,-20,-20,-20,-20,-20,-10,
         20, 20,  0,  0,  0,  0, 20, 20,
         20, 30, 10,  0,  0, 10, 30, 20
    };

    private static readonly int[] KingEndTable =
    {
        -50,-40,-30,-20,-20,-30,-40,-50,
        -30,-20,-10,  0,  0,-10,-20,-30,
        -30,-10, 20, 30, 30, 20,-10,-30,
        -30,-10, 30, 40, 40, 30,-10,-30,
        -30,-10, 30, 40, 40, 30,-10,-30,
        -30,-10, 20, 30, 30, 20,-10,-30,
        -30,-30,  0,  0,  0,  0,-30,-30,
        -50,-30,-30,-30,-30,-30,-30,-50
    };

    private static int TableIndex(int sq, int side)
        => side == 0 ? (7 - Square.Rank(sq)) * 8 + Square.File(sq) : sq;

    public static int Evaluate(Board board)
    {
        Span<int> score = stackalloc int[2];
        Span<int> bishops = stackalloc int[2];
        int nonPawnMaterial = 0;
        int whiteKing = -1, blackKing = -1;

        for (int sq = 0; sq < 64; sq++)
        {
            int p = board.Squares[sq];
            if (p == Piece.None) continue;
            int type = Piece.Type(p), side = Piece.Side(p);
            int idx = TableIndex(sq, side);
            int value = PieceValue[type];

            switch (type)
            {
                case Piece.Pawn: value += PawnTable[idx]; break;
                case Piece.Knight: value += KnightTable[idx]; nonPawnMaterial += 320; break;
                case Piece.Bishop: value += BishopTable[idx]; nonPawnMaterial += 330; bishops[side]++; break;
                case Piece.Rook: value += RookTable[idx]; nonPawnMaterial += 500; break;
                case Piece.Queen: value += QueenTable[idx]; nonPawnMaterial += 900; break;
                case Piece.King:
                    value = 0;
                    if (side == 0) whiteKing = sq; else blackKing = sq;
                    break;
            }
            score[side] += value;
        }

        bool endgame = nonPawnMaterial <= 1600;
        var kingTable = endgame ? KingEndTable : KingMiddleTable;
        score[0] += kingTable[TableIndex(whiteKing, 0)];
        score[1] += kingTable[TableIndex(blackKing, 1)];

        if (bishops[0] >= 2) score[0] += 30;
        if (bishops[1] >= 2) score[1] += 30;

        int eval = score[0] - score[1];

        // No final, ajuda a dar mate: aproxima o rei vencedor e empurra o perdedor para a borda.
        if (endgame && Math.Abs(eval) > 300)
        {
            int winner = eval > 0 ? 0 : 1;
            int loserKing = winner == 0 ? blackKing : whiteKing;
            int winnerKing = winner == 0 ? whiteKing : blackKing;
            int centerDist = Math.Max(3 - Square.File(loserKing), Square.File(loserKing) - 4)
                           + Math.Max(3 - Square.Rank(loserKing), Square.Rank(loserKing) - 4);
            int kingDist = Math.Abs(Square.File(loserKing) - Square.File(winnerKing))
                         + Math.Abs(Square.Rank(loserKing) - Square.Rank(winnerKing));
            int bonus = centerDist * 10 + (14 - kingDist) * 4;
            eval += winner == 0 ? bonus : -bonus;
        }

        return board.SideToMove == 0 ? eval : -eval;
    }
}
