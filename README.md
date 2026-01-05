# Code Review Assistant

AI-powered code review tool using Microsoft Agent Framework with comprehensive telemetry.

## Features

- **Multi-agent workflow**: 5 specialized agents (Security, Quality, Performance, Documentation, Summary)
- **Sequential orchestration**: Each agent builds on previous findings
- **OpenTelemetry integration**: Full logging, tracing, and metrics
- **Command-line interface**: Easy to use with System.CommandLine
- **Flexible AI providers**: Supports Ollama (local) and Azure AI Inference

## Prerequisites

- .NET 9 SDK
- Ollama (for local models) OR Azure AI Inference credentials

### Install Ollama

```bash
# macOS/Linux
curl -fsSL https://ollama.com/install.sh | sh

# Windows
# Download from https://ollama.com/download

# Pull a model
ollama pull llama3.2
```

### Azure AI Inference Setup

Set environment variables:

```bash
export AZURE_INFERENCE_ENDPOINT="https://your-endpoint.inference.ai.azure.com"
export AZURE_INFERENCE_KEY="your-api-key"
```

## Installation

```bash
dotnet restore
dotnet build
```

## Usage

### Using Ollama (default)

```bash
dotnet run -- --repo-path /path/to/your/repo --pattern "*.cs"
```

### Using Azure AI Inference

```bash
dotnet run -- --repo-path /path/to/your/repo --pattern "*.cs" --provider azure --model gpt-4o-mini
```

### Full Options

```bash
dotnet run -- \
  --repo-path /path/to/repo \     # Required: path to git repository
  --pattern "*.cs" \              # Optional: file pattern (default: *.cs)
  --provider ollama \             # Optional: ollama or azure (default: ollama)
  --model llama3.2                # Optional: model name (default: llama3.2)
```

## Example Output

```
==============================================================
Review for: UserService.cs
==============================================================

SECURITY ANALYSIS:
No critical security vulnerabilities detected. Password hashing
uses bcrypt appropriately. Consider adding rate limiting for
authentication endpoints.

QUALITY ANALYSIS:
Code follows SOLID principles well. The UserService class has
high cohesion. Consider extracting validation logic into
separate validator classes for better testability.

PERFORMANCE ANALYSIS:
Database queries could benefit from async enumeration. The
GetAllUsers method loads entire dataset into memory. Consider
implementing pagination for large result sets.

DOCUMENTATION ANALYSIS:
Public API methods lack XML documentation. Internal methods
are well-commented. Add parameter descriptions and return
value documentation for public interface.

SEVERITY: MEDIUM
TOP ISSUES:
1. Missing pagination in GetAllUsers causes memory issues
2. No XML documentation on public API
3. Validation logic tightly coupled to service

RECOMMENDATIONS:
- Implement IAsyncEnumerable for large queries
- Add comprehensive XML documentation
- Create separate validator classes
```

## Telemetry Output

The application outputs OpenTelemetry data to console including:

- **Traces**: Agent execution flow, LLM calls, workflow steps
- **Metrics**: Token usage, response times, agent invocations
- **Logs**: Detailed execution logs with correlation IDs

Example trace output:

```
Activity.TraceId:            7b2c8f1e9d4a3c5b6e7f8a9b0c1d2e3f
Activity.SpanId:             1234567890abcdef
Activity.TraceFlags:         Recorded
Activity.ActivitySourceName: Microsoft.Extensions.AI
Activity.DisplayName:        chat llama3.2
Activity.Kind:               Client
Activity.StartTime:          2025-01-05T10:30:45.1234567Z
Activity.Duration:           00:00:02.3456789
```

## Architecture

### Agent Workflow

```
SecurityAnalyzer → QualityReviewer → PerformanceOptimizer →
DocumentationChecker → SummaryGenerator
```

Each agent:
1. Receives code and previous agent outputs
2. Analyzes from their specialty perspective
3. Passes findings to next agent
4. Final agent synthesizes everything

### Telemetry Pipeline

```
Agent Execution → OpenTelemetry → Console Exporter
     ↓
  Traces/Metrics/Logs
```

## Extending the Tool

### Add New Agent

```csharp
private AIAgent CreateNewAgent()
{
    return new ChatClientAgent(
        _chatClient,
        new ChatClientAgentOptions
        {
            Name = "AgentName",
            Instructions = "Your specific instructions here"
        });
}
```

### Change Workflow

```csharp
var workflow = AgentWorkflowBuilder.BuildSequential(
    agent1,
    agent2,
    newAgent,
    agent3);
```

### Add Custom Tools

```csharp
[Description("Calculate code complexity")]
int CalculateComplexity(string code) => /* implementation */;

var agent = new ChatClientAgent(
    _chatClient,
    new ChatClientAgentOptions
    {
        Name = "Analyzer",
        Instructions = "...",
        ChatOptions = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(CalculateComplexity)]
        }
    });
```

## Troubleshooting

### Ollama Connection Failed

```bash
# Check Ollama is running
ollama list

# Restart Ollama service
# macOS/Linux: automatically runs as daemon
# Windows: check system tray
```

### Azure Inference Authentication Error

Verify environment variables are set:

```bash
echo $AZURE_INFERENCE_ENDPOINT
echo $AZURE_INFERENCE_KEY
```

### No Files Found

- Check repository path is correct
- Verify file pattern matches your files
- Ensure files aren't in bin/obj directories (automatically excluded)

## Performance Tips

1. **Limit file count**: Tool processes first 3 files by default (see code)
2. **Use smaller models**: llama3.2 or gpt-4o-mini for faster reviews
3. **Adjust pattern**: Be specific with file patterns to reduce scope
4. **Local models**: Ollama is faster for development/testing

## Next Steps

- Add support for more file types (JavaScript, Python, etc.)
- Implement concurrent agent execution for independent analyses
- Add file filtering by git diff (only review changed files)
- Export results to JSON/Markdown reports
- Add custom evaluation metrics
- Integrate with CI/CD pipelines
