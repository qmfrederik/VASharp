using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using VASharp.Native;

namespace VASharp
{
    /// <summary>
    /// A <see cref="VADisplay"/> which represents a graphical device which is managed by the Linux Direct Rendering Manager (DRM).
    /// </summary>
    [SupportedOSPlatform("linux")]
    public unsafe class DrmDisplay : VADisplay
    {
        private readonly FileStream stream;

        /// <summary>
        /// Initializes a new instance of the <see cref="DrmDisplay"/> class.
        /// </summary>
        /// <param name="options">
        /// Options for the Video Acceleration library.
        /// </param>
        /// <param name="logger">
        /// The logger to use for this display.
        /// </param>
        public DrmDisplay(VAOptions options, ILogger<DrmDisplay> logger)
            : base(options, logger)
        {
            this.stream = File.Open(options.DrmPath!, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

#if WITHOUT_DRM
            throw new NotSupportedException("DrmDisplay is not supported on this build of VASharp.");
#else
            this.display = DrmMethods.vaGetDisplayDRM((int)stream.SafeFileHandle.DangerousGetHandle());
            this.Initialize();
#endif
        }

        /// <inheritdoc/>
        public override void Dispose()
        {
            base.Dispose();

            this.stream.Dispose();
        }
    }
}