using SuiteCreatorAvalonia.Enums;

namespace SuiteCreatorAvalonia.Models.Events
{
    public class BrowserExt : EventCoreWithPermanence
    {
        public ExtAction? Action { get; set; }
        public string? ExtensionId { get; set; }
        public BrowserType? Browser { get; set; }
        public BrowserExtensionSource? Source { get; set; }
        public string? ExtPath { get; set; }
        public override BrowserExt Clone()
        {
            return new BrowserExt
            {
                Id = Id,
                Action = Action,
                ExtensionId = ExtensionId,
                Browser = Browser,
                Source = Source,
                ExtPath = ExtPath,
                Schedules = Schedules.ConvertAll(s => s.Clone()),
            };
        }
        public override void UpdateFrom(EventCore ecore)
        {
            if (ecore is not BrowserExt browserExt) return;
            Id = browserExt.Id;
            Action = browserExt.Action;
            ExtensionId = browserExt.ExtensionId;
            Browser = browserExt.Browser;
            Source = browserExt.Source;
            ExtPath = browserExt.ExtPath;
            Schedules = browserExt.Schedules.ConvertAll(s => s.Clone());
        }
        public override string? Validate()
        {
            if (Action == null)
            {
                return "Action must be specified for BrowserExt event.";
            }
            if (string.IsNullOrWhiteSpace(ExtensionId))
            {
                return "ExtensionId must be specified for BrowserExt event.";
            }
            if (Browser == null)
            {
                return "Browser must be specified for BrowserExt event.";
            }
            if (Source == null)
            {
                return "Source must be specified for BrowserExt event.";
            }
            if (string.IsNullOrWhiteSpace(ExtensionId) && string.IsNullOrWhiteSpace(ExtPath))
            {
                return "You must specify an ExtensionId or File Path";
            }
            if (!string.IsNullOrWhiteSpace(ExtPath) && !Path.Exists(ExtPath))
            {
                return $"Extension File path: {ExtPath} does not exist";
            }
            return null;
        }
        public override void Reverse()
        {
            if (IsPermanent) return;
            // Already an Uninstall: nothing to reverse, so leave the schedule alone (it should only run on the side it was configured for).
            if (Action == ExtAction.Uninstall) return;
            base.Reverse();
            Action = ExtAction.Uninstall;
        }
    }
}
