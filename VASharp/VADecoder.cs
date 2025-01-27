using Microsoft;
using Microsoft.Extensions.Logging;
using VASharp.Native;

namespace VASharp
{
    /// <summary>
    /// Supports decoding various media formats.
    /// </summary>
    public class VADecoder : IDisposableObservable
    {
        private readonly VADisplay display;
        private readonly ILogger<VADecoder> logger;

        private uint config = Methods.VA_INVALID_ID;
        private uint surface  = Methods.VA_INVALID_SURFACE;
        private VAContext? context = null;

        /// <summary>
        /// Initializes a new instance of the <see cref="VADecoder"/> class.
        /// </summary>
        /// <param name="display">
        /// The display to use when decoding.
        /// </param>
        /// <param name="logger">
        /// A logger to which to write diagnostic messages.
        /// </param>
        public VADecoder(VADisplay display, ILogger<VADecoder> logger)
        {
            this.display = display ?? throw new ArgumentNullException(nameof(display));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public bool IsDisposed
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the current rendering surface.  Available after <see cref="Initialize(VAProfile, VAFormat, int, int)"/> has been called.
        /// </summary>
        public uint Surface
        {
            get
            {
                Verify.NotDisposed(this);
                return this.surface;
            }
        }

        /// <summary>
        /// Gets the current rendering context.  Available after <see cref="Initialize(VAProfile, VAFormat, int, int)"/> has been called.
        /// </summary>
        public VAContext? Context
        {
            get
            {
                Verify.NotDisposed(this);
                return this.context;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (this.surface != Methods.VA_INVALID_SURFACE)
            {
                this.display.DestroySurface(this.surface);
                this.surface = Methods.VA_INVALID_SURFACE;
            }

            if (this.config != Methods.VA_INVALID_ID)
            {
                this.display.DestroyConfig(this.config);
                this.config = Methods.VA_INVALID_ID;
            }

            if (this.context != null)
            {
                this.context.Dispose();
                this.context = null;
            }

            this.IsDisposed = true;
        }

        /// <summary>
        /// Initializes the decoder.
        /// </summary>
        /// <param name="profile">
        /// The decoder profile.
        /// </param>
        /// <param name="format">
        /// The target pixel format.
        /// </param>
        /// <param name="width">
        /// The width of the decoded image.
        /// </param>
        /// <param name="height">
        /// The height of the decoded image.
        /// </param>
        public void Initialize(VAProfile profile, VAFormat format, int width, int height)
        {
            var profiles = display.QueryConfigProfiles();
            if (!profiles.Contains(profile))
            {
                throw new NotSupportedException();
            }

            var entryPoints = display.QueryConfigEntrypoints(profile);
            if (!entryPoints.Contains(VAEntrypoint.VAEntrypointVLD))
            {
                throw new NotSupportedException();
            }

            var supportedFormats = (VAFormat)display.GetConfigAttribute(profile, VAEntrypoint.VAEntrypointVLD, VAConfigAttribType.VAConfigAttribRTFormat);
            if (!supportedFormats.HasFlag(format))
            {
                throw new NotSupportedException();
            }
            
            this.config = this.display.CreateConfig(profile, VAEntrypoint.VAEntrypointVLD, Array.Empty<_VAConfigAttrib>());

            this.surface = this.display.CreateSurfaces(
                format,
                (uint)width,
                (uint)height);

            this.context = this.display.CreateContext(
                config,
                width,
                ((height + 15) / 16) * 16,
                VAContextFlags.VA_PROGRESSIVE,
                surface);
        }

        /// <summary>
        /// Renders a specific image.
        /// </summary>
        /// <param name="buffers">
        /// The buffers which contain the image to be rendered.
        /// </param>
        public void Render(params uint[] buffers)
        {
            Verify.NotDisposed(this);

            if (this.context == null)
            {
                throw new InvalidOperationException();
            }

            this.context.BeginPicture(surface);
            this.context.RenderPicture(buffers);
            this.context.EndPicture();
            this.display.SyncSurface(surface);
        }
    }
}
