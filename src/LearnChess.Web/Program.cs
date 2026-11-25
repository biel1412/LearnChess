using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LearnChess.Web.Chess;
using LearnChess.Web.Games;

// Modo de teste do gerador de lances: dotnet run -- perft
if (args.Length > 0 && args[0] == "perft")
    return Perft.RunSuite(Console.Out) ? 0 : 1;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

string dataDir = builder.Configuration["LearnChess:DataDirectory"] ?? "data/games";
if (!Path.IsPathRooted(dataDir))
    dataDir = Path.Combine(builder.Environment.ContentRootPath, dataDir);
builder.Services.AddSingleton(new JsonGameStore(dataDir));
builder.Services.AddSingleton<GameService>();

var app = builder.Build();

app.Logger.LogInformation("Partidas salvas em: {Dir}", app.Services.GetRequiredService<JsonGameStore>().DataDirectory);

// Converte erros de regra/partida inexistente em respostas HTTP amigáveis.
app.Use(async (ctx, next) =>
{
    try
    {
        await next();
    }
    catch (GameNotFoundException ex)
    {
        await Results.Problem(ex.Message, statusCode: 404).ExecuteAsync(ctx);
    }
    catch (Exception ex) when (ex is GameRuleException or ArgumentException or BadHttpRequestException)
    {
        await Results.Problem(ex.Message, statusCode: 400).ExecuteAsync(ctx);
    }
});

app.UseDefaultFiles();
app.UseStaticFiles();

var api = app.MapGroup("/api");

api.MapGet("/levels", () => GameService.Levels);
api.MapGet("/annotations", () => Annotations.Allowed.Select(a => new { symbol = a.Key, description = a.Value }));

api.MapGet("/games", (GameService games) => games.List());
api.MapPost("/games", (CreateGameRequest req, GameService games) => games.Create(req));
api.MapGet("/games/{id}", (string id, GameService games) => games.GetAsync(id));
api.MapDelete("/games/{id}", (string id, GameService games) =>
    games.Delete(id) ? Results.NoContent() : Results.NotFound());

api.MapPost("/games/{id}/moves", (string id, PlayMoveRequest req, GameService games) => games.PlayerMoveAsync(id, req.Uci));
api.MapPost("/games/{id}/engine-move", (string id, GameService games) => games.EngineMoveAsync(id));
api.MapPost("/games/{id}/undo", (string id, GameService games) => games.UndoAsync(id));
api.MapPost("/games/{id}/resign", (string id, GameService games) => games.ResignAsync(id));

api.MapPut("/games/{id}/moves/{ply:int}/annotation", (string id, int ply, AnnotateRequest req, GameService games) =>
    games.AnnotateAsync(id, ply, req));
api.MapPut("/games/{id}/notes", (string id, NotesRequest req, GameService games) => games.SetNotesAsync(id, req.Notes));

api.MapGet("/games/{id}/export/json", (string id, GameService games) =>
{
    string? json = games.ExportJson(id);
    return json is null
        ? Results.NotFound()
        : Results.File(Encoding.UTF8.GetBytes(json), "application/json", $"learnchess-{id}.json");
});

api.MapGet("/games/{id}/export/pgn", async (string id, GameService games) =>
    Results.File(Encoding.UTF8.GetBytes(await games.ExportPgnAsync(id)), "application/x-chess-pgn", $"learnchess-{id}.pgn"));

app.Run();
return 0;
