using System.Diagnostics;

namespace LearnChess.Web.Chess;

/// <summary>
/// Teste de corretude do gerador de lances: conta os nós até uma profundidade
/// e compara com valores conhecidos. Execute com: dotnet run -- perft
/// </summary>
public static class Perft
{
    public static long Count(Board board, int depth)
    {
        if (depth == 0) return 1;
        Span<Move> moves = stackalloc Move[256];
        int n = board.GenerateMoves(moves);
        long nodes = 0;
        for (int i = 0; i < n; i++)
        {
            board.MakeMove(moves[i]);
            if (!board.LastMoveLeftKingInCheck())
                nodes += Count(board, depth - 1);
            board.UnmakeMove();
        }
        return nodes;
    }

    private static readonly (string Name, string Fen, long[] Expected)[] Suite =
    {
        ("Inicial", Board.StartFen, new long[] { 20, 400, 8902, 197281, 4865609 }),
        ("Kiwipete", "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", new long[] { 48, 2039, 97862, 4085603 }),
        ("Posição 3", "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", new long[] { 14, 191, 2812, 43238, 674624 }),
        ("Posição 4", "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1", new long[] { 6, 264, 9467, 422333 }),
        ("Posição 5", "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8", new long[] { 44, 1486, 62379, 2103487 }),
    };

    /// <summary>Roda a bateria de testes; devolve true se tudo bater.</summary>
    public static bool RunSuite(TextWriter output)
    {
        bool ok = true;
        foreach (var (name, fen, expected) in Suite)
        {
            var board = new Board(fen);
            for (int d = 1; d <= expected.Length; d++)
            {
                var sw = Stopwatch.StartNew();
                long nodes = Count(board, d);
                bool pass = nodes == expected[d - 1];
                ok &= pass;
                output.WriteLine($"{(pass ? "OK  " : "FALHOU")} {name,-10} prof. {d}: {nodes,10} (esperado {expected[d - 1],10}) {sw.ElapsedMilliseconds} ms");
            }
            if (board.ToFen() != fen)
            {
                ok = false;
                output.WriteLine($"FALHOU {name}: FEN mudou após perft -> {board.ToFen()}");
            }
        }
        output.WriteLine(ok ? "Todos os testes de perft passaram." : "Há falhas no perft!");
        return ok;
    }
}
