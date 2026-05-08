namespace WorkflowCheckpointWithHumanInTheLoop.Models;

/// <summary>Starts the workflow with the contract text.</summary>
public record ContractSubmission(string ContractText, string ContractId);