using System.CommandLine;
using System.CommandLine.Parsing;
using Azure;
using Azure.AI.Inference;
using Code.Review.Assistant;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

Option<string> repoPathOption = new(name: "--repo-path")
{
    Description = "Path to the local git repository to review",
    Required = true,
};

Option<string> filePatternOption = new(name: "--pattern")
{
    Description = "File pattern to match (e.g., *.cs, *.js)",
    DefaultValueFactory = (result) => "*.cs"
};

Option<string> modelProviderOption = new(name: "--provider")
{
    Description = "AI model provider: ollama or azure",
    DefaultValueFactory = (result) => "ollama"
};

Option<string> modelNameOption = new(name: "--model")
{
    Description = "Model name to use",
    DefaultValueFactory = (result) => "llama3.2"
};

RootCommand rootCommand = new(description: "AI-powered code review assistant");
rootCommand.Options.Add(repoPathOption);
rootCommand.Options.Add(filePatternOption);
rootCommand.Options.Add(modelProviderOption);
rootCommand.Options.Add(modelNameOption);

ParseResult parseResult = rootCommand.Parse(args);

if (parseResult.Errors.Count != 0)
{
    foreach (ParseError parseError in parseResult.Errors)
    {
        Console.Error.WriteLine(parseError.Message);
    }

    return 1;
}

string repoPath = parseResult.GetValue(repoPathOption) ?? throw new InvalidOperationException("Repo path option must contain a value");
string pattern = parseResult.GetValue(filePatternOption) ?? throw new InvalidOperationException("File pattern option must contain a value");
string provider = parseResult.GetValue(modelProviderOption) ?? throw new InvalidOperationException("Model provider option must contain a value");
string model = parseResult.GetValue(modelNameOption) ?? throw new InvalidOperationException("Model name option must contain a value");

HostApplicationBuilder builder = Host.CreateApplicationBuilder();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

ResourceBuilder resourceBuilder = ResourceBuilder.CreateDefault()
                                                 .AddService("CodeReviewAssistant");

builder.Logging.AddOpenTelemetry(options =>
{
    options.SetResourceBuilder(resourceBuilder);
    options.AddConsoleExporter();
    options.IncludeFormattedMessage = true;
    options.IncludeScopes = true;
});

builder.Services
       .AddOpenTelemetry()
       .WithMetrics(metrics =>
       {
           metrics.SetResourceBuilder(resourceBuilder)
                  .AddMeter("Microsoft.Extensions.AI")
                  .AddMeter("Microsoft.Agents.AI")
                  .AddConsoleExporter();
       })
       .WithTracing(tracing =>
       {
           tracing.SetResourceBuilder(resourceBuilder)
                  .AddSource("Microsoft.Extensions.AI")
                  .AddSource("Microsoft.Agents.AI")
                  .AddConsoleExporter();
       });

builder.Services.AddSingleton<IChatClient>(_ =>
{
    switch (provider.ToLowerInvariant())
    {
        case "ollama":
            {
                HttpClient httpClient = new();
                httpClient.BaseAddress = new Uri("http://localhost:11434");
                httpClient.Timeout = TimeSpan.FromMinutes(30);

                return new OllamaApiClient(httpClient, model);
            }
        case "azure":
            {
                Uri endpoint = new(Environment.GetEnvironmentVariable("AZURE_INFERENCE_ENDPOINT") ?? throw new InvalidOperationException("AZURE_INFERENCE_ENDPOINT not set"));
                AzureKeyCredential azureKeyCredential = new(Environment.GetEnvironmentVariable("AZURE_INFERENCE_KEY") ?? throw new InvalidOperationException("AZURE_INFERENCE_KEY not set"));

                return new ChatCompletionsClient(endpoint, azureKeyCredential).AsIChatClient(); // TODO: how do we pass the model?
            }
        default:
            throw new ArgumentException($"Unknown provider: {provider}");
    }
});

builder.Services.AddSingleton<CodeReviewService>();
builder.Services.AddSingleton(_ => new CodeReviewConfig(repoPath, pattern));

IHost host = builder.Build();

CodeReviewService reviewService = host.Services.GetRequiredService<CodeReviewService>();
await reviewService.RunReviewAsync();

return 0;


public sealed record CodeReviewConfig(string RepoPath, string FilePattern);