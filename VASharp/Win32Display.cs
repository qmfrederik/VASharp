using Microsoft.Extensions.Logging;
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
        /// <param name="logger">
        /// The logger to use for this display.
        /// </param>
        [SupportedOSPlatform("windows")]
        public Win32Display(ILogger<Win32Display> logger)
            : base(logger)
        {
#if WITHOUT_WIN32
            throw new NotSupportedException("Win32Display is not supported on this build of VASharp.");
#else
            this.display = Win32Methods.vaGetDisplayWin32(null);

            this.Initialize();
#endif
        }
    }
}
