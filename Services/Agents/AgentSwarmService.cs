using System.Text;
using OzzieAI.Jarvis.Models;
using OzzieAI.Jarvis.Services.Llm;

namespace OzzieAI.Jarvis.Services.Agents;

public record SwarmStepResult(string AgentRole, string Directive, string Thought, string Artifact);
public record SwarmExecutionReport(string Goal, List<SwarmStepResult> Steps, string SynthesizedPlan, bool Completed);

public interface IAgentSwarmService
{
    Task<SwarmExecutionReport> ExecuteSwarmDirectiveAsync(string goal, Action<SwarmStepResult>? onStep = null);
}

public class AgentSwarmService : IAgentSwarmService
{
    private readonly LlmProviderFactory _llmFactory;
    private readonly ILogger<AgentSwarmService> _logger;

    public AgentSwarmService(LlmProviderFactory llmFactory, ILogger<AgentSwarmService> logger)
    {
        _llmFactory = llmFactory;
        _logger = logger;
    }

    public async Task<SwarmExecutionReport> ExecuteSwarmDirectiveAsync(string goal, Action<SwarmStepResult>? onStep = null)
    {
        _logger.LogInformation("Deploying House Party Multi-Agent Swarm for goal: {Goal}", goal);
        var (provider, _, _) = await _llmFactory.GetResilientProviderAsync();
        var steps = new List<SwarmStepResult>();

        // 1. Role: Architect
        var archPrompt = $"Break down this goal into clean technical components, architecture requirements, and interfaces: {goal}";
        var archResponse = await provider.GenerateResponseAsync(new List<ChatMessage> { new() { Role = "user", Content = archPrompt } }, "You are the Stark Industries Lead System Architect.");
        var step1 = new SwarmStepResult("Lead Architect", "System Architecture & Decomposition", "Structuring modular topology and security boundary.", archResponse);
        steps.Add(step1);
        onStep?.Invoke(step1);

        // 2. Role: Developer
        var devPrompt = $"Given the architect's specification:\n{archResponse}\n\nProvide the concrete implementation plan, key algorithms, and core code structures for: {goal}";
        var devResponse = await provider.GenerateResponseAsync(new List<ChatMessage> { new() { Role = "user", Content = devPrompt } }, "You are the Senior Systems Developer.");
        var step2 = new SwarmStepResult("Senior Developer", "Code Implementation & Logic", "Formulating algorithmic patterns and implementation structures.", devResponse);
        steps.Add(step2);
        onStep?.Invoke(step2);

        // 3. Role: Security Sentinel
        var secPrompt = $"Evaluate this implementation plan for potential security hazards, memory leaks, permissions issues, or edge cases:\n{devResponse}";
        var secResponse = await provider.GenerateResponseAsync(new List<ChatMessage> { new() { Role = "user", Content = secPrompt } }, "You are the Stark Terminal Sentinel Security Auditor.");
        var step3 = new SwarmStepResult("Security Sentinel", "Hazard & Boundary Audit", "Evaluating security posture and terminal hazard exposure.", secResponse);
        steps.Add(step3);
        onStep?.Invoke(step3);

        // 4. Role: QA & Verification Engineer
        var qaPrompt = $"Generate verification test cases, assertions, and validation checks for:\n{devResponse}\nAddressing security notes:\n{secResponse}";
        var qaResponse = await provider.GenerateResponseAsync(new List<ChatMessage> { new() { Role = "user", Content = qaPrompt } }, "You are the Autonomous Quality Assurance Engineer.");
        var step4 = new SwarmStepResult("QA & Verification", "Test Suite & Edge Case Coverage", "Formulating unit assertions and regression tests.", qaResponse);
        steps.Add(step4);
        onStep?.Invoke(step4);

        // Final Synthesis
        var sb = new StringBuilder();
        sb.AppendLine($"# Stark Multi-Agent Swarm Report: {goal}\n");
        sb.AppendLine("### 1. Architectural Blueprint (Architect)");
        sb.AppendLine(archResponse + "\n");
        sb.AppendLine("### 2. Implementation Specifications (Developer)");
        sb.AppendLine(devResponse + "\n");
        sb.AppendLine("### 3. Security & Sentinel Audit (Security Sentinel)");
        sb.AppendLine(secResponse + "\n");
        sb.AppendLine("### 4. Verification & Test Assertions (QA Engineer)");
        sb.AppendLine(qaResponse);

        return new SwarmExecutionReport(goal, steps, sb.ToString(), true);
    }
}
