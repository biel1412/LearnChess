using LearnChess.Web.Chess;

namespace LearnChess.Web.Games;

/// <summary>Partida completa como é salva no arquivo JSON.</summary>
public sealed class GameRecord
{
    public string Id { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public DifficultyLevel Level { get; set; }
    public string LevelName { get; set; } = "";

    /// <summary>"white" ou "black" — cor do jogador humano.</summary>
    public string PlayerColor { get; set; } = "white";
    public string StartFen { get; set; } = Board.StartFen;

    public GameStatus Status { get; set; } = GameStatus.EmAndamento;
    /// <summary>"1-0", "0-1", "1/2-1/2" ou "*".</summary>
    public string Result { get; set; } = "*";
    public string? ResultReason { get; set; }

    /// <summary>Anotações livres sobre a partida inteira.</summary>
    public string Notes { get; set; } = "";

    public List<MoveRecord> Moves { get; set; } = new();
}

public enum GameStatus
{
    EmAndamento,
    XequeMate,
    Afogamento,
    Empate,
    Desistencia
}

/// <summary>Um lance com sua notação algébrica, anotação (!, ?, ...) e comentário.</summary>
public sealed class MoveRecord
{
    /// <summary>Meio-lance, começando em 1.</summary>
    public int Ply { get; set; }
    public int MoveNumber { get; set; }
    /// <summary>"white" ou "black".</summary>
    public string Color { get; set; } = "";
    /// <summary>"jogador" ou "maquina".</summary>
    public string PlayedBy { get; set; } = "";
    /// <summary>Notação algébrica padrão, ex.: "Nf3", "exd5", "O-O", "e8=Q#".</summary>
    public string San { get; set; } = "";
    public string Uci { get; set; } = "";
    public string FenAfter { get; set; } = "";
    /// <summary>Símbolo de anotação: "", "!", "?", "!!", "??", "!?" ou "?!".</summary>
    public string Annotation { get; set; } = "";
    public string Comment { get; set; } = "";
    public DateTimeOffset PlayedAt { get; set; }
    /// <summary>Avaliação da máquina em centipeões (do ponto de vista das brancas), quando ela joga.</summary>
    public int? EngineEval { get; set; }
    public int? EngineDepth { get; set; }
}

public static class Annotations
{
    public static readonly IReadOnlyDictionary<string, string> Allowed = new Dictionary<string, string>
    {
        [""] = "Sem anotação",
        ["!!"] = "Lance brilhante",
        ["!"] = "Bom lance",
        ["!?"] = "Lance interessante",
        ["?!"] = "Lance duvidoso",
        ["?"] = "Erro",
        ["??"] = "Erro grave",
    };
}
