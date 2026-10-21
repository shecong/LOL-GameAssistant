using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Infrastructure.Ai;

namespace LOL_GameAssistant.Infrastructure.Coaching;

/// <summary>采样与云端分析分开执行，新快照立即撤下失效建议并取消旧分析。</summary>
public sealed class RecommendationCoordinator : IRecommendationCoordinator
{
    private readonly IAiCoachingService _contexts;
    private readonly IAiRecommendationProvider _provider;
    private readonly TimeProvider _clock;
    private readonly ILaneKnowledgeService? _knowledge;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _sampling = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private ITimer? _timer;
    private CloudAiSettings _settings = new();
    private RecommendationState _current = RecommendationState.Initial;
    private AiGameContext? _context;
    private IReadOnlyList<CoachRecommendation> _cards = [];
    private readonly List<RecommendationHistoryEntry> _history = [];
    private readonly Queue<AiGameContext> _samples = new();
    private readonly Dictionary<string, DateTimeOffset> _dismissedUntil = new(StringComparer.Ordinal);
    private int _consecutiveFailures;
    private DateTimeOffset _retryAfter = DateTimeOffset.MinValue;
    private CancellationTokenSource? _requestCts;
    private Task? _request;
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;
    private DateTimeOffset? _analyzedAt;
    private DateTimeOffset _stableSince;
    private string _version = "", _analyzedVersion = "", _matchup = "";
    private string _pendingTrigger = "首次分析";
    private string? _notifiedPhase;
    private bool _manualPending, _phasePending, _started, _disposed;
    private int _generation, _requests, _sessionVersion;
    private RecommendationStatus _resultStatus = RecommendationStatus.NoActiveGame;
    private string _resultMessage = "等待进入英雄选择或对局。";

