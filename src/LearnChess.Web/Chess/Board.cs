using System.Text;

namespace LearnChess.Web.Chess;

/// <summary>
/// Representação do tabuleiro (mailbox de 64 casas, a1 = 0, h8 = 63),
/// geração de lances, execução/desfazer e leitura/escrita de FEN.
/// </summary>
public sealed class Board
{
    public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    public const int CastleWhiteKing = 1;
    public const int CastleWhiteQueen = 2;
    public const int CastleBlackKing = 4;
    public const int CastleBlackQueen = 8;

    private struct Undo
    {
        public Move Move;
        public int Captured;
        public int Castling;
        public int EnPassant;
        public int HalfmoveClock;
        public ulong Hash;
    }

    public int[] Squares = new int[64];
    public int SideToMove { get; private set; }
    public int Castling { get; private set; }
    public int EnPassant { get; private set; } = -1;
    public int HalfmoveClock { get; private set; }
    public int FullmoveNumber { get; private set; } = 1;
    public ulong Hash { get; private set; }

    private int[] _kingSquare = new int[2];
    private Undo[] _history = new Undo[512];
    private int _historyCount;

    // ---------- tabelas pré-calculadas ----------
    private static readonly int[][] KnightTargets = new int[64][];
    private static readonly int[][] KingTargets = new int[64][];
    private static readonly int[][] PawnAttacks0 = new int[64][]; // casas atacadas por um peão branco
    private static readonly int[][] PawnAttacks1 = new int[64][]; // casas atacadas por um peão preto
    // Rays[sq][dir]: 0-3 ortogonais (torre), 4-7 diagonais (bispo)
    private static readonly int[][][] Rays = new int[64][][];
    private static readonly int[] CastleMask = new int[64];

    static Board()
    {
        int[] kn = { 1, 2, 2, 1, -1, -2, -2, -1 };
        int[] kr = { 2, 1, -1, -2, -2, -1, 1, 2 };
        int[] kf = { 1, 1, 0, -1, -1, -1, 0, 1 };
        int[] kgr = { 0, 1, 1, 1, 0, -1, -1, -1 };
        int[] df = { 0, 0, 1, -1, 1, -1, 1, -1 };
        int[] dr = { 1, -1, 0, 0, 1, 1, -1, -1 };

        for (int sq = 0; sq < 64; sq++)
        {
            int f = Square.File(sq), r = Square.Rank(sq);
            var knights = new List<int>();
            var kings = new List<int>();
            for (int i = 0; i < 8; i++)
            {
                int nf = f + kn[i], nr = r + kr[i];
                if (nf is >= 0 and < 8 && nr is >= 0 and < 8) knights.Add(Square.Make(nf, nr));
                nf = f + kf[i];
                nr = r + kgr[i];
                if (nf is >= 0 and < 8 && nr is >= 0 and < 8) kings.Add(Square.Make(nf, nr));
            }
            KnightTargets[sq] = knights.ToArray();
            KingTargets[sq] = kings.ToArray();

            var pw = new List<int>();
            var pb = new List<int>();
            if (r < 7)
            {
                if (f > 0) pw.Add(sq + 7);
                if (f < 7) pw.Add(sq + 9);
            }
            if (r > 0)
            {
                if (f > 0) pb.Add(sq - 9);
                if (f < 7) pb.Add(sq - 7);
            }
            PawnAttacks0[sq] = pw.ToArray();
            PawnAttacks1[sq] = pb.ToArray();

            Rays[sq] = new int[8][];
            for (int d = 0; d < 8; d++)
            {
                var ray = new List<int>();
                int nf = f + df[d], nr = r + dr[d];
                while (nf is >= 0 and < 8 && nr is >= 0 and < 8)
                {
                    ray.Add(Square.Make(nf, nr));
                    nf += df[d];
                    nr += dr[d];
                }
                Rays[sq][d] = ray.ToArray();
            }

            CastleMask[sq] = 15;
        }

        CastleMask[Square.Parse("a1")] &= ~CastleWhiteQueen;
        CastleMask[Square.Parse("h1")] &= ~CastleWhiteKing;
        CastleMask[Square.Parse("e1")] &= ~(CastleWhiteKing | CastleWhiteQueen);
        CastleMask[Square.Parse("a8")] &= ~CastleBlackQueen;
        CastleMask[Square.Parse("h8")] &= ~CastleBlackKing;
        CastleMask[Square.Parse("e8")] &= ~(CastleBlackKing | CastleBlackQueen);
    }

