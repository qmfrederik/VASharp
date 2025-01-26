﻿using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.Versioning;
using VASharp.Native;
using Xunit;

namespace VASharp.Tests
{
    public unsafe class Mpeg2DecoderTests
    {
        // Data dump of a 16x16 MPEG2 video clip with a single frame
        private byte[] mpeg2_clip =
            {
                0x00, 0x00, 0x01, 0xb3, 0x01, 0x00, 0x10, 0x13, 0xff, 0xff, 0xe0, 0x18, 0x00, 0x00, 0x01, 0xb5,
                0x14, 0x8a, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x01, 0xb8, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00,
                0x01, 0x00, 0x00, 0x0f, 0xff, 0xf8, 0x00, 0x00, 0x01, 0xb5, 0x8f, 0xff, 0xf3, 0x41, 0x80, 0x00,
                0x00, 0x01, 0x01, 0x13, 0xe1, 0x00, 0x15, 0x81, 0x54, 0xe0, 0x2a, 0x05, 0x43, 0x00, 0x2d, 0x60,
                0x18, 0x01, 0x4e, 0x82, 0xb9, 0x58, 0xb1, 0x83, 0x49, 0xa4, 0xa0, 0x2e, 0x05, 0x80, 0x4b, 0x7a,
                0x00, 0x01, 0x38, 0x20, 0x80, 0xe8, 0x05, 0xff, 0x60, 0x18, 0xe0, 0x1d, 0x80, 0x98, 0x01, 0xf8,
                0x06, 0x00, 0x54, 0x02, 0xc0, 0x18, 0x14, 0x03, 0xb2, 0x92, 0x80, 0xc0, 0x18, 0x94, 0x42, 0x2c,
                0xb2, 0x11, 0x64, 0xa0, 0x12, 0x5e, 0x78, 0x03, 0x3c, 0x01, 0x80, 0x0e, 0x80, 0x18, 0x80, 0x6b,
                0xca, 0x4e, 0x01, 0x0f, 0xe4, 0x32, 0xc9, 0xbf, 0x01, 0x42, 0x69, 0x43, 0x50, 0x4b, 0x01, 0xc9,
                0x45, 0x80, 0x50, 0x01, 0x38, 0x65, 0xe8, 0x01, 0x03, 0xf3, 0xc0, 0x76, 0x00, 0xe0, 0x03, 0x20,
                0x28, 0x18, 0x01, 0xa9, 0x34, 0x04, 0xc5, 0xe0, 0x0b, 0x0b, 0x04, 0x20, 0x06, 0xc0, 0x89, 0xff,
                0x60, 0x12, 0x12, 0x8a, 0x2c, 0x34, 0x11, 0xff, 0xf6, 0xe2, 0x40, 0xc0, 0x30, 0x1b, 0x7a, 0x01,
                0xa9, 0x0d, 0x00, 0xac, 0x64
            };

        private _VAPictureParameterBufferMPEG2 pictureParameter =
            new _VAPictureParameterBufferMPEG2()
            {
                horizontal_size = 16,
                vertical_size = 16,
                forward_reference_picture = 0xffffffff,
                backward_reference_picture = 0xffffffff,
                picture_coding_type = 1,
                f_code = 0xffff,
                picture_coding_extension =
                {
                    bits =
                    {
                        intra_dc_precision = 0,
                        picture_structure = 3,
                        top_field_first = 0,
                        frame_pred_frame_dct = 1,
                        concealment_motion_vectors = 0,
                        q_scale_type = 0,
                        intra_vlc_format = 0,
                        alternate_scan = 0,
                        repeat_first_field = 0,
                        progressive_frame = 1,
                        is_first_field = 1
                    }
                }
            };

        private _VASliceParameterBufferMPEG2 sliceParameter =
            new _VASliceParameterBufferMPEG2()
            {
                slice_data_size = 150,
                slice_data_offset = 0,
                slice_data_flag = 0,
                macroblock_offset = 38, /* 4byte + 6bits=38bits */
                slice_horizontal_position = 0,
                slice_vertical_position = 0,
                quantiser_scale_code = 2,
                intra_slice_flag = 0
            };

