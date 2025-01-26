using Microsoft.Extensions.DependencyInjection;
using System.Runtime.InteropServices;

namespace VASharp
{
    /// <summary>
    /// VASharp (libva/VA-API) extensions for <see cref="IServiceCollection"/>.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds the core Video Acceleration services to <paramref name="services"/>.
        /// </summary>
        /// <param name="services">
        /// The <see cref="IServiceCollection"/> to which to add the services.
        /// </param>
        /// <param name="configureOptions">
        /// An optional action used to configure <see cref="VAOptions">.
        /// </param>
        /// <returns>
        /// The same <paramref name="services"/> for chaining.
        /// </returns>
        public static IServiceCollection AddVideoAcceleration(
            this IServiceCollection services,
            Action<VAOptions>? configureOptions = null)
        {
            var options = new VAOptions();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                services.AddScoped<VADisplay, Win32Display>();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                services.AddScoped<VADisplay, DrmDisplay>();
            }

            configureOptions?.Invoke(options);

            if (options.LibraryPath != null && !Directory.Exists(options.LibraryPath))
            {
                throw new DirectoryNotFoundException($"The directory {options.LibraryPath} could not be found.");
            }

            if (options.DriverPath != null && !Directory.Exists(options.DriverPath))
            {
                throw new DirectoryNotFoundException($"The directory {options.DriverPath} could not be found.");
            }

            services.AddSingleton(options);
            return services;
        }
    }
}