    public Board() : this(StartFen) { }

    public Board(string fen) => LoadFen(fen);

    public Board Clone()
    {
        var b = (Board)MemberwiseClone();
        b.Squares = (int[])Squares.Clone();
        b._kingSquare = (int[])_kingSquare.Clone();
        b._history = (Undo[])_history.Clone();
        return b;
    }

    public int KingSquare(int side) => _kingSquare[side];

    // ---------- FEN ----------

    public void LoadFen(string fen)
    {
        var parts = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
            throw new FormatException("FEN incompleta.");

        Array.Clear(Squares);
        _kingSquare[0] = _kingSquare[1] = -1;
        int rank = 7, file = 0;
        foreach (char c in parts[0])
        {
            if (c == '/')
            {
                rank--;
                file = 0;
            }
            else if (char.IsDigit(c))
            {
                file += c - '0';
            }
            else
            {
                if (rank < 0 || file > 7) throw new FormatException("FEN inválida.");
                int p = Piece.FromFenChar(c);
                int sq = Square.Make(file, rank);
                Squares[sq] = p;
                if (Piece.Type(p) == Piece.King) _kingSquare[Piece.Side(p)] = sq;
                file++;
            }
        }
        if (_kingSquare[0] < 0 || _kingSquare[1] < 0)
            throw new FormatException("A FEN precisa ter os dois reis.");

        SideToMove = parts[1] == "b" ? 1 : 0;
        Castling = 0;
        if (parts[2].Contains('K')) Castling |= CastleWhiteKing;
        if (parts[2].Contains('Q')) Castling |= CastleWhiteQueen;
        if (parts[2].Contains('k')) Castling |= CastleBlackKing;
        if (parts[2].Contains('q')) Castling |= CastleBlackQueen;
        EnPassant = parts[3] == "-" ? -1 : Square.Parse(parts[3]);
        HalfmoveClock = parts.Length > 4 ? int.Parse(parts[4]) : 0;
        FullmoveNumber = parts.Length > 5 ? int.Parse(parts[5]) : 1;
        _historyCount = 0;
        Hash = ComputeHash();
    }

    public string ToFen()
    {
        var sb = new StringBuilder();
        for (int rank = 7; rank >= 0; rank--)
        {
            int empty = 0;
            for (int file = 0; file < 8; file++)
            {
                int p = Squares[Square.Make(file, rank)];
                if (p == Piece.None)
                {
                    empty++;
                    continue;
                }
                if (empty > 0)
                {
                    sb.Append(empty);
                    empty = 0;
                }
                sb.Append(Piece.ToFenChar(p));
            }
            if (empty > 0) sb.Append(empty);
            if (rank > 0) sb.Append('/');
        }
        sb.Append(SideToMove == 0 ? " w " : " b ");
        string castle = "";
        if ((Castling & CastleWhiteKing) != 0) castle += "K";
        if ((Castling & CastleWhiteQueen) != 0) castle += "Q";
        if ((Castling & CastleBlackKing) != 0) castle += "k";
        if ((Castling & CastleBlackQueen) != 0) castle += "q";
        sb.Append(castle.Length == 0 ? "-" : castle);
        sb.Append(' ').Append(EnPassant < 0 ? "-" : Square.Name(EnPassant));
        sb.Append(' ').Append(HalfmoveClock).Append(' ').Append(FullmoveNumber);
        return sb.ToString();
    }

    private ulong ComputeHash()
    {
        ulong h = 0;
        for (int sq = 0; sq < 64; sq++)
            if (Squares[sq] != Piece.None)
                h ^= Zobrist.Pieces[Squares[sq], sq];
        h ^= Zobrist.Castling[Castling];
        if (EnPassant >= 0) h ^= Zobrist.EnPassantFile[Square.File(EnPassant)];
        if (SideToMove == 1) h ^= Zobrist.SideToMove;
        return h;
    }

    // ---------- ataques ----------

