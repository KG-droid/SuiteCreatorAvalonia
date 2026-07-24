using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using SuiteUserPopup.Helpers;

namespace SuiteUserPopup.ViewModels
{
    internal partial class PopupPickReminderViewModel : ViewModelBase
    {
        [ObservableProperty]
        private TimeSpan _selectedTime = DateTime.Now.AddMinutes(30).TimeOfDay;

        public PopupPickReminderViewModel()
        {
        }

        [RelayCommand]
        private void Ok()
        {
            string output = $"{SelectedTime.Hours:00}:{SelectedTime.Minutes:00}";
            LogInfo($"Reminder time selected: {output}");
            Console.Out.WriteLine(output);
            Console.Out.Flush();

            ApplicationHelper.ExitApplication(3);
        }
    }
}