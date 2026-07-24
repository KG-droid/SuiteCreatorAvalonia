namespace SuiteCreatorAvalonia.Models.Common
{
    public enum ActionType
    {
        Continue,
        Abort,
        RestartImmediate,
        RestartDelayed,
    }

    public class ReturnCode
    {
        public ActionType Action { get; set; }
        public int Code { get; set; }
        public ReturnCode Clone()
        {
            return new ReturnCode
            {
                Action = Action,
                Code = Code
            };
        }

        public void UpdateFrom(ReturnCode returnCode)
        {
            Action = returnCode.Action;
            Code = returnCode.Code;
        }
    }
}
