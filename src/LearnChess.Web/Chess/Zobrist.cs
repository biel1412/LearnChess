namespace LearnChess.Web.Chess;

/// <summary>Chaves Zobrist para hashing de posições (repetição e tabela de transposição).</summary>
public static class Zobrist
{
    public static readonly ulong[,] Pieces = new ulong[16, 64];
    public static readonly ulong[] Castling = new ulong[16];
    public static readonly ulong[] EnPassantFile = new ulong[8];
    public static readonly ulong SideToMove;

    static Zobrist()
    {
        ulong state = 0x9E3779B97F4A7C15UL;
        ulong Next()
        {
            // SplitMix64: determinístico e com boa distribuição.
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        for (int p = 0; p < 16; p++)
            for (int sq = 0; sq < 64; sq++)
                Pieces[p, sq] = Next();
        for (int i = 0; i < 16; i++)
            Castling[i] = Next();
        for (int i = 0; i < 8; i++)
            EnPassantFile[i] = Next();
        SideToMove = Next();
    }
}
