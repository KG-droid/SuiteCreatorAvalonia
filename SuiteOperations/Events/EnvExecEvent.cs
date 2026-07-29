using Logger;
using SuiteCreatorAvalonia.Enums;
using Environment = SuiteCreatorAvalonia.Models.Events.Environment;

namespace SuiteOperations.Events
{
    public partial class EnvExecEvent : Environment
    {
        private Log _log;

        public EnvExecEvent(Log log)
        {
            _log = log;
        }

        public EnvExecEvent() { }

        public void SetLog(Log log)
        {
            _log = log;
        }

        public void ExecuteEvent()
        {
            switch (Behaviour)
            {
                case Environmentbehaviour.Append:
                    AppendEnvironmentVariable();
                    break;
                case Environmentbehaviour.Replace:
                    ReplaceEnvironmentVariable();
                    break;
                case Environmentbehaviour.Prepend:
                    PrependEnvironmentVariable();
                    break;
                case Environmentbehaviour.Remove:
                    RemoveEnvironmentVariable();
                    break;
                default:
                    _log.WriteLog($"Unknown environment variable behaviour: {Behaviour}");
                    throw new InvalidOperationException($"Unknown environment variable behaviour: {Behaviour}");
            }
        }

        private void AppendEnvironmentVariable()
        {
            _log.WriteLog($"Appending: {Value}, to Environment Variable: {Name}.");
            if (string.IsNullOrEmpty(Name) || string.IsNullOrEmpty(Value))
            {
                _log.WriteLog("Name or Value is null or empty. Append aborted.", "EnvExecEvent", Log.Severity.Error);
                throw new InvalidOperationException("Name or Value is null or empty. Append aborted.");
            }
            string current = System.Environment.GetEnvironmentVariable(Name, EnvironmentVariableTarget.Machine) ?? string.Empty;
            char sep = (char)Separator;
            List<string> values = current.Split(sep, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (!values.Contains(Value))
            {
                if (!string.IsNullOrEmpty(current))
                    current += sep;
                current += Value;
                System.Environment.SetEnvironmentVariable(Name, current, EnvironmentVariableTarget.Machine);
                _log.WriteLog("Environment variable appended successfully.");
            }
            else
            {
                _log.WriteLog("Value already present in environment variable.", "EnvExecEvent", Log.Severity.Info);
            }
        }

        private void ReplaceEnvironmentVariable()
        {
            _log.WriteLog($"Replacing Environment Variable: {Name}, with new value: {Value}.");
            if (string.IsNullOrEmpty(Name))
            {
                _log.WriteLog("Name is null or empty. Replace aborted.", "EnvExecEvent", Log.Severity.Error);
                throw new InvalidOperationException("Name is null or empty. Replace aborted.");
            }
            System.Environment.SetEnvironmentVariable(Name, Value, EnvironmentVariableTarget.Machine);
            _log.WriteLog("Environment variable replaced successfully.");
        }

        private void PrependEnvironmentVariable()
        {
            _log.WriteLog($"Prepending: {Value}, to Environment Variable: {Name}.");
            if (string.IsNullOrEmpty(Name) || string.IsNullOrEmpty(Value))
            {
                _log.WriteLog("Name or Value is null or empty. Prepend aborted.", "EnvExecEvent", Log.Severity.Error);
                throw new InvalidOperationException("Name or Value is null or empty. Prepend aborted.");
            }
            string current = System.Environment.GetEnvironmentVariable(Name, EnvironmentVariableTarget.Machine) ?? string.Empty;
            char sep = (char)Separator;
            List<string> values = current.Split(sep, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (!values.Contains(Value))
            {
                string newValue = string.IsNullOrEmpty(current) ? Value : Value + sep + current;
                System.Environment.SetEnvironmentVariable(Name, newValue, EnvironmentVariableTarget.Machine);
                _log.WriteLog("Environment variable prepended successfully.");
            }
            else
            {
                _log.WriteLog("Value already present in environment variable.", "EnvExecEvent", Log.Severity.Info);
            }
        }

        private void RemoveEnvironmentVariable()
        {
            _log.WriteLog($"Removing Environment Variable: {Name}");
            if (string.IsNullOrEmpty(Name))
            {
                _log.WriteLog("Name is null or empty. Remove aborted.", "EnvExecEvent", Log.Severity.Error);
                throw new InvalidOperationException("Name is null or empty. Remove aborted.");
            }
            if (string.IsNullOrEmpty(Value))
            {
                System.Environment.SetEnvironmentVariable(Name, null, EnvironmentVariableTarget.Machine);
                _log.WriteLog("Environment variable has been removed.");
            }
            else 
            {
                RemoveEnvironmentVariableValue();
            }
        }

        private void RemoveEnvironmentVariableValue()
        {
            _log.WriteLog($"Removing: {Value}, from Environment Variable: {Name}.");
            if (string.IsNullOrEmpty(Name) || string.IsNullOrEmpty(Value))
            {
                _log.WriteLog("Name or Value is null or empty. Remove value aborted.", "EnvExecEvent", Log.Severity.Warning);
                return;
            }
            var current = System.Environment.GetEnvironmentVariable(Name, EnvironmentVariableTarget.Machine) ?? string.Empty;
            var sep = (char)Separator;
            var values = current.Split(sep, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (values.Remove(Value))
            {
                if (values.Count == 0)
                {
                    System.Environment.SetEnvironmentVariable(Name, null, EnvironmentVariableTarget.Machine);
                    _log.WriteLog("Environment variable value has been removed, and variable was left empty, so it has been fully removed.");
                }
                else
                {
                    var newValue = string.Join(sep, values);
                    System.Environment.SetEnvironmentVariable(Name, newValue, EnvironmentVariableTarget.Machine);
                    _log.WriteLog("Environment variable value has been removed.");
                }
            }
            else
            {
                _log.WriteLog("Value not found in environment variable.", "EnvExecEvent", Log.Severity.Info);
            }
        }
    }
}
