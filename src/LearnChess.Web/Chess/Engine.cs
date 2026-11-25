using System.Diagnostics;

namespace LearnChess.Web.Chess;

public sealed record SearchResult(Move BestMove, int Score, int Depth, long Nodes, long ElapsedMs, bool RandomChoice)
{
    public const int MateScore = Engine.Mate;

    /// <summary>Lances até o mate (positivo = quem jogou dá mate), ou null.</summary>
    public int? MateIn => Math.Abs(Score) >= Engine.Mate - 1000
        ? (Score > 0 ? (Engine.Mate - Score + 1) / 2 : -(Engine.Mate + Score + 1) / 2)
        : null;
}

/// <summary>
/// Motor de busca: negamax com poda alfa-beta, aprofundamento iterativo,
/// tabela de transposição, busca de quiescência, lance nulo, killers e histórico.
/// Cada instância é usada por uma única busca (não é thread-safe).
/// </summary>
public sealed class Engine
{
    public const int Inf = 1_000_000;
    public const int Mate = 100_000;
    private const int MaxPly = 128;
    private const int TtBits = 20;

    private const byte FlagExact = 0, FlagLower = 1, FlagUpper = 2;

    private struct TtEntry
    {
        public ulong Key;
        public int Move;
        public int Score;
        public sbyte Depth;
        public byte Flag;
    }

    private readonly TtEntry[] _tt = new TtEntry[1 << TtBits];
    private readonly Move[,] _killers = new Move[MaxPly, 2];
    private readonly int[,] _history = new int[64, 64];
    private readonly Stopwatch _clock = new();

    private Board _board = null!;
    private LevelSettings _settings = null!;
    private long _nodes;
    private bool _stop;
    private Move _rootBest;

    public SearchResult Search(Board position, LevelSettings settings, Random rng)
    {
        _board = position.Clone();
        _settings = settings;
        _nodes = 0;
        _stop = false;
        _clock.Restart();

        var legal = _board.GetLegalMoves();
        if (legal.Count == 0)
            throw new InvalidOperationException("Não há lances legais nesta posição.");

        if (legal.Count == 1)
            return new SearchResult(legal[0], Evaluation.Evaluate(_board), 0, 0, 0, false);

        // Níveis fracos: às vezes joga um lance qualquer, como um iniciante.
        if (settings.BlunderChance > 0 && rng.NextDouble() < settings.BlunderChance)
        {
            var move = legal[rng.Next(legal.Count)];
            return new SearchResult(move, 0, 0, 0, _clock.ElapsedMilliseconds, true);
        }

        if (settings.RandomMarginCp > 0)
            return SearchWithVariety(legal, rng);

        return IterativeDeepening(legal);
    }

    private SearchResult IterativeDeepening(List<Move> legal)
    {
        Move best = default;
        int bestScore = 0, completedDepth = 0;

        for (int depth = 1; depth <= _settings.MaxDepth; depth++)
        {
            _rootBest = default;
            int score = Negamax(depth, -Inf, Inf, 0, true);

            if (_stop)
            {
                // Iteração incompleta: aproveita o melhor lance se já houver um.
                if (!_rootBest.IsNull) best = _rootBest;
                break;
            }

            best = _rootBest;
            bestScore = score;
            completedDepth = depth;

            if (Math.Abs(score) >= Mate - 1000) break; // mate encontrado
            if (_clock.ElapsedMilliseconds > _settings.TimeLimitMs / 2) break; // não daria tempo de outra iteração
        }

        if (best.IsNull) best = legal[0];
        return new SearchResult(best, bestScore, completedDepth, _nodes, _clock.ElapsedMilliseconds, false);
    }

