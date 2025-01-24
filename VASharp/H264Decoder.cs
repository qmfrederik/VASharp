using System.Diagnostics;
using VASharp.Native;

namespace VASharp
{
    public unsafe class H264Decoder
    {
        private readonly VADisplay display;

        public H264Decoder(VADisplay display)
        {
            this.display = display ?? throw new ArgumentNullException(nameof(display));
        }

        public void Foo()
        {
            // See MPEG decode as an example
            var entrypoints = this.display.QueryConfigEntrypoints(VAProfile.VAProfileH264High);

            if (!entrypoints.Contains(VAEntrypoint.VAEntrypointVLD))
            {
                throw new NotSupportedException();
            }

            _VAConfigAttrib attrib;
            attrib.type = VAConfigAttribType.VAConfigAttribRTFormat;
            int ret = Methods.vaGetConfigAttributes(
                this.display.Handle,
                VAProfile.VAProfileH264High,
                VAEntrypoint.VAEntrypointVLD,
                &attrib,
                1);
        }
    }
}
