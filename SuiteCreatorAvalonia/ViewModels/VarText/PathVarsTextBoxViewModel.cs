using AvaloniaEdit.Utils;
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
        private ObservableCollection<FileSystemNode> _treeNodes;

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

        public ObservableCollection<VariableText> VariablePath
        {
            get
            {
                var clone = new ObservableCollection<VariableText>(InternalVariablePath.Select(cmd => cmd.Clone()));
                // Add event handler to maintain the CMDUpdated, since when Adding to the collect it calls get not set
                clone.CollectionChanged += (s, e) =>
                {
                    QualityControlAndSetCMD(clone);
                    CMDUpdated?.Invoke(this, EventArgs.Empty);
                };
                return clone;
            }
            set
            {
                if (value != null)
                    QualityControlAndSetCMD(value);
            }
        }

        [ObservableProperty]
        private ObservableCollection<VariableText> _internalVariablePath;

        public event EventHandler? CMDUpdated;

        public PathVarsTextBoxViewModel() : this(
            new ObservableCollection<FileSystemNode> { new FileSystemNode("VarTextRoot") })
        {
        }

        public PathVarsTextBoxViewModel(ObservableCollection<FileSystemNode> fileTree)
        {
            if (fileTree == null) throw new ArgumentNullException(nameof(fileTree), "The provided TreeNode is null, cannot create the PathVarsTextBoxViewModel");
            _treeNodes = fileTree;
            InternalVariablePath = new ObservableCollection<VariableText> { new LiteralText(string.Empty) };
            VariablePath.CollectionChanged += (s, e) => QualityControlAndSetCMD(VariablePath);
        }

        public void TriggerCMDChanged()
        {
            CMDUpdated?.Invoke(this, EventArgs.Empty);
        }

        public bool HasCommand()
        {
            if (
                InternalVariablePath.Count > 1 ||
                (
                    InternalVariablePath != null &&
                    InternalVariablePath.Count > 0 &&
                    !string.IsNullOrWhiteSpace(((LiteralText)InternalVariablePath.First()).Value)
                )
            )
                return true;
            else
                return false;
        }

        private void QualityControlAndSetCMD(ObservableCollection<VariableText> newCMD)
        {
            List<VariableText> cmdCopy = newCMD.Select(cmd => cmd.Clone()).ToList();
            if (!(cmdCopy.LastOrDefault() is LiteralText))
            {
                cmdCopy.Add(new LiteralText(string.Empty));
            }
            for (int i = 0; i < cmdCopy.Count - 1; i++)
            {
                if (cmdCopy[i] is LiteralText currentCMD && cmdCopy[i + 1] is LiteralText nextCMD)
                {
                    nextCMD.Value = currentCMD.Value + nextCMD.Value;
                    cmdCopy.RemoveAt(i);
                    i--;
                }
            }
            InternalVariablePath.Clear();
            InternalVariablePath.AddRange(cmdCopy);
        }
    }
}
