namespace Toaster.Core;

public enum ToastLifecycle { Candidate, Provisional, Established, Challenged, Superseded, Deprecated }

public sealed record Toast(
    Guid Id,
    string Title,
    string Statement,
    string? Explanation,
    string[] Domains,
    string[] Tags,
    string[] Conditions,
    string[] Exceptions,
    double Confidence,
    ToastLifecycle Lifecycle,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record Provenance(
    Guid Id,
    Guid ToastId,
    Guid? SourceId,
    Guid? SectionId,
    string Kind,
    string? Note,
    DateTimeOffset CreatedAt);

public sealed record SourceDocument(
    Guid Id,
    string Title,
    string? Origin,
    string ContentType,
    string Sha256,
    DateTimeOffset ImportedAt);

public sealed record SourceSection(
    Guid Id,
    Guid SourceId,
    string Heading,
    string Content,
    int Ordinal);

public sealed record Observation(
    Guid Id,
    string Activity,
    string Attempt,
    string Result,
    string? Resolution,
    string? EnvironmentJson,
    DateTimeOffset CreatedAt);

public sealed record QueryRequest(string Query, string? Application = null, string[]? Environment = null, int Limit = 8);

public sealed record QueryHit(Guid Id, string Title, string Statement, double Score, double Confidence, string Lifecycle);

public sealed record QueryResponse(string Query, IReadOnlyList<QueryHit> Toast, IReadOnlyList<SourcePreview> PossibleSources, double Coverage);

public sealed record SourcePreview(Guid Id, string Title, string? Origin, string? BestSectionHeading, string? Preview);

public sealed record SystemStatus(string Service, string Version, string DataPath, string McpEndpoint, long ToastCount, long SourceCount, long ObservationCount);

public interface IKnowledgeStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<SystemStatus> GetStatusAsync(string mcpEndpoint, CancellationToken ct = default);
    Task<Toast> AddToastAsync(Toast toast, CancellationToken ct = default);
    Task<Toast?> GetToastAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<QueryHit>> SearchToastAsync(string query, int limit, CancellationToken ct = default);
    Task<Observation> AddObservationAsync(Observation observation, CancellationToken ct = default);
    Task<SourceDocument> AddSourceAsync(SourceDocument source, IReadOnlyList<SourceSection> sections, CancellationToken ct = default);
    Task<IReadOnlyList<SourcePreview>> SearchSourcesAsync(string query, int limit, CancellationToken ct = default);
}