        private byte[] intra_quantiser_matrix =
            {
                8, 16, 16, 19, 16, 19, 22, 22,
                22, 22, 22, 22, 26, 24, 26, 27,
                27, 27, 26, 26, 26, 26, 27, 27,
                27, 29, 29, 29, 34, 34, 34, 29,
                29, 29, 27, 27, 29, 29, 32, 32,
                34, 34, 37, 38, 37, 35, 35, 34,
                35, 38, 38, 40, 40, 40, 48, 48,
                46, 46, 56, 56, 58, 69, 69, 83
            };
        private byte[] non_intra_quantiser_matrix = new byte[16];

        [DrmFact, SupportedOSPlatform("linux")]
        public void VADisplay_CanDecodeMpeg2()
        {
            const VAProfile Profile = VAProfile.VAProfileMPEG2Main;
            const VAFormat Format = VAFormat.VA_RT_FORMAT_YUV420;
            const int Width = 16;
            const int Height = 16;

            using var display = new DrmDisplay(new VAOptions(), NullLogger<DrmDisplay>.Instance);

            var profiles = display.QueryConfigProfiles();
            Assert.Contains(VAProfile.VAProfileMPEG2Main, profiles);

            var entryPoints = display.QueryConfigEntrypoints(Profile);
            Assert.Contains(VAEntrypoint.VAEntrypointVLD, entryPoints);

            var format = (VAFormat)display.GetConfigAttribute(Profile, VAEntrypoint.VAEntrypointVLD, VAConfigAttribType.VAConfigAttribRTFormat);
            Assert.True(format.HasFlag(Format));

            var config = display.CreateConfig(Profile, VAEntrypoint.VAEntrypointVLD, Array.Empty<_VAConfigAttrib>());

            var surface = display.CreateSurfaces(
                Format,
                Width,
                Height);

            var context = display.CreateContext(
                config,
                Width,
                ((Height + 15) / 16) * 16,
                VAContextFlags.VA_PROGRESSIVE,
                surface);

            var iqMatrix =
                new _VAIQMatrixBufferMPEG2()
                {
                    load_intra_quantiser_matrix = 1,
                    load_non_intra_quantiser_matrix = 1,
                    load_chroma_intra_quantiser_matrix = 0,
                    load_chroma_non_intra_quantiser_matrix = 0,
                };

            this.intra_quantiser_matrix.CopyTo(new Span<byte>(iqMatrix.intra_quantiser_matrix, 64));
            this.non_intra_quantiser_matrix.CopyTo(new Span<byte>(iqMatrix.non_intra_quantiser_matrix, 16));

            fixed (byte* clip = mpeg2_clip)
            fixed (byte* intraQuantiserMatrix = intra_quantiser_matrix)
            {
                var pictureParameterBuffer = context.CreateBuffer(VABufferType.VAPictureParameterBufferType, ref pictureParameter);
                var iqMatrixBuffer = context.CreateBuffer(VABufferType.VAIQMatrixBufferType, ref iqMatrix);
                var sliceParameterbuffer = context.CreateBuffer(VABufferType.VASliceParameterBufferType, ref sliceParameter);

                var sliceDataBuffer = context.CreateBuffer(
                    VABufferType.VASliceDataBufferType,
                    clip + 0x2f,
                    0xc4 - 0x2f + 1);

                context.BeginPicture(surface);
                context.RenderPicture(pictureParameterBuffer);
                context.RenderPicture(iqMatrixBuffer);
                context.RenderPicture(sliceParameterbuffer);
                context.RenderPicture(sliceDataBuffer);

                context.EndPicture();
            }

            display.SyncSurface(surface);
            
            var image = display.DeriveImage(surface);
            Assert.Equal(Height, image.height);
            Assert.Equal(Width, image.width);

            var bytes = display.MapBuffer(image);

#if HAVE_YUV
            byte* argb = stackalloc byte[Width * 4 * Height];

            // Use libyuv to convert the pixel in nv12 format to ARGB format
            fixed(byte* raw = bytes)
            {
                int ret = Yuv.NV12ToARGB(
                    src_y: raw + image.offsets[0],
                    src_stride_y: (int)image.pitches[0],
                    src_uv: raw + image.offsets[1],
                    src_stride_uv: (int)image.pitches[0],
                    dst_argb: argb,
                    dst_stride_argb: Width * 4,
                    width: Width,
                    height: Height);
            }
#endif

            display.UnmapBuffer(image);

            display.DestroyImage(image);

            display.DestroySurface(surface);
            display.DestroyConfig(config);
        }
    }
}
