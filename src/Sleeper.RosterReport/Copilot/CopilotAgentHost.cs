using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Sleeper.RosterReport.Agents;

namespace Sleeper.RosterReport.Copilot;

#pragma warning disable GHCP001 // Deny-all permission decisions are experimental in SDK 1.0.13.

internal sealed class CopilotAgentHost : IAsyncDisposable
{
    private readonly CopilotClient _client;
    private readonly CopilotAgentSettings _settings;
    private readonly CopilotUsageLedger _usage;

    private CopilotAgentHost(
        CopilotClient client,
        CopilotAgentSettings settings,
        CopilotUsageLedger usage)
    {
        _client = client;
        _settings = settings;
        _usage = usage;
    }

    public static async Task<CopilotAgentHost> StartAsync(
        CopilotAgentSettings settings,
        CopilotUsageLedger usage,
        CancellationToken cancellationToken = default,
        bool skipModelVerification = false,
        bool requireEvaluatorModel = true)
    {
        var copilotHome = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".copilot");
        var client = new CopilotClient(new CopilotClientOptions
        {
            Mode = CopilotClientMode.CopilotCli,
            BaseDirectory = copilotHome,
            UseLoggedInUser = true,
            WorkingDirectory = Recap.RecapPaths.WorkspaceRoot,
            LogLevel = CopilotLogLevel.Warning
        });

        try
        {
            await client.StartAsync(cancellationToken).ConfigureAwait(false);
            if (!skipModelVerification)
            {
                var models = await client.ListModelsAsync(cancellationToken).ConfigureAwait(false);
                var available = models.Select(model => model.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                VerifyModel(available, settings.WriterModel, "writer");
                VerifyModel(available, settings.LeagueWriterModel, "league writer");
                VerifyModel(available, settings.ProofreaderModel, "proofreader");
                if (requireEvaluatorModel)
                {
                    VerifyModel(available, settings.EvaluatorModel, "evaluator");
                    if (string.Equals(settings.WriterModel, settings.EvaluatorModel, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(settings.LeagueWriterModel, settings.EvaluatorModel, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("Writer models and EvaluatorModel must differ for the blind spike.");
                    }
                }
            }

            return new CopilotAgentHost(client, settings, usage);
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public IReportTextAgent CreateWriter(string instructions, string? model = null)
        => new CopilotReportTextAgent(
            _client,
            model ?? _settings.WriterModel,
            _settings.ReasoningEffort,
            TimeSpan.FromSeconds(_settings.TimeoutSeconds),
            instructions,
            _usage);

    public IReportTextAgent CreateEvaluator(string instructions)
        => new CopilotReportTextAgent(
            _client,
            _settings.EvaluatorModel,
            _settings.ReasoningEffort,
            TimeSpan.FromSeconds(_settings.TimeoutSeconds),
            instructions,
            _usage);

    public IReportTextAgent CreateProofreader(string instructions, string model)
        => new CopilotReportTextAgent(
            _client,
            model,
            null,
            TimeSpan.FromSeconds(_settings.TimeoutSeconds),
            instructions,
            _usage);

    private static void VerifyModel(IReadOnlySet<string> available, string model, string role)
    {
        if (!available.Contains(model))
            throw new InvalidOperationException($"Copilot {role} model '{model}' is unavailable. Available models: {string.Join(", ", available.Order())}");
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}

internal sealed class CopilotReportTextAgent(
    CopilotClient client,
    string model,
    string? reasoningEffort,
    TimeSpan timeout,
    string instructions,
    CopilotUsageLedger usage) : IReportTextAgent
{
    public async Task<string> GenerateAsync(
        string prompt,
        AgentCallContext context,
        CancellationToken cancellationToken = default)
    {
        await using var session = await client.CreateSessionAsync(
            new SessionConfig
            {
                Model = model,
                ReasoningEffort = reasoningEffort,
                AvailableTools = [],
                EnableSessionStore = false,
                EnableConfigDiscovery = false,
                EnableOnDemandInstructionDiscovery = false,
                EnableFileHooks = false,
                EnableHostGitOperations = false,
                EnableSkills = false,
                SkipCustomInstructions = true,
                Memory = new MemoryConfiguration { Enabled = false },
                PluginDirectories = [],
                McpServers = new Dictionary<string, McpServerConfig>(),
                OnPermissionRequest = (_, _) => Task.FromResult(
                    PermissionDecision.Reject("This report session does not permit tools.")),
                SystemMessage = new SystemMessageConfig
                {
                    Mode = SystemMessageMode.Replace,
                    Content = instructions
                }
            },
            cancellationToken).ConfigureAwait(false);

        using var subscription = session.On<AssistantUsageEvent>(
            evt => usage.Record(session.SessionId, context, evt.Data));

        AssistantMessageEvent? response;
        try
        {
            response = await session.SendAndWaitAsync(
                new MessageOptions { Prompt = prompt },
                timeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            using var abortCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await session.AbortAsync(abortCts.Token).ConfigureAwait(false);
            }
            catch (Exception abortException)
            {
                throw new AggregateException(
                    "Copilot generation did not complete and the in-flight session could not be aborted.",
                    ex,
                    abortException);
            }
            throw;
        }

        var text = response?.Data.Content?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException($"Copilot returned no text for {context.Role}.");

        return text;
    }
}
