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
            ThrowOnError(ret);

            this.Version = new Version(major_version, minor_version);
        }

        /// <summary>
        /// Cleans up all internal resources.
        /// </summary>
        protected void Terminate()
        {
            int ret = Methods.vaTerminate(this.display);
            ThrowOnError(ret);
        }

        protected static void ThrowOnError(int status)
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
        private static unsafe void OnInfo(void *user_context, sbyte *message)
        {
            var loggerHandle = GCHandle.FromIntPtr((nint)user_context);
            var logger = (ILogger)loggerHandle.Target!;
            var str = new string(message);

            logger.LogInformation(str);
        }
    }
}
