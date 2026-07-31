using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace SuiteCreatorAvalonia.ViewModels
{
    public class RegexMatchItem
    {
        public int Index { get; init; }
        public int Length { get; init; }
        public string Value { get; init; } = string.Empty;
    }

    // Sample text is scratch input for testing only - never persisted back to the rule. Pattern
    // mirrors Rule.EvaluateStringComparator's case-insensitive matching so results here reflect
    // what the rule will actually do at detection time.
    internal partial class RegexTesterWindowViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string? _pattern;

        [ObservableProperty]
        private string? _sampleText;

        [ObservableProperty]
        private bool _isMatch;

        [ObservableProperty]
        private string? _errorMessage;

        [ObservableProperty]
        private string _resultSummary = "Enter a pattern and some sample text to test against";

        [ObservableProperty]
        private ObservableCollection<RegexMatchItem> _matches = new();

        partial void OnPatternChanged(string? value) => Evaluate();
        partial void OnSampleTextChanged(string? value) => Evaluate();

        private void Evaluate()
        {
            Matches.Clear();
            ErrorMessage = null;

            if (string.IsNullOrEmpty(Pattern))
            {
                IsMatch = false;
                ResultSummary = "Enter a pattern to test";
                return;
            }

            Regex regex;
            try
            {
                regex = new Regex(Pattern, RegexOptions.IgnoreCase);
            }
            catch (ArgumentException ex)
            {
                IsMatch = false;
                ErrorMessage = $"Invalid regular expression: {ex.Message}";
                ResultSummary = "Pattern is not valid";
                return;
            }

            string sample = SampleText ?? string.Empty;
            IsMatch = regex.IsMatch(sample);

            foreach (Match match in regex.Matches(sample))
            {
                Matches.Add(new RegexMatchItem { Index = match.Index, Length = match.Length, Value = match.Value });
            }

            ResultSummary = IsMatch
                ? $"Matches - {Matches.Count} match{(Matches.Count == 1 ? string.Empty : "es")} found"
                : "No match";
        }
    }
}
