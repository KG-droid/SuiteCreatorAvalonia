using SuiteCreatorAvalonia.ViewModels;
using System.Collections.Generic;

namespace SuiteCreatorAvalonia.Services
{
    /// <summary>
    /// Scripted guided tours. Steps target live controls by their Help.TourTag,
    /// navigating between pages as they go, so the tour always reflects the current UI.
    /// </summary>
    public static class HelpTours
    {
        public static IReadOnlyList<HelpStep> GettingStarted() => new List<HelpStep>
        {
            new HelpStep
            {
                Title = "Welcome to Suite Creator",
                Text = "Suite Creator builds a single deployable suite that installs multiple applications, along with registry keys, files, certificates, shortcuts, environment variables, scripts and more. " +
                    "This short tour shows you around — use Next to continue, or press Esc to leave at any time.",
            },
            new HelpStep
            {
                Title = "Project toolbar",
                ResolveTarget = () => HelpService.Instance.FindByTourTag("ProjectToolbar"),
                Text = "Manage your Suite project here: open an existing project, save, save a copy, start a new project, or import an already-built suite to edit it. " +
                    "Ctrl+S saves at any time.",
            },
            new HelpStep
            {
                Title = "Navigation pane",
                ResolveTarget = () => HelpService.Instance.FindByTourTag("NavPane"),
                Text = "Each icon is one part of your suite — Packages, Files & Folders, Registry, Certs, Shortcuts, Scripts and more. " +
                    "Hover over the pane to expand it and see the page names. Build and Settings are pinned at the bottom.",
            },
            new HelpStep
            {
                Title = "Packages",
                NavigateTo = typeof(PackageViewModel),
                ResolveTarget = () => HelpService.Instance.FindByTourTag("PageArea"),
                Text = "This is usually where you start: add the applications your suite installs (MSI, MSIX or other installers) and configure how each one is installed and removed.",
            },
            new HelpStep
            {
                Title = "Build",
                NavigateTo = typeof(BuildViewModel),
                ResolveTarget = () => HelpService.Instance.FindByTourTag("PageArea"),
                Text = "When everything is configured, build your suite here. The result is a single executable ready for your deployment tool, " +
                    "along with a detection rule you can copy into it.",
            },
            new HelpStep
            {
                Title = "Help is always here",
                ResolveTarget = () => HelpService.Instance.FindByTourTag("HelpButton"),
                Text = "Whenever you're unsure, open this menu: 'Help on this Page' walks you through the page you're on, " +
                    "and 'What's This?' lets you point at any control to see what it does. That's the tour — enjoy!",
            },
        };
    }
}