    public bool IsSquareAttacked(int sq, int bySide)
    {
        // Peões: as casas de onde um peão de "bySide" atacaria "sq" são
        // as casas que um peão do lado oposto em "sq" atacaria.
        int pawn = Piece.Make(Piece.Pawn, bySide);
        foreach (int s in bySide == 0 ? PawnAttacks1[sq] : PawnAttacks0[sq])
            if (Squares[s] == pawn) return true;

        int knight = Piece.Make(Piece.Knight, bySide);
        foreach (int s in KnightTargets[sq])
            if (Squares[s] == knight) return true;

        int king = Piece.Make(Piece.King, bySide);
        foreach (int s in KingTargets[sq])
            if (Squares[s] == king) return true;

        int rook = Piece.Make(Piece.Rook, bySide);
        int bishop = Piece.Make(Piece.Bishop, bySide);
        int queen = Piece.Make(Piece.Queen, bySide);
        var rays = Rays[sq];
        for (int d = 0; d < 8; d++)
        {
            int slider = d < 4 ? rook : bishop;
            foreach (int s in rays[d])
            {
                int p = Squares[s];
                if (p == Piece.None) continue;
                if (p == slider || p == queen) return true;
                break;
            }
        }
        return false;
    }

    public bool InCheck() => IsSquareAttacked(_kingSquare[SideToMove], SideToMove ^ 1);

    /// <summary>Depois de <see cref="MakeMove"/>: o lado que acabou de jogar deixou o rei em xeque?</summary>
    public bool LastMoveLeftKingInCheck() => IsSquareAttacked(_kingSquare[SideToMove ^ 1], SideToMove);

    // ---------- geração de lances (pseudo-legais) ----------

    public int GenerateMoves(Span<Move> moves, bool capturesOnly = false)
    {
        int n = 0;
        int us = SideToMove, them = us ^ 1;

        for (int from = 0; from < 64; from++)
        {
            int p = Squares[from];
            if (p == Piece.None || Piece.Side(p) != us) continue;

            switch (Piece.Type(p))
            {
                case Piece.Pawn:
                    n = GeneratePawnMoves(moves, n, from, us, capturesOnly);
                    break;
                case Piece.Knight:
                    n = GenerateStepMoves(moves, n, from, KnightTargets[from], them, capturesOnly);
                    break;
                case Piece.King:
                    n = GenerateStepMoves(moves, n, from, KingTargets[from], them, capturesOnly);
                    if (!capturesOnly) n = GenerateCastling(moves, n, us);
                    break;
                case Piece.Bishop:
                    n = GenerateSliderMoves(moves, n, from, 4, 8, them, capturesOnly);
                    break;
                case Piece.Rook:
                    n = GenerateSliderMoves(moves, n, from, 0, 4, them, capturesOnly);
                    break;
                case Piece.Queen:
                    n = GenerateSliderMoves(moves, n, from, 0, 8, them, capturesOnly);
                    break;
            }
        }
        return n;
    }

    private int GeneratePawnMoves(Span<Move> moves, int n, int from, int us, bool capturesOnly)
    {
        int dir = us == 0 ? 8 : -8;
        int rank = Square.Rank(from);
        int startRank = us == 0 ? 1 : 6;
        int lastRankBefore = us == 0 ? 6 : 1;
        bool promotes = rank == lastRankBefore;

        int to = from + dir;
        if (Squares[to] == Piece.None)
        {
            if (promotes)
            {
                n = AddPromotions(moves, n, from, to, capturesOnly);
            }
            else if (!capturesOnly)
            {
                moves[n++] = new Move(from, to);
                if (rank == startRank && Squares[to + dir] == Piece.None)
                    moves[n++] = new Move(from, to + dir, 0, Move.FlagDoublePush);
            }
        }

        foreach (int target in us == 0 ? PawnAttacks0[from] : PawnAttacks1[from])
        {
            int victim = Squares[target];
            if (victim != Piece.None && Piece.Side(victim) != us)
            {
                if (promotes) n = AddPromotions(moves, n, from, target, false);
                else moves[n++] = new Move(from, target);
            }
            else if (target == EnPassant)
            {
                moves[n++] = new Move(from, target, 0, Move.FlagEnPassant);
            }
        }
        return n;
    }

    private static int AddPromotions(Span<Move> moves, int n, int from, int to, bool queenOnly)
    {
        moves[n++] = new Move(from, to, Piece.Queen);
        if (queenOnly) return n;
        moves[n++] = new Move(from, to, Piece.Rook);
        moves[n++] = new Move(from, to, Piece.Bishop);
        moves[n++] = new Move(from, to, Piece.Knight);
        return n;
    }

    private int GenerateStepMoves(Span<Move> moves, int n, int from, int[] targets, int them, bool capturesOnly)
    {
        foreach (int to in targets)
        {
            int p = Squares[to];
            if (p == Piece.None)
            {
                if (!capturesOnly) moves[n++] = new Move(from, to);
            }
            else if (Piece.Side(p) == them)
            {
                moves[n++] = new Move(from, to);
            }
        }
        return n;
    }

