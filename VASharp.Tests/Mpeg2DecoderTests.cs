using Microsoft.Extensions.DependencyInjection;
using System.Runtime.Versioning;
using VASharp.Native;
using Xunit;

namespace VASharp.Tests
{
    public unsafe class Mpeg2DecoderTests : DecoderTests
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

            var provider = this.GetServiceProvider();
            using var display = provider.GetRequiredService<VADisplay>();
            using var decoder = provider.GetRequiredService<VADecoder>();

            decoder.Initialize(
                Profile,
                Format,
                Width,
                Height);
            Assert.NotNull(decoder.Context);

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

            var pictureParameterBuffer = decoder.Context.CreateBuffer(VABufferType.VAPictureParameterBufferType, ref pictureParameter);
            var iqMatrixBuffer = decoder.Context.CreateBuffer(VABufferType.VAIQMatrixBufferType, ref iqMatrix);
            var sliceParameterbuffer = decoder.Context.CreateBuffer(VABufferType.VASliceParameterBufferType, ref sliceParameter);

            var sliceDataBuffer = decoder.Context.CreateBuffer(
                VABufferType.VASliceDataBufferType,
                new Span<byte>(mpeg2_clip, 0x2f, 0xc4 - 0x2f + 1));

            decoder.Render(
                pictureParameterBuffer,
                iqMatrixBuffer,
                sliceParameterbuffer,
                sliceDataBuffer);
            
            var image = display.DeriveImage(decoder.Surface);
            Assert.NotEqual(Methods.VA_INVALID_ID, image.image_id);
            Assert.NotEqual(Methods.VA_INVALID_ID, image.buf);
            Assert.Equal((uint)Methods.VA_FOURCC_NV12, image.format.fourcc);
            Assert.Equal(Height, image.height);
            Assert.Equal(Width, image.width);

            this.SaveImage(display, image, "mpeg2.rgb");

            display.DestroyImage(image);
        }
    }
}
