using System.CommandLine;
using Azure;
using Azure.AI.Inference;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
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

public sealed class CodeReviewService
{
    private readonly IChatClient _chatClient;
    private readonly CodeReviewConfig _config;
    private readonly ILogger<CodeReviewService> _logger;

    public CodeReviewService(
        IChatClient chatClient,
        CodeReviewConfig config,
        ILogger<CodeReviewService> logger)
    {
        _chatClient = chatClient;
        _config = config;
        _logger = logger;
    }

    public async Task RunReviewAsync()
    {
        _logger.LogInformation("Starting code review for repository: {RepoPath}", _config.RepoPath);

        if (!Directory.Exists(_config.RepoPath))
        {
            _logger.LogError("Repository path does not exist: {RepoPath}", _config.RepoPath);

            return;
        }

        List<string> files = Directory.GetFiles(_config.RepoPath, _config.FilePattern, SearchOption.AllDirectories)
                                      .Where(f => !f.Contains("bin") && !f.Contains("obj"))
                                      .ToList();

        _logger.LogInformation("Found {FileCount} files matching pattern {Pattern}", files.Count, _config.FilePattern);

        if (files.Count == 0)
        {
            _logger.LogWarning("No files found matching pattern");

            return;
        }

        AIAgent securityAgent = CreateSecurityAgent();
        AIAgent qualityAgent = CreateQualityAgent();
        AIAgent performanceAgent = CreatePerformanceAgent();
        AIAgent documentationAgent = CreateDocumentationAgent();
        AIAgent summaryAgent = CreateSummaryAgent();

        Workflow workflow = AgentWorkflowBuilder.BuildSequential(securityAgent,
                                                                 qualityAgent,
                                                                 performanceAgent,
                                                                 documentationAgent,
                                                                 summaryAgent);

        AIAgent workflowAgent = await workflow.AsAgentAsync();

        foreach (string file in files.Take(3))
        {
            _logger.LogInformation("Reviewing file: {FileName}", Path.GetFileName(file));

            string code = await File.ReadAllTextAsync(file);
            string prompt = $"Review this code file: {Path.GetFileName(file)}\n\n```\n{code}\n```";

            AgentRunResponse response = await workflowAgent.RunAsync(prompt);

            Console.WriteLine($"\n{'='..60}");
            Console.WriteLine($"Review for: {Path.GetFileName(file)}");
            Console.WriteLine($"{'='..60}");
            Console.WriteLine(response.Text);
        }

        _logger.LogInformation("Code review completed");
    }

    private AIAgent CreateSecurityAgent()
    {
        return new ChatClientAgent(_chatClient,
                                   new ChatClientAgentOptions
                                   {
                                       Name = "SecurityAnalyzer",
                                       Instructions = """
                                                      You are a security expert reviewing code for vulnerabilities.
                                                      Focus on:
                                                        - SQL injection risks
                                                        - XSS vulnerabilities
                                                        - Insecure authentication
                                                        - Sensitive data exposure
                                                        - Cryptographic issues

                                                        Provide a brief security assessment (2-3 sentences) highlighting critical issues only.
                                                      """
                                   });
    }

    private AIAgent CreateQualityAgent()
    {
        return new ChatClientAgent(_chatClient,
                                   new ChatClientAgentOptions
                                   {
                                       Name = "QualityReviewer",
                                       Instructions = """
                                                      You are a code quality expert reviewing code maintainability.
                                                      Focus on:
                                                        - Code complexity
                                                        - SOLID principles
                                                        - Code smells
                                                        - Error handling
                                                        - Naming conventions

                                                        Provide a brief quality assessment (2-3 sentences) highlighting main concerns.
                                                      """
                                   });
    }

    private AIAgent CreatePerformanceAgent()
    {
        return new ChatClientAgent(_chatClient,
                                   new ChatClientAgentOptions
                                   {
                                       Name = "PerformanceOptimizer",
                                       Instructions = """
                                                      You are a performance expert reviewing code efficiency.
                                                      Focus on:
                                                        - Algorithmic complexity
                                                        - Memory allocation
                                                        - Database query optimization
                                                        - Async/await patterns
                                                        - Resource management

                                                        Provide a brief performance assessment (2-3 sentences) highlighting optimization opportunities.
                                                      """
                                   });
    }

    private AIAgent CreateDocumentationAgent()
    {
        return new ChatClientAgent(_chatClient,
                                   new ChatClientAgentOptions
                                   {
                                       Name = "DocumentationChecker",
                                       Instructions = """
                                                      You are a documentation expert reviewing code clarity.
                                                      Focus on:
                                                        - XML documentation completeness
                                                        - Comment quality
                                                        - API usability
                                                        - Code readability

                                                        Provide a brief documentation assessment (2-3 sentences) highlighting gaps.
                                                      """
                                   });
    }

    private AIAgent CreateSummaryAgent()
    {
        return new ChatClientAgent(_chatClient,
                                   new ChatClientAgentOptions
                                   {
                                       Name = "SummaryGenerator",
                                       Instructions = """
                                                      You are synthesizing multiple code review perspectives.
                                                      Based on the previous agent assessments:
                                                        - Identify the top 3 priority issues
                                                        - Provide actionable recommendations
                                                        - Assign an overall severity: LOW, MEDIUM, HIGH, CRITICAL

                                                      Format output as:
                                                      SEVERITY: [level]
                                                      TOP ISSUES:
                                                      1. [issue]
                                                      2. [issue]
                                                      3. [issue]

                                                      RECOMMENDATIONS:
                                                      - [recommendation]
                                                      """
                                   });
    }
}