using CommunityToolkit.Mvvm.ComponentModel;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Common.TreeNodes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class PathVarsTextBoxViewModel : ViewModelBase
    {
        private readonly ObservableCollection<FileSystemNode> _treeNodes;

        public ObservableCollection<FileSystemNode> TreeNodes
        {
            get => _treeNodes;
        }

        [ObservableProperty]
        private bool _dontShowUserVars = false;

        [ObservableProperty]
        private string? _placeholderText;

        [ObservableProperty]
        private bool _isAddFileButtonVisible = true;

        [ObservableProperty]
        private Contexts _context = Contexts.System;

        private readonly VariablePathCollection _variablePath = new();
        private bool _suppressCMDUpdated;

        /// <summary>
        /// The command/path as alternating literal and variable segments. The collection instance is
        /// stable for the lifetime of the view model; assigning to this property replaces its contents
        /// (without raising CMDUpdated, so consumers can load silently). The collection is never empty
        /// and always ends with a LiteralText, matching the shape the path parsers expect.
        /// </summary>
        public ObservableCollection<VariableText> VariablePath
        {
            get => _variablePath;
            set
            {
                if (value == null || ReferenceEquals(value, _variablePath)) return;
                _suppressCMDUpdated = true;
                try
                {
                    ReplaceContents(value.Select(v => v.Clone()).ToList());
                }
                finally
                {
                    _suppressCMDUpdated = false;
                }
            }
        }

        public event EventHandler? CMDUpdated;

        public PathVarsTextBoxViewModel() : this(
            new ObservableCollection<FileSystemNode> { new FileSystemNode("VarTextRoot") })
        {
        }

        public PathVarsTextBoxViewModel(ObservableCollection<FileSystemNode> fileTree)
        {
            if (fileTree == null) throw new ArgumentNullException(nameof(fileTree), "The provided TreeNode is null, cannot create the PathVarsTextBoxViewModel");
            _treeNodes = fileTree;
            _variablePath.Add(new LiteralText(string.Empty));
            _variablePath.CollectionChanged += (s, e) =>
            {
                // Count == 0 is the transient state mid-Clear; VariablePathCollection reseeds right after
                if (!_suppressCMDUpdated && _variablePath.Count > 0)
                    CMDUpdated?.Invoke(this, EventArgs.Empty);
            };
        }

        public void TriggerCMDChanged()
        {
            CMDUpdated?.Invoke(this, EventArgs.Empty);
        }

        public bool HasCommand()
        {
            return _variablePath.Any(v => !(v is LiteralText lit) || !string.IsNullOrWhiteSpace(lit.Value));
        }

        /// <summary>
        /// Called by PathVarsTextBoxView after each user edit: replaces the contents with the parsed
        /// document and raises a single CMDUpdated.
        /// </summary>
        internal void SetFromEditor(IReadOnlyList<VariableText> newPath)
        {
            _suppressCMDUpdated = true;
            try
            {
                ReplaceContents(newPath);
            }
            finally
            {
                _suppressCMDUpdated = false;
            }
            CMDUpdated?.Invoke(this, EventArgs.Empty);
        }

        private void ReplaceContents(IReadOnlyList<VariableText> items)
        {
            _variablePath.Clear();
            foreach (VariableText item in items)
                _variablePath.Add(item);
        }
    }

    internal class VariablePathCollection : ObservableCollection<VariableText>
    {
        protected override void ClearItems()
        {
            base.ClearItems();
            base.InsertItem(0, new LiteralText(string.Empty));
        }

        protected override void InsertItem(int index, VariableText item)
        {
            if (index == Count && Count > 0 && item is LiteralText lit && this[Count - 1] is LiteralText trailing)
            {
                if (lit.Value.Length == 0) return;
                base.SetItem(Count - 1, new LiteralText(trailing.Value + lit.Value));
                return;
            }
            base.InsertItem(index, item);
            if (index == Count - 1 && item is not LiteralText)
                base.InsertItem(Count, new LiteralText(string.Empty));
        }

        protected override void RemoveItem(int index)
        {
            base.RemoveItem(index);
            if (Count == 0)
                base.InsertItem(0, new LiteralText(string.Empty));
        }
    }
}
