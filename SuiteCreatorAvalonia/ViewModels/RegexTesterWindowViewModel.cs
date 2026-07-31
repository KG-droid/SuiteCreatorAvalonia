using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SuiteCreatorAvalonia.ViewModels
{
    // Sample text is scratch input for testing only - never persisted back to the rule. Pattern
    // mirrors Rule.EvaluateStringComparator's case-insensitive matching so results here reflect
    // what the rule will actually do at detection time.
    internal partial class RegexTesterWindowViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string? _pattern;

        [ObservableProperty]
        private TextDocument _sampleDoc = new();

        [ObservableProperty]
        private bool _isMatch;

        [ObservableProperty]
        private string? _errorMessage;

        [ObservableProperty]
        private string _resultSummary = "Enter a pattern and some sample text to test against";

        private List<(int Start, int Length)> _matchRanges = new();
        public IReadOnlyList<(int Start, int Length)> MatchRanges => _matchRanges;

        // Raised whenever MatchRanges changes, so the view can redraw the highlighted sample text.
        public event Action? MatchesUpdated;

        public RegexTesterWindowViewModel()
        {
            SampleDoc.TextChanged += (s, e) => Evaluate();
        }

        partial void OnPatternChanged(string? value) => Evaluate();

        private void Evaluate()
        {
            _matchRanges.Clear();
            ErrorMessage = null;

            if (string.IsNullOrEmpty(Pattern))
            {
                IsMatch = false;
                ResultSummary = "Enter a pattern to test";
                MatchesUpdated?.Invoke();
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
                MatchesUpdated?.Invoke();
                return;
            }

            string sample = SampleDoc.Text ?? string.Empty;
            int count = 0;
            foreach (Match match in regex.Matches(sample))
            {
                count++;
                if (match.Length > 0)
                {
                    _matchRanges.Add((match.Index, match.Length));
                }
            }
            IsMatch = count > 0;

            ResultSummary = IsMatch
                ? $"Matches - {count} match{(count == 1 ? string.Empty : "es")} found"
                : "No match";
            MatchesUpdated?.Invoke();
        }
    }
}
