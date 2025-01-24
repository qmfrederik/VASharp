using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.InteropServices;
using Xunit;

namespace VASharp.Tests
{
    public class H264DecoderTests
    {
        public H264DecoderTests()
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

        [Fact]
        public void Foo()
        {
            var d = new Win32Display(NullLogger<Win32Display>.Instance);
            var profiles = d.QueryConfigProfiles();
            var m = new H264Decoder(d);
            m.Foo();
        }
    }
}
