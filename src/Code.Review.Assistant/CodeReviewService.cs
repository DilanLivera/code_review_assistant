using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Code.Review.Assistant;

internal sealed class CodeReviewService
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