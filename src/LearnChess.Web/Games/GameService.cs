using System.Collections.Concurrent;
using System.Text;
using LearnChess.Web.Chess;

namespace LearnChess.Web.Games;

/// <summary>
/// Regras de uma partida contra a máquina: aplica lances, chama o motor,
/// detecta fim de jogo e salva tudo em JSON a cada alteração.
/// </summary>
public sealed class GameService(JsonGameStore store)
{
    private sealed class Session(GameRecord record, Board board)
    {
        public GameRecord Record { get; } = record;
        public Board Board { get; set; } = board;
        public SemaphoreSlim Lock { get; } = new(1, 1);
        public EngineThinking? LastEngine { get; set; }
    }

    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private readonly Random _rng = Random.Shared;

    public static IReadOnlyList<LevelDto> Levels =>
        LevelSettings.All.Select(l => new LevelDto(l.Level, l.DisplayName, l.Description)).ToList();

    // ---------- operações ----------

    public GameState Create(CreateGameRequest request)
    {
        if (!Enum.IsDefined(request.Level))
            throw new GameRuleException("Nível inválido.");
        string color = (request.PlayerColor ?? "white").ToLowerInvariant();
        if (color == "random") color = _rng.Next(2) == 0 ? "white" : "black";
        if (color is not ("white" or "black"))
            throw new GameRuleException("Cor inválida: use white, black ou random.");

        var now = DateTimeOffset.Now;
        var record = new GameRecord
        {
            Id = $"{now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
            CreatedAt = now,
            UpdatedAt = now,
            Level = request.Level,
            LevelName = LevelSettings.For(request.Level).DisplayName,
            PlayerColor = color,
            StartFen = Board.StartFen,
        };
        var session = new Session(record, new Board(record.StartFen));
        _sessions[record.Id] = session;
        store.Save(record);
        return BuildState(session);
    }

    public async Task<GameState> GetAsync(string id)
    {
        var s = GetSession(id);
        await s.Lock.WaitAsync();
        try { return BuildState(s); }
        finally { s.Lock.Release(); }
    }

    public Task<GameState> PlayerMoveAsync(string id, string uci) => WithSession(id, s =>
    {
        EnsurePlaying(s);
        if (!IsPlayerTurn(s))
            throw new GameRuleException("Não é a sua vez: aguarde a jogada da máquina.");
        var move = Notation.ParseUci(s.Board, uci)
            ?? throw new GameRuleException($"Lance ilegal: {uci}");
        ApplyMove(s, move, "jogador", null);
        s.LastEngine = null;
    });

    public Task<GameState> EngineMoveAsync(string id) => WithSession(id, s =>
    {
        EnsurePlaying(s);
        if (IsPlayerTurn(s))
            throw new GameRuleException("É a vez do jogador.");

        var settings = LevelSettings.For(s.Record.Level);
        var result = new Engine().Search(s.Board, settings, _rng);
        int evalWhite = s.Board.SideToMove == 0 ? result.Score : -result.Score;
        int? mateWhite = result.MateIn is int m ? (s.Board.SideToMove == 0 ? m : -m) : null;
        s.LastEngine = new EngineThinking(result.Depth, result.Nodes, result.ElapsedMs, evalWhite, mateWhite, result.RandomChoice);
        ApplyMove(s, result.BestMove, "maquina", result.RandomChoice ? null : (evalWhite, result.Depth));
    });

    /// <summary>Volta até o último lance do jogador (desfazendo também a resposta da máquina).</summary>
    public Task<GameState> UndoAsync(string id) => WithSession(id, s =>
    {
        var moves = s.Record.Moves;
        if (!moves.Any(m => m.PlayedBy == "jogador"))
            throw new GameRuleException("Não há lances seus para desfazer.");

        while (moves.Count > 0 && moves[^1].PlayedBy == "maquina") moves.RemoveAt(moves.Count - 1);
        if (moves.Count > 0) moves.RemoveAt(moves.Count - 1);

        s.Board = Replay(s.Record);
        s.Record.Status = GameStatus.EmAndamento;
        s.Record.Result = "*";
        s.Record.ResultReason = null;
        s.LastEngine = null;
        UpdateStatus(s);
    });

    public Task<GameState> ResignAsync(string id) => WithSession(id, s =>
    {
        EnsurePlaying(s);
        s.Record.Status = GameStatus.Desistencia;
        s.Record.Result = s.Record.PlayerColor == "white" ? "0-1" : "1-0";
        s.Record.ResultReason = "Você desistiu.";
    });

