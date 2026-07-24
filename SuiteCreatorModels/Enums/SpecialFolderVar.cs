namespace SuiteCreatorModels.Enums
{
    public enum SpecialFolderVar
    {
        //
        // Summary:
        //     The logical Desktop rather than the physical file system location.
        Desktop = 0,
        //
        // Summary:
        //     The My Documents folder. This member is equivalent to System.Environment.SpecialFolder.Personal.
        MyDocuments = 5,
        //
        // Summary:
        //     The directory that corresponds to the user's Startup program group. The system
        //     starts these programs whenever a user logs on or starts Windows.
        Startup = 7,
        //
        // Summary:
        //     The directory that contains the Start menu items.
        StartMenu = 11,
        //
        // Summary:
        //     The My Music folder.
        MyMusic = 13,
        //
        // Summary:
        //     The file system directory that serves as a repository for videos that belong
        //     to a user.
        MyVideos = 14,
        //
        // Summary:
        //     A virtual folder that contains fonts.
        Fonts = 20,
        //
        // Summary:
        //     The file system directory that contains the programs and folders that appear
        //     on the Start menu for all users.
        CommonStartMenu = 22,
        //
        // Summary:
        //     A folder for components that are shared across applications.
        CommonPrograms = 23,
        //
        // Summary:
        //     The file system directory that contains the programs that appear in the Startup
        //     folder for all users.
        CommonStartup = 24,
        //
        // Summary:
        //     The file system directory that contains files and folders that appear on the
        //     desktop for all users.
        CommonDesktopDirectory = 25,
        //
        // Summary:
        //     The directory that serves as a common repository for application-specific data
        //     for the current roaming user. A roaming user works on more than one computer
        //     on a network. A roaming user's profile is kept on a server on the network and
        //     is loaded onto a system when the user logs on.
        ApplicationData = 26,
        //
        // Summary:
        //     The directory that serves as a common repository for application-specific data
        //     that is used by the current, non-roaming user.
        LocalApplicationData = 28,
        //
        // Summary:
        //     The directory that serves as a common repository for application-specific data
        //     that is used by all users.
        CommonApplicationData = 35,
        //
        // Summary:
        //     The Windows directory or SYSROOT. This corresponds to the %windir% or %SYSTEMROOT%
        //     environment variables.
        Windows = 36,
        //
        // Summary:
        //     The System directory.
        System = 37,
        //
        // Summary:
        //     The program files directory. In a non-x86 process, passing System.Environment.SpecialFolder.ProgramFiles
        //     to the System.Environment.GetFolderPath(System.Environment.SpecialFolder) method
        //     returns the path for non-x86 programs. To get the x86 program files directory
        //     in a non-x86 process, use the System.Environment.SpecialFolder.ProgramFilesX86
        //     member.
        ProgramFiles = 38,
        //
        // Summary:
        //     The My Pictures folder.
        MyPictures = 39,
        //
        // Summary:
        //     The user's profile folder. Applications should not create files or folders at
        //     this level; they should put their data under the locations referred to by System.Environment.SpecialFolder.ApplicationData.
        UserProfile = 40,
        //
        // Summary:
        //     The Windows System folder.
        SystemX86 = 41,
        //
        // Summary:
        //     The x86 Program Files folder.
        ProgramFilesX86 = 42,
        //
        // Summary:
        //     The directory for components that are shared across applications. To get the
        //     x86 common program files directory in a non-x86 process, use the System.Environment.SpecialFolder.ProgramFilesX86
        //     member.
        CommonProgramFiles = 43,
        //
        // Summary:
        //     The Program Files folder.
        CommonProgramFilesX86 = 44,
        //
        // Summary:
        //     The file system directory that contains documents that are common to all users.
        CommonDocuments = 46,
        //
        // Summary:
        //     The file system directory that serves as a repository for music files common
        //     to all users.
        CommonMusic = 53,
        //
        // Summary:
        //     The file system directory that serves as a repository for image files common
        //     to all users.
        CommonPictures = 54,
        //
        // Summary:
        //     The file system directory that serves as a repository for video files common
        //     to all users.
        CommonVideos = 55,
    }

    public enum SpecialSysFolderVar
    {
        //
        // Summary:
        //     A virtual folder that contains fonts.
        Fonts = 20,
        //
        // Summary:
        //     The file system directory that contains the programs and folders that appear
        //     on the Start menu for all users.
        CommonStartMenu = 22,
        //
        // Summary:
        //     A folder for components that are shared across applications.
        CommonPrograms = 23,
        //
        // Summary:
        //     The file system directory that contains the programs that appear in the Startup
        //     folder for all users.
        CommonStartup = 24,
        //
        // Summary:
        //     The file system directory that contains files and folders that appear on the
        //     desktop for all users.
        CommonDesktopDirectory = 25,
        //
        // Summary:
        //     The directory that serves as a common repository for application-specific data
        //     that is used by all users.
        CommonApplicationData = 35,
        //
        // Summary:
        //     The Windows directory or SYSROOT. This corresponds to the %windir% or %SYSTEMROOT%
        //     environment variables.
        Windows = 36,
        //
        // Summary:
        //     The System directory.
        System = 37,
        //
        // Summary:
        //     The program files directory. In a non-x86 process, passing System.Environment.SpecialFolder.ProgramFiles
        //     to the System.Environment.GetFolderPath(System.Environment.SpecialFolder) method
        //     returns the path for non-x86 programs. To get the x86 program files directory
        //     in a non-x86 process, use the System.Environment.SpecialFolder.ProgramFilesX86
        //     member.
        ProgramFiles = 38,
        //
        // Summary:
        //     The Windows System folder.
        SystemX86 = 41,
        //
        // Summary:
        //     The x86 Program Files folder.
        ProgramFilesX86 = 42,
        //
        // Summary:
        //     The directory for components that are shared across applications. To get the
        //     x86 common program files directory in a non-x86 process, use the System.Environment.SpecialFolder.ProgramFilesX86
        //     member.
        CommonProgramFiles = 43,
        //
        // Summary:
        //     The Program Files folder.
        CommonProgramFilesX86 = 44,
        //
        // Summary:
        //     The file system directory that contains documents that are common to all users.
        CommonDocuments = 46,
        //
        // Summary:
        //     The file system directory that serves as a repository for music files common
        //     to all users.
        CommonMusic = 53,
        //
        // Summary:
        //     The file system directory that serves as a repository for image files common
        //     to all users.
        CommonPictures = 54,
        //
        // Summary:
        //     The file system directory that serves as a repository for video files common
        //     to all users.
        CommonVideos = 55,
    }
}
