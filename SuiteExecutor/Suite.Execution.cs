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

            for (int i = 0; i < stages.Count; i++)
            {
                Stage stage = stages[i];
                _log.WriteLog($"--- Stage {i + 1}/{stages.Count}: {stage.Name} (Id: {stage.Id}) ---", "Execution", Log.Severity.Info);

                int stagePercentage = stages.Count > 0 ? (int)Math.Round(i / (double)stages.Count * 100) : 0;
                UpdateProgress(stagePercentage, !string.IsNullOrWhiteSpace(stage.Name) ? $"{_action}: {stage.Name}" : $"Suite {_action}: {_suiteConfig.BuildSettings.Name}");

                PackageBase? package = ResolvePackage(stage);

                if (package != null && !ShouldPackageExecute(package))
                {
                    continue;
                }

                RunEventsForStage(allEvents, stage.Id, before: true);

                if (package != null) // Not every stage is a package (SuiteStart/End for example), but if it is, execute it
                {
                    ExecutePackage(package);
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
                    // Package Remove events should also run before the package installs
                    if (seq == Sequence.DuringRemoveBeforeStage)
                        return true;
                }

                if (_action == SuiteAction.Removal || _action == SuiteAction.Rollback)
                {
                    if (seq == Sequence.DuringRemoveBeforeStage)
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
