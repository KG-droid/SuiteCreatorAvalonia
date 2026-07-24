using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DialogHostAvalonia;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SuiteCreatorAvalonia.Services
{
    public class DialogManager
    {
        private static readonly Dictionary<object, Visual> RegistrationMapper =
            new Dictionary<object, Visual>();

        static DialogManager()
        {
            RegisterProperty.Changed.AddClassHandler<Visual>(RegisterChanged);
        }

        private static void RegisterChanged(Visual sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (sender is null)
            {
                throw new InvalidOperationException("The DialogManager can only be registered on a Visual");
            }

            // Unregister any old registered context
            if (e.OldValue != null)
            {
                RegistrationMapper.Remove(e.OldValue);
            }

            // Register any new context
            if (e.NewValue != null && !RegistrationMapper.ContainsKey(e.NewValue))
            {
                RegistrationMapper.Add(e.NewValue, sender);
            }
        }

        /// <summary>
        /// This property handles the registration of Views and ViewModel
        /// </summary>
        public static readonly AttachedProperty<object?> RegisterProperty = AvaloniaProperty.RegisterAttached<DialogManager, Visual, object?>(
            "Register");

        /// <summary>
        /// Accessor for Attached property <see cref="RegisterProperty"/>.
        /// </summary>
        public static void SetRegister(AvaloniaObject element, object value)
        {
            element.SetValue(RegisterProperty, value);
        }

        /// <summary>
        /// Accessor for Attached property <see cref="RegisterProperty"/>.
        /// </summary>
        public static object? GetRegister(AvaloniaObject element)
        {
            return element.GetValue(RegisterProperty);
        }

        /// <summary>
        /// Gets the associated <see cref="Visual"/> for a given context. Returns null, if none was registered
        /// </summary>
        /// <param name="context">The context to lookup</param>
        /// <returns>The registered Visual for the context or null if none was found</returns>
        public static Visual? GetVisualForContext(object context)
        {
            return RegistrationMapper.TryGetValue(context, out var result) ? result : null;
        }

        /// <summary>
        /// Gets the parent <see cref="TopLevel"/> for the given context. Returns null, if no TopLevel was found
        /// </summary>
        /// <param name="context">The context to lookup</param>
        /// <returns>The registered TopLevel for the context or null if none was found</returns>
        public static TopLevel? GetTopLevelForContext(object context)
        {
            return TopLevel.GetTopLevel(GetVisualForContext(context));
        }
    }

    /// <summary>
    /// A helper class to manage dialogs via extension methods. Add more on your own
    /// </summary>
    public static class DialogHelper
    {
        /// <summary>
        /// Shows an open file dialog for a registered context, most likely a ViewModel
        /// </summary>
        /// <param name="context">The context</param>
        /// <param name="title">The dialog title or a default is null</param>
        /// <param name="selectMany">Is selecting many files allowed?</param>
        /// <returns>An array of file names</returns>
        /// <exception cref="ArgumentNullException">if context was null</exception>
        public static async Task<IEnumerable<string>?> OpenFileDialogAsync(this object? context, FilePickerOpenOptions filePickerOpenOptions)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            // lookup the TopLevel for the context
            var topLevel = DialogManager.GetTopLevelForContext(context);

            if (topLevel != null)
            {
                // Open the file dialog
                var storageFiles = await topLevel.StorageProvider.OpenFilePickerAsync(filePickerOpenOptions);

                // return the result
                return storageFiles.Select(s => s.Path.LocalPath);
            }
            return null;
        }

        /// <summary>
        /// Shows an save file dialog for a registered context, most likely a ViewModel
        /// </summary>
        /// <param name="context">The context</param>
        /// <param name="title">The dialog title or a default is null</param>
        /// <param name="selectMany">Is selecting many files allowed?</param>
        /// <returns>An array of file names</returns>
        /// <exception cref="ArgumentNullException">if context was null</exception>
        public static async Task<string?> SaveFileDialogAsync(this object? context, FilePickerSaveOptions filePickerSaveOptions)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            // lookup the TopLevel for the context
            var topLevel = DialogManager.GetTopLevelForContext(context);

            if (topLevel != null)
            {
                // Open the file dialog
                var storageFiles = await topLevel.StorageProvider.SaveFilePickerAsync(filePickerSaveOptions);

                // return the result
                return storageFiles?.Path.LocalPath;
            }
            return null;
        }

        /// <summary>
        /// Shows an open folder dialog for a registered context, most likely a ViewModel
        /// </summary>
        /// <param name="context">The context</param>
        /// <param name="title">The dialog title or a default is null</param>
        /// <param name="selectMany">Is selecting many files allowed?</param>
        /// <returns>An array of file names</returns>
        /// <exception cref="ArgumentNullException">if context was null</exception>
        public static async Task<IEnumerable<string>?> OpenFolderDialogAsync(this object? context, FolderPickerOpenOptions folderPickerOpenOptions)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            // lookup the TopLevel for the context
            var topLevel = DialogManager.GetTopLevelForContext(context);

            if (topLevel != null)
            {
                // Open the file dialog
                var storageFolders = await topLevel.StorageProvider.OpenFolderPickerAsync(folderPickerOpenOptions);

                // return the result
                return storageFolders.Select(s => s.Path.LocalPath);
            }
            return null;
        }

        /// <summary>
        /// Shows a modal dialog using DialogHost for a registered context.
        /// </summary>
        /// <param name="context">The context (usually a ViewModel)</param>
        /// <param name="dialogContent">The dialog content (View or ViewModel)</param>
        /// <param name="identifier">DialogHost identifier (default: "MainDialogHost")</param>
        /// <returns>Result from DialogHost.Show</returns>
        public static async Task<object?> ShowDialogAsync(this object? context, object dialogContent, string identifier = "MainDialogHost")
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (DialogHost.IsDialogOpen(identifier))
            {
                DialogHost.Close(identifier);
                // Allow any in-progress close/open transition to settle before opening
                await Task.Delay(150);
            }
            return await DialogHost.Show(dialogContent, identifier);
        }

        /// <summary>
        /// Closes the modal dialog for the given DialogHost identifier.
        /// </summary>
        /// <param name="context">The context (usually a ViewModel, not used but for API consistency)</param>
        /// <param name="identifier">DialogHost identifier (default: "MainDialogHost")</param>
        public static void CloseDialog(this object? context, string identifier = "MainDialogHost")
        {
            if (DialogHost.IsDialogOpen(identifier))
            {
                DialogHost.Close(identifier);
            }
        }
    }
}