    private int GenerateSliderMoves(Span<Move> moves, int n, int from, int dirStart, int dirEnd, int them, bool capturesOnly)
    {
        var rays = Rays[from];
        for (int d = dirStart; d < dirEnd; d++)
        {
            foreach (int to in rays[d])
            {
                int p = Squares[to];
                if (p == Piece.None)
                {
                    if (!capturesOnly) moves[n++] = new Move(from, to);
                    continue;
                }
                if (Piece.Side(p) == them) moves[n++] = new Move(from, to);
                break;
            }
        }
        return n;
    }

    private int GenerateCastling(Span<Move> moves, int n, int us)
    {
        int them = us ^ 1;
        int e = us == 0 ? 4 : 60;
        if (_kingSquare[us] != e) return n;

        int kingSide = us == 0 ? CastleWhiteKing : CastleBlackKing;
        int queenSide = us == 0 ? CastleWhiteQueen : CastleBlackQueen;

        if ((Castling & kingSide) != 0
            && Squares[e + 1] == Piece.None && Squares[e + 2] == Piece.None
            && Squares[e + 3] == Piece.Make(Piece.Rook, us)
            && !IsSquareAttacked(e, them) && !IsSquareAttacked(e + 1, them) && !IsSquareAttacked(e + 2, them))
        {
            moves[n++] = new Move(e, e + 2, 0, Move.FlagCastle);
        }

        if ((Castling & queenSide) != 0
            && Squares[e - 1] == Piece.None && Squares[e - 2] == Piece.None && Squares[e - 3] == Piece.None
            && Squares[e - 4] == Piece.Make(Piece.Rook, us)
            && !IsSquareAttacked(e, them) && !IsSquareAttacked(e - 1, them) && !IsSquareAttacked(e - 2, them))
        {
            moves[n++] = new Move(e, e - 2, 0, Move.FlagCastle);
        }
        return n;
    }

    public List<Move> GetLegalMoves()
    {
        Span<Move> buffer = stackalloc Move[256];
        int n = GenerateMoves(buffer);
        var legal = new List<Move>(n);
        for (int i = 0; i < n; i++)
        {
            MakeMove(buffer[i]);
            if (!LastMoveLeftKingInCheck()) legal.Add(buffer[i]);
            UnmakeMove();
        }
        return legal;
    }

    // ---------- executar / desfazer ----------

    public void MakeMove(Move m)
    {
        if (_historyCount == _history.Length)
            Array.Resize(ref _history, _history.Length * 2);

        int from = m.From, to = m.To, us = SideToMove;
        int piece = Squares[from];
        int captured = Squares[to];
        int capturedSquare = to;

        if (m.Flag == Move.FlagEnPassant)
        {
            capturedSquare = us == 0 ? to - 8 : to + 8;
            captured = Squares[capturedSquare];
        }

        _history[_historyCount++] = new Undo
        {
            Move = m,
            Captured = captured,
            Castling = Castling,
            EnPassant = EnPassant,
            HalfmoveClock = HalfmoveClock,
            Hash = Hash
        };

        ulong h = Hash;
        if (EnPassant >= 0) h ^= Zobrist.EnPassantFile[Square.File(EnPassant)];
        h ^= Zobrist.Castling[Castling];

        if (captured != Piece.None)
        {
            Squares[capturedSquare] = Piece.None;
            h ^= Zobrist.Pieces[captured, capturedSquare];
        }

        Squares[from] = Piece.None;
        h ^= Zobrist.Pieces[piece, from];
        int placed = m.Promotion != 0 ? Piece.Make(m.Promotion, us) : piece;
        Squares[to] = placed;
        h ^= Zobrist.Pieces[placed, to];

        if (Piece.Type(piece) == Piece.King)
        {
            _kingSquare[us] = to;
            if (m.Flag == Move.FlagCastle)
            {
                int rookFrom = to > from ? to + 1 : to - 2;
                int rookTo = to > from ? to - 1 : to + 1;
                int rook = Squares[rookFrom];
                Squares[rookFrom] = Piece.None;
                Squares[rookTo] = rook;
                h ^= Zobrist.Pieces[rook, rookFrom] ^ Zobrist.Pieces[rook, rookTo];
            }
        }

        Castling &= CastleMask[from] & CastleMask[to];
        h ^= Zobrist.Castling[Castling];

        EnPassant = m.Flag == Move.FlagDoublePush ? (from + to) / 2 : -1;
        if (EnPassant >= 0) h ^= Zobrist.EnPassantFile[Square.File(EnPassant)];

        HalfmoveClock = Piece.Type(piece) == Piece.Pawn || captured != Piece.None ? 0 : HalfmoveClock + 1;
        if (us == 1) FullmoveNumber++;

        SideToMove ^= 1;
        h ^= Zobrist.SideToMove;
        Hash = h;
    }

