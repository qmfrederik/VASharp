using Microsoft;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VASharp.Native;

namespace VASharp
{
    /// <summary>
    /// Represents a hardware accelerator which can perform video decode/encode/processing operations.
    /// </summary>
    public unsafe abstract class VADisplay : IDisposableObservable
    {
        protected readonly VAOptions options;
        protected readonly ILogger logger;
        protected GCHandle loggerHandle;

        protected void* display = null;

        /// <summary>
        /// Initializes a new instance of the <see cref="VADisplay"/> class.
        /// </summary>
        /// <param name="options">
        /// Options for the Video Acceleration library.
        /// </param>
        /// <param name="logger">
        /// A <see cref="ILogger"/> to which diagnostic messages will be logged.
        /// </param>
        protected VADisplay(VAOptions options, ILogger logger)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (options.LibraryPath != null)
            {
                NativeLibrary.SetDllImportResolver(typeof(VADisplay).Assembly, this.LibraryResolver);
            }

            if (options.DriverPath != null)
            {
                Environment.SetEnvironmentVariable("LIBVA_DRIVERS_PATH", options.DriverPath);
            }
        }

        protected virtual IntPtr LibraryResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            // On Windows, try to load "va.dll" in LibraryPath, if one was specified.
            if (libraryName == "va" && this.options.LibraryPath != null && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return NativeLibrary.Load(Path.Combine(this.options.LibraryPath, "va.dll"));
            }

            return IntPtr.Zero;
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
            if (!this.IsDisposed)
            {
                this.Terminate();
                this.loggerHandle.Free();
            }

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
            this.loggerHandle = GCHandle.Alloc(this.logger);

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
        /// Blocks until all pending operations on the render target have been completed. Upon return it is safe to use the render target for a different picture.
        /// </summary>
        /// <param name="surface">
        /// The surface for which to wait for pending operations to complete.
        /// </param>
        public void SyncSurface(uint surface)
        {
            Verify.NotDisposed(this);

            int ret = Methods.vaSyncSurface(
                this.display,
                surface);
            VAException.ThrowOnError(ret);
        }

        /// <summary>
        /// Derive an VAImage from an existing surface. This interface will derive a VAImage and corresponding image buffer from an existing VA Surface.
        /// The image buffer can then be mapped/unmapped for direct CPU access. This operation is only possible on implementations with direct rendering capabilities and internal surface formats that can be represented with a VAImage.
        /// When the operation is not possible this interface will return VA_STATUS_ERROR_OPERATION_FAILED. Clients should then fall back to using vaCreateImage + vaPutImage to accomplish the same task in an indirect manner.
        /// </summary>
        /// <remarks>
        /// When directly accessing a surface special care must be taken to insure proper synchronization with the graphics hardware.
        /// Clients should call vaQuerySurfaceStatus to insure that a surface is not the target of concurrent rendering or currently being displayed by an overlay.
        /// Additionally nothing about the contents of a surface should be assumed following a vaPutSurface. Implementations are free to modify the surface for scaling or subpicture blending within a call to vaPutImage.
        /// Calls to vaPutImage or vaGetImage using the same surface from which the image has been derived will return VA_STATUS_ERROR_SURFACE_BUSY. vaPutImage or vaGetImage with other surfaces is supported.
        /// An image created with vaDeriveImage should be freed with vaDestroyImage. The image and image buffer structures will be destroyed; however, the underlying surface will remain unchanged until freed with vaDestroySurfaces.
        /// </summary>
        /// <param name="surface">
        /// The surface for which to derive the image.
        /// </param>
        /// <returns>
        /// A <see cref="_VAImage"/> which represents the derived image.
        /// </returns>
        public _VAImage DeriveImage(uint surface)
        {
            Verify.NotDisposed(this);

            _VAImage image;
            image.image_id = Methods.VA_INVALID_ID;
            image.buf = Methods.VA_INVALID_ID;

            int ret = Methods.vaDeriveImage(
                this.display,
                surface,
                &image);
            VAException.ThrowOnError(ret);

            return image;
        }

        /// <summary>
        /// Frees all resources associated with an image.
        /// </summary>
        /// <param name="image">
        /// The image for which to free the associated resources.
        /// </param>
        public void DestroyImage(_VAImage image)
        {
            Verify.NotDisposed(this);

            int ret = Methods.vaDestroyImage(
                this.display,
                image.image_id);

            VAException.ThrowOnError(ret);
        }

        /// <summary>
        /// Map data store of the buffer into the client's address space.
        /// </summary>
        /// <param name="image">
        /// The image for which to map the buffer.
        /// </param>
        /// <returns>
        /// A <see cref="Span{byte}"/> which represents teh buffer in the client's address space.
        /// </returns>
        public Span<byte> MapBuffer(_VAImage image)
        {
            Verify.NotDisposed(this);
            
            void* bytes;
            int ret = Methods.vaMapBuffer(
                this.display,
                image.buf,
                &bytes);
            VAException.ThrowOnError(ret);

            return new Span<byte>(bytes, (int)image.data_size);
        }

        /// <summary>
        /// Unmaps the data store of a buffer from the client's address space.
        /// </summary>
        /// <param name="image">
        /// The image for which to unmap the buffer.
        /// </param>
        public void UnmapBuffer(_VAImage image)
        {
            Verify.NotDisposed(this);

            int ret = Methods.vaUnmapBuffer(
                this.display,
                image.buf);
            VAException.ThrowOnError(ret);
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
        private static unsafe void OnInfo(void* user_context, sbyte* message)
        {
            var loggerHandle = GCHandle.FromIntPtr((nint)user_context);
            var logger = (ILogger)loggerHandle.Target!;
            var str = new string(message);

            logger.LogInformation(str);
        }
    }
}
