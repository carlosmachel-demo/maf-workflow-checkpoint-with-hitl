using System.Text.Json;
using WorkflowDemoWeb.Models;

namespace WorkflowDemoWeb.Services;

/// <summary>
/// Persists incomplete workflow runs to <c>runs.json</c> inside the checkpoint directory.
/// On restart, the app loads this file and resumes any run that has a saved checkpoint.
/// </summary>
public sealed class RunPersistenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public RunPersistenceService(string checkpointDirectory)
    {
        _filePath = Path.Combine(checkpointDirectory, "runs.json");
    }

    /// <summary>
    /// Saves all runs to disk. Only persists runs that are not yet completed/failed
    /// (or that completed — so the UI still shows history across restarts).
    /// </summary>
    public async Task SaveAsync(IEnumerable<WorkflowRunState> states)
    {
        var dtos = states.Select(s => new PersistedRunState
        {
            RunId = s.RunId,
            SessionId = s.SessionId,
            ContractId = s.ContractId,
            Status = s.Status,
            CreatedAt = s.CreatedAt,
            AiSummary = s.AiSummary,
            SuggestedDecision = s.SuggestedDecision,
            CheckpointId = s.CheckpointId,
            Outcome = s.Outcome,
            ErrorMessage = s.ErrorMessage
        }).ToList();

        await _lock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(dtos, JsonOptions);
            await File.WriteAllTextAsync(_filePath, json);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Returns all runs that were persisted. Empty list if file does not exist.</summary>
    public async Task<List<PersistedRunState>> LoadAsync()
    {
        if (!File.Exists(_filePath))
            return [];

        await _lock.WaitAsync();
        try
        {
            var json = await File.ReadAllTextAsync(_filePath);
            return JsonSerializer.Deserialize<List<PersistedRunState>>(json, JsonOptions) ?? [];
        }
        finally
        {
            _lock.Release();
        }
    }
}
