namespace LearnChess.Web.Chess;

public enum DifficultyLevel
{
    Basico,
    Medio,
    Avancado,
    Profissional
}

/// <summary>Parâmetros que definem a força da máquina em cada nível.</summary>
public sealed record LevelSettings(
    DifficultyLevel Level,
    string DisplayName,
    string Description,
    int MaxDepth,
    int TimeLimitMs,
    int RandomMarginCp,
    double BlunderChance,
    bool UseQuiescence,
    bool UseNullMove)
{
    public static readonly IReadOnlyList<LevelSettings> All = new[]
    {
        new LevelSettings(DifficultyLevel.Basico, "Básico",
            "Para quem está começando: enxerga só 1 lance à frente e às vezes erra de propósito.",
            MaxDepth: 1, TimeLimitMs: 500, RandomMarginCp: 150, BlunderChance: 0.25,
            UseQuiescence: false, UseNullMove: false),
        new LevelSettings(DifficultyLevel.Medio, "Médio",
            "Joga com atenção a trocas simples, calcula 2 lances e varia as jogadas.",
            MaxDepth: 2, TimeLimitMs: 1000, RandomMarginCp: 40, BlunderChance: 0.05,
            UseQuiescence: true, UseNullMove: false),
        new LevelSettings(DifficultyLevel.Avancado, "Avançado",
            "Calcula 5 lances com busca de capturas; pune erros táticos.",
            MaxDepth: 5, TimeLimitMs: 2500, RandomMarginCp: 0, BlunderChance: 0,
            UseQuiescence: true, UseNullMove: true),
        new LevelSettings(DifficultyLevel.Profissional, "Profissional",
            "Força máxima: aprofundamento iterativo, tabela de transposição e ~4 s por lance.",
            MaxDepth: 64, TimeLimitMs: 4000, RandomMarginCp: 0, BlunderChance: 0,
            UseQuiescence: true, UseNullMove: true),
    };

    public static LevelSettings For(DifficultyLevel level) => All.First(l => l.Level == level);
}
