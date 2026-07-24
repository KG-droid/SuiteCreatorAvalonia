namespace SuiteCreatorAvalonia.Enums
{
    public enum Contexts
    {
        User,
        System,
    }
    public enum RestartBehavior
    {
        BasedOnReturnCodeImmediate,
        BasedOnReturnCodeDelayed,
        AlwaysImmediate,
        AlwaysDelayed,
        Ignore
    }
    public enum Architecture
    {
        x86,
        x64,
    }
    public enum ComparatorPlus
    {
        NotEqual,
        Equal,
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual,
        True,
        False,
    }
    public enum Comparator
    {
        GreaterThanOrEqual,
        GreaterThan,
        Equal,
        LessThan,
        LessThanOrEqual
    }
    public enum Separator
    {
        Semicolon = ';',
        Colon = ':',
        Comma = ',',
        Pipe = '|'
    }
    public enum SuitePathVar
    {
        PackageFilesDIR,
        ExecutableDIR,
    }

    public enum UserPathVar
    {
        UserProfile,
        AppData,
        LocalAppData,
    }

    public enum SystemPathVar
    {
        WindowsDIR,
        System32DIR,
        RootDrive,
        ProgramFilesDIR,
        ProgramFilesX86DIR,
    }

    public enum PackageType
    {
        MSIxPkg,
        MSIxRemoval,
        MSIPkg,
        MSIRemoval,
        OtherPkg,
        OtherRemovalPkg,
    }
}
