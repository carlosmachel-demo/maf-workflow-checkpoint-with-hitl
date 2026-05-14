using System.Collections.Concurrent;
using WorkflowDemoWeb.Models;

namespace WorkflowDemoWeb.Services;

public sealed class WorkflowStateService
{
    private readonly ConcurrentDictionary<string, WorkflowRunState> _runs = new();

    public void Add(WorkflowRunState state) => _runs[state.RunId] = state;

    public WorkflowRunState? Get(string runId) =>
        _runs.TryGetValue(runId, out var state) ? state : null;

    public IReadOnlyList<WorkflowRunState> GetAll() =>
        _runs.Values.OrderByDescending(r => r.CreatedAt).ToList();
}
