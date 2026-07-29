using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SuiteCreatorAvalonia.ViewModels
{
    public partial class BuildViewModel : ViewModelBase
    {
        private readonly SuiteCoreManager _suiteCoreMgr;
        private readonly SuiteBuilder _suiteBuilder;
        private readonly MainViewModel _mainViewModel;
        private bool _isLoading = false;
        private CancellationTokenSource _buildCancellationTokenSource = new CancellationTokenSource();

        [ObservableProperty]
        public string? _manufacturer;

        [ObservableProperty]
        public string? _name;

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [CustomValidation(typeof(BuildViewModel), nameof(ValidateSuiteVersion))]
        public string? _suiteVersion;

        public static ValidationResult? ValidateSuiteVersion(string? value, ValidationContext context)
        {
            if (string.IsNullOrWhiteSpace(value) || !Version.TryParse(value, out _))
                return new ValidationResult("Invalid version format. Example: 1.0.0");
            return ValidationResult.Success;
        }

        [ObservableProperty]
        public int _revision;

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [CustomValidation(typeof(BuildViewModel), nameof(ValidateUpgradeCode))]
        public string? _upgradeCode;

        public static ValidationResult? ValidateUpgradeCode(string? value, ValidationContext context)
        {
            if (string.IsNullOrWhiteSpace(value) || !Guid.TryParse(value, out _))
                return new ValidationResult("Not a valid GUID, generate or enter a valid GUID");
            return ValidationResult.Success;
        }

        [ObservableProperty]
        public Architecture _selectedArchitecture = Architecture.x64;

        [ObservableProperty]
        public bool _isFailSafe = false;

        [ObservableProperty]
        public bool _isRefreshEnv = false;

        [ObservableProperty]
        public bool _isKeepCache = false;

        [ObservableProperty]
        public bool _isRetryOnFailure = false;

        [ObservableProperty]
        public bool _isRestartBeforeRetry = false;

        [ObservableProperty]
        public bool _isReverseUninstall = false;

        [ObservableProperty]
        public string? _detection;

        partial void OnManufacturerChanged(string? value)
            => BuildSuiteCommand.NotifyCanExecuteChanged();
        partial void OnNameChanged(string? value)
            => BuildSuiteCommand.NotifyCanExecuteChanged();
        partial void OnSuiteVersionChanged(string? value)
            => BuildSuiteCommand.NotifyCanExecuteChanged();
        partial void OnRevisionChanged(int value)
            => BuildSuiteCommand.NotifyCanExecuteChanged();
        partial void OnUpgradeCodeChanged(string? value)
            => BuildSuiteCommand.NotifyCanExecuteChanged();
        private bool HasErrorsFor(string propertyName)
        {
            return GetErrors(propertyName)?.Cast<object>().Any() == true;
        }

        public BuildViewModel() : this(new SuiteCoreManager(), new SuiteBuilder(new SuiteCoreManager(), new AppSettingsControl()), new MainViewModel())
        {
        }

        public BuildViewModel(SuiteCoreManager suiteCoreMgr, SuiteBuilder suiteBuilder, MainViewModel mainViewModel)
        {
            _suiteCoreMgr = suiteCoreMgr;
            _suiteBuilder = suiteBuilder;
            _mainViewModel = mainViewModel;
            Build buildSettings = _suiteCoreMgr.GetBuildSettings();
            LoadSettings(buildSettings);
            PropertyChanged += (s, e) =>
            {
                CommitSettings();
            };
        }

        [RelayCommand]
        public void GenerateGUID()
        {
            UpgradeCode = Guid.NewGuid().ToString();
        }

        private void LoadSettings(Build buildSettings)
        {
            try
            {
                _isLoading = true;
                Manufacturer = buildSettings.Manufacturer;
                Name = buildSettings.Name;
                SuiteVersion = buildSettings.SuiteVersion == null ? "1.0.0" : buildSettings.SuiteVersion.ToString();
                Revision = buildSettings.Revision;
                UpgradeCode = buildSettings.UpgradeCode.ToString();
                IsFailSafe = buildSettings.FailSafe;
                IsRefreshEnv = buildSettings.RefreshEnv;
                IsKeepCache = buildSettings.KeepCache;
                IsRetryOnFailure = buildSettings.FailureRetry;
                IsRestartBeforeRetry = buildSettings.RestartOnFailure;
                IsReverseUninstall = buildSettings.ReverseUninstall;
                Detection = buildSettings.Detection;
            }
            finally
            {
                _isLoading = false;
            }
        }

        private void CommitSettings()
        {
            if (_isLoading) return;

            Build buildSettings = new Build
            {
                Manufacturer = Manufacturer,
                Name = Name,
                Revision = Revision,
                // Generate a new Guid if UpgradeCode is not a valid Guid
                UpgradeCode = Guid.TryParse(UpgradeCode, out var parsedGuid) ? parsedGuid : Guid.NewGuid(),
                FailSafe = IsFailSafe,
                RefreshEnv = IsRefreshEnv,
                KeepCache = IsKeepCache,
                FailureRetry = IsRetryOnFailure,
                RestartOnFailure = IsRestartBeforeRetry,
                ReverseUninstall = IsReverseUninstall,
                Detection = Detection
            };
            Version.TryParse(SuiteVersion, out var parsedVersion);
            if (parsedVersion != null)
            {
                buildSettings.SuiteVersion = parsedVersion;
            }
            _suiteCoreMgr.UpdateBuildSettings(buildSettings);
        }

        [RelayCommand(CanExecute = nameof(CanBuildSuite))]
        public async Task BuildSuite()
        {
            // Cancel any previous build
            Detection = null;
            _mainViewModel.BuildValidationErrors = null;
            _buildCancellationTokenSource?.Dispose();
            _buildCancellationTokenSource = new CancellationTokenSource();
            try
            {
                if (string.IsNullOrEmpty(_suiteCoreMgr.GetSavePath()))
                {
                    await _mainViewModel.SaveProject();
                    if (string.IsNullOrEmpty(_suiteCoreMgr.GetSavePath()))
                        return;
                }
                var progressDialogVM = new ProgressDiagViewModel
                {
                    Message = "Starting Build",
                };
                _ = this.ShowDialogAsync(progressDialogVM);
                var progress = new Progress<BuildProgress>(buildProgress =>
                {
                    progressDialogVM.Message = buildProgress.Stage;
                    progressDialogVM.ZipPercent = buildProgress.ZipPercent;
                });
                List<SuiteValidationError>? validationErrors = await _suiteBuilder.Build(_buildCancellationTokenSource.Token, progress);
                if (validationErrors != null)
                {
                    _mainViewModel.BuildValidationErrors = validationErrors;
                }
                else
                {
                    CommitSettings();
                    await _mainViewModel.SaveProject();
                }
            }
            catch (OperationCanceledException)
            {
                AppLog.Info("Build cancelled by user", "SuiteBuilder");
            }
            catch (Exception ex)
            {
                AppLog.Error("Build failed", ex, "SuiteBuilder");
                await ShowErrorPop("Error:", ex.Message);
            }
            finally
            {
                this.CloseDialog("MainDialogHost");
            }
        }

        private bool CanBuildSuite()
        {
            // All required properties except Detection must have a value
            return !string.IsNullOrWhiteSpace(Manufacturer)
                && !string.IsNullOrWhiteSpace(Name)
                && !string.IsNullOrWhiteSpace(SuiteVersion)
                && Revision != 0
                && !string.IsNullOrWhiteSpace(UpgradeCode)
                && !HasErrorsFor(nameof(SuiteVersion))
                && !HasErrorsFor(nameof(UpgradeCode));
        }

        [RelayCommand]
        public void CancelBuild()
        {
            _buildCancellationTokenSource?.Cancel();
        }

        [RelayCommand]
        public void OpenBuildDir()
        {
            try
            {
                string buildPath = Path.Combine(Path.GetDirectoryName(_suiteCoreMgr.GetSavePath()),Path.GetFileNameWithoutExtension(_suiteCoreMgr.GetSavePath()) + "-Build");
                if (Directory.Exists(buildPath))
                {
                    System.Diagnostics.Process.Start("explorer.exe", buildPath);
                }
            }
            catch (Exception ex)
            {
                // ignore its not a critical function
                AppLog.Warning($"Failed to open build directory in explorer: {ex.Message}", "SuiteBuilder");
            }
        }
    }
}