    /// <summary>
    /// Avalia todos os lances da raiz e sorteia entre os que ficam a até
    /// <see cref="LevelSettings.RandomMarginCp"/> do melhor, para variar o jogo.
    /// </summary>
    private SearchResult SearchWithVariety(List<Move> legal, Random rng)
    {
        int depth = _settings.MaxDepth;
        var scored = new List<(Move Move, int Score)>(legal.Count);
        foreach (var m in legal)
        {
            _board.MakeMove(m);
            int score = -Negamax(depth - 1, -Inf, Inf, 1, false);
            _board.UnmakeMove();
            scored.Add((m, score));
        }

        int best = scored.Max(s => s.Score);
        // Nunca deixa passar um mate a favor.
        int margin = best >= Mate - 1000 ? 0 : _settings.RandomMarginCp;
        var candidates = scored.Where(s => s.Score >= best - margin).ToList();
        var pick = candidates[rng.Next(candidates.Count)];
        return new SearchResult(pick.Move, pick.Score, depth, _nodes, _clock.ElapsedMilliseconds, candidates.Count > 1);
    }

    private void CheckTime()
    {
        if ((_nodes & 2047) == 0 && _clock.ElapsedMilliseconds > _settings.TimeLimitMs)
            _stop = true;
    }

    private int Negamax(int depth, int alpha, int beta, int ply, bool allowNull)
    {
        _nodes++;
        CheckTime();
        if (_stop) return 0;

        var b = _board;
        if (ply > 0 && (b.HalfmoveClock >= 100 || b.RepetitionCount() > 0 || b.IsInsufficientMaterial()))
            return 0;

        bool inCheck = b.InCheck();
        if (inCheck) depth++; // extensão de xeque

        if (depth <= 0)
            return _settings.UseQuiescence ? Quiesce(alpha, beta, ply) : Evaluation.Evaluate(b);
        if (ply >= MaxPly - 1)
            return Evaluation.Evaluate(b);

        // Tabela de transposição
        ref var entry = ref _tt[b.Hash & ((1UL << TtBits) - 1)];
        Move ttMove = default;
        if (entry.Key == b.Hash)
        {
            ttMove = Move.FromValue(entry.Move);
            if (ply > 0 && entry.Depth >= depth)
            {
                int ttScore = FromTt(entry.Score, ply);
                if (entry.Flag == FlagExact) return ttScore;
                if (entry.Flag == FlagLower && ttScore >= beta) return ttScore;
                if (entry.Flag == FlagUpper && ttScore <= alpha) return ttScore;
            }
        }

        // Poda de lance nulo
        if (_settings.UseNullMove && allowNull && !inCheck && depth >= 3 && ply > 0
            && b.HasNonPawnMaterial(b.SideToMove) && Evaluation.Evaluate(b) >= beta)
        {
            b.MakeNullMove();
            int nullScore = -Negamax(depth - 3, -beta, -beta + 1, ply + 1, false);
            b.UnmakeNullMove();
            if (_stop) return 0;
            if (nullScore >= beta) return beta;
        }

        Span<Move> moves = stackalloc Move[256];
        Span<int> scores = stackalloc int[256];
        int n = b.GenerateMoves(moves);
        ScoreMoves(moves, scores, n, ttMove, ply);

        int originalAlpha = alpha;
        int bestScore = -Inf;
        Move bestMove = default;
        int legal = 0;

        for (int i = 0; i < n; i++)
        {
            PickNext(moves, scores, i, n);
            var m = moves[i];
            bool isQuiet = b.Squares[m.To] == Piece.None && m.Flag != Move.FlagEnPassant && m.Promotion == 0;

            b.MakeMove(m);
            if (b.LastMoveLeftKingInCheck())
            {
                b.UnmakeMove();
                continue;
            }
            legal++;
            int score = -Negamax(depth - 1, -beta, -alpha, ply + 1, true);
            b.UnmakeMove();
            if (_stop) return 0;

            if (score > bestScore)
            {
                bestScore = score;
                bestMove = m;
                if (score > alpha)
                {
                    alpha = score;
                    if (ply == 0) _rootBest = m;
                    if (alpha >= beta)
                    {
                        if (isQuiet)
                        {
                            if (_killers[ply, 0] != m)
                            {
                                _killers[ply, 1] = _killers[ply, 0];
                                _killers[ply, 0] = m;
                            }
                            _history[m.From, m.To] += depth * depth;
                        }
                        break;
                    }
                }
            }
        }

        if (legal == 0)
            return inCheck ? -Mate + ply : 0; // mate ou afogamento

        if (ply == 0 && _rootBest.IsNull) _rootBest = bestMove;

        byte flag = bestScore <= originalAlpha ? FlagUpper : bestScore >= beta ? FlagLower : FlagExact;
        entry.Key = b.Hash;
        entry.Move = bestMove.Value;
        entry.Score = ToTt(bestScore, ply);
        entry.Depth = (sbyte)Math.Min(depth, 127);
        entry.Flag = flag;

        return bestScore;
    }

