using CommunityToolkit.Mvvm.ComponentModel;
using SuiteCreatorAvalonia.Factories;
using SuiteCreatorAvalonia.Models.Package;
using SuiteCreatorAvalonia.Services;
using System;
using System.Linq;
using System.ComponentModel;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class PackageMSIInstallDetailsViewModel : PackageDetailsBaseViewModel
    {
        private ViewFactory _fact;
        private bool _isLoading = false;
        private SuiteCoreManager _suiteCoreManager;
        private PropertyChangedEventHandler? _msiSubDetailsPropHandler;
        private System.Collections.Specialized.NotifyCollectionChangedEventHandler? _msiSubDetailsCollHandler;
        private PropertyChangedEventHandler? _rollbackPropHandler;
        private System.Collections.Specialized.NotifyCollectionChangedEventHandler? _rollbackCollHandler;

        [ObservableProperty]
        private PackageMSIInstallSubDetailsViewModel _msiSubDetails;

        [ObservableProperty]
        private bool _addRollBack = false;

        [ObservableProperty]
        private PackageMSIInstallSubDetailsViewModel _rollback;

        partial void OnMsiSubDetailsChanged(PackageMSIInstallSubDetailsViewModel? oldValue, PackageMSIInstallSubDetailsViewModel value)
        {
            if (oldValue != null)
            {
                if (_msiSubDetailsPropHandler != null)
                    oldValue.PropertyChanged -= _msiSubDetailsPropHandler;
                if (_msiSubDetailsCollHandler != null)
                    oldValue.Properties.CollectionChanged -= _msiSubDetailsCollHandler;
            }
            if (value != null)
            {
                _msiSubDetailsPropHandler = (s, e) => OnPropertyChanged(nameof(MsiSubDetails));
                _msiSubDetailsCollHandler = (s, e) => OnPropertyChanged(nameof(MsiSubDetails));
                value.PropertyChanged += _msiSubDetailsPropHandler;
                value.Properties.CollectionChanged += _msiSubDetailsCollHandler;
            }
        }

        partial void OnRollbackChanged(PackageMSIInstallSubDetailsViewModel? oldValue, PackageMSIInstallSubDetailsViewModel value)
        {
            if (oldValue != null)
            {
                if (_rollbackPropHandler != null)
                    oldValue.PropertyChanged -= _rollbackPropHandler;
                if (_rollbackCollHandler != null)
                    oldValue.Properties.CollectionChanged -= _rollbackCollHandler;
            }
            if (value != null)
            {
                _rollbackPropHandler = (s, e) => OnPropertyChanged(nameof(Rollback));
                _rollbackCollHandler = (s, e) => OnPropertyChanged(nameof(Rollback));
                value.PropertyChanged += _rollbackPropHandler;
                value.Properties.CollectionChanged += _rollbackCollHandler;
            }
        }

        public PackageMSIInstallDetailsViewModel() : this(new ViewFactory(type => { return (ViewModelBase)Activator.CreateInstance(type)!; }), new SuiteCoreManager())
        {
            PropertyChanged += (sender, args) => SavePackage();
        }

        public PackageMSIInstallDetailsViewModel(ViewFactory viewFactory, SuiteCoreManager suiteCoreManager) : base(viewFactory, suiteCoreManager)
        {
            _fact = viewFactory;
            _suiteCoreManager = suiteCoreManager;
            MsiSubDetails = (PackageMSIInstallSubDetailsViewModel)viewFactory.GetVM(typeof(PackageMSIInstallSubDetailsViewModel));
            Rollback = (PackageMSIInstallSubDetailsViewModel)viewFactory.GetVM(typeof(PackageMSIInstallSubDetailsViewModel));
            Rollback.IsRollbackSection = true;
        }

        public override void LoadPackage(Guid packageId)
        {
            _isLoading = true;
            var msiPkg = _suiteCoreManager.GetPackage(packageId) as MSIPkg;
            if (msiPkg == null || string.IsNullOrWhiteSpace(msiPkg.Name)) return;
            base.LoadPackage(packageId);
            MsiSubDetails.MsiFilePath = msiPkg.MSIPath;
            MsiSubDetails.IsUserContext = msiPkg.Context == Enums.Contexts.User;
            MsiSubDetails.IsCreateLog = msiPkg.IsCreateLog;
            MsiSubDetails.LogPath = msiPkg.LogPath;
            MsiSubDetails.IsRemoveFamily = msiPkg.IsRemoveFamily;
            MsiSubDetails.IncludeAdjacentFilesFolders = msiPkg.IncludeAdjacentFiles;
            MsiSubDetails.TransformFile = msiPkg.TransformsPath;
            MsiSubDetails.PatchFile = msiPkg.PatchPath;
            MsiSubDetails.SecureTransforms = msiPkg.SecureTransforms;
            MsiSubDetails.LegacyLongFilePath = msiPkg.LegacyLongFilePath;
            MsiSubDetails.RestartBehavior = msiPkg.RestartBehavior;
            MsiSubDetails.RemoveOnSuiteRemoval = msiPkg.RemoveOnSuiteRemoval;
            // Update Properties in place
            MsiSubDetails.Properties.Clear();
            if (msiPkg.Properties != null)
            {
                foreach (var prop in msiPkg.Properties)
                    MsiSubDetails.Properties.Add(prop);
            }
            if (msiPkg.Rollback is MSIPkg rollbackPkg)
            {
                Rollback.MsiFilePath = rollbackPkg.MSIPath;
                Rollback.IsUserContext = rollbackPkg.Context == Enums.Contexts.User;
                Rollback.IsCreateLog = rollbackPkg.IsCreateLog;
                Rollback.LogPath = rollbackPkg.LogPath;
                Rollback.IsRemoveFamily = rollbackPkg.IsRemoveFamily;
                Rollback.IncludeAdjacentFilesFolders = rollbackPkg.IncludeAdjacentFiles;
                Rollback.TransformFile = rollbackPkg.TransformsPath;
                Rollback.PatchFile = rollbackPkg.PatchPath;
                Rollback.SecureTransforms = rollbackPkg.SecureTransforms;
                Rollback.LegacyLongFilePath = rollbackPkg.LegacyLongFilePath;
                // Update Rollback Properties in place
                Rollback.Properties.Clear();
                if (rollbackPkg.Properties != null)
                {
                    foreach (var prop in rollbackPkg.Properties)
                        Rollback.Properties.Add(prop);
                }
            }
            _isLoading = false;
        }

        public override void SavePackage()
        {
            if (_isLoading || Id is not Guid) return;
            base.SavePackage();
            var msiPkg = GetVerifiedPackage<MSIPkg>((Guid)Id);
            msiPkg.MSIPath = MsiSubDetails.MsiFilePath;
            msiPkg.TransformsPath = MsiSubDetails.TransformFile;
            msiPkg.IsRemoveFamily = MsiSubDetails.IsRemoveFamily;
            msiPkg.PatchPath = MsiSubDetails.PatchFile;
            msiPkg.SecureTransforms = MsiSubDetails.SecureTransforms;
            msiPkg.Context = MsiSubDetails.IsUserContext ? Enums.Contexts.User : Enums.Contexts.System;
            msiPkg.IsCreateLog = MsiSubDetails.IsCreateLog;
            msiPkg.IncludeAdjacentFiles = MsiSubDetails.IncludeAdjacentFilesFolders;
            msiPkg.LegacyLongFilePath = MsiSubDetails.LegacyLongFilePath;
            msiPkg.LogPath = MsiSubDetails.LogPath;
            msiPkg.RestartBehavior = MsiSubDetails.RestartBehavior;
            msiPkg.Properties = MsiSubDetails.Properties.ToList();
            msiPkg.RemoveOnSuiteRemoval = MsiSubDetails.RemoveOnSuiteRemoval;
            if (Rollback != null && msiPkg.Rollback is MSIPkg rollbackPkg)
            {
                rollbackPkg.MSIPath = Rollback.MsiFilePath;
                rollbackPkg.TransformsPath = Rollback.TransformFile;
                rollbackPkg.PatchPath = Rollback.PatchFile;
                rollbackPkg.IsRemoveFamily = Rollback.IsRemoveFamily;
                rollbackPkg.SecureTransforms = Rollback.SecureTransforms;
                rollbackPkg.Context = Rollback.IsUserContext ? Enums.Contexts.User : Enums.Contexts.System;
                rollbackPkg.IsCreateLog = Rollback.IsCreateLog;
                rollbackPkg.LegacyLongFilePath = Rollback.LegacyLongFilePath;
                rollbackPkg.LogPath = Rollback.LogPath;
                rollbackPkg.Properties = Rollback.Properties.ToList();
            }
            _suiteCoreManager.UpdatePackage(msiPkg);
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            SavePackage();
        }
    }
}
