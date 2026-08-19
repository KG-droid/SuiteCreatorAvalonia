using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Events;
using SuiteCreatorAvalonia.Models.Package;
using SuiteCreatorAvalonia.Models.Rules;
using SuiteOperations.Events;
using Log = Logger.Log;

namespace SuiteExecutor
{
    internal partial class Suite
    {
        private void RunSuite()
        {
            List<Stage> stages = GetOrderedStages();
            List<EventCore> allEvents = GetAllEvents();

            _log.WriteLog($"Suite has {stages.Count} stage(s) to process", "Execution", Log.Severity.Info);
            _log.WriteLog($"Suite has {allEvents.Count} event(s) configured", "Execution", Log.Severity.Info);

            if (_action == SuiteAction.Removal)
            {
                _log.WriteLog($"Suite is running as a removal, so certain events actions need to be reverse, e.g remove a file instead of deploying it", "Execution", Log.Severity.Info);
                foreach (EventCore evt in allEvents)
                {
                    if (evt is EventCoreWithPermanence permEvt)
                        permEvt.Reverse();
                }
                _log.WriteLog($"Events have been reversed, so anything that was installing or placing something will now remove it, unless it was set to permanent", "Execution", Log.Severity.Info);
            }

            // Resolve every stage's package and detection outcome up front so skipped stages (e.g. a removal
            // package whose target was never detected) don't each claim an equal slice of the progress bar -
            // only stages that actually do something count towards the percentage, otherwise the bar looks
            // like it "jumps" past a chunk of the suite almost instantly.
            List<(PackageBase? Package, bool ExecutePackage, bool HasWork)> resolvedStages = new();
            foreach (Stage stage in stages)
            {
                PackageBase? package = ResolvePackage(stage);
                bool executePackage = package != null && ShouldPackageExecute(package);
                bool hasWork = executePackage
                    || (package != null && _action == SuiteAction.Removal)
                    || StageHasApplicableEvent(allEvents, stage.Id);
                resolvedStages.Add((package, executePackage, hasWork));
            }

            int activeStageCount = resolvedStages.Count(s => s.HasWork);
            if (activeStageCount == 0) activeStageCount = 1;

            int activeIndex = 0;
            for (int i = 0; i < stages.Count; i++)
            {
                Stage stage = stages[i];
                _log.WriteLog($"--- Stage {i + 1}/{stages.Count}: {stage.Name} (Id: {stage.Id}) ---", "Execution", Log.Severity.Info);

                (PackageBase? package, bool executePackage, bool hasWork) = resolvedStages[i];

                // During Deployment, a skipped package (e.g. already detected) means this stage never
                // really happens, so its before/after events are skipped too. During Removal, the events
                // must still run even when the package itself won't be touched (not detected, a removal-only
                // package with no reverse action, or RemoveOnSuiteRemoval disabled) - each event's own
                // IsPermanent flag is what decides whether it gets reversed, not the fate of whatever
                // package happens to share its stage.
                if (package != null && !executePackage && _action != SuiteAction.Removal)
                {
                    continue;
                }

                // A stage with no package and no events attached (e.g. an untouched Start/End stage) does
                // nothing at all, so it must not consume a slice of the progress bar or move the indicator -
                // only stages with actual work (hasWork) advance activeIndex/UpdateProgress.
                double stageStartPercent = activeIndex / (double)activeStageCount * 100;
                double stageEndPercent = hasWork ? (activeIndex + 1) / (double)activeStageCount * 100 : stageStartPercent;
                string stageStatusText = !string.IsNullOrWhiteSpace(stage.Name) ? $"{_action}: {stage.Name}" : $"Suite {_action}: {_suiteConfig.BuildSettings.Name}";
                if (hasWork)
                {
                    UpdateProgress((int)Math.Round(stageStartPercent), stageStatusText);
                    activeIndex++;
                }

                RunEventsForStage(allEvents, stage.Id, before: true);

                if (executePackage)
                {
                    ExecutePackage(package!, stageStartPercent, stageEndPercent, stageStatusText);
                }

                RunEventsForStage(allEvents, stage.Id, before: false);
            }

            if (_action == SuiteAction.Removal && _suiteConfig.RegistryEvents != null && _suiteConfig.RegistryEvents.Count > 0)
            {
                // Keys are only ever deleted once every registry-removal event in the suite has had a chance
                // to remove its own value(s) - a key emptied out by one event might still hold a value another
                // event (in an earlier or later stage) still needs to remove.
                _log.WriteLog($"Pruning now-empty registry keys left behind by removal", "Execution", Log.Severity.Info);
                foreach (RegExecEvent reg in _suiteConfig.RegistryEvents)
                {
                    reg.PruneEmptyKeysAfterRemoval();
                }
            }

            if (_suiteConfig.ProcClosureEvents.Count > 0)
            {
                _log.WriteLog($"Unblocking executables", "Execution", Log.Severity.Info);
                foreach (ProcClosureExecEvent evt in _suiteConfig.ProcClosureEvents.Where(e => e.Action == ProcAction.StopAndBlock))
                {
                    evt.Action = ProcAction.Unblock;
                    evt.ExecuteEvent();
                }

                // One teardown check for the whole batch above, not one per exe — see ClosureFailsafe.TeardownIfEmpty.
                ClosureFailsafe.TeardownIfEmpty(_log, _suiteConfig.BuildSettings.UpgradeCode.ToString());
            }
        }

