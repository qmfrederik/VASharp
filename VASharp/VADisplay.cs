using Microsoft;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
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

        public void* Handle => this.display;

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
            ThrowOnError(ret);

            this.Version = new Version(major_version, minor_version);
        }

        public VAProfile[] QueryConfigProfiles()
        {
            int num_profiles = Methods.vaMaxNumProfiles(this.display);
            var profiles = stackalloc VAProfile[num_profiles];

            var ret = Methods.vaQueryConfigProfiles(this.display, profiles, &num_profiles);
            ThrowOnError(ret);

            return new Span<VAProfile>(profiles, num_profiles).ToArray();
        }

        public VAEntrypoint[] QueryConfigEntrypoints(VAProfile profile)
        {
            int num_entrypoints = Methods.vaMaxNumEntrypoints(this.display);
            var entrypoints = stackalloc VAEntrypoint[num_entrypoints];

            int ret = Methods.vaQueryConfigEntrypoints(
                this.display,
                profile,
                entrypoints,
                &num_entrypoints);
            ThrowOnError(ret);

            return new Span<VAEntrypoint>(entrypoints, num_entrypoints).ToArray();
        }

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
                ThrowOnError(ret);
            }

            return config_id;
        }

        public uint CreateSurfaces(uint format, uint width, uint height)
        {
            uint surface_id;

            int ret = Methods.vaCreateSurfaces(
                this.Handle,
                format,
                width,
                height,
                &surface_id,
                1,
                null,
                0);
            ThrowOnError(ret);

            return surface_id;
        }

        public uint CreateContext(uint configId, int width, int height, int flag, uint renderTarget)
        {
            uint context_id;
            int ret = Methods.vaCreateContext(
                this.Handle,
                configId,
                width,
                height,
                flag,
                &renderTarget,
                1,
                &context_id);
            ThrowOnError(ret);

            return context_id;
        }

        public uint CreateBuffer<T>(uint context, VABufferType type, ref T value)
            where T : unmanaged
        {
            uint buf_id;
            var x= sizeof(_VAPictureParameterBufferH264);

            fixed (T* v = &value)
            {
                int ret = Methods.vaCreateBuffer(
                    this.Handle,
                    context,
                    type,
                    (uint)sizeof(T),
                    1,
                    v,
                    &buf_id);
                ThrowOnError(ret);
            }

            return buf_id;
        }

        public void BeginPicture(uint context, uint renderTarget)
        {
            int ret = Methods.vaBeginPicture(
                this.Handle,
                context,
                renderTarget);
            ThrowOnError(ret);
        }

        public void RenderPicture(uint context, uint bufferId)
        {
            int ret = Methods.vaRenderPicture(
                this.Handle,
                context,
                &bufferId,
                1);
            ThrowOnError(ret);
        }

        public void EndPicture(uint context)
        {
            int ret = Methods.vaEndPicture(
                this.Handle,
                context);
            ThrowOnError(ret);
        }

        public void DestroyConfig(uint configId)
        {
            int ret = Methods.vaDestroyConfig(this.Handle, configId);
            ThrowOnError(ret);
        }

        public void DestroySurface(uint surfaceId)
        {
            int ret = Methods.vaDestroySurfaces(this.Handle, &surfaceId, 1);
            ThrowOnError(ret);
        }

        public void DestroyContext(uint contextId)
        {
            int ret = Methods.vaDestroyContext(this.Handle, contextId);
            ThrowOnError(ret);
        }

        /// <summary>
        /// Cleans up all internal resources.
        /// </summary>
        protected void Terminate()
        {
            int ret = Methods.vaTerminate(this.display);
            ThrowOnError(ret);
        }

        public static void ThrowOnError(int status)
        {
            if (status != 0)
            {
                throw new VAException(status);
            }
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
