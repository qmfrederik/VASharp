using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft;
using Microsoft.Extensions.Logging;
using VASharp.Native;

namespace VASharp
{
    /// <summary>
    /// Represents a hardware accelerator which can perform video decode/encode/processing operations.
    /// </summary>
    public unsafe abstract class VADisplay : IDisposableObservable
    {
        protected ILogger logger;
        protected GCHandle loggerHandle;

        protected void* display = null;

        /// <summary>
        /// Initializes a new instance of the <see cref="VADisplay"/> class.
        /// </summary>
        /// <param name="logger">
        /// A <see cref="ILogger"/> to which diagnostic messages will be logged.
        /// </param>
        protected VADisplay(ILogger logger)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Returns a pointer to a zero-terminated string describing some aspects of the VA implemenation on a specific hardware accelerator.
        /// </summary>
        /// <value>
        /// The format of the returned string is vendor specific and at the discretion of the implementer.
        /// </value>
        /// <example>
        /// Intel GMA500 - 2.0.0.32L.0005
        /// </example>
        public string VendorString
        {
            get
            {
                Verify.NotDisposed(this);
                return new string(Methods.vaQueryVendorString(this.display));
            }
        }

        /// <inheritdoc/>
        public virtual void Dispose()
        {
            this.Terminate();

            this.loggerHandle.Free();
            this.IsDisposed = true;
        }

        /// <summary>
        /// Gets the version of the libva library.
        /// </summary>
        public Version? Version { get; private set; }

        /// <summary>
        /// Initialize the library, and set up logging callbacks.
        /// </summary>
        protected void Initialize()
        {
            this.loggerHandle = GCHandle.Alloc(this.logger, GCHandleType.Pinned);

            Methods.vaSetErrorCallback(this.display, &OnError, (void*)GCHandle.ToIntPtr(this.loggerHandle));
            Methods.vaSetInfoCallback(this.display, &OnInfo, (void*)GCHandle.ToIntPtr(this.loggerHandle));
            int major_version;
            int minor_version;

            int ret = Methods.vaInitialize(this.display, &major_version, &minor_version);
            VAException.ThrowOnError(ret);

            this.Version = new Version(major_version, minor_version);
        }

        /// <summary>
        /// Cleans up all internal resources.
        /// </summary>
        protected void Terminate()
        {
            int ret = Methods.vaTerminate(this.display);
            VAException.ThrowOnError(ret);
        }

        /// <summary>
        /// Queries the display for supported profiles.
        /// </summary>
        /// <returns>
        /// An array of profiles supported by the display.
        /// </returns>
        public VAProfile[] QueryConfigProfiles()
        {
            int num_profiles = Methods.vaMaxNumProfiles(this.display);
            var profiles = stackalloc VAProfile[num_profiles];

            var ret = Methods.vaQueryConfigProfiles(this.display, profiles, &num_profiles);
            VAException.ThrowOnError(ret);

            return new Span<VAProfile>(profiles, num_profiles).ToArray();
        }

        /// <summary>
        /// Queries the display for supported entrypoints for a given profile.
        /// </summary>
        /// <param name="profile">
        /// The profile for which to list the supported entrypoints.
        /// </param>
        /// <returns>
        /// The entrypoints supported for <paramref name="profile"/>.
        /// </returns>
        public VAEntrypoint[] QueryConfigEntrypoints(VAProfile profile)
        {
            int num_entrypoints = Methods.vaMaxNumEntrypoints(this.display);
            var entrypoints = stackalloc VAEntrypoint[num_entrypoints];

            int ret = Methods.vaQueryConfigEntrypoints(
                this.display,
                profile,
                entrypoints,
                &num_entrypoints);
            VAException.ThrowOnError(ret);

            return new Span<VAEntrypoint>(entrypoints, num_entrypoints).ToArray();
        }

        /// <summary>
        /// Gets the value of <paramref name="attribute"/> for the given
        /// <paramref name="profile"/> / <paramref name="entrypoint"/> pair.
        /// </summary>
        /// <param name="profile">
        /// The profile for which to get the attribute value.
        /// </param>
        /// <param name="entrypoint">
        /// The entrypoint for which to get the attribute value.
        /// </param>
        /// <param name="attribute">
        /// The attribute for which to get the value.
        /// </param>
        /// <returns>
        /// The value of the requested attribute.
        /// </returns>
        /// <remarks>
        /// Unknown attributes or attributes that are not supported for the given 
        /// <paramref name="profile"/> / <paramref name="entrypoint"/> pair
        /// will have their value set to <see cref="Methods.VA_ATTRIB_NOT_SUPPORTED"/>.
        /// </remarks>
        public uint GetConfigAttribute(VAProfile profile, VAEntrypoint entrypoint, VAConfigAttribType attribute)
        {
            _VAConfigAttrib attrib = default;
            attrib.type = attribute;

            int ret = Methods.vaGetConfigAttributes(
                this.display,
                profile,
                entrypoint,
                &attrib,
                1);
            VAException.ThrowOnError(ret);

            return attrib.value;
        }