        private List<Stage> GetOrderedStages()
        {
            List<Stage> stages;

            if (_suiteConfig.Stages != null && _suiteConfig.Stages.Count > 0)
            {
                stages = new List<Stage>(_suiteConfig.Stages);
            }
            else
            {
                stages = _suiteConfig.Packages.Cast<Stage>().ToList();
            }

            Stage? startStage = stages.FirstOrDefault(s => s.Id == Stage.StartStageId);
            Stage? endStage = stages.FirstOrDefault(s => s.Id == Stage.EndStageId);

            if (startStage != null) stages.Remove(startStage);
            if (endStage != null) stages.Remove(endStage);

            if ((_action == SuiteAction.Removal || _action == SuiteAction.Rollback) && _suiteConfig.BuildSettings.ReverseUninstall)
            {
                stages.Reverse();
                _log.WriteLog("ReverseUninstall is enabled, processing stages in reverse order", "Execution", Log.Severity.Info);
            }

            if (startStage != null) stages.Insert(0, startStage);
            if (endStage != null) stages.Add(endStage);

            return stages;
        }

        private List<EventCore> GetAllEvents()
        {
            List<EventCore> allEvents = new();
            if (_suiteConfig.CertEvents != null) allEvents.AddRange(_suiteConfig.CertEvents);
            if (_suiteConfig.DriverEvents != null) allEvents.AddRange(_suiteConfig.DriverEvents);
            if (_suiteConfig.ProcClosureEvents != null) allEvents.AddRange(_suiteConfig.ProcClosureEvents);
            if (_suiteConfig.EnvironmentEvents != null) allEvents.AddRange(_suiteConfig.EnvironmentEvents);
            if (_suiteConfig.ExecutableEvents != null) allEvents.AddRange(_suiteConfig.ExecutableEvents);
            if (_suiteConfig.ExtensionEvents != null) allEvents.AddRange(_suiteConfig.ExtensionEvents);
            if (_suiteConfig.FileEvents != null) allEvents.AddRange(_suiteConfig.FileEvents);
            if (_suiteConfig.PowerShellEvents != null) allEvents.AddRange(_suiteConfig.PowerShellEvents);
            if (_suiteConfig.RegistryEvents != null) allEvents.AddRange(_suiteConfig.RegistryEvents);
            if (_suiteConfig.ServiceClosureEvents != null) allEvents.AddRange(_suiteConfig.ServiceClosureEvents);
            if (_suiteConfig.ShortcutEvents != null) allEvents.AddRange(_suiteConfig.ShortcutEvents);
            return allEvents;
        }

        // Weight-check only - deliberately ignores each schedule's Condition (unlike RunEventsForStage) so
        // this can be evaluated once up front without triggering rule-set side effects; a stage whose only
        // schedule turns out condition-false at runtime is a rarer, smaller inaccuracy than the alternative
        // of Start/End-style stages eating a full slice of the progress bar for doing nothing.
        private bool StageHasApplicableEvent(List<EventCore> allEvents, Guid stageId)
        {
            foreach (EventCore evt in allEvents)
            {
                foreach (Schedule schedule in evt.Schedules)
                {
                    if (schedule.EventStageId != stageId)
                        continue;

                    if (IsScheduleApplicable(schedule, before: true) || IsScheduleApplicable(schedule, before: false))
                        return true;
                }
            }
            return false;
        }

