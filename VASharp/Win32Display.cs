using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using VASharp.Native;

namespace VASharp
{
    /// <summary>
    /// A <see cref="VADisplay"/> which represents a Win32 graphical device.
    /// </summary>
    public unsafe class Win32Display : VADisplay
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Win32Display"/> class.
        /// </summary>
        /// <param name="options">
        /// Options for the Video Accelleration library.
        /// </param>
        /// <param name="logger">
        /// The logger to use for this display.
        /// </param>
        [SupportedOSPlatform("windows")]
        public Win32Display(VAOptions options, ILogger<Win32Display> logger)
            : base(options, logger)
        {
#if WITHOUT_WIN32
            throw new NotSupportedException("Win32Display is not supported on this build of VASharp.");
#else
            this.display = Win32Methods.vaGetDisplayWin32(null);

            this.Initialize();
#endif
        }

        protected override nint LibraryResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            // On Windows, try to load "va_win32.dll" in LibraryPath, if one was specified.
            if (libraryName == "va_win32" && this.options.LibraryPath != null && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return NativeLibrary.Load(Path.Combine(this.options.LibraryPath, "va_win32.dll"));
            }

            return base.LibraryResolver(libraryName, assembly, searchPath);
        }
    }
}
