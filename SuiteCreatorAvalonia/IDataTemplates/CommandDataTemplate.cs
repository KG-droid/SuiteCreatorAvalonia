using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.VisualTree;
using AvaloniaEdit.Utils;
using CommunityToolkit.Mvvm.Input;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Common.TreeNodes;
using SuiteCreatorAvalonia.ViewModels;
using SuiteCreatorAvalonia.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SuiteCreatorAvalonia.IDataTemplates
{
    internal class CommandDataTemplate : IDataTemplate
    {

        private ObservableCollection<FileSystemNode> _tree;
        private ObservableCollection<List<VariableText>> _variableCMDs;

        public CommandDataTemplate(ObservableCollection<FileSystemNode> tree, ObservableCollection<List<VariableText>> variableCMDs)
        {
            _tree = tree;
            _variableCMDs = variableCMDs;
        }
        public Control Build(object item)
        {
            if (item is List<VariableText> cmd)
            {
                Grid grid = new();
                grid.Margin = new Thickness(0, 0, 0, 5);
                PathVarsTextBoxView eventView = new();
                PathVarsTextBoxViewModel vm = new(_tree);
                vm.VariablePath.AddRange(cmd);
                vm.CMDUpdated += (s, e) =>
                {
                    // Save this specific CMD within the list of CMDs
                    if (s is PathVarsTextBoxViewModel pVM)
                    {
                        cmd.Clear();
                        cmd.AddRange(pVM.VariablePath);
                    }
                    UserControl? parent = eventView.FindAncestorOfType<UserControl>();
                    if (parent != null && parent.DataContext is PackageOtherBaseDetailsViewModel oVM)
                    {
                        oVM.SavePackage();
                    }
                };
                eventView.DataContext = vm;
                Grid.SetColumn(eventView, 0);

                if (_variableCMDs.Count > 1)
                {
                    Button delButton = new();
                    delButton.Classes.Add("IconButton");
                    delButton.Tag = "Bin";
                    delButton.Foreground = Avalonia.Media.Brushes.Red;
                    ToolTip.SetTip(delButton, "Delete this command");
                    delButton.Command = new RelayCommand(() =>
                    {
                        _variableCMDs.Remove(cmd);
                    });
                    delButton.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(delButton, 1);
                    grid.Children.Add(delButton);
                }

                grid.Children.Add(eventView);
                grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
                return grid;
            }
            else
            {
                throw new ArgumentException($"Invalid type for EventCard IDataTemplate, type provide was: {item.GetType().Name}");
            }
        }

        public bool Match(object? data)
        {
            return data != null && data is List<VariableText>;
        }
    }
}