using VASharp.Native;

namespace VASharp
{
    /// <summary>
    /// The exception that is thrown when a libva error occurs.
    /// </summary>
    public class VAException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="VAException"/> class.
        /// </summary>
        /// <param name="status">
        /// A value of the <see cref="VAStatus"/> enumeration which represents the error.
        /// </param>
        public VAException(int status)
            : base(GetErrorMessage(status))
        {
            this.HResult = status;
        }

        /// <summary>
        /// Throws a <see cref="VAException"/> if <paramref name="status"/> represents
        /// an error.
        /// </summary>
        /// <param name="status">
        /// A VA status code.
        /// </param>
        public static void ThrowOnError(int status)
        {
            if (status != 0)
            {
                throw new VAException(status);
            }
        }

        private static unsafe string GetErrorMessage(int status)
        {
            return new string(Methods.vaErrorStr(status));
        }
    }
}
