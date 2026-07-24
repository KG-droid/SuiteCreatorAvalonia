using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.ViewModels;
using System;
using System.Collections.Generic;

namespace SuiteCreatorAvalonia.Models.Package
{
    public class PackageTypeList
    {
        public static List<PackageTypeListItem> PackageTypes = new()
        {
            new PackageTypeListItem
            {
                Name = "MSIPkg",
                IconPath = "avares://SuiteCreatorAvalonia/Assets/Images/MSILogo.png"
            },
            new PackageTypeListItem
            {
                Name = "MSIRemoval",
                IconPath = "avares://SuiteCreatorAvalonia/Assets/Images/MSILogo.png",
                IsRemoval = true
            },
            new PackageTypeListItem
            {
                Name = "MSIxPkg",
                IconPath = "avares://SuiteCreatorAvalonia/Assets/Images/MSIxLogo.png"
            },
            new PackageTypeListItem
            {
                Name = "MSIxRemoval",
                IconPath = "avares://SuiteCreatorAvalonia/Assets/Images/MSIxLogo.png",
                IsRemoval = true
            },
            new PackageTypeListItem
            {
                Name = "OtherPkg",
                IconPath = "avares://SuiteCreatorAvalonia/Assets/Images/TerminalLogo.png"
            },
            new PackageTypeListItem
            {
                Name = "OtherRemovalPkg",
                IconPath = "avares://SuiteCreatorAvalonia/Assets/Images/TerminalLogo.png",
                IsRemoval = true
            },
        };
    }

    public class PackageTypeToViewModelMapper
    {
        public static Dictionary<PackageType, Type> Mappings = new()
        {
            { PackageType.MSIPkg, typeof(PackageMSIInstallDetailsViewModel) },
            { PackageType.MSIRemoval, typeof(PackageMSIRemoveDetailsViewModel) },
            { PackageType.MSIxPkg, typeof(PackageMSIxInstallDetailsViewModel) },
            { PackageType.MSIxRemoval, typeof(PackageMSIxRemoveDetailsViewModel) },
            { PackageType.OtherPkg, typeof(PackageOtherInstallDetailsViewModel) },
            { PackageType.OtherRemovalPkg, typeof(PackageOtherRemoveDetailsViewModel) }
        };
    }

    public class PackageTypeListItem
    {
        public string? Name { get; set; }
        public string? IconPath { get; set; }
        public bool IsRemoval { get; set; }

        public PackageTypeListItem Clone()
        {
            return new PackageTypeListItem
            {
                Name = Name,
                IconPath = IconPath,
                IsRemoval = IsRemoval
            };
        }

        public void UpdateFrom(PackageTypeListItem other)
        {
            if (other == null) return;

            Name = other.Name;
            IconPath = other.IconPath;
            IsRemoval = other.IsRemoval;
        }
    }

}
