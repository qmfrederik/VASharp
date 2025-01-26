namespace VASharp
{
    /// <summary>
    /// Provides options for the Video Acceleration API.
    /// </summary>
    public record VAOptions
    {
        /// <summary>
        /// Gets or sets the path to the directory in which the va library is located.
        /// </summary>
        public string? LibraryPath { get; set; }

        /// <summary>
        /// Gets or sets the path to the director in which the va drivers are located.
        /// </summary>
        public string? DriverPath { get; set; }
    }
}