    public void UnmakeMove()
    {
        var u = _history[--_historyCount];
        var m = u.Move;
        SideToMove ^= 1;
        int us = SideToMove;
        if (us == 1) FullmoveNumber--;

        int from = m.From, to = m.To;
        int piece = Squares[to];
        if (m.Promotion != 0) piece = Piece.Make(Piece.Pawn, us);

        Squares[from] = piece;
        Squares[to] = Piece.None;

        if (m.Flag == Move.FlagEnPassant)
            Squares[us == 0 ? to - 8 : to + 8] = u.Captured;
        else
            Squares[to] = u.Captured;

        if (Piece.Type(piece) == Piece.King)
        {
            _kingSquare[us] = from;
            if (m.Flag == Move.FlagCastle)
            {
                int rookFrom = to > from ? to + 1 : to - 2;
                int rookTo = to > from ? to - 1 : to + 1;
                Squares[rookFrom] = Squares[rookTo];
                Squares[rookTo] = Piece.None;
            }
        }

        Castling = u.Castling;
        EnPassant = u.EnPassant;
        HalfmoveClock = u.HalfmoveClock;
        Hash = u.Hash;
    }

    /// <summary>"Passar a vez" — usado pela poda de lance nulo na busca.</summary>
    public void MakeNullMove()
    {
        if (_historyCount == _history.Length)
            Array.Resize(ref _history, _history.Length * 2);
        _history[_historyCount++] = new Undo
        {
            Move = default,
            Castling = Castling,
            EnPassant = EnPassant,
            HalfmoveClock = HalfmoveClock,
            Hash = Hash
        };
        ulong h = Hash;
        if (EnPassant >= 0) h ^= Zobrist.EnPassantFile[Square.File(EnPassant)];
        EnPassant = -1;
        HalfmoveClock++;
        SideToMove ^= 1;
        Hash = h ^ Zobrist.SideToMove;
    }

    public void UnmakeNullMove()
    {
        var u = _history[--_historyCount];
        SideToMove ^= 1;
        EnPassant = u.EnPassant;
        HalfmoveClock = u.HalfmoveClock;
        Hash = u.Hash;
    }

    // ---------- regras de empate ----------

    /// <summary>Quantas vezes a posição atual já ocorreu antes (dentro da regra dos 50 lances).</summary>
    public int RepetitionCount()
    {
        int count = 0;
        int limit = Math.Max(0, _historyCount - HalfmoveClock);
        for (int i = _historyCount - 2; i >= limit; i -= 2)
            if (_history[i].Hash == Hash) count++;
        return count;
    }

    public bool IsInsufficientMaterial()
    {
        int minors = 0;
        int bishopColorMask = 0; // bit 0: bispo em casa escura, bit 1: casa clara
        int bishops = 0;
        for (int sq = 0; sq < 64; sq++)
        {
            int p = Squares[sq];
            switch (Piece.Type(p))
            {
                case Piece.None:
                case Piece.King:
                    break;
                case Piece.Knight:
                    minors++;
                    break;
                case Piece.Bishop:
                    minors++;
                    bishops++;
                    bishopColorMask |= 1 << ((Square.File(sq) + Square.Rank(sq)) & 1);
                    break;
                default:
                    return false; // peão, torre ou dama
            }
        }
        if (minors <= 1) return true;
        // Só bispos, todos em casas da mesma cor.
        return bishops == minors && (bishopColorMask == 1 || bishopColorMask == 2);
    }

    public bool HasNonPawnMaterial(int side)
    {
        for (int sq = 0; sq < 64; sq++)
        {
            int p = Squares[sq];
            if (p != Piece.None && Piece.Side(p) == side)
            {
                int t = Piece.Type(p);
                if (t != Piece.Pawn && t != Piece.King) return true;
            }
        }
        return false;
    }
}
