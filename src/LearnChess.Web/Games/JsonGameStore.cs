using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LearnChess.Web.Games;

/// <summary>Salva cada partida em um arquivo JSON próprio: {DataDirectory}/{id}.json.</summary>
public sealed partial class JsonGameStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // mantém acentos legíveis no arquivo
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _directory;
    private readonly object _ioLock = new();

    public JsonGameStore(string directory)
    {
        _directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(_directory);
    }

    public string DataDirectory => _directory;

    [GeneratedRegex("^[a-zA-Z0-9-]{1,64}$")]
    private static partial Regex IdPattern();

    public static bool IsValidId(string id) => IdPattern().IsMatch(id);

    private string PathFor(string id)
    {
        if (!IsValidId(id)) throw new ArgumentException("Id de partida inválido.");
        return Path.Combine(_directory, id + ".json");
    }

    public void Save(GameRecord record)
    {
        string path = PathFor(record.Id);
        string json = JsonSerializer.Serialize(record, JsonOptions);
        lock (_ioLock)
        {
            // Grava em arquivo temporário e troca, para nunca deixar um JSON pela metade.
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
    }

    public GameRecord? Load(string id)
    {
        if (!IsValidId(id)) return null;
        string path = PathFor(id);
        lock (_ioLock)
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<GameRecord>(File.ReadAllText(path), JsonOptions);
        }
    }

    public string? ReadRaw(string id)
    {
        if (!IsValidId(id)) return null;
        string path = PathFor(id);
        lock (_ioLock)
            return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public bool Delete(string id)
    {
        if (!IsValidId(id)) return false;
        string path = PathFor(id);
        lock (_ioLock)
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
    }

    public IEnumerable<GameRecord> LoadAll()
    {
        string[] files;
        lock (_ioLock)
            files = Directory.GetFiles(_directory, "*.json");

        foreach (var file in files)
        {
            GameRecord? record = null;
            try
            {
                record = Load(Path.GetFileNameWithoutExtension(file));
            }
            catch (JsonException)
            {
                // arquivo corrompido ou editado à mão: ignora na listagem
            }
            if (record is not null) yield return record;
        }
    }
}