    public Task<GameState> AnnotateAsync(string id, int ply, AnnotateRequest request) => WithSession(id, s =>
    {
        var move = s.Record.Moves.FirstOrDefault(m => m.Ply == ply)
            ?? throw new GameRuleException($"Lance {ply} não existe.");
        string annotation = (request.Annotation ?? move.Annotation).Trim();
        if (!Annotations.Allowed.ContainsKey(annotation))
            throw new GameRuleException("Anotação inválida. Use !!, !, !?, ?!, ? ou ??.");
        string comment = (request.Comment ?? move.Comment).Trim();
        if (comment.Length > 2000)
            throw new GameRuleException("Comentário muito longo (máx. 2000 caracteres).");
        move.Annotation = annotation;
        move.Comment = comment;
    });

    public Task<GameState> SetNotesAsync(string id, string? notes) => WithSession(id, s =>
    {
        notes ??= "";
        if (notes.Length > 10000)
            throw new GameRuleException("Anotações muito longas (máx. 10000 caracteres).");
        s.Record.Notes = notes;
    });

    public IReadOnlyList<GameSummary> List() =>
        store.LoadAll()
            .Select(r => new GameSummary(
                r.Id, r.LevelName, r.PlayerColor, r.Status, r.Result, r.Moves.Count,
                string.Join(" ", r.Moves.Take(6).Select(FormatMoveShort)),
                r.CreatedAt, r.UpdatedAt))
            .OrderByDescending(g => g.UpdatedAt)
            .ToList();

    public bool Delete(string id)
    {
        _sessions.TryRemove(id, out _);
        return store.Delete(id);
    }

    public string? ExportJson(string id) => store.ReadRaw(id);

    public async Task<string> ExportPgnAsync(string id)
    {
        var s = GetSession(id);
        await s.Lock.WaitAsync();
        try { return BuildPgn(s.Record); }
        finally { s.Lock.Release(); }
    }

    // ---------- internos ----------

    private async Task<GameState> WithSession(string id, Action<Session> action)
    {
        var s = GetSession(id);
        await s.Lock.WaitAsync();
        try
        {
            // A busca do motor é pesada: roda fora da thread da requisição.
            await Task.Run(() => action(s));
            s.Record.UpdatedAt = DateTimeOffset.Now;
            store.Save(s.Record);
            return BuildState(s);
        }
        finally
        {
            s.Lock.Release();
        }
    }

    private Session GetSession(string id)
    {
        if (_sessions.TryGetValue(id, out var s)) return s;
        var record = store.Load(id) ?? throw new GameNotFoundException(id);
        var session = new Session(record, Replay(record));
        return _sessions.GetOrAdd(id, session);
    }

    private static Board Replay(GameRecord record)
    {
        var board = new Board(record.StartFen);
        foreach (var m in record.Moves)
        {
            var move = Notation.ParseUci(board, m.Uci)
                ?? throw new InvalidDataException($"Lance inválido no arquivo da partida {record.Id}: {m.Uci}");
            board.MakeMove(move);
        }
        return board;
    }

    private static bool IsPlayerTurn(Session s) =>
        (s.Board.SideToMove == 0 ? "white" : "black") == s.Record.PlayerColor;

    private static void EnsurePlaying(Session s)
    {
        if (s.Record.Status != GameStatus.EmAndamento)
            throw new GameRuleException("A partida já terminou.");
    }

    private static void ApplyMove(Session s, Move move, string playedBy, (int Eval, int Depth)? engine)
    {
        var board = s.Board;
        string color = board.SideToMove == 0 ? "white" : "black";
        int moveNumber = board.FullmoveNumber;
        string san = Notation.ToSan(board, move);
        board.MakeMove(move);

        s.Record.Moves.Add(new MoveRecord
        {
            Ply = s.Record.Moves.Count + 1,
            MoveNumber = moveNumber,
            Color = color,
            PlayedBy = playedBy,
            San = san,
            Uci = move.ToUci(),
            FenAfter = board.ToFen(),
            PlayedAt = DateTimeOffset.Now,
            EngineEval = engine?.Eval,
            EngineDepth = engine?.Depth,
        });
        UpdateStatus(s);
    }

    private static void UpdateStatus(Session s)
    {
        var board = s.Board;
        var r = s.Record;
        if (board.GetLegalMoves().Count == 0)
        {
            if (board.InCheck())
            {
                r.Status = GameStatus.XequeMate;
                bool whiteWins = board.SideToMove == 1;
                r.Result = whiteWins ? "1-0" : "0-1";
                bool playerWins = (whiteWins ? "white" : "black") == r.PlayerColor;
                r.ResultReason = playerWins ? "Xeque-mate! Você venceu." : "Xeque-mate! A máquina venceu.";
            }
            else
            {
                r.Status = GameStatus.Afogamento;
                r.Result = "1/2-1/2";
                r.ResultReason = "Empate por afogamento (rei sem lances e fora de xeque).";
            }
        }
        else if (board.IsInsufficientMaterial())
        {
            SetDraw(r, "Empate por material insuficiente.");
        }
        else if (board.RepetitionCount() >= 2)
        {
            SetDraw(r, "Empate por tripla repetição.");
        }
        else if (board.HalfmoveClock >= 100)
        {
            SetDraw(r, "Empate pela regra dos 50 lances.");
        }
    }

