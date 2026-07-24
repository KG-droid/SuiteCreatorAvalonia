using System;

namespace SuiteCreatorAvalonia.Models.Common
{
    public class SuiteBuiltEventArgs : EventArgs
    {
        public string? BuildPath { get; }
        public DateTime? BuildTime { get; }

        public SuiteBuiltEventArgs(string? buildPath, DateTime? buildTime)
        {
            BuildPath = buildPath;
            BuildTime = buildTime;
        }
    }
}
