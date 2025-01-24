using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Xunit;

namespace VASharp.Tests
{
    public class Win32DisplayTests
    {
        public Win32DisplayTests()
        {
            // For the time being, hardcode the paths to:
            // - The va.dll and va_win32.dll libraries which can be installed via vcpkg
            // - The drivers which can be downloaded at https://www.nuget.org/packages/Microsoft.Direct3D.VideoAccelerationCompatibilityPack/
            NativeLibrary.Load(
                Path.GetFullPath("../../../../vcpkg_installed/x64-windows/bin/va.dll"));
            NativeLibrary.Load(
                Path.GetFullPath(@"../../../../vcpkg_installed/x64-windows/bin/va_win32.dll"));

            Environment.SetEnvironmentVariable(
                "LIBVA_DRIVERS_PATH",
                Path.GetFullPath("../../../../"));
        }

        /// <summary>
        /// The <see cref="DrmDisplay.Win32Display(ILogger{Win32Display})"/> constructor can be used to
        /// open a Win32 display device.
        /// </summary>
        [SkippableFact, SupportedOSPlatform("windows")]
        public void Constructor_OpensDisplay()
        {
            using var display = new Win32Display(NullLogger<Win32Display>.Instance);
            Assert.Equal("Mesa Gallium driver 23.3.0-devel for D3D12 (Intel(R) UHD Graphics)", display.VendorString);
        }
    }
}
