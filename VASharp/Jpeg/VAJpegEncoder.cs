using System.Diagnostics;
using Tmds.Linux;
using VASharp.Native;

namespace VASharp.Jpeg
{
    public class VAJpegEncoder
    {
        const int O_RDWR = 2;

        public VAJpegEncoder()
        {
        }

        public unsafe void Foo()
        {
            int fd = this.Open("/dev/dri/renderD128"u8);

            void* display = DrmMethods.vaGetDisplayDRM(fd);
            int major_version;
            int minor_version;
            
            int ret = Methods.vaInitialize(display, &major_version, &minor_version);
            Debug.Assert(ret == 0);

            ret = Methods.vaTerminate(display);
            Debug.Assert(ret == 0);

            this.Close(fd);
        }

        public unsafe int Open(ReadOnlySpan<byte> path)
        {
            int fd;

            fixed (byte* ptr = path)
            {
                if((fd = LibC.open(ptr, 0, O_RDWR)) < 0)
                {
                    // You need permissions on /dev/dri/renderD128; e.g.
                    // sudo usermod -a -G render $(whoami)
                    // newgrp render
                    throw new PlatformException();
                }
            }

            return fd;
        }

        public void Close(int fd)
        {
            if(LibC.close(fd) < 0)
            {
                throw new PlatformException();
            }
        }
    }
}