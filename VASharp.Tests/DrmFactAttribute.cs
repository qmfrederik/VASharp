using System.Runtime.InteropServices;
using Xunit;

namespace VASharp.Tests
{
    /// <summary>
    /// Indicate that the method is a fact (test) which requires a DRM device
    /// (<c>/dev/dri/render128</c>) to execute.  The fact (test) is skipped
    /// when not running on Linux or when a DRM device is not available.
    /// </summary>
    public class DrmFactAttribute : FactAttribute
    {
        /// <inheritdoc/>
        public override string Skip
        {
            get
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return "DRM devices are supported on Linux only.";
                }

                if (!File.Exists("/dev/dri/renderD128"))
                {
                    return "No DRM device found.";
                }

                return base.Skip;
            }

            set { base.Skip = value; }
        }
    }
}