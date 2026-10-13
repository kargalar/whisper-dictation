using System.Globalization;
using Microsoft.Data.Sqlite;

namespace WhisperDictation.Core;

public sealed class HistoryStore : IHistoryStore
{
    private readonly string connectionString;

    public HistoryStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataDirectory, "history.db"),
            Pooling = false
        }.ToString();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS transcripts (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                created_at TEXT NOT NULL,
                model INTEGER NOT NULL,
                text TEXT NOT NULL
            );
            PRAGMA secure_delete = ON;
            """;
        command.ExecuteNonQuery();
    }

    public Task<HistoryEntry> AddAsync(ModelKind model, string text) => Task.Run(() =>
    {
        var createdAt = DateTimeOffset.UtcNow;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO transcripts (created_at, model, text) VALUES ($date, $model, $text); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$date", createdAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$model", (int)model);
        command.Parameters.AddWithValue("$text", text);
        var id = (long)command.ExecuteScalar()!;
        return new HistoryEntry(id, createdAt, model, text);
    });

    public Task<IReadOnlyList<HistoryEntry>> ReadAsync() => Task.Run<IReadOnlyList<HistoryEntry>>(() =>
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, created_at, model, text FROM transcripts ORDER BY id DESC;";
        using var reader = command.ExecuteReader();
        var entries = new List<HistoryEntry>();
        while (reader.Read())
            entries.Add(new(reader.GetInt64(0), DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                (ModelKind)reader.GetInt32(2), reader.GetString(3)));
        return entries;
    });

    public Task DeleteAsync(long id) => ExecuteAsync("DELETE FROM transcripts WHERE id = $id;", id);
    public Task ClearAsync() => ExecuteAsync("DELETE FROM transcripts; VACUUM;");

    private Task ExecuteAsync(string sql, long? id = null) => Task.Run(() =>
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (id.HasValue)
            command.Parameters.AddWithValue("$id", id.Value);
        command.ExecuteNonQuery();
    });

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA secure_delete = ON;";
        command.ExecuteNonQuery();
        return connection;
    }
}
