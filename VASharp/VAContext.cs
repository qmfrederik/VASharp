using Microsoft;
using VASharp.Native;

namespace VASharp
{
    public unsafe class VAContext : IDisposableObservable
    {
        private readonly void* display;
        private readonly uint handle;

        public VAContext(void* display, uint handle)
        {
            this.display = display;
            this.handle = handle;
        }

        /// <inheritdoc/>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Creates a buffer of <paramref name="type"/> with initial data <paramref name="value"/>.
        /// </summary>
        /// <typeparam name="T">
        /// The type of data to store in the buffer.
        /// </typeparam>
        /// <param name="type">
        /// The type of data to store in the buffer.
        /// </param>
        /// <param name="value">
        /// The initial data in the buffer.
        /// </param>
        /// <returns>
        /// A handle to the buffer.
        /// </returns>
        public uint CreateBuffer<T>(VABufferType type, ref T value)
            where T : unmanaged
        {
            Verify.NotDisposed(this);

            uint buf_id;
            var x = sizeof(_VAPictureParameterBufferH264);

            fixed (T* v = &value)
            {
                int ret = Methods.vaCreateBuffer(
                    this.display,
                    this.handle,
                    type,
                    (uint)sizeof(T),
                    1,
                    v,
                    &buf_id);
                VAException.ThrowOnError(ret);
            }

            return buf_id;
        }

        public uint CreateBuffer(VABufferType type, Span<byte> data)
        {
            Verify.NotDisposed(this);

            uint buf_id;
            fixed(byte* value = data)
            {
                int ret = Methods.vaCreateBuffer(
                    this.display,
                    this.handle,
                    type,
                    (uint)data.Length,
                    1,
                    (void*)value,
                    &buf_id);
            }

            return buf_id;
        }

        public void BeginPicture(uint renderTarget)
        {
            Verify.NotDisposed(this);

            int ret = Methods.vaBeginPicture(
                this.display,
                this.handle,
                renderTarget);
            VAException.ThrowOnError(ret);
        }

        /// <summary>
        /// Send video decode, encode or processing buffers to the server.
        /// </summary>
        /// <param name="bufferId">
        /// The buffer to send to the server.
        /// </param>
        public void RenderPicture(params uint[] bufferIds)
        {
            Verify.NotDisposed(this);

            ArgumentNullException.ThrowIfNull(bufferIds);

            fixed (uint* ids = bufferIds)
            {
                int ret = Methods.vaRenderPicture(
                    this.display,
                    this.handle,
                    ids,
                    bufferIds.Length);

                VAException.ThrowOnError(ret);
            }
        }

        /// <summary>
        /// Make the end of rendering for a picture. The server should start processing
        /// all pending operations for this surface. 
        /// </summary>
        /// <remarks>
        /// This call is non-blocking. The client can start another Begin/Render/End sequence
        /// on a different render target.
        /// </remarks>
        public void EndPicture()
        {
            Verify.NotDisposed(this);

            int ret = Methods.vaEndPicture(
                this.display,
                this.handle);
            VAException.ThrowOnError(ret);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            int ret = Methods.vaDestroyContext(this.display, this.handle);
            VAException.ThrowOnError(ret);

            this.IsDisposed = true;
        }
    }
}
