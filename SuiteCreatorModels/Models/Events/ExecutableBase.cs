using SuiteCreatorAvalonia.Models.Common;

namespace SuiteCreatorAvalonia.Models.Events
{
    public partial class ExecutableBase : EventCore
    {
        public List<VariableText>? Command { get; set; }
        public List<VariableText>? WorkingDIR { get; set; }
        public bool ContinueOnError { get; set; } = false;
        public bool ContinueOnNotFound { get; set; } = false;
        public bool SecureParams { get; set; } = false;
        public override ExecutableBase Clone()
        {
            return new ExecutableBase
            {
                Id = Id,
                Schedules = Schedules.ConvertAll(s => s.Clone()),
                Command = Command?.Select(c => c.Clone()).ToList(),
                WorkingDIR = WorkingDIR?.Select(w => w.Clone()).ToList(),
                ContinueOnError = ContinueOnError,
                ContinueOnNotFound = ContinueOnNotFound,
                SecureParams = SecureParams
            };
        }
        public override void UpdateFrom(EventCore ecore)
        {
            if (ecore is not ExecutableBase exec) return;
            Id = exec.Id;
            Schedules = exec.Schedules.ConvertAll(s => s.Clone());
            Command = exec.Command?.Select(c => c.Clone()).ToList();
            WorkingDIR = exec.WorkingDIR?.Select(w => w.Clone()).ToList();
            ContinueOnError = exec.ContinueOnError;
            ContinueOnNotFound = exec.ContinueOnNotFound;
            SecureParams = exec.SecureParams;
        }
        public override string? Validate()
        {
            if (Command == null || Command.Count == 0 || Command.All(c => string.IsNullOrWhiteSpace(c?.GetValue())))
            {
                return "Command must be specified for Executable event.";
            }
            return null;
        }
    }
}
