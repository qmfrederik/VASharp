using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using VASharp.Native;

namespace VASharp.Tests
{
    public partial class DecoderTests
    {
        static DecoderTests()
        {
            const string YuvPath = "../../../../vcpkg_installed/x64-windows/bin/libyuv.dll";

            // When on Windows, load libyuv if available
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                && File.Exists(YuvPath))
            {
                NativeLibrary.TryLoad(YuvPath, out nint _);
            }
        }

        public unsafe void SaveImage(VADisplay display, _VAImage image, string name)
        {      
            var bytes = display.MapBuffer(image);

#if HAVE_YUV
            Span<byte> argb = stackalloc byte[image.width * 4 * image.height];

            // Use libyuv to convert the pixel in nv12 format to ARGB format
            fixed(byte* raw = bytes)
            fixed(byte* rawArgb = argb)
            {
                int ret = Yuv.NV12ToARGB(
                    src_y: raw + image.offsets[0],
                    src_stride_y: (int)image.pitches[0],
                    src_uv: raw + image.offsets[1],
                    src_stride_uv: (int)image.pitches[1],
                    dst_argb: rawArgb,
                    dst_stride_argb: image.width * 4,
                    width: image.width,
                    height: image.height);

#if NET9_0_OR_GREATER
                File.WriteAllBytes(name, argb);
#else
                File.WriteAllBytes(name, argb.ToArray());
#endif
            }
#endif

            display.UnmapBuffer(image);
        }
        public IServiceProvider GetServiceProvider()
            => new ServiceCollection()
                .AddLogging()
                .AddVideoAcceleration(
                (options) =>
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        // For the time being, hardcode the paths to:
                        // - The va.dll and va_win32.dll libraries which can be installed via vcpkg
                        // - The drivers which can be downloaded at https://www.nuget.org/packages/Microsoft.Direct3D.VideoAccelerationCompatibilityPack/
                        options.LibraryPath = Path.GetFullPath("../../../../vcpkg_installed/x64-windows/bin/");
                        options.DriverPath = Path.GetFullPath("../../../../");
                    }
                })
                .BuildServiceProvider();
    }
}