    public RecommendationCoordinator(IAiCoachingService contexts, IAiRecommendationProvider provider, TimeProvider? clock = null, ILaneKnowledgeService? laneKnowledge = null)
    { _contexts = contexts; _provider = provider; _clock = clock ?? TimeProvider.System; _knowledge = laneKnowledge; }
    public RecommendationState Current { get { lock (_sync) return _current; } }
    public event Action<RecommendationState>? StateChanged;
    public void Start(AssistantSettings settings)
    {
        lock (_sync)
        {
            if (_disposed || _started) return;
            _settings = settings.Ai.Copy(); _settings.Normalize(); _started = true; _phasePending = true;
            _timer = _clock.CreateTimer(_ => _ = RefreshAsync(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        }
        _ = RefreshAsync();
    }
    public void UpdateSettings(AssistantSettings settings)
    {
        bool refresh;
        lock (_sync)
        {
            if (_disposed) return;
            CloudAiSettings next = settings.Ai.Copy(); next.Normalize();
            bool changed = next.Provider != _settings.Provider || next.Model != _settings.Model ||
                next.BaseUrl != _settings.BaseUrl || next.EncryptedApiKey != _settings.EncryptedApiKey ||
                next.RecommendationEnabled != _settings.RecommendationEnabled;
            refresh = changed || next.DynamicRefreshEnabled != _settings.DynamicRefreshEnabled ||
                next.MinimumRequestSeconds != _settings.MinimumRequestSeconds || next.MatchRequestBudget != _settings.MatchRequestBudget;
            _settings = next;
            if (changed) { Invalidate(); _analyzedVersion = ""; _phasePending = true; }
            if (!next.RecommendationEnabled) { _manualPending = false; _phasePending = false; }
            PublishCurrent();
        }
        if (_started && refresh) _ = RefreshAsync();
    }
    public void NotifyGamePhaseChanged(string? phase)
    {
        lock (_sync)
        {
            if (_disposed || !_started || string.Equals(_notifiedPhase, phase, StringComparison.OrdinalIgnoreCase)) return;
            if (string.Equals(_context?.Phase, phase, StringComparison.OrdinalIgnoreCase) && _version.Length > 0)
            { _notifiedPhase = phase; return; }
            _notifiedPhase = phase; Invalidate(); if (phase != "InProgress") _matchup = ""; _phasePending = true;
            if (phase == "ChampSelect") ResetSession();
            _context = new AiGameContext { Phase = phase ?? "未知" }; _version = "";
            Publish(RecommendationStatus.Collecting, "阶段已变化，正在同步当前对局。", "阶段变化");
        }
        _ = RefreshAsync();
    }
    public void ConfirmMatchup(string? champion)
    {
        lock (_sync)
        {
            if (_disposed || _context == null) return;
            string next = champion?.Trim() ?? "";
            if (next.Length > 0 && !_context.EnemyChampions.Contains(next, StringComparer.Ordinal)) return;
            if (_matchup == next) return;
            _matchup = next; Invalidate(); _manualPending = true;
            _context = _context with { MatchupChampion = next };
            Publish(RecommendationStatus.Collecting, "对线对象已更新，等待重新分析。", "对线对象更新");
        }
        _ = RefreshAsync();
    }
    public void RecordFeedback(string recommendationId, string feedback)
    {
        if (feedback is not ("有帮助" or "不适用")) return;
        lock (_sync)
        {
            for (int i = _history.Count - 1; i >= 0; i--)
                if (_history[i].Recommendation.Id == recommendationId)
                { _history[i] = _history[i] with { Feedback = feedback }; break; }
            if (feedback == "不适用")
            {
                var dismissed = _cards.FirstOrDefault(c => c.Id == recommendationId);
                if (dismissed != null) _dismissedUntil[DismissalKey(dismissed)] = _clock.GetUtcNow().AddSeconds(90);
                _cards = _cards.Where(c => c.Id != recommendationId).ToArray();
                _analyzedVersion = "";
                _pendingTrigger = "玩家反馈不适用，重新检查执行条件";
            }
            PublishCurrent();
        }
    }

    public async Task RefreshAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        lock (_sync) { if (_disposed) return; if (force) _manualPending = true; }
        if (!await _sampling.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return;
        Task? pending = null;
        try
        {
            using var sampleCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
            int sampleGeneration;
            lock (_sync) { if (!_settings.RecommendationEnabled) { PublishCurrent(); return; } sampleGeneration = _generation; }
            AiGameContext sampled = await _contexts.CollectContextAsync(sampleCts.Token).ConfigureAwait(false);
            lock (_sync)
            {
                if (_disposed || sampleGeneration != _generation || !_settings.RecommendationEnabled) return;
                DateTimeOffset now = _clock.GetUtcNow();
                bool active = sampled.Phase is "ChampSelect" or "InProgress";
                bool newGame = sampled.Phase == "InProgress" && _context?.Phase == "InProgress" && sampled.GameTimeSeconds + 60 < _context.GameTimeSeconds;
                if (newGame) { ResetSession(); Invalidate(); }
                if (sampled.Phase == "ChampSelect" && _context?.Phase != "ChampSelect" ||
                    active && _context?.Phase is not ("ChampSelect" or "InProgress") && _requests > 0) ResetSession();
                if (!active || _context is { MyChampionId: > 0 } && _context.MyChampionId != sampled.MyChampionId) _matchup = "";
                if (!sampled.EnemyChampions.Contains(_matchup, StringComparer.Ordinal)) _matchup = "";
                sampled = sampled with { MatchupChampion = _matchup,
                    LaneKnowledge = _knowledge?.GetAdvice(sampled.MyChampion, sampled.MyRole, _matchup) ?? sampled.LaneKnowledge };
                sampled = EnrichWindow(sampled);
                string nextVersion = RecommendationPolicy.Version(sampled);
                string trigger = RecommendationPolicy.Trigger(_context, sampled);
                bool changed = nextVersion != _version;
                if (changed)
                {
                    // 英雄/阶段改变才能使整个请求无效；普通状态变化按每条建议的依赖核验。
                    if (_context == null || RecommendationPolicy.Identity(_context) != RecommendationPolicy.Identity(sampled)) Invalidate();
                    _version = nextVersion; _stableSince = now;
                    _pendingTrigger = trigger;
                    _resultMessage = "局势已变化，正在等待稳定后分析。";
                }
                _context = sampled;
                _cards = _cards.Where(c => RecommendationPolicy.IsValid(c, sampled, now)).ToArray();
                if (!active) { _phasePending = false; _manualPending = false; Publish(RecommendationStatus.NoActiveGame, "等待进入英雄选择或对局。", ""); return; }
                if (sampled.MyChampionId <= 0) { Publish(RecommendationStatus.DataUnavailable, "尚未确认当前英雄，等待客户端同步。", ""); return; }
                if (sampled.Phase == "InProgress" && !sampled.HasLiveClientData)
                { Invalidate(); Publish(RecommendationStatus.DataUnavailable, "实时数据暂不可用，已撤下旧建议。", ""); return; }
                foreach (var key in _dismissedUntil.Where(p => p.Value <= now).Select(p => p.Key).ToArray()) _dismissedUntil.Remove(key);
                if (!_cards.Any(c => c.OpportunityId == "survival.health") && CoachingImmediateAdvice.Create(sampled, now) is { } immediate && !IsDismissed(immediate, now))
                {
                    _cards = new[] { immediate }.Concat(_cards).Take(3).ToArray();
                    _history.Add(new(now, sampled.GameTimeText, immediate));
                    if (_history.Count > 60) _history.RemoveAt(0);
                }
                if (string.IsNullOrWhiteSpace(_settings.Model) || string.IsNullOrWhiteSpace(_settings.EncryptedApiKey))
                { Publish(RecommendationStatus.ConfigurationRequired, "请在设置中配置 AI 服务、模型与 API Key。", ""); return; }
                if (CoachingDecisionFrame.Create(sampled).Opportunities.Count == 0)
                {
                    _manualPending = false; _phasePending = false; _analyzedVersion = _version;
                    Publish(RecommendationStatus.NoActionNeeded, "当前已知事实不足以形成具体行动，等待更多可见数据。", trigger);
                    return;
                }
                if (_request is { IsCompleted: false })
                {
                    if (_requestCts?.IsCancellationRequested == false) _manualPending = false;
                    Publish(RecommendationStatus.Collecting, "正在分析当前局势…", trigger); return;
                }
                bool manual = _manualPending || _phasePending;
                bool needsAnalysis = _analyzedVersion != _version || _cards.Count == 0 ||
                    now - (_analyzedAt ?? DateTimeOffset.MinValue) >= TimeSpan.FromSeconds(_settings.DynamicRefreshSeconds);
                if (_resultStatus == RecommendationStatus.NoActionNeeded && _analyzedVersion == _version)
                    needsAnalysis = now - (_analyzedAt ?? DateTimeOffset.MinValue) >= TimeSpan.FromSeconds(_settings.DynamicRefreshSeconds);
                if (!manual && (!_settings.DynamicRefreshEnabled || !needsAnalysis)) { PublishCurrent(); return; }
                if (!manual && now < _retryAfter) { PublishCurrent(); return; }
                if (_requests >= _settings.MatchRequestBudget)
                { _manualPending = false; _phasePending = false; Publish(RecommendationStatus.BudgetPaused, "已达到本局请求预算；可在设置中提高预算。", trigger); return; }
                TimeSpan cooldown = TimeSpan.FromSeconds(manual ? 8 : _settings.MinimumRequestSeconds);
                if (now - _lastRequest < cooldown) { PublishCurrent("请求冷却中，已合并重复分析请求。"); return; }
                if (!manual && now - _stableSince < TimeSpan.FromSeconds(2))
                { Publish(RecommendationStatus.Collecting, "等待装备或阵容变化稳定…", trigger); return; }
                _manualPending = false; _phasePending = false; _lastRequest = now; _requests++;
                _requestCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
                Publish(RecommendationStatus.Collecting, "正在分析新变化，仍有效的建议继续保留。", manual ? "手动或阶段分析" : _analyzedVersion != _version ? _pendingTrigger : "定期检查");
                pending = _request = RequestAsync(_settings.Copy(), sampled, _generation, now, _requestCts);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        { lock (_sync) { Invalidate(); if (!_settings.RecommendationEnabled) PublishCurrent(); else Publish(RecommendationStatus.DataUnavailable, "无法读取当前对局数据，稍后自动重试。", ""); } }
        finally { _sampling.Release(); }
        if (pending != null) await pending.ConfigureAwait(false);
    }

    private async Task RequestAsync(CloudAiSettings settings, AiGameContext context, int generation, DateTimeOffset startedAt, CancellationTokenSource cts)
    {
        await Task.Yield();
        try
        {
            AiRecommendationResult result = await _provider.CreateAsync(settings, context, cts.Token)
                .WaitAsync(TimeSpan.FromSeconds(45), cts.Token).ConfigureAwait(false);
            lock (_sync)
            {
                if (_disposed || cts.IsCancellationRequested || generation != _generation || !_settings.RecommendationEnabled) return;
                DateTimeOffset now = _clock.GetUtcNow();
                if (!result.FromAi || string.IsNullOrWhiteSpace(result.Recommendation))
                { Failure(RecommendationStatus.Failed, "分析失败：" + (result.Error ?? "服务未返回内容。")); return; }
                var parsed = CloudRecommendationParser.Parse(result.Recommendation, now, context, strict: true);
                if (parsed.Any(c => c.IsFormatError))
                { Failure(RecommendationStatus.FormatError, "本次输出未通过行动与事实校验，已拦截空泛或依据不足的建议。稍后重试。" ); return; }
                _consecutiveFailures = 0; _retryAfter = DateTimeOffset.MinValue;
                var newCards = parsed.Select(c => c with { ExpiresAt = c.ExpiresAt - (now - startedAt) })
                    .Where(c => _context != null && RecommendationPolicy.IsValid(c, _context, now) && !IsDismissed(c, now)).OrderByDescending(c => c.Priority).Take(3).ToArray();
                _cards = parsed.Count == 0 ? _cards.Where(c => _context != null && RecommendationPolicy.IsValid(c, _context, now)).ToArray() :
                    newCards.Concat(_cards.Where(c => c.Source == RecommendationSource.LocalObservation && _context != null && RecommendationPolicy.IsValid(c, _context, now)))
                        .GroupBy(c => c.OpportunityId).Select(g => g.First()).OrderByDescending(c => c.Priority).Take(3).ToArray();
                _analyzedVersion = RecommendationPolicy.Version(context); _analyzedAt = now;
                _resultStatus = parsed.Count == 0 ? RecommendationStatus.NoActionNeeded : _cards.Count == 0 ? RecommendationStatus.Stale : RecommendationStatus.AiReady;
                _resultMessage = parsed.Count == 0 ? "本次没有值得新增的可靠行动，继续观察局势变化。" : _cards.Count == 0
                    ? "分析期间条件已改变，本次建议已撤下，等待下一次分析。" : "已按本局事实生成行动步骤；执行前核对适用条件。";
                foreach (var card in newCards) _history.Add(new(now, context.GameTimeText, card));
                if (_history.Count > 60) _history.RemoveRange(0, _history.Count - 60);
                PublishCurrent();
            }
        }
        catch (OperationCanceledException) { }
        catch (TimeoutException)
        { cts.Cancel(); lock (_sync) { if (generation == _generation && !_disposed) Failure(RecommendationStatus.Failed, "本次分析超时，已取消；稍后重试。" ); } }
        catch (Exception)
        { lock (_sync) { if (generation == _generation && !_disposed) Failure(RecommendationStatus.Failed, "分析请求失败，请检查服务配置后重试。" ); } }
        finally { lock (_sync) { if (ReferenceEquals(_requestCts, cts)) _requestCts = null; } cts.Dispose(); }
    }
    private void Invalidate() { _generation++; _requestCts?.Cancel(); _cards = []; _resultStatus = RecommendationStatus.Collecting; }
    private void ResetSession()
    { _sessionVersion++; _requests = 0; _history.Clear(); _samples.Clear(); _dismissedUntil.Clear(); _consecutiveFailures = 0; _retryAfter = DateTimeOffset.MinValue; _analyzedAt = null; _analyzedVersion = ""; _lastRequest = DateTimeOffset.MinValue; _matchup = ""; }

    // 去重依据由本机决策定义，不能因模型增加可选引用而绕过玩家反馈。
    private string DismissalKey(CoachRecommendation card)
    {
        var opportunity = _context == null ? null : CoachingDecisionFrame.Create(_context).Opportunities
            .FirstOrDefault(o => o.Id == card.OpportunityId);
        return card.OpportunityId + "|" + (opportunity == null ? card.ContextVersion :
            RecommendationPolicy.EvidenceVersion(_context!, opportunity.RequiredFacts));
    }

    private bool IsDismissed(CoachRecommendation card, DateTimeOffset now) =>
        _dismissedUntil.TryGetValue(DismissalKey(card), out var until) && until > now;

    private AiGameContext EnrichWindow(AiGameContext sample)
    {
        if (_samples.TryPeek(out var previous) && (RecommendationPolicy.Identity(previous) != RecommendationPolicy.Identity(sample) ||
            previous.GameTimeSeconds > sample.GameTimeSeconds)) _samples.Clear();
        while (_samples.TryPeek(out previous) && sample.GameTimeSeconds - previous.GameTimeSeconds > 120) _samples.Dequeue();
        FarmWindow? window = null;
        if (_samples.TryPeek(out previous) && sample.GameTimeSeconds - previous.GameTimeSeconds >= 90 &&
            sample.Telemetry?.Self is { CreepScore: { } cs, Deaths: { } deaths } &&
            previous.Telemetry?.Self is { CreepScore: { } oldCs, Deaths: { } oldDeaths } && cs >= oldCs && deaths >= oldDeaths)
            window = new(sample.GameTimeSeconds - previous.GameTimeSeconds, cs - oldCs, deaths - oldDeaths);
        _samples.Enqueue(sample);
        while (_samples.Count > 65) _samples.Dequeue();
        return sample with { FarmWindow = window, RecentAdvice = _history.TakeLast(3)
            .Select(h => new RecentCoachingAdvice(h.Recommendation.OpportunityId, h.Recommendation.ActionKey, h.Recommendation.Title, h.Feedback,
                h.Recommendation.Body, h.Recommendation.Conditions, h.Feedback != "不适用" &&
                    RecommendationPolicy.IsValid(h.Recommendation, sample, _clock.GetUtcNow()))).ToArray() };
    }

    private void Failure(RecommendationStatus status, string message)
    {
        _consecutiveFailures++;
        _retryAfter = _clock.GetUtcNow().AddSeconds(Math.Min(180, 15 * Math.Pow(2, Math.Min(4, _consecutiveFailures))));
        _resultStatus = status; _resultMessage = message; PublishCurrent();
    }
    private void PublishCurrent(string? message = null)
    {
        _cards = _context == null ? [] : _cards.Where(c => RecommendationPolicy.IsValid(c, _context, _clock.GetUtcNow())).ToArray();
        if (_cards.Count == 0 && _resultStatus == RecommendationStatus.AiReady)
        { _resultStatus = RecommendationStatus.Stale; _resultMessage = "上次建议已失效，等待重新分析。"; }
        if (!_settings.RecommendationEnabled) { _cards = []; Publish(RecommendationStatus.Disabled, "AI 建议已关闭。", ""); }
        else Publish(_resultStatus, message ?? _resultMessage, _current.Trigger);
    }
    private void Publish(RecommendationStatus status, string diagnostic, string trigger)
    {
        if (_disposed) return;
        _resultStatus = status; _resultMessage = diagnostic;
        DateTimeOffset now = _clock.GetUtcNow();
        DateTimeOffset? next = _settings.RecommendationEnabled && _settings.DynamicRefreshEnabled && _requests < _settings.MatchRequestBudget
            ? new[] { _retryAfter, _lastRequest.AddSeconds(_settings.MinimumRequestSeconds),
                _analyzedVersion == _version && status == RecommendationStatus.NoActionNeeded ? (_analyzedAt ?? now).AddSeconds(_settings.DynamicRefreshSeconds) : now }.Max() : null;
        _current = new(status, _context ?? new AiGameContext(), _cards, diagnostic, now, next)
        { AnalyzedAt = _analyzedAt, RequestCount = _requests, RequestBudget = _settings.MatchRequestBudget, SessionVersion = _sessionVersion, Trigger = trigger, History = _history.ToArray() };
        if (StateChanged == null) return;
        foreach (Action<RecommendationState> handler in StateChanged.GetInvocationList())
            try { handler(_current); } catch { /* 展示失败不影响调度。 */ }
    }
    public void Dispose()
    { lock (_sync) { if (_disposed) return; _disposed = true; _requestCts?.Cancel(); _timer?.Dispose(); _lifetime.Cancel(); } }
}
