using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SuiteCreatorAvalonia.Models.Common;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.AccessControl;
using static SuiteCreatorAvalonia.Models.Common.Security;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class FileSysPermissionsViewModel : ViewModelBase
    {
        private bool _isLoading = false;

        [ObservableProperty]
        private bool _read = false;

        [ObservableProperty]
        private bool _readAndExecute = false;

        [ObservableProperty]
        private bool _modify = false;

        [ObservableProperty]
        private bool _fullControl = false;

        [ObservableProperty]
        private bool _write = false;

        [ObservableProperty]
        private IEnumerable<AccessControlType> _accessControlTypeList = Enum.GetValues<AccessControlType>();

        [ObservableProperty]
        private bool _isEnabled = false;

        [ObservableProperty]
        private ObservableCollection<Permission> _permissionsList = new ObservableCollection<Permission>();

        [ObservableProperty]
        private PermissionGroup? _comboSelectedUserGroup;

        [ObservableProperty]
        private IEnumerable<PermissionGroup> _userGroupsComboList = GetPermissionGroups();

        [ObservableProperty]
        private Permission? _permissionsListSelectedValue;
        partial void OnPermissionsListSelectedValueChanged(Permission? value)
        {
            _isLoading = true;
            if (null != value)
            {
                IsEnabled = true;
                Read = value.PermissionRights.Contains(FileSystemRights.Read);
                ReadAndExecute = value.PermissionRights.Contains(FileSystemRights.ReadAndExecute); ;
                Modify = value.PermissionRights.Contains(FileSystemRights.Modify); ;
                FullControl = value.PermissionRights.Contains(FileSystemRights.FullControl); ;
                Write = value.PermissionRights.Contains(FileSystemRights.Write); ;
            }
            else
            {
                IsEnabled = false;
                Read = false;
                ReadAndExecute = false;
                Modify = false;
                FullControl = false;
                Write = false;
            }
            _isLoading = false;
        }
        public FileSysPermissionsViewModel()
        {
            ComboSelectedUserGroup = UserGroupsComboList.First();
            PropertyChanged += OnPropertyChanged;
        }

        private void OnPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_isLoading)
                return;
            if (e.PropertyName == nameof(Read) ||
                e.PropertyName == nameof(ReadAndExecute) ||
                e.PropertyName == nameof(Modify) ||
                e.PropertyName == nameof(FullControl) ||
                e.PropertyName == nameof(Write))
            {
                if (null != PermissionsListSelectedValue)
                {
                    PermissionsListSelectedValue.PermissionRights.Clear();
                    if (Read)
                        PermissionsListSelectedValue.PermissionRights.Add(FileSystemRights.Read);
                    if (ReadAndExecute)
                        PermissionsListSelectedValue.PermissionRights.Add(FileSystemRights.ReadAndExecute);
                    if (Modify)
                        PermissionsListSelectedValue.PermissionRights.Add(FileSystemRights.Modify);
                    if (FullControl)
                        PermissionsListSelectedValue.PermissionRights.Add(FileSystemRights.FullControl);
                    if (Write)
                        PermissionsListSelectedValue.PermissionRights.Add(FileSystemRights.Write);
                }
            }
        }

        [RelayCommand]
        public void AddUserGroup()
        {
            if (null == ComboSelectedUserGroup || PermissionsList.Where(x => x.UserGroup.SID == ComboSelectedUserGroup.SID).Any())
                return;
            PermissionsList.Add(new Permission
            {
                UserGroup = ComboSelectedUserGroup,
                PermissionType = AccessControlType.Allow,
                PermissionRights = new List<FileSystemRights>()
            });
        }

        [RelayCommand]
        public void RemoveUserGroup()
        {
            if (null != PermissionsListSelectedValue)
            {
                PermissionsList.Remove(PermissionsListSelectedValue);
            }
        }
    }
}
