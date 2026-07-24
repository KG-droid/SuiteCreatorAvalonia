namespace SuiteCreatorAvalonia.Enums
{
    #region Browser Extensions
    public enum ExtAction
    {
        Install,
        Uninstall
    }
    public enum BrowserType
    {
        GoogleChrome,
        MSEdge,
    }
    public enum BrowserExtensionSource
    {
        Local,
        MSEdgeWebStore,
        ChromeWebStore,
    }
    #endregion

    #region Certs
    public enum CertAction
    {
        Add,
        Remove
    }
    public enum CertStore
    {
        TrustedRootCA,
        IntermediateCA,
        TrustedPublisher,
        Personal,
        ThirdPartyRootCA
    }
    #endregion

    #region Drivers
    public enum DriverAction
    {
        Install,
        Remove
    }
    #endregion

    #region Environment
    public enum Environmentbehaviour
    {
        Append,
        Prepend,
        Replace,
        Remove
    }
    #endregion

    #region FileSysIO
    public enum FileSysIOAction
    {
        Deploy,
        Copy,
        Move,
        Delete,
        Rename
    }

    public enum FileSysIOType
    {
        File,
        Directory
    }
    #endregion

    #region ProcessClosure
    public enum ProcAction
    {
        Stop,
        StopAndBlock,
        Unblock
    }
    #endregion

    #region Registry
    public enum RegistryPropertyType
    {
        String,
        ExpandString,
        Binary,
        DWord,
        QWord,
        MultiString,
        None
    }
    public enum RegAction
    {
        AddAmend,
        Remove,
        Import
    }
    #endregion

    #region ServiceClosure
    public enum ClosureType
    {
        Stop,
        Disable,
        EnableAndStart
    }
    #endregion

    #region Shortcuts
    public enum ShortcutType
    {
        Program,
        Web
    }
    public enum ShortcutPlacement
    {
        Desktop,
        StartMenu,
    }
    public enum ShortcutManualIconType
    {
        Index,
        File,
    }
    public enum ShortcutAction
    {
        Create,
        Delete
    }
    #endregion
}
