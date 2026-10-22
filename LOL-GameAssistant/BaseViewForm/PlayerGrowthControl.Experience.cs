using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

internal sealed partial class PlayerGrowthControl
{
    private readonly ICompanionExperienceStore _experienceStore;
    private readonly IPatchWatchService _patches;
    private PlayerExperience _experience = new();
    private readonly UiRequestScope _patchLoads = new();
    private string _patchNotice = "", _patchCheckedScope = "";
    private readonly AntdUI.Label _goalProgress = new() { Name = "practiceProgress", Dock = DockStyle.Bottom, Height = 86, TextMultiLine = true, Padding = new Padding(12, 6, 12, 6) };
    private void InitializeExperienceActions()
    {
        var review = new AntdUI.Button { Name = "minuteReview", Text = "一分钟复盘", AutoSize = true };
        review.Click += (_, _) => ShowMinuteReview();
        var goal = new AntdUI.Button { Name = "practiceGoal", Text = "练习目标", AutoSize = true };
        goal.Click += async (_, _) => await ShowPracticeGoalAsync();
        var patches = new AntdUI.Button { Name = "patchWatch", Text = "主练与方案版本提醒", AutoSize = true };
        patches.Click += async (_, _) =>
        {
            if (patches.Loading) return; patches.Loading = true;
            try { await ShowPatchWatchAsync(); }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!IsDisposed) _scope.Text = "版本检查未完成：" + e.Message + "；已保留原有关注基准。"; }
            finally { if (!patches.IsDisposed) patches.Loading = false; }
        };
        _filters.Controls.AddRange([review, goal, patches]); Controls.Add(_goalProgress);
    }
    private async Task<PlayerExperience> LoadExperienceAsync(string puuid, CancellationToken token)
    {
        try { return await _experienceStore.LoadAsync(puuid, token); }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { RuntimeDiagnostics.WriteException(e); return new(); }
    }
    private void RenderExperience()
    {
        if (_experience.Goal is not { } goal || _snapshot == null)
        { _goalProgress.Text = "练习目标：选择一个模式和一个指标，连续练习；可在上方设置，不以单局胜负评判进步。\n" + _patchNotice; return; }
        var progress = CompanionExperience.Progress(_snapshot.Report.Matches, goal);
        _goalProgress.Text = $"练习目标 · {goal.Mode} · {(goal.ChampionId is int id ? _champions.GetDisplayName(id) : "不限英雄")} · {CompanionExperience.MetricName(goal.Metric)} {(goal.Metric == PracticeMetric.Deaths ? "≤" : "≥")} {goal.Target:0.##}\n" +
            $"当前样本内设置后有效 {progress.Completed}/{progress.Required} 局 · 达标 {progress.Passed} 局 · 缺失指标 {progress.Missing} 局（不计失败）；切换到目标模式查看进度。\n" + _patchNotice;
    }
    private async Task CheckPatchNoticeAsync()
    {
        if (_snapshot == null || _puuid == null || _experience.PatchVersion.Length == 0) return;
        var ids = _snapshot.Champions.Where(c => c.Training).Select(c => c.ChampionId).Order().ToArray();
        if (ids.Length == 0) return;
        string puuid = _puuid, scope = puuid + ":" + _experience.PatchVersion + ":" + string.Join(",", ids);
        if (_patchCheckedScope == scope) return;
        _patchCheckedScope = scope;
        using var request = _patchLoads.Begin();
        try
        {
            var presets = AppCompositionRoot.ApplicationSettingsStore.Load().PersonalRunePresets.Where(p => ids.Contains(p.ChampionId)).ToArray();
            var items = presets.SelectMany(p => p.ItemBlocks).SelectMany(b => b.ItemIds);
            var result = await _patches.CheckAsync(_experience, ids, items, request.Token, presets.SelectMany(p => p.RunePerkIds));
            if (!request.IsCurrent || IsDisposed || puuid != _puuid) return;
            _patchNotice = result.Changes.Count > 0 ? $"版本提醒：{result.CurrentVersion} 发现 {result.Changes.Count} 项关注资源变化；点击“主练与方案版本提醒”查看并确认。" : $"已检查资源版本 {result.CurrentVersion}，已关注字段未变化。";
            RenderExperience();
        }
        catch (OperationCanceledException) { _patchCheckedScope = ""; }
        catch (Exception e) { if (!IsDisposed && puuid == _puuid) { _patchNotice = "版本检查暂不可用；原关注基准保留，可手动重试。"; RenderExperience(); RuntimeDiagnostics.WriteException(e); } }
    }
    private void ShowMinuteReview()
    {
        var match = SelectedMatch() ?? _snapshot?.Report.Matches.FirstOrDefault();
        if (match == null || _snapshot == null) { _scope.Text = "先取得战绩样本，再选择一局复盘。"; return; }
        var review = CompanionExperience.Review(match, _snapshot.Report.Matches);
        using var dialog = new AntdContentWindow { Text = "一分钟复盘 · " + _champions.GetDisplayName(match.ChampionId), ClientSize = new(700, 440), MinimumSize = new(480, 340), StartPosition = FormStartPosition.CenterParent, Font = Font };
        var text = new AntdUI.Input { Name = "minuteReviewText", Dock = DockStyle.Fill, ReadOnly = true, Multiline = true,
            Text = $"本局 {match.GameId} · {match.Mode}\n\n值得保持\n{review.Highlight}\n\n值得关注\n{review.Improvement}\n\n下一局一个目标\n{review.NextGoal}\n\n比较范围：此前最多 10 场同英雄同模式，实际 {review.BaselineGames} 场；每项至少 3 个已知值。只描述统计差异，不推断死亡、团战或输赢原因。\n样本采集：{_snapshot.AcquiredAt.ToLocalTime():MM-dd HH:mm}{(_snapshot.Cached ? "（缓存）" : "")}" };
        dialog.Controls.Add(text); AntdWindowChrome.AddHeader(dialog); UiTheme.Apply(dialog); dialog.ShowDialog(FindForm());
    }
    private async Task ShowPracticeGoalAsync()
    {
        if (_snapshot == null || _puuid == null) { _scope.Text = "请先查询玩家并取得样本。"; return; }
        string puuid = _puuid;
        using var dialog = new PracticeGoalForm(_snapshot, _champions, _experience.Goal);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            await _experienceStore.UpdateAsync(puuid, value => value.Goal = dialog.Goal, _requests.Token);
            var value = await _experienceStore.LoadAsync(puuid, _requests.Token);
            if (!IsDisposed && puuid == _puuid) { _experience = value; RenderExperience(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!IsDisposed) _scope.Text = "目标未保存：" + e.Message; }
    }
    private async Task ShowPatchWatchAsync()
    {
        if (_snapshot == null || _puuid == null) { _scope.Text = "请先查询玩家。"; return; }
        string puuid = _puuid;
        var champions = _snapshot.Champions.Where(c => c.Training).Select(c => c.ChampionId).ToHashSet();
        var presets = AppCompositionRoot.ApplicationSettingsStore.Load().PersonalRunePresets.Where(p => champions.Contains(p.ChampionId)).ToArray();
        var items = presets.SelectMany(p => p.ItemBlocks).SelectMany(b => b.ItemIds).ToArray();
        if (champions.Count == 0) { _scope.Text = "请先在英雄池中标记主练英雄；只检查这些英雄及其个人方案装备。"; return; }
        var previous = await _experienceStore.LoadAsync(puuid, _requests.Token);
        var result = await _patches.CheckAsync(previous, champions, items, _requests.Token, presets.SelectMany(p => p.RunePerkIds));
        if (IsDisposed || puuid != _puuid) return;
        using var dialog = new AntdContentWindow { Text = "主练与方案版本提醒", ClientSize = new(760, 520), MinimumSize = new(520, 360), StartPosition = FormStartPosition.CenterParent, Font = Font };
        var text = new AntdUI.Input { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true,
            Text = $"关注基准：{(previous.PatchVersion.Length == 0 ? "尚未建立" : previous.PatchVersion)} → 官方资源 {result.CurrentVersion}\n\n" +
                (result.Changes.Count == 0 ? "已关注条目未检测到资源字段变化。" : string.Join("\n\n", result.Changes)) + "\n\n" + result.Message + "\n来源：https://ddragon.leagueoflegends.com/\n确认后保存本次资源为下次比较基准；取消不修改原基准。" };
        var acknowledge = new AntdUI.Button { Text = "已查看，保存比较基准", Dock = DockStyle.Bottom, Height = 44 };
        acknowledge.Click += async (_, _) =>
        {
            if (acknowledge.Loading) return; acknowledge.Loading = true;
            try
            {
                await _experienceStore.UpdateAsync(puuid, value => { value.PatchVersion = result.CurrentVersion; value.PatchAssets = new(result.Assets); }, _requests.Token);
                var saved = await _experienceStore.LoadAsync(puuid, _requests.Token);
                if (!IsDisposed && puuid == _puuid) { _experience = saved; _patchNotice = "关注基准已更新到 " + saved.PatchVersion; _patchCheckedScope = ""; RenderExperience(); }
                if (!dialog.IsDisposed) { dialog.DialogResult = DialogResult.OK; dialog.Close(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!dialog.IsDisposed) text.Text += "\n保存失败：" + e.Message; }
            finally { if (!acknowledge.IsDisposed) acknowledge.Loading = false; }
        };
        dialog.Controls.Add(text); dialog.Controls.Add(acknowledge); AntdWindowChrome.AddHeader(dialog); UiTheme.Apply(dialog); dialog.ShowDialog(FindForm());
    }
}
