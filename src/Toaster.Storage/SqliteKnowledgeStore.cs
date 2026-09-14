using System.Text.Json;
using Microsoft.Data.Sqlite;
using Toaster.Core;

namespace Toaster.Storage;

public sealed class SqliteKnowledgeStore(string databasePath) : IKnowledgeStore
{
    private readonly string _databasePath = databasePath;
    private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = _databasePath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        await using var db = new SqliteConnection(ConnectionString);
        await db.OpenAsync(ct);
        var sql = """
PRAGMA journal_mode=WAL;
PRAGMA foreign_keys=ON;
CREATE TABLE IF NOT EXISTS toast (
 id TEXT PRIMARY KEY, title TEXT NOT NULL, statement TEXT NOT NULL, explanation TEXT,
 domains_json TEXT NOT NULL, tags_json TEXT NOT NULL, conditions_json TEXT NOT NULL, exceptions_json TEXT NOT NULL,
 confidence REAL NOT NULL, lifecycle TEXT NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL
);
CREATE VIRTUAL TABLE IF NOT EXISTS toast_fts USING fts5(id UNINDEXED, title, statement, explanation);
CREATE TABLE IF NOT EXISTS sources (
 id TEXT PRIMARY KEY, title TEXT NOT NULL, origin TEXT, content_type TEXT NOT NULL, sha256 TEXT NOT NULL UNIQUE, imported_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS source_sections (
 id TEXT PRIMARY KEY, source_id TEXT NOT NULL, heading TEXT NOT NULL, content TEXT NOT NULL, ordinal INTEGER NOT NULL,
 FOREIGN KEY(source_id) REFERENCES sources(id) ON DELETE CASCADE
);
CREATE VIRTUAL TABLE IF NOT EXISTS source_fts USING fts5(id UNINDEXED, source_id UNINDEXED, heading, content);
CREATE TABLE IF NOT EXISTS provenance (
 id TEXT PRIMARY KEY, toast_id TEXT NOT NULL, source_id TEXT, section_id TEXT, kind TEXT NOT NULL, note TEXT, created_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS observations (
 id TEXT PRIMARY KEY, activity TEXT NOT NULL, attempt TEXT NOT NULL, result TEXT NOT NULL, resolution TEXT, environment_json TEXT, created_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS graph_edges (
 id TEXT PRIMARY KEY, source_id TEXT NOT NULL, target_id TEXT NOT NULL, relation TEXT NOT NULL,
 weight REAL NOT NULL DEFAULT 1.0, confidence REAL NOT NULL DEFAULT 0.5, metadata_json TEXT, created_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS toasting_jobs (
 id TEXT PRIMARY KEY, kind TEXT NOT NULL, source_id TEXT, section_id TEXT, observation_id TEXT,
 instruction TEXT NOT NULL, context_json TEXT NOT NULL, status TEXT NOT NULL, created_at TEXT NOT NULL, completed_at TEXT
);
CREATE INDEX IF NOT EXISTS ix_toasting_jobs_status_created ON toasting_jobs(status, created_at);
""";
        await new SqliteCommand(sql, db).ExecuteNonQueryAsync(ct);
    }

    public async Task<SystemStatus> GetStatusAsync(string mcpEndpoint, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(ConnectionString);
        await db.OpenAsync(ct);
        async Task<long> Count(string table) => Convert.ToInt64(await new SqliteCommand($"SELECT COUNT(*) FROM {table}", db).ExecuteScalarAsync(ct));
        var pending = Convert.ToInt64(await new SqliteCommand("SELECT COUNT(*) FROM toasting_jobs WHERE status='Pending'", db).ExecuteScalarAsync(ct));
        return new SystemStatus("ok", "0.2.0", Path.GetDirectoryName(_databasePath)!, mcpEndpoint, await Count("toast"), await Count("sources"), await Count("observations"), pending);
    }

    public async Task<Toast> AddToastAsync(Toast toast, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(ConnectionString);
        await db.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        var cmd = db.CreateCommand();
        cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = "INSERT INTO toast VALUES ($id,$title,$statement,$explanation,$domains,$tags,$conditions,$exceptions,$confidence,$lifecycle,$created,$updated)";
        cmd.Parameters.AddWithValue("$id", toast.Id.ToString());
        cmd.Parameters.AddWithValue("$title", toast.Title);
        cmd.Parameters.AddWithValue("$statement", toast.Statement);
        cmd.Parameters.AddWithValue("$explanation", (object?)toast.Explanation ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$domains", JsonSerializer.Serialize(toast.Domains));
        cmd.Parameters.AddWithValue("$tags", JsonSerializer.Serialize(toast.Tags));
        cmd.Parameters.AddWithValue("$conditions", JsonSerializer.Serialize(toast.Conditions));
        cmd.Parameters.AddWithValue("$exceptions", JsonSerializer.Serialize(toast.Exceptions));
        cmd.Parameters.AddWithValue("$confidence", toast.Confidence);
        cmd.Parameters.AddWithValue("$lifecycle", toast.Lifecycle.ToString());
        cmd.Parameters.AddWithValue("$created", toast.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$updated", toast.UpdatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);

        var fts = db.CreateCommand();
        fts.Transaction = (SqliteTransaction)tx;
        fts.CommandText = "INSERT INTO toast_fts(id,title,statement,explanation) VALUES($id,$title,$statement,$explanation)";
        fts.Parameters.AddWithValue("$id", toast.Id.ToString());
        fts.Parameters.AddWithValue("$title", toast.Title);
        fts.Parameters.AddWithValue("$statement", toast.Statement);
        fts.Parameters.AddWithValue("$explanation", toast.Explanation ?? "");
        await fts.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return toast;
    }

    public async Task<Toast?> GetToastAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(ConnectionString);
        await db.OpenAsync(ct);
        var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT * FROM toast WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return ReadToast(r);
    }

    public async Task<IReadOnlyList<QueryHit>> SearchToastAsync(string query, int limit, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        await using var db = new SqliteConnection(ConnectionString);
        await db.OpenAsync(ct);
        var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT t.id,t.title,t.statement,t.confidence,t.lifecycle,bm25(toast_fts) score FROM toast_fts JOIN toast t ON t.id=toast_fts.id WHERE toast_fts MATCH $q ORDER BY score LIMIT $limit";
        cmd.Parameters.AddWithValue("$q", ToFtsQuery(query));
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 50));
        var hits = new List<QueryHit>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) hits.Add(new QueryHit(Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2), -r.GetDouble(5), r.GetDouble(3), r.GetString(4)));
        return hits;
    }

    public async Task<Provenance> AddProvenanceAsync(Provenance p, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(ConnectionString); await db.OpenAsync(ct);
        var c = db.CreateCommand();
        c.CommandText = "INSERT INTO provenance VALUES($id,$toast,$source,$section,$kind,$note,$created)";
        c.Parameters.AddWithValue("$id", p.Id.ToString()); c.Parameters.AddWithValue("$toast", p.ToastId.ToString());
        c.Parameters.AddWithValue("$source", (object?)p.SourceId?.ToString() ?? DBNull.Value); c.Parameters.AddWithValue("$section", (object?)p.SectionId?.ToString() ?? DBNull.Value);
        c.Parameters.AddWithValue("$kind", p.Kind); c.Parameters.AddWithValue("$note", (object?)p.Note ?? DBNull.Value); c.Parameters.AddWithValue("$created", p.CreatedAt.ToString("O"));
        await c.ExecuteNonQueryAsync(ct); return p;
    }

    public async Task<IReadOnlyList<Provenance>> GetProvenanceAsync(Guid toastId, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(ConnectionString); await db.OpenAsync(ct);
        var c = db.CreateCommand(); c.CommandText = "SELECT * FROM provenance WHERE toast_id=$toast ORDER BY created_at"; c.Parameters.AddWithValue("$toast", toastId.ToString());
        var result = new List<Provenance>(); await using var r = await c.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) result.Add(new Provenance(Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), r.IsDBNull(2)?null:Guid.Parse(r.GetString(2)), r.IsDBNull(3)?null:Guid.Parse(r.GetString(3)), r.GetString(4), r.IsDBNull(5)?null:r.GetString(5), DateTimeOffset.Parse(r.GetString(6))));
        return result;
    }

    public async Task<Observation> AddObservationAsync(Observation o, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(ConnectionString); await db.OpenAsync(ct);
        var c = db.CreateCommand(); c.CommandText = "INSERT INTO observations VALUES($id,$a,$attempt,$result,$resolution,$env,$created)";
        c.Parameters.AddWithValue("$id", o.Id.ToString()); c.Parameters.AddWithValue("$a", o.Activity); c.Parameters.AddWithValue("$attempt", o.Attempt); c.Parameters.AddWithValue("$result", o.Result);
        c.Parameters.AddWithValue("$resolution", (object?)o.Resolution ?? DBNull.Value); c.Parameters.AddWithValue("$env", (object?)o.EnvironmentJson ?? DBNull.Value); c.Parameters.AddWithValue("$created", o.CreatedAt.ToString("O"));
        await c.ExecuteNonQueryAsync(ct); return o;
    }

    public async Task<SourceDocument> AddSourceAsync(SourceDocument source, IReadOnlyList<SourceSection> sections, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(ConnectionString); await db.OpenAsync(ct); await using var tx = await db.BeginTransactionAsync(ct);
        var c = db.CreateCommand(); c.Transaction = (SqliteTransaction)tx; c.CommandText = "INSERT INTO sources VALUES($id,$title,$origin,$type,$sha,$at)";
        c.Parameters.AddWithValue("$id", source.Id.ToString()); c.Parameters.AddWithValue("$title", source.Title); c.Parameters.AddWithValue("$origin", (object?)source.Origin ?? DBNull.Value);
        c.Parameters.AddWithValue("$type", source.ContentType); c.Parameters.AddWithValue("$sha", source.Sha256); c.Parameters.AddWithValue("$at", source.ImportedAt.ToString("O")); await c.ExecuteNonQueryAsync(ct);
        foreach (var s in sections)
        {
            var sc = db.CreateCommand(); sc.Transaction = (SqliteTransaction)tx; sc.CommandText = "INSERT INTO source_sections VALUES($id,$source,$heading,$content,$ordinal)";
            sc.Parameters.AddWithValue("$id", s.Id.ToString()); sc.Parameters.AddWithValue("$source", s.SourceId.ToString()); sc.Parameters.AddWithValue("$heading", s.Heading); sc.Parameters.AddWithValue("$content", s.Content); sc.Parameters.AddWithValue("$ordinal", s.Ordinal); await sc.ExecuteNonQueryAsync(ct);
            var f = db.CreateCommand(); f.Transaction = (SqliteTransaction)tx; f.CommandText = "INSERT INTO source_fts(id,source_id,heading,content) VALUES($id,$source,$heading,$content)";
            f.Parameters.AddWithValue("$id", s.Id.ToString()); f.Parameters.AddWithValue("$source", s.SourceId.ToString()); f.Parameters.AddWithValue("$heading", s.Heading); f.Parameters.AddWithValue("$content", s.Content); await f.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct); return source;
    }

    public async Task<SourceDocument?> FindSourceByShaAsync(string sha256, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(ConnectionString); await db.OpenAsync(ct);
        var c = db.CreateCommand(); c.CommandText = "SELECT * FROM sources WHERE sha256=$sha"; c.Parameters.AddWithValue("$sha", sha256);
        await using var r = await c.ExecuteReaderAsync(ct); return await r.ReadAsync(ct) ? ReadSource(r) : null;
    }

    public async Task<SourceDocument?> GetSourceAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(ConnectionString); await db.OpenAsync(ct);
        var c = db.CreateCommand(); c.CommandText = "SELECT * FROM sources WHERE id=$id"; c.Parameters.AddWithValue("$id", id.ToString());
        await using var r = await c.ExecuteReaderAsync(ct); return await r.ReadAsync(ct) ? ReadSource(r) : null;
    }

    public async Task<IReadOnlyList<SourceSection>> GetSourceSectionsAsync(Guid sourceId, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(ConnectionString); await db.OpenAsync(ct);
        var c = db.CreateCommand(); c.CommandText = "SELECT * FROM source_sections WHERE source_id=$source ORDER BY ordinal"; c.Parameters.AddWithValue("$source", sourceId.ToString());
        var result = new List<SourceSection>(); await using var r = await c.ExecuteReaderAsync(ct); while (await r.ReadAsync(ct)) result.Add(ReadSection(r)); return result;
    }

    public async Task<SourceSection?> GetSourceSectionAsync(Guid sectionId, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(ConnectionString); await db.OpenAsync(ct);
        var c = db.CreateCommand(); c.CommandText = "SELECT * FROM source_sections WHERE id=$id"; c.Parameters.AddWithValue("$id", sectionId.ToString());
        await using var r = await c.ExecuteReaderAsync(ct); return await r.ReadAsync(ct) ? ReadSection(r) : null;
    }

    public async Task<IReadOnlyList<SourcePreview>> SearchSourcesAsync(string query, int limit, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        await using var db = new SqliteConnection(ConnectionString); await db.OpenAsync(ct);
        var c = db.CreateCommand();
        c.CommandText = "SELECT s.id,s.title,s.origin,source_fts.heading,snippet(source_fts,3,'','', '…',24) FROM source_fts JOIN sources s ON s.id=source_fts.source_id WHERE source_fts MATCH $q ORDER BY bm25(source_fts) LIMIT $limit";
        c.Parameters.AddWithValue("$q", ToFtsQuery(query)); c.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 50));
        var x = new List<SourcePreview>(); await using var r = await c.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) x.Add(new SourcePreview(Guid.Parse(r.GetString(0)), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.GetString(4))); return x;
    }

    public async Task<ToastingJob> EnqueueToastingJobAsync(ToastingJob job, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(ConnectionString); await db.OpenAsync(ct);
        var c = db.CreateCommand(); c.CommandText = "INSERT INTO toasting_jobs VALUES($id,$kind,$source,$section,$observation,$instruction,$context,$status,$created,$completed)";
        c.Parameters.AddWithValue("$id",job.Id.ToString()); c.Parameters.AddWithValue("$kind",job.Kind); c.Parameters.AddWithValue("$source",(object?)job.SourceId?.ToString()??DBNull.Value); c.Parameters.AddWithValue("$section",(object?)job.SectionId?.ToString()??DBNull.Value); c.Parameters.AddWithValue("$observation",(object?)job.ObservationId?.ToString()??DBNull.Value);
        c.Parameters.AddWithValue("$instruction",job.Instruction); c.Parameters.AddWithValue("$context",job.ContextJson); c.Parameters.AddWithValue("$status",job.Status.ToString()); c.Parameters.AddWithValue("$created",job.CreatedAt.ToString("O")); c.Parameters.AddWithValue("$completed",(object?)job.CompletedAt?.ToString("O")??DBNull.Value);
        await c.ExecuteNonQueryAsync(ct); return job;
    }

    public async Task<IReadOnlyList<ToastingJob>> GetPendingToastingJobsAsync(int limit, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(ConnectionString); await db.OpenAsync(ct);
        var c = db.CreateCommand(); c.CommandText = "SELECT * FROM toasting_jobs WHERE status='Pending' ORDER BY created_at LIMIT $limit"; c.Parameters.AddWithValue("$limit",Math.Clamp(limit,1,25));
        var result=new List<ToastingJob>(); await using var r=await c.ExecuteReaderAsync(ct); while(await r.ReadAsync(ct)) result.Add(ReadJob(r)); return result;
    }

    public async Task<ToastingJob?> GetToastingJobAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(ConnectionString); await db.OpenAsync(ct);
        var c=db.CreateCommand(); c.CommandText="SELECT * FROM toasting_jobs WHERE id=$id"; c.Parameters.AddWithValue("$id",id.ToString()); await using var r=await c.ExecuteReaderAsync(ct); return await r.ReadAsync(ct)?ReadJob(r):null;
    }

    public async Task CompleteToastingJobAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(ConnectionString); await db.OpenAsync(ct);
        var c=db.CreateCommand(); c.CommandText="UPDATE toasting_jobs SET status='Completed',completed_at=$at WHERE id=$id"; c.Parameters.AddWithValue("$id",id.ToString()); c.Parameters.AddWithValue("$at",DateTimeOffset.UtcNow.ToString("O")); await c.ExecuteNonQueryAsync(ct);
    }

    private static Toast ReadToast(SqliteDataReader r) => new(Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), JsonSerializer.Deserialize<string[]>(r.GetString(4)) ?? [], JsonSerializer.Deserialize<string[]>(r.GetString(5)) ?? [], JsonSerializer.Deserialize<string[]>(r.GetString(6)) ?? [], JsonSerializer.Deserialize<string[]>(r.GetString(7)) ?? [], r.GetDouble(8), Enum.Parse<ToastLifecycle>(r.GetString(9)), DateTimeOffset.Parse(r.GetString(10)), DateTimeOffset.Parse(r.GetString(11)));
    private static SourceDocument ReadSource(SqliteDataReader r) => new(Guid.Parse(r.GetString(0)),r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),r.GetString(3),r.GetString(4),DateTimeOffset.Parse(r.GetString(5)));
    private static SourceSection ReadSection(SqliteDataReader r) => new(Guid.Parse(r.GetString(0)),Guid.Parse(r.GetString(1)),r.GetString(2),r.GetString(3),r.GetInt32(4));
    private static ToastingJob ReadJob(SqliteDataReader r) => new(Guid.Parse(r.GetString(0)),r.GetString(1),r.IsDBNull(2)?null:Guid.Parse(r.GetString(2)),r.IsDBNull(3)?null:Guid.Parse(r.GetString(3)),r.IsDBNull(4)?null:Guid.Parse(r.GetString(4)),r.GetString(5),r.GetString(6),Enum.Parse<ToastingJobStatus>(r.GetString(7)),DateTimeOffset.Parse(r.GetString(8)),r.IsDBNull(9)?null:DateTimeOffset.Parse(r.GetString(9)));
    private static string ToFtsQuery(string query) => string.Join(" OR ", query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => $"\"{x.Replace("\"", "\"\"")}\""));
}
