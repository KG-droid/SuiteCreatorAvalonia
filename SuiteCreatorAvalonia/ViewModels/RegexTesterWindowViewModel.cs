using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using SuiteCreatorAvalonia.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;

namespace SuiteCreatorAvalonia.ViewModels
{
    // Sample text is scratch input for testing only - never persisted back to the rule. Pattern
    // mirrors Rule.EvaluateStringComparator's case-insensitive matching so results here reflect
    // what the rule will actually do at detection time.
    internal partial class RegexTesterWindowViewModel : ViewModelBase
    {
        [ObservableProperty]
        private TextDocument _patternDoc = new();

        [ObservableProperty]
        private TextDocument _sampleDoc = new();

        // Plain string wrapper over PatternDoc, for callers (e.g. RuleCardViewModel) that just
        // want to get/set the pattern without depending on AvaloniaEdit.Document.
        public string? Pattern
        {
            get => PatternDoc.Text;
            set => PatternDoc.Text = value ?? string.Empty;
        }

        [ObservableProperty]
        private bool _isMatch;

        [ObservableProperty]
        private string? _errorMessage;

        [ObservableProperty]
        private string _resultSummary = "Enter a pattern and some sample text to test against";

        private List<(int Start, int Length)> _matchRanges = new();
        public IReadOnlyList<(int Start, int Length)> MatchRanges => _matchRanges;

        // Zero-width matches (e.g. a lookahead like (?=llo)) have no characters to paint a
        // background over, so they're tracked separately and drawn as a thin marker instead.
        private List<int> _zeroWidthMatchOffsets = new();
        public IReadOnlyList<int> ZeroWidthMatchOffsets => _zeroWidthMatchOffsets;

        private List<RegexToken> _patternTokens = new();
        public IReadOnlyList<RegexToken> PatternTokens => _patternTokens;

        [ObservableProperty]
        private string? _snippetFilter;

        [ObservableProperty]
        private ObservableCollection<RegexSnippet> _filteredSnippets = new(RegexSnippetLibrary.All);

        // Raised whenever MatchRanges changes, so the view can redraw the highlighted sample text.
        public event Action? MatchesUpdated;

        // Raised whenever PatternTokens changes, so the view can redraw the colorized pattern text.
        public event Action? PatternTokensUpdated;

        public RegexTesterWindowViewModel()
        {
            SampleDoc.TextChanged += (s, e) => Evaluate();
            PatternDoc.TextChanged += (s, e) =>
            {
                _patternTokens = RegexPatternTokenizer.Tokenize(PatternDoc.Text ?? string.Empty);
                PatternTokensUpdated?.Invoke();
                Evaluate();
            };
        }

        partial void OnSnippetFilterChanged(string? value)
        {
            IEnumerable<RegexSnippet> source = RegexSnippetLibrary.All;
            if (!string.IsNullOrWhiteSpace(value))
            {
                source = source.Where(s =>
                    s.Token.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                    s.Name.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                    s.Description.Contains(value, StringComparison.OrdinalIgnoreCase));
            }
            FilteredSnippets = new ObservableCollection<RegexSnippet>(source);
        }

        private void Evaluate()
        {
            _matchRanges.Clear();
            _zeroWidthMatchOffsets.Clear();
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
                else
                {
                    _zeroWidthMatchOffsets.Add(match.Index);
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
