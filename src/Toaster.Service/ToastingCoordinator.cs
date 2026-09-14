using System.Text.Json;
using Toaster.Core;

namespace Toaster.Service;

internal static class ToastingCoordinator
{
    public const string Instruction = "Extract reusable application-specific operational lessons from the supplied evidence. Do not merely summarize it and do not preserve project-specific state. Capture techniques, constraints, compatibility facts, failure patterns, successful remedies, warnings, or useful heuristics. Include applicability conditions and exceptions where known. It is valid to return zero candidates if there is no reusable lesson. Never invent evidence that is not present.";

    public static async Task<List<ToastingJob>> QueueSourceJobs(SourceDocument source, IReadOnlyList<SourceSection> sections, IKnowledgeStore store, CancellationToken ct)
    {
        var jobs = new List<ToastingJob>();
        foreach (var section in sections)
        {
            var context = JsonSerializer.Serialize(new
            {
                source = new { source.Id, source.Title, source.Origin, source.ContentType },
                section = new { section.Id, section.Heading, section.Ordinal, section.Content }
            });
            var job = new ToastingJob(Guid.NewGuid(), "source-section", source.Id, section.Id, null, Instruction, context, ToastingJobStatus.Pending, DateTimeOffset.UtcNow, null);
            jobs.Add(await store.EnqueueToastingJobAsync(job, ct));
        }
        return jobs;
    }

    public static async Task<ToastingJob> QueueObservationJob(Observation observation, IKnowledgeStore store, CancellationToken ct)
    {
        var context = JsonSerializer.Serialize(observation);
        var job = new ToastingJob(Guid.NewGuid(), "live-observation", null, null, observation.Id, Instruction, context, ToastingJobStatus.Pending, DateTimeOffset.UtcNow, null);
        return await store.EnqueueToastingJobAsync(job, ct);
    }

    public static async Task<object?> SubmitResult(Guid jobId, IReadOnlyList<ToastCandidateInput> candidates, IKnowledgeStore store, CancellationToken ct)
    {
        var job = await store.GetToastingJobAsync(jobId, ct);
        if (job is null) return null;
        if (job.Status == ToastingJobStatus.Completed)
            return new { jobId, alreadyCompleted = true, toastIds = Array.Empty<Guid>() };

        var created = new List<Guid>();
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.Title) || string.IsNullOrWhiteSpace(candidate.Statement)) continue;
            var now = DateTimeOffset.UtcNow;
            var toast = new Toast(
                Guid.NewGuid(), candidate.Title.Trim(), candidate.Statement.Trim(), candidate.Explanation,
                candidate.Domains ?? [], candidate.Tags ?? [], candidate.Conditions ?? [], candidate.Exceptions ?? [],
                Math.Clamp(candidate.Confidence ?? .6, 0, 1), ToastLifecycle.Provisional, now, now);
            await store.AddToastAsync(toast, ct);

            var kind = job.Kind == "source-section" ? "manual-distillation" : "implementation-observation";
            var note = job.ObservationId is { } observationId
                ? $"Distilled from observation {observationId}"
                : $"Distilled by external provider from toasting job {job.Id}";
            await store.AddProvenanceAsync(new Provenance(Guid.NewGuid(), toast.Id, job.SourceId, job.SectionId, kind, note, now), ct);
            created.Add(toast.Id);
        }

        await store.CompleteToastingJobAsync(jobId, ct);
        return new { jobId, candidatesReceived = candidates.Count, toastIds = created };
    }
}

public sealed record ToastCandidateInput(
    string Title,
    string Statement,
    string? Explanation,
    string[]? Domains,
    string[]? Tags,
    string[]? Conditions,
    string[]? Exceptions,
    double? Confidence);
