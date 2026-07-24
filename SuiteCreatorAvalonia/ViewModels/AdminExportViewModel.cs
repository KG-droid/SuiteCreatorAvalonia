using Avalonia.Controls;
using Avalonia.Labs.Gif;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using SuiteCreatorAvalonia.Converters;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Services;
using SuiteCreatorAvalonia.Tools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuiteCreatorAvalonia.ViewModels
{
    // Builds a standalone AppSettings.json for an admin to drop into the install directory - this is a
    // separate, disconnected form from the live app settings, so nothing here reads or writes the current
    // user's or admin's actual settings files until the Export button is pressed.
    internal partial class AdminExportViewModel : ViewModelBase
    {
        private byte[]? _companyLogoBytes;
        private Type? _companyLogoType;

        [ObservableProperty]
        private Control? _companyLogoCtrl;

        [ObservableProperty]
        private Color _companyLogoBackgroundColor = Color.Parse("#497cab");

        [ObservableProperty]
        private string? _companyLogoError;

        [ObservableProperty]
        private string? _logLocation = @"C:\Modern-Workplace-Logs";

        [ObservableProperty]
        private TextDocument _globalPopupCondition = new();

        [ObservableProperty]
        private string? _exportError;

        [RelayCommand]
        private async Task ImportCompanyLogo()
        {
            IEnumerable<string>? result = await this.OpenFileDialogAsync(new FilePickerOpenOptions()
            {
                AllowMultiple = false,
                Title = "Select a Logo",
                FileTypeFilter = SysIOPickerTypes.ImageIncGif
            });
            if (result == null || !result.Any())
            {
                return;
            }

            string filePath = result.First();
            if (filePath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    GifImage gifImage = new();
                    using (FileStream fileStream = File.OpenRead(filePath))
                    {
                        MemoryStream memoryStream = new();
                        fileStream.CopyTo(memoryStream);
                        _companyLogoBytes = memoryStream.ToArray();
                        memoryStream.Seek(0, SeekOrigin.Begin);
                        gifImage.Source = GifStreamSource.FromStream(memoryStream);
                    }
                    _companyLogoType = typeof(GifImage);
                    CompanyLogoCtrl = gifImage;
                }
                catch (Exception ex)
                {
                    AppLog.Error($"Failed to load company logo GIF from: {filePath}", ex, "AdminExport");
                    Dispatcher.UIThread.Post(() =>
                    {
                        CompanyLogoError = $"Error loading the GIF image: {ex.Message}";
                    });
                }
            }
            else
            {
                try
                {
                    byte[] bmpBytes = ImageLoader.GetBytes(new Uri(filePath));
                    _companyLogoBytes = bmpBytes;
                    MemoryStream ms = new(bmpBytes);
                    Bitmap bitmap = new(ms);
                    _companyLogoType = typeof(Image);
                    CompanyLogoCtrl = new Image { Source = bitmap };
                }
                catch (Exception ex)
                {
                    AppLog.Error($"Failed to load company logo image from: {filePath}", ex, "AdminExport");
                    Dispatcher.UIThread.Post(() =>
                    {
                        CompanyLogoError = $"Error loading the provided image: {ex.Message}";
                    });
                }
            }
        }

        [RelayCommand]
        private void ResetCompanyLogo()
        {
            _companyLogoBytes = null;
            _companyLogoType = null;
            CompanyLogoCtrl = null;
            CompanyLogoBackgroundColor = Color.Parse("#497cab");
        }

        [RelayCommand]
        private async Task Export()
        {
            ExportError = null;

            UISettings exportSettings = new()
            {
                CompanyLogoBackgroundColor = CompanyLogoBackgroundColor,
                CompanyLogoBytes = _companyLogoBytes,
                CompanyLogoType = _companyLogoBytes != null ? _companyLogoType : null,
                LogLocation = string.IsNullOrWhiteSpace(LogLocation) ? null : LogLocation,
                GlobalPopupCondition = string.IsNullOrWhiteSpace(GlobalPopupCondition.Text)
                    ? null
                    : Convert.ToBase64String(Encoding.UTF8.GetBytes(GlobalPopupCondition.Text))
            };

            string? savePath = await this.SaveFileDialogAsync(new FilePickerSaveOptions
            {
                Title = "Export Admin AppSettings.json",
                SuggestedFileName = "AppSettings",
                DefaultExtension = "json",
                FileTypeChoices = SysIOPickerTypes.Json
            });
            if (string.IsNullOrWhiteSpace(savePath))
            {
                return;
            }

            try
            {
                string json = JsonConvert.SerializeObject(exportSettings, Formatting.Indented, new JsonSerializerSettings
                {
                    Converters = { new ColorToJson(), new BitmapToJson() }
                });
                await File.WriteAllTextAsync(savePath, json);
                this.CloseDialog();
            }
            catch (Exception ex)
            {
                AppLog.Error($"Failed to export admin settings to: {savePath}", ex, "AdminExport");
                ExportError = $"Failed to export settings: {ex.Message}";
            }
        }

        [RelayCommand]
        private void Close()
        {
            this.CloseDialog();
        }
    }
}