        private void RunEventsForStage(List<EventCore> allEvents, Guid stageId, bool before)
        {
            foreach (EventCore evt in allEvents)
            {
                foreach (Schedule schedule in evt.Schedules)
                {
                    if (schedule.EventStageId != stageId)
                        continue;

                    if (!IsScheduleApplicable(schedule, before))
                        continue;

                    if (schedule.Condition != null)
                    {
                        RuleResult conditionResult = schedule.Condition.ParseRuleSet();
                        if (!conditionResult.IsMet)
                        {
                            _log.WriteLog($"Event condition not met, skipping. Summary: {conditionResult.Summary}", "Events", Log.Severity.Info);
                            continue;
                        }
                    }

                    ExecuteEvent(evt);
                }
            }
        }

        private bool IsScheduleApplicable(Schedule schedule, bool before)
        {
            Sequence seq = schedule.StageSequence;

            if (before)
            {
                if (seq == Sequence.AlwaysBeforeStage)
                    return true;

                if (_action == SuiteAction.Deployment)
                {
                    if (seq == Sequence.DuringInstallBeforeStage)
                        return true;
                }

                if (_action == SuiteAction.Removal || _action == SuiteAction.Rollback)
                {
                    if (seq == Sequence.DuringRemoveBeforeStage)
                        return true;
                }

                // Dedicated Rollback hooks, e.g. to restore detection for the package a rollback reinstalls -
                // these only fire during Rollback, not a plain Removal.
                if (_action == SuiteAction.Rollback)
                {
                    if (seq == Sequence.DuringRollbackBeforeStage)
                        return true;
                }
            }
            else
            {
                if (seq == Sequence.AlwaysAfterStage)
                    return true;

                if (_action == SuiteAction.Deployment)
                {
                    if (seq == Sequence.DuringInstallAfterStage)
                        return true;
                }

                if (_action == SuiteAction.Removal || _action == SuiteAction.Rollback)
                {
                    if (seq == Sequence.DuringRemoveAfterStage)
                        return true;
                }

                if (_action == SuiteAction.Rollback)
                {
                    if (seq == Sequence.DuringRollbackAfterStage)
                        return true;
                }
            }

            return false;
        }

        private void ExecuteEvent(EventCore evt)
        {
            try
            {
                switch (evt)
                {
                    case ProcClosureExecEvent proc:
                        _log.WriteLog($"Executing Process Closure event: {proc.Name}", "Events", Log.Severity.Info);
                        proc.ExecuteEvent();
                        break;
                    case ServiceClosureExecEvent svc:
                        _log.WriteLog($"Executing Service Closure event: {svc.Name}", "Events", Log.Severity.Info);
                        svc.ExecuteEvent();
                        break;
                    case CertExecEvent cert:
                        _log.WriteLog($"Executing Certificate event", "Events", Log.Severity.Info);
                        cert.ExecuteEvent();
                        break;
                    case DriverExecEvent driver:
                        _log.WriteLog($"Executing Driver event", "Events", Log.Severity.Info);
                        driver.ExecuteEvent();
                        break;
                    case EnvExecEvent env:
                        _log.WriteLog($"Executing Environment Variable event: {env.Name}", "Events", Log.Severity.Info);
                        env.ExecuteEvent();
                        break;
                    case ExecutableExecEvent exe:
                        _log.WriteLog($"Executing Executable event", "Events", Log.Severity.Info);
                        exe.ExecuteEvent();
                        break;
                    case BrowserExExecEvent ext:
                        _log.WriteLog($"Executing Browser Extension event", "Events", Log.Severity.Info);
                        ext.ExecuteEvent();
                        break;
                    case FileExecEvent file:
                        _log.WriteLog($"Executing File event", "Events", Log.Severity.Info);
                        file.ExecuteEvent();
                        break;
                    case PowerShellExecEvent ps:
                        _log.WriteLog($"Executing PowerShell event: {ps.ScriptName}", "Events", Log.Severity.Info);
                        ps.ExecuteEvent();
                        break;
                    case RegExecEvent reg:
                        _log.WriteLog($"Executing Registry event", "Events", Log.Severity.Info);
                        reg.ExecuteEvent();
                        break;
                    case ShortcutExecEvent shortcut:
                        _log.WriteLog($"Executing Shortcut event: {shortcut.Name}", "Events", Log.Severity.Info);
                        shortcut.ExecuteEvent();
                        break;
                    default:
                        _log.WriteLog($"Unknown event type: {evt.GetType().Name}", "Events", Log.Severity.Warning);
                        break;
                }
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Event execution failed ({evt.GetType().Name}): {ex.Message}", "Events", Log.Severity.Error);
                throw;
            }
        }
    }
}
