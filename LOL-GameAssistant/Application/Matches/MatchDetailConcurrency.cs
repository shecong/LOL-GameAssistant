namespace LOL_GameAssistant.Application.Matches;

/// <summary>Shared budget for recent rows, overlay samples and performance detail requests.</summary>
internal static class MatchDetailConcurrency
{
    internal static readonly SemaphoreSlim Gate = new(20, 20);
}
