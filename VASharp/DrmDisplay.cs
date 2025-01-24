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
        /// <param name="stream">
        /// A <see cref="Stream"/> which represents the DRM GPU, such as <c>/dev/dri/renderD128</c></param>
        /// <param name="logger"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public DrmDisplay(FileStream stream, ILogger<DrmDisplay> logger)
            : base(logger)
        {
            this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
            
            this.display = DrmMethods.vaGetDisplayDRM((int)stream.SafeFileHandle.DangerousGetHandle());

            this.Initialize();
        }

        /// <inheritdoc/>
        public override void Dispose()
        {
            base.Dispose();

            this.stream.Dispose();
        }
    }
}