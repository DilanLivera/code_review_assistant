# Code Review Assistant

AI-powered code review tool using Microsoft Agent Framework with comprehensive telemetry.

## Features

- **Multi-agent workflow**: 5 specialized agents (Security, Quality, Performance, Documentation, Summary)
- **Sequential orchestration**: Each agent builds on previous findings
- **OpenTelemetry integration**: Full logging, tracing, and metrics
- **Command-line interface**: Easy to use with System.CommandLine
- **Flexible AI providers**: Supports Ollama (local) and Azure AI Inference

## Prerequisites

- .NET SDK
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
--repo-path <repo-path> (REQUIRED)  Path to the local git repository to review
--pattern <pattern>                 File pattern to match (e.g., *.cs, *.js) [default: *.cs]
--provider <provider>               AI model provider: ollama or azure [default: ollama]
--model <model>                     Model name to use [default: llama3.2]
--version                           Show version information
-?, -h, --help                      Show help and usage information
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
