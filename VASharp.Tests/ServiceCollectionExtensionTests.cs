using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace VASharp.Tests
{
    /// <summary>
    /// Tests the <see cref="ServiceCollectionExtensions"/> class.
    /// </summary>
    public class ServiceCollectionExtensionTests
    {
        /// <summary>
        /// A <see cref="VADisplay"/> object can be sourced from a <see cref="ServiceProvider"/> after
        /// <see cref="ServiceCollectionExtensions.AddVideoAcceleration(IServiceCollection, Action{VAOptions}?)"/>
        /// has been called.
        /// </summary>
        [Fact(Skip = "Requires OS-specific configuration")]
        public void AddVideoAcceleration_Works()
        {
            using var provider = new ServiceCollection()
                .AddLogging()
                .AddVideoAcceleration(
                (options) =>
                {
                    // For the time being, hardcode the paths to:
                    // - The va.dll and va_win32.dll libraries which can be installed via vcpkg
                    // - The drivers which can be downloaded at https://www.nuget.org/packages/Microsoft.Direct3D.VideoAccelerationCompatibilityPack/
                    options.LibraryPath = Path.GetFullPath("../../../../vcpkg_installed/x64-windows/bin/");
                    options.DriverPath = Path.GetFullPath("../../../../");
                })
                .BuildServiceProvider();

            var display = provider.GetRequiredService<VADisplay>();
        }
    }
}
