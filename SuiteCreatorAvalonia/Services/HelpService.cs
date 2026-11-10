using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SuiteCreatorAvalonia.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SuiteCreatorAvalonia.Services
{
    /// <summary>One step of a help walkthrough or guided tour.</summary>
    public class HelpStep
    {
        public string? Title { get; init; }
        public required string Text { get; init; }

        /// <summary>ViewModel type to navigate to before showing this step (guided tours only).</summary>
        public Type? NavigateTo { get; init; }

        /// <summary>Resolves the control to spotlight. Null (or resolving null) shows a centered callout with no spotlight.</summary>
        public Func<Control?>? ResolveTarget { get; init; }
    }

    /// <summary>
    /// Central registry of help-annotated controls (see <see cref="Help"/>) and the
    /// engine that drives spotlight walkthroughs, guided tours and inspect mode
    /// through the <see cref="HelpOverlayView"/> layered over the main window.
    /// </summary>
    public class HelpService
    {
        public static HelpService Instance { get; } = new HelpService();

        private readonly List<Control> _targets = new();
        private IReadOnlyList<HelpStep>? _steps;
        private int _stepIndex = -1;

        /// <summary>The overlay hosted in MainWindow; set by MainWindow on startup.</summary>
        public HelpOverlayView? Overlay { get; set; }

        /// <summary>The control hosting the current page's view; set by MainView on load.</summary>
        public Control? PageHost { get; set; }

        /// <summary>Navigates the main content to a page ViewModel type; set by MainViewModel.</summary>
        public Action<Type>? NavigateToPage { get; set; }

        public void Register(Control control)
        {
            if (!_targets.Contains(control))
                _targets.Add(control);
        }

        public void Unregister(Control control) => _targets.Remove(control);

        public Control? FindByTourTag(string tag) =>
            _targets.FirstOrDefault(c => Help.GetTourTag(c) == tag && c.IsEffectivelyVisible)
            ?? _targets.FirstOrDefault(c => Help.GetTourTag(c) == tag);

        /// <summary>All visible annotated controls on the current page, in walkthrough order.</summary>
        private List<Control> CurrentPageTargets()
        {
            Control? host = PageHost;
            if (host == null) return new List<Control>();
            return _targets
                .Where(c => !string.IsNullOrWhiteSpace(Help.GetText(c))
                    && c.IsEffectivelyVisible
                    && c.GetVisualAncestors().Contains(host))
                .OrderBy(c => Help.GetOrder(c))
                .ThenBy(c => RowOf(c, host))
                .ThenBy(c => PositionOf(c, host).X)
                .ToList();
        }

        private static Avalonia.Point PositionOf(Control control, Control host) =>
            control.TranslatePoint(new Avalonia.Point(0, 0), host) ?? new Avalonia.Point(0, 0);

        // Bucket Y into rows so controls side by side read left-to-right despite tiny offsets
        private static int RowOf(Control control, Control host) =>
            (int)(PositionOf(control, host).Y / 24);

        /// <summary>Walks the help-annotated controls on the current page, page summary first.</summary>
        public async Task StartPageHelpAsync()
        {
            List<HelpStep> steps = new List<HelpStep>();

            Control? summaryControl = PageHost == null ? null : _targets.FirstOrDefault(c =>
                !string.IsNullOrWhiteSpace(Help.GetPageSummary(c))
                && c.IsEffectivelyVisible
                && c.GetVisualAncestors().Contains(PageHost));
            if (summaryControl != null)
            {
                steps.Add(new HelpStep
                {
                    Title = Help.GetTitle(summaryControl) ?? "About this page",
                    Text = Help.GetPageSummary(summaryControl)!,
                    ResolveTarget = () => summaryControl,
                });
            }

            // Repeating controls (one per list/card item - e.g. a per-row action button) can end up
            // annotated with the exact same Title/Text on every instance. Walking through the identical
            // step once per instance would be repetitive and confusing, so only the first instance of any
            // given (Title, Text) pair becomes a step; that first control is still what gets spotlighted.
            HashSet<(string? Title, string Text)> seenSteps = new();
            foreach (Control target in CurrentPageTargets())
            {
                string? title = Help.GetTitle(target);
                string text = Help.GetText(target)!;
                if (!seenSteps.Add((title, text)))
                    continue;

                Control captured = target;
                steps.Add(new HelpStep
                {
                    Title = title,
                    Text = text,
                    ResolveTarget = () => captured,
                });
            }

            if (steps.Count == 0)
            {
                steps.Add(new HelpStep
                {
                    Title = "No help here yet",
                    Text = "This page doesn't have detailed help yet. Hovering over most controls shows a tooltip describing what they do.",
                });
            }

            await StartTourAsync(steps);
        }

        public async Task StartTourAsync(IReadOnlyList<HelpStep> steps)
        {
            if (Overlay == null || steps.Count == 0) return;
            _steps = steps;
            await ShowStepAsync(0);
        }

        private async Task ShowStepAsync(int index)
        {
            if (_steps == null || Overlay == null) return;
            HelpStep step = _steps[index];
            _stepIndex = index;

            try
            {
                if (step.NavigateTo != null)
                {
                    NavigateToPage?.Invoke(step.NavigateTo);
                    await Task.Delay(350); // let the page transition settle
                }

                Control? target = null;
                if (step.ResolveTarget != null)
                {
                    // The target may still be materialising after a page navigation; poll briefly
                    for (int attempt = 0; attempt < 20; attempt++)
                    {
                        target = step.ResolveTarget();
                        if (target != null && target.IsEffectivelyVisible && target.Bounds.Width > 0)
                            break;
                        await Task.Delay(80);
                    }
                }

                if (target != null)
                {
                    // The target may be inside a ScrollViewer and currently scrolled out of view (e.g. an
                    // item further down a long page); scroll it into view first so the spotlight and callout
                    // always land somewhere the user can actually see, rather than off the visible window.
                    target.BringIntoView();
                    await Task.Delay(80);
                }

                Overlay.ShowStep(target, step.Title, step.Text, index, _steps.Count);
            }
            catch (Exception ex)
            {
                // A walkthrough step is never worth crashing over, and leaving the dimmed overlay up with
                // no callout would trap the user behind it. Falling back to a centered, un-spotlit callout
                // keeps the walkthrough usable (Next/Back/Close still work) even if a target couldn't be
                // resolved or positioned.
                AppLog.Error($"Help walkthrough step {index} failed; showing it without a spotlight", ex, "Help");
                Overlay.ShowStep(null, step.Title, step.Text, index, _steps.Count);
            }
        }

        public async Task NextAsync()
        {
            if (_steps == null) return;
            if (_stepIndex + 1 < _steps.Count)
                await ShowStepAsync(_stepIndex + 1);
            else
                End();
        }

        public async Task BackAsync()
        {
            if (_steps != null && _stepIndex > 0)
                await ShowStepAsync(_stepIndex - 1);
        }

        public void End()
        {
            _steps = null;
            _stepIndex = -1;
            Overlay?.HideOverlay();
        }

        /// <summary>"What's this?" mode: hover any annotated control to see its help.</summary>
        public void StartInspect()
        {
            _steps = null;
            _stepIndex = -1;
            Overlay?.StartInspect();
        }

        /// <summary>
        /// Finds the smallest annotated control under a point (in overlay coordinates) for inspect mode.
        /// Only controls with Help.Text count — page summaries would otherwise match the whole page.
        /// </summary>
        public Control? HitTestHelpTarget(Avalonia.Point overlayPoint)
        {
            if (Overlay == null) return null;
            Control? best = null;
            double bestArea = double.MaxValue;
            foreach (Control c in _targets)
            {
                if (string.IsNullOrWhiteSpace(Help.GetText(c)) || !c.IsEffectivelyVisible) continue;
                Avalonia.Point? p = c.TranslatePoint(new Avalonia.Point(0, 0), Overlay);
                if (p == null) continue;
                Avalonia.Rect rect = new Avalonia.Rect(p.Value, c.Bounds.Size);
                double area = rect.Width * rect.Height;
                if (rect.Contains(overlayPoint) && area < bestArea)
                {
                    best = c;
                    bestArea = area;
                }
            }
            return best;
        }
    }
}
