using LearnChess.Web.Chess;

namespace LearnChess.Web.Games;

public sealed record CreateGameRequest(DifficultyLevel Level, string? PlayerColor);
public sealed record PlayMoveRequest(string Uci);
public sealed record AnnotateRequest(string? Annotation, string? Comment);
public sealed record NotesRequest(string? Notes);

public sealed record LegalMoveDto(string From, string To, string Uci, string San, string? Promotion);

public sealed record EngineThinking(int Depth, long Nodes, long ElapsedMs, int EvalWhiteCp, int? MateIn, bool RandomChoice);

public sealed record GameState(
    string Id,
    DifficultyLevel Level,
    string LevelName,
    string PlayerColor,
    string Fen,
    string Turn,
    bool IsPlayerTurn,
    bool InCheck,
    string? CheckSquare,
    GameStatus Status,
    string StatusText,
    string Result,
    string? ResultReason,
    bool IsOver,
    string? LastMoveUci,
    IReadOnlyList<LegalMoveDto> LegalMoves,
    IReadOnlyList<MoveRecord> Moves,
    string Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    EngineThinking? Engine);

public sealed record GameSummary(
    string Id,
    string LevelName,
    string PlayerColor,
    GameStatus Status,
    string Result,
    int MoveCount,
    string Opening,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record LevelDto(DifficultyLevel Level, string Name, string Description);

public sealed class GameNotFoundException(string id) : Exception($"Partida '{id}' não encontrada.");

public sealed class GameRuleException(string message) : Exception(message);
