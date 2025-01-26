using System.Runtime.Versioning;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace VASharp.Tests
{
    /// <summary>
    /// Tests the <see cref="DrmDisplay"/> class.
    /// </summary>
    public class DrmDisplayTests
    {
        /// <summary>
        /// The <see cref="DrmDisplay.DrmDisplay(VAOptions, ILogger{DrmDisplay})"/> constructor validates its arguments.
        /// </summary>
        [SkippableFact, SupportedOSPlatform("linux")]
        public void Constructor_ValidatesArguments()
        {
            using var file = File.Open("test.txt", FileMode.Create);

            Assert.Throws<ArgumentNullException>(() => new DrmDisplay(null, NullLogger<DrmDisplay>.Instance));
            Assert.Throws<ArgumentNullException>(() => new DrmDisplay(new VAOptions(), null));
        }

        /// <summary>
        /// The <see cref="DrmDisplay.DrmDisplay(VAOptions, ILogger{DrmDisplay})"/> constructor throws an exception
        /// when a <see cref="FileStream"/> is passed which does not represent a DRI device.
        /// </summary>
        [SkippableFact, SupportedOSPlatform("linux")]
        public void Constructor_ThrowsOnInvalidFile()
        {
            File.WriteAllText("test.txt", "this is a test file");
            var ex = Assert.Throws<VAException>(() => new DrmDisplay(new VAOptions() { DrmPath = "test.txt"}, NullLogger<DrmDisplay>.Instance));
            Assert.Equal("invalid VADisplay", ex.Message);
        }

        /// <summary>
        /// The <see cref="DrmDisplay.DrmDisplay(FileStream, ILogger{DrmDisplay})"/> constructor can be used to
        /// open the <c>/dev/dri/renderD128</c> device.
        /// </summary>
        [SkippableFact(typeof(DirectoryNotFoundException)), SupportedOSPlatform("linux")]
        public void Constructor_OpensDisplay()
        {
            using var display = new DrmDisplay(new VAOptions(), NullLogger<DrmDisplay>.Instance);
            
            Assert.Equal(new Version(1, 20), display.Version);
            var s = display.VendorString;
            Assert.Equal("Intel iHD driver for Intel(R) Gen Graphics - 24.1.0 ()", display.VendorString);
        }
    }
}