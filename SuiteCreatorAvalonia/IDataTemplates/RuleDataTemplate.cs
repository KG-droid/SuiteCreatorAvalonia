using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using SuiteCreatorAvalonia.Factories;
using SuiteCreatorAvalonia.Models.Rules;
using SuiteCreatorAvalonia.ViewModels;
using SuiteCreatorAvalonia.ViewModels.RuleBuilder;
using SuiteCreatorAvalonia.Views.RuleBuilder;
using System;

namespace SuiteCreatorAvalonia.IDataTemplates
{
    internal class RuleDataTemplate : IDataTemplate
    {
        private readonly ViewFactory _viewFactory;
        private readonly RuleBuilderViewModel _ruleBuilderVM;

        public RuleDataTemplate() : this(new ViewFactory(type => { return (ViewModelBase)Activator.CreateInstance(type)!; }), new RuleBuilderViewModel())
        {
        }

        public RuleDataTemplate(ViewFactory viewFactory, RuleBuilderViewModel ruleBuilderVM)
        {
            _viewFactory = viewFactory;
            _ruleBuilderVM = ruleBuilderVM;
        }

        public Control Build(object? item)
        {
            if (item is RuleBase rule)
            {
                Panel rulePanel;
                if (rule.Type == RuleType.GroupOpen || rule.Type == RuleType.GroupClose)
                {
                    rulePanel = new Grid();
                    ((Grid)rulePanel).RowDefinitions = new RowDefinitions("*,Auto");
                }
                else
                {
                    rulePanel = new StackPanel();
                    ((StackPanel)rulePanel).Orientation = Avalonia.Layout.Orientation.Horizontal;
                    ((StackPanel)rulePanel).HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
                }
                switch (rule.Type)
                {
                    case RuleType.OR:
                        RuleOperatorCardViewModel newOperatorVM = (RuleOperatorCardViewModel)_viewFactory.GetVM(typeof(RuleOperatorCardViewModel));
                        RuleOperatorView newOperator = new RuleOperatorView() { DataContext = newOperatorVM };
                        newOperatorVM.OperatorType = RuleType.OR;
                        newOperatorVM.Load(rule);
                        rulePanel.Children.Add(newOperator);
                        break;
                    case RuleType.GroupOpen:
                    case RuleType.GroupClose:
                        RuleOperatorCardViewModel newOperatorGroupVM = (RuleOperatorCardViewModel)_viewFactory.GetVM(typeof(RuleOperatorCardViewModel));
                        newOperatorGroupVM.OperatorType = rule.Type;
                        RuleOperatorView newOperatorGroup = new RuleOperatorView() { DataContext = newOperatorGroupVM };
                        newOperatorGroupVM.Load(rule);
                        Grid.SetColumn(newOperatorGroup, 0);
                        rulePanel.Children.Add(newOperatorGroup);
                        break;
                    default:
                        RuleCardViewModel cardViewModel = (RuleCardViewModel)_viewFactory.GetVM(typeof(RuleCardViewModel));
                        RuleCardView newCard = new RuleCardView() { DataContext = cardViewModel };
                        cardViewModel.Load(rule);
                        rulePanel.Children.Add(newCard);
                        cardViewModel.PropertyChanged += (s, e) =>
                        {
                            _ruleBuilderVM.TriggerRulesAmended();
                        };
                        break;
                }
                Button delButton = new();
                delButton.Classes.Add("IconButton");
                delButton.Tag = "Bin";
                delButton.Foreground = Avalonia.Media.Brushes.Red;
                delButton.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
                delButton.Bind(Button.IsVisibleProperty, new Avalonia.Data.Binding
                {
                    Source = _ruleBuilderVM,
                    Path = nameof(_ruleBuilderVM.IsRemoveMode),
                    Mode = Avalonia.Data.BindingMode.OneWay
                });
                Grid.SetColumn(delButton, 1);
                delButton.Command = new RelayCommand(() =>
                {
                    RuleBuilderViewModel? builderVM = delButton.FindAncestorOfType<UserControl>()?.DataContext as RuleBuilderViewModel;
                    if (builderVM != null)
                        builderVM.Rules.Remove(rule);
                });
                rulePanel.Children.Add(delButton);
                return rulePanel;
            }
            else
            {
                throw new ArgumentException($"Invalid type for Rule IDataTemplate, type provide was: {item?.GetType().Name}");
            }
        }

        public bool Match(object? data)
        {
            return data != null && data is RuleBase;
        }
    }
}