global using static Toaster.Service.ToastingPrompts;

namespace Toaster.Service;

internal static class ToastingPrompts
{
    internal const string ToastInstruction = "Extract reusable application-specific operational lessons from the supplied evidence. Do not merely summarize it and do not preserve project-specific state. Capture techniques, constraints, compatibility facts, failure patterns, successful remedies, warnings, or useful heuristics. Include applicability conditions and exceptions where known. It is valid to return zero candidates if there is no reusable lesson. Never invent evidence that is not present.";
}
