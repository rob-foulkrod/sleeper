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
    private readonly CopilotModelSelection _modelSelection;

    public string WriterModel => _modelSelection.WriterModel;
    public string LeagueWriterModel => _modelSelection.LeagueWriterModel;
    public string ProofreaderModel => _modelSelection.ProofreaderModel;
    public string EvaluatorModel => _modelSelection.EvaluatorModel;

    private CopilotAgentHost(
        CopilotClient client,
        CopilotAgentSettings settings,
        CopilotUsageLedger usage,
        CopilotModelSelection modelSelection)
    {
        _client = client;
        _settings = settings;
        _usage = usage;
        _modelSelection = modelSelection;
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
            var modelSelection = skipModelVerification
                ? new CopilotModelSelection(settings.WriterModel, settings.LeagueWriterModel, settings.ProofreaderModel, settings.EvaluatorModel)
                : ResolveModelSelection(
                    settings,
                    (await client.ListModelsAsync(cancellationToken).ConfigureAwait(false)).Select(model => model.Id),
                    requireEvaluatorModel);
            if (!skipModelVerification)
            {
                LogFallback("writer", settings.WriterModel, modelSelection.WriterModel);
                LogFallback("league writer", settings.LeagueWriterModel, modelSelection.LeagueWriterModel);
                LogFallback("proofreader", settings.ProofreaderModel, modelSelection.ProofreaderModel);
                if (requireEvaluatorModel)
                    LogFallback("evaluator", settings.EvaluatorModel, modelSelection.EvaluatorModel);
            }

            return new CopilotAgentHost(client, settings, usage, modelSelection);
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static CopilotModelSelection ResolveModelSelection(
        CopilotAgentSettings settings,
        IEnumerable<string>? models,
        bool requireEvaluatorModel = true)
    {
        var available = (models ?? [])
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Select(model => model.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (available.Length == 0)
        {
            throw new InvalidOperationException("Copilot reported no available models.");
        }

        var writer = SelectAvailableModel(
            available,
            [settings.WriterModel, settings.LeagueWriterModel, settings.ProofreaderModel, settings.EvaluatorModel]);
        var leagueWriter = SelectAvailableModel(
            available,
            [settings.LeagueWriterModel, settings.WriterModel, settings.EvaluatorModel, settings.ProofreaderModel]);
        var proofreader = SelectAvailableModel(
            available,
            [settings.ProofreaderModel, settings.WriterModel, settings.LeagueWriterModel, settings.EvaluatorModel]);
        IReadOnlySet<string>? evaluatorDisallowed = requireEvaluatorModel
            ? new HashSet<string>([writer, leagueWriter], StringComparer.OrdinalIgnoreCase)
            : null;
        var evaluator = SelectAvailableModel(
            available,
            [settings.EvaluatorModel, settings.ProofreaderModel],
            evaluatorDisallowed);

        return new CopilotModelSelection(writer, leagueWriter, proofreader, evaluator);
    }

    private static string SelectAvailableModel(
        IReadOnlyList<string> available,
        IEnumerable<string> preferences,
        IReadOnlySet<string>? disallowed = null)
    {
        disallowed ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var availableSet = available.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var preference in preferences)
        {
            var candidate = preference.Trim();
            if (!string.IsNullOrWhiteSpace(candidate) &&
                availableSet.Contains(candidate) &&
                !disallowed.Contains(candidate))
            {
                return available.First(model => model.Equals(candidate, StringComparison.OrdinalIgnoreCase));
            }
        }

        var fallback = available.FirstOrDefault(model => !disallowed.Contains(model));

        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback;

        throw new InvalidOperationException(
            $"Copilot model selection requires another available model. Available models: {string.Join(", ", available)}");
    }

    private static void LogFallback(string role, string configured, string resolved)
    {
        if (!configured.Equals(resolved, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(
                $"  (Copilot {role} model '{configured}' is unavailable; using live model '{resolved}')");
        }
    }

    public IReportTextAgent CreateWriter(string instructions, string? model = null)
        => new CopilotReportTextAgent(
            _client,
            model ?? _modelSelection.WriterModel,
            _settings.ReasoningEffort,
            TimeSpan.FromSeconds(_settings.TimeoutSeconds),
            instructions,
            _usage);

    public IReportTextAgent CreateEvaluator(string instructions)
        => new CopilotReportTextAgent(
            _client,
            _modelSelection.EvaluatorModel,
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

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}

internal sealed record CopilotModelSelection(
    string WriterModel,
    string LeagueWriterModel,
    string ProofreaderModel,
    string EvaluatorModel);

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