    private int Quiesce(int alpha, int beta, int ply)
    {
        _nodes++;
        CheckTime();
        if (_stop) return 0;

        var b = _board;
        int standPat = Evaluation.Evaluate(b);
        if (ply >= MaxPly - 1) return standPat;
        if (standPat >= beta) return standPat;
        if (standPat > alpha) alpha = standPat;

        Span<Move> moves = stackalloc Move[256];
        Span<int> scores = stackalloc int[256];
        int n = b.GenerateMoves(moves, capturesOnly: true);
        ScoreMoves(moves, scores, n, default, ply);

        for (int i = 0; i < n; i++)
        {
            PickNext(moves, scores, i, n);
            var m = moves[i];

            // Poda delta: mesmo ganhando a peça, não alcança alfa.
            int victim = m.Flag == Move.FlagEnPassant ? Piece.Pawn : Piece.Type(b.Squares[m.To]);
            if (m.Promotion == 0 && standPat + Evaluation.PieceValue[victim] + 200 < alpha)
                continue;

            b.MakeMove(m);
            if (b.LastMoveLeftKingInCheck())
            {
                b.UnmakeMove();
                continue;
            }
            int score = -Quiesce(-beta, -alpha, ply + 1);
            b.UnmakeMove();
            if (_stop) return 0;

            if (score >= beta) return score;
            if (score > alpha) alpha = score;
        }
        return alpha;
    }

    private void ScoreMoves(Span<Move> moves, Span<int> scores, int n, Move ttMove, int ply)
    {
        var b = _board;
        for (int i = 0; i < n; i++)
        {
            var m = moves[i];
            if (m == ttMove)
            {
                scores[i] = 10_000_000;
                continue;
            }
            int victim = b.Squares[m.To];
            if (victim != Piece.None || m.Flag == Move.FlagEnPassant)
            {
                int victimValue = victim == Piece.None ? 100 : Evaluation.PieceValue[Piece.Type(victim)];
                int attacker = Piece.Type(b.Squares[m.From]);
                scores[i] = 1_000_000 + victimValue * 10 - attacker; // MVV-LVA
            }
            else if (m.Promotion == Piece.Queen)
            {
                scores[i] = 900_000;
            }
            else if (ply < MaxPly && m == _killers[ply, 0])
            {
                scores[i] = 800_000;
            }
            else if (ply < MaxPly && m == _killers[ply, 1])
            {
                scores[i] = 790_000;
            }
            else
            {
                scores[i] = Math.Min(_history[m.From, m.To], 700_000);
            }
        }
    }

    private static void PickNext(Span<Move> moves, Span<int> scores, int start, int n)
    {
        int best = start;
        for (int j = start + 1; j < n; j++)
            if (scores[j] > scores[best]) best = j;
        if (best != start)
        {
            (moves[start], moves[best]) = (moves[best], moves[start]);
            (scores[start], scores[best]) = (scores[best], scores[start]);
        }
    }

    private static int ToTt(int score, int ply)
        => score >= Mate - 1000 ? score + ply : score <= -Mate + 1000 ? score - ply : score;

    private static int FromTt(int score, int ply)
        => score >= Mate - 1000 ? score - ply : score <= -Mate + 1000 ? score + ply : score;
}