        /// <summary>
        /// Create a configuration for the video decode/encode/processing pipeline.
        /// </summary>
        /// <param name="profile">
        /// The profile of the processing pipeline.
        /// </param>
        /// <param name="entrypoint">
        /// The entrypoint to use.
        /// </param>
        /// <param name="attributes">
        /// Attribute values to pass on to the pipeline.
        /// </param>
        /// <returns>
        /// A handle to the newly created configuration.
        /// </returns>
        public uint CreateConfig(VAProfile profile, VAEntrypoint entrypoint, _VAConfigAttrib[] attributes)
        {
            uint config_id;

            fixed (_VAConfigAttrib* attrib_list = attributes)
            {
                int ret = Methods.vaCreateConfig(
                    this.display,
                    profile,
                    entrypoint,
                    attrib_list,
                    attributes.Length,
                    &config_id);
                VAException.ThrowOnError(ret);
            }

            return config_id;
        }

        /// <summary>
        /// Creates an array of surfaces.
        /// </summary>
        /// <param name="format">
        /// The desired surface format.
        /// </param>
        /// <param name="width">
        /// The surface width</param>
        /// <param name="height">
        /// The surface width</param>
        /// <returns>
        /// A handle to the newly created surface.
        /// </returns>
        public uint CreateSurfaces(VAFormat format, uint width, uint height)
        {
            uint surface_id;

            int ret = Methods.vaCreateSurfaces(
                this.display,
                (uint)format,
                width,
                height,
                &surface_id,
                1,
                null,
                0);
            VAException.ThrowOnError(ret);

            return surface_id;
        }

        /// <summary>
        /// Creates a context.
        /// </summary>
        /// <param name="configId">
        /// The configuration for the context.
        /// </param>
        /// <param name="width">
        /// The coded picture width.
        /// </param>
        /// <param name="height">
        /// The coded picture height.
        /// </param>
        /// <param name="flag">
        /// A combination of <see cref="VAContextFlags"/>.
        /// </param>
        /// <param name="renderTarget">
        /// A hint for render targets (surfaces) tied to the context.
        /// </param>
        /// <returns>
        /// A handle to the created context.
        /// </returns>
        public VAContext CreateContext(uint configId, int width, int height, VAContextFlags flag, uint renderTarget)
        {
            uint context_id;
            int ret = Methods.vaCreateContext(
                this.display,
                configId,
                width,
                height,
                (int)flag,
                &renderTarget,
                1,
                &context_id);
            VAException.ThrowOnError(ret);

            return new VAContext(this.display, context_id);
        }

        /// <summary>
        /// Frees resources associated with a given config.
        /// </summary>
        /// <param name="configId">
        /// The config for which to free the resources.
        /// </param>
        public void DestroyConfig(uint configId)
        {
            int ret = Methods.vaDestroyConfig(this.display, configId);
            VAException.ThrowOnError(ret);
        }

        /// <summary>
        /// Destroy resources associated with surfaces.
        /// </summary>
        /// <param name="surfaceId">
        /// The surfaces for which to free the associated resources.
        /// </param>
        /// <remarks>
        /// Surfaces can only be destroyed after all contexts using these surfaces have been destroyed.
        /// </remarks>
        public void DestroySurface(uint surfaceId)
        {
            int ret = Methods.vaDestroySurfaces(this.display, &surfaceId, 1);
            VAException.ThrowOnError(ret);
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static unsafe void OnError(void* user_context, sbyte* message)
        {
            var loggerHandle = GCHandle.FromIntPtr((nint)user_context);
            var logger = (ILogger)loggerHandle.Target!;
            var str = new string(message);

            logger.LogError(str);
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static unsafe void OnInfo(void *user_context, sbyte *message)
        {
            var loggerHandle = GCHandle.FromIntPtr((nint)user_context);
            var logger = (ILogger)loggerHandle.Target!;
            var str = new string(message);

            logger.LogInformation(str);
        }
    }
}
