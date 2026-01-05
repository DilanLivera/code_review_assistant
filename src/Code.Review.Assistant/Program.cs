using System.CommandLine;
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

Option<string> repoPathOption = new(name: "--repo-path",
                                    description: "Path to the local git repository to review")
{
    IsRequired = true
};

Option<string> filePatternOption = new(name: "--pattern",
                                       description: "File pattern to match (e.g., *.cs, *.js)",
                                       getDefaultValue: () => "*.cs");

Option<string> modelProviderOption = new(name: "--provider",
                                         description: "AI model provider: ollama or azure",
                                         getDefaultValue: () => "ollama");

Option<string> modelNameOption = new(name: "--model",
                                     description: "Model name to use",
                                     getDefaultValue: () => "llama3.2");

RootCommand rootCommand = new(description: "AI-powered code review assistant");
rootCommand.AddOption(repoPathOption);
rootCommand.AddOption(filePatternOption);
rootCommand.AddOption(modelProviderOption);
rootCommand.AddOption(modelNameOption);

rootCommand.SetHandler(async (repoPath, pattern, provider, model) =>
                       {
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

                           builder.Services.AddSingleton<IChatClient>(serviceProvider =>
                           {
                               // Uri endpoint = new(Environment.GetEnvironmentVariable("AZURE_INFERENCE_ENDPOINT") ?? throw new InvalidOperationException("AZURE_INFERENCE_ENDPOINT not set"));
                               // AzureKeyCredential azureKeyCredential = new(Environment.GetEnvironmentVariable("AZURE_INFERENCE_KEY") ?? throw new InvalidOperationException("AZURE_INFERENCE_KEY not set"));

                               HttpClient httpClient = new();
                               httpClient.BaseAddress = new Uri("http://localhost:11434");
                               httpClient.Timeout = TimeSpan.FromMinutes(30);

                               IChatClient client = provider.ToLower() switch
                               {
                                   "ollama" => new OllamaApiClient(new Uri("http://localhost:11434"), model),
                                   // "azure" => new ChatCompletionsClient(endpoint, azureKeyCredential).AsIChatClient(), // TODO: fix
                                   _ => throw new ArgumentException($"Unknown provider: {provider}")
                               };

                               return client;
                           });

                           builder.Services.AddSingleton<CodeReviewService>();
                           builder.Services.AddSingleton(serviceProvider => new CodeReviewConfig(repoPath, pattern));

                           IHost host = builder.Build();

                           CodeReviewService reviewService = host.Services.GetRequiredService<CodeReviewService>();
                           await reviewService.RunReviewAsync();

                       },
                       repoPathOption,
                       filePatternOption,
                       modelProviderOption,
                       modelNameOption);

return await rootCommand.InvokeAsync(args);

public sealed record CodeReviewConfig(string RepoPath, string FilePattern);