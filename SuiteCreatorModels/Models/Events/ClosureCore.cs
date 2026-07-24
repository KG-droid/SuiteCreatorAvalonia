using System;
using System.Collections.Generic;
using System.Linq;

namespace SuiteCreatorAvalonia.Models.Events
{
    public abstract class ClosureCore : EventCore
    {
        private static readonly char[] NameSeparators = { ';', ',' };

        public string? Name { get; set; }

        public ClosureCore()
        {
        }

        public ClosureCore(string name)
        {
            Name = name;
        }

        // Name can hold a single entry or a ;/,-delimited list, letting one card close
        // several processes/services (with the same action) instead of needing a card each.
        public IEnumerable<string> GetNames()
        {
            if (string.IsNullOrWhiteSpace(Name)) return Enumerable.Empty<string>();
            return Name.Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Select(n => n.Trim())
                .Where(n => n.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        public override abstract EventCore Clone();

        public override abstract void UpdateFrom(EventCore other);
    }
}