    private static void SetDraw(GameRecord r, string reason)
    {
        r.Status = GameStatus.Empate;
        r.Result = "1/2-1/2";
        r.ResultReason = reason;
    }

    private static string StatusText(Session s)
    {
        var r = s.Record;
        if (r.Status != GameStatus.EmAndamento) return r.ResultReason ?? "Partida encerrada.";
        string who = IsPlayerTurn(s) ? "Sua vez" : "Máquina pensando...";
        return s.Board.InCheck() ? $"{who} — xeque!" : who;
    }

    private GameState BuildState(Session s)
    {
        var board = s.Board;
        var r = s.Record;
        bool over = r.Status != GameStatus.EmAndamento;
        bool playerTurn = !over && IsPlayerTurn(s);
        bool inCheck = board.InCheck();

        var legal = new List<LegalMoveDto>();
        if (playerTurn)
        {
            var moves = board.GetLegalMoves();
            foreach (var m in moves)
            {
                string? promo = m.Promotion == 0 ? null : char.ToLowerInvariant(Piece.ToFenChar(m.Promotion)).ToString();
                legal.Add(new LegalMoveDto(Square.Name(m.From), Square.Name(m.To), m.ToUci(), Notation.ToSan(board, m, moves), promo));
            }
        }

        return new GameState(
            r.Id, r.Level, r.LevelName, r.PlayerColor,
            board.ToFen(),
            board.SideToMove == 0 ? "white" : "black",
            playerTurn,
            inCheck,
            inCheck ? Square.Name(board.KingSquare(board.SideToMove)) : null,
            r.Status, StatusText(s), r.Result, r.ResultReason, over,
            r.Moves.Count > 0 ? r.Moves[^1].Uci : null,
            legal,
            r.Moves,
            r.Notes,
            r.CreatedAt, r.UpdatedAt,
            s.LastEngine);
    }

    private static string FormatMoveShort(MoveRecord m) =>
        m.Color == "white" ? $"{m.MoveNumber}.{m.San}" : m.San;

    private static string BuildPgn(GameRecord r)
    {
        string machine = $"LearnChess ({r.LevelName})";
        var sb = new StringBuilder();
        void Tag(string name, string value) => sb.Append('[').Append(name).Append(" \"")
            .Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"]\n");

        Tag("Event", "Partida contra a máquina");
        Tag("Site", "LearnChess");
        Tag("Date", r.CreatedAt.ToString("yyyy.MM.dd"));
        Tag("Round", "-");
        Tag("White", r.PlayerColor == "white" ? "Jogador" : machine);
        Tag("Black", r.PlayerColor == "black" ? "Jogador" : machine);
        Tag("Result", r.Result);
        if (r.StartFen != Board.StartFen)
        {
            Tag("SetUp", "1");
            Tag("FEN", r.StartFen);
        }
        sb.Append('\n');

        if (!string.IsNullOrWhiteSpace(r.Notes))
            sb.Append('{').Append(PgnComment(r.Notes)).Append("}\n");

        var body = new StringBuilder();
        bool needNumber = true;
        foreach (var m in r.Moves)
        {
            if (m.Color == "white") body.Append(m.MoveNumber).Append(". ");
            else if (needNumber) body.Append(m.MoveNumber).Append("... ");
            body.Append(m.San).Append(m.Annotation).Append(' ');
            needNumber = false;
            if (!string.IsNullOrWhiteSpace(m.Comment))
            {
                body.Append('{').Append(PgnComment(m.Comment)).Append("} ");
                needNumber = true; // após um comentário, o lance das pretas repete o número
            }
        }
        body.Append(r.Result);

        // Quebra as linhas em ~80 colunas, como recomenda o padrão PGN.
        int col = 0;
        foreach (var token in body.ToString().Split(' '))
        {
            if (col > 0 && col + token.Length + 1 > 80)
            {
                sb.Append('\n');
                col = 0;
            }
            else if (col > 0)
            {
                sb.Append(' ');
                col++;
            }
            sb.Append(token);
            col += token.Length;
        }
        sb.Append('\n');
        return sb.ToString();
    }

    private static string PgnComment(string text) =>
        text.Replace("}", ")").Replace("{", "(").Replace("\r", "").Replace("\n", " ").Trim();
}
