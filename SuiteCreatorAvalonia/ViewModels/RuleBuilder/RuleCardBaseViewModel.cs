using CommunityToolkit.Mvvm.ComponentModel;
using SuiteCreatorAvalonia.Models.Rules;

namespace SuiteCreatorAvalonia.ViewModels.RuleBuilder
{
    public partial class RuleCardBaseViewModel : ViewModelBase
    {
        [ObservableProperty]
        private RuleBase? _linkedRule;

        public virtual void Load(RuleBase rule)
        {
            if (rule != null)
            {
                LinkedRule = rule;
            }
        }
    }
}
