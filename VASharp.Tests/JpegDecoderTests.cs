using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using VASharp.Native;
using Xunit;

namespace VASharp.Tests
{
    public unsafe class JpegDecoderTests : DecoderTests
    {
        const int Width = 320;
        const int Height = 240;
        const int DataOffset = 363; 
        const int DataSize = 24118;

        _VAPictureParameterBufferJPEGBaseline pictureParameters = new()
        {
            picture_width = Width,
            picture_height = Height,
            components = new ()
            {
                e0 = new ()
                {
                    component_id = 1,
                    h_sampling_factor = 2,
                    v_sampling_factor = 2,
                    quantiser_table_selector = 0,
                },
                e1 = new()
                {                    
                    component_id = 2,
                    h_sampling_factor = 1,
                    v_sampling_factor = 1,
                    quantiser_table_selector = 1,
                },
                e2 = new()
                {
                    component_id = 3,
                    h_sampling_factor = 1,
                    v_sampling_factor = 1,
                    quantiser_table_selector = 1,
                }
            },
            num_components = 3,
        };

        _VASliceParameterBufferJPEGBaseline sliceParameters = new()
        {
            slice_data_size = DataSize,
            slice_data_offset = 0,
            slice_horizontal_position = 0,
            slice_vertical_position = 0,
            components = new(){
                e0 = new() {
                    component_selector = 1,
                    dc_table_selector = 0,
                    ac_table_selector = 0,
                },
                e1 = new() {
                    component_selector = 2,
                    dc_table_selector = 1,
                    ac_table_selector = 1,
                },
                e2 = new() {
                    component_selector = 3,
                    dc_table_selector = 1,
                    ac_table_selector = 1,
                },
            },
            num_components = 3,
            restart_interval = 0,
            num_mcus = 300,
        };   

        _VAIQMatrixBufferJPEGBaseline iqMatrix;
        _VAHuffmanTableBufferJPEGBaseline huffmanTable;

        public JpegDecoderTests()
        {
            fixed(byte* load_quantiser_table = iqMatrix.load_quantiser_table)
            fixed(byte* quantiser_table = iqMatrix.quantiser_table )
            {
                new byte[] { 1, 1 }
                    .CopyTo(new Span<byte>(load_quantiser_table, 2));

                new byte[]
                {
                    0x05, 0x03, 0x04, 0x04, 0x04, 0x03, 0x05, 0x04,
                    0x04, 0x04, 0x05, 0x05, 0x05, 0x06, 0x07, 0x0c,
                    0x08, 0x07, 0x07, 0x07, 0x07, 0x0f, 0x0b, 0x0b,
                    0x09, 0x0c, 0x11, 0x0f, 0x12, 0x12, 0x11, 0x0f,
                    0x11, 0x11, 0x13, 0x16, 0x1c, 0x17, 0x13, 0x14,
                    0x1a, 0x15, 0x11, 0x11, 0x18, 0x21, 0x18, 0x1a,
                    0x1d, 0x1d, 0x1f, 0x1f, 0x1f, 0x13, 0x17, 0x22,
                    0x24, 0x22, 0x1e, 0x24, 0x1c, 0x1e, 0x1f, 0x1e,

                    0x05, 0x03, 0x04, 0x04, 0x04, 0x03, 0x05, 0x04,
                    0x04, 0x04, 0x05, 0x05, 0x05, 0x06, 0x07, 0x0c,
                    0x08, 0x07, 0x07, 0x07, 0x07, 0x0f, 0x0b, 0x0b,
                    0x09, 0x0c, 0x11, 0x0f, 0x12, 0x12, 0x11, 0x0f,
                    0x11, 0x11, 0x13, 0x16, 0x1c, 0x17, 0x13, 0x14,
                    0x1a, 0x15, 0x11, 0x11, 0x18, 0x21, 0x18, 0x1a,
                    0x1d, 0x1d, 0x1f, 0x1f, 0x1f, 0x13, 0x17, 0x22,
                    0x24, 0x22, 0x1e, 0x24, 0x1c, 0x1e, 0x1f, 0x1e
                }.CopyTo(new Span<byte>(quantiser_table, 2 * 8 * 8));
            }

            fixed(byte* load_huffman_table = this.huffmanTable.load_huffman_table)
            {
                new byte[] { 1, 1 }
                    .CopyTo(new Span<byte>(load_huffman_table, 2));
            }
            
            fixed(byte* num_dc_codes = this.huffmanTable.huffman_table.e0.num_dc_codes)
            fixed(byte* dc_values = this.huffmanTable.huffman_table.e0.dc_values)
            fixed(byte* num_ac_codes = this.huffmanTable.huffman_table.e0.num_ac_codes)
            fixed(byte* ac_values = this.huffmanTable.huffman_table.e0.ac_values)
            {
                new byte[] 
                {
                    0x00, 0x02, 0x03, 0x01, 0x01, 0x01, 0x01, 0x01,
                }.CopyTo(new Span<byte>(num_dc_codes, 8));
                new byte[]
                {
                    0x04, 0x05, 0x03, 0x06, 0x07, 0x02, 0x08, 0x01,
                    0x00, 0x09,
                }.CopyTo(new Span<byte>(dc_values, 10));
                new byte[] 
                {
                    0x00, 0x02, 0x01, 0x03, 0x02, 0x04, 0x04, 0x05,
                    0x02, 0x04, 0x03, 0x07, 0x03, 0x04, 0x03, 0x01
                }.CopyTo(new Span<byte>(num_ac_codes, 2 * 8));
                new byte[]
                {
                    0x01, 0x02, 0x03, 0x04, 0x05, 0x11, 0x00, 0x21,
                    0x06, 0x12, 0x31, 0x41, 0x13, 0x22, 0x51, 0x61,
                    0x07, 0x14, 0x32, 0x71, 0x81, 0x91, 0xa1, 0x15,
                    0x23, 0x42, 0xb1, 0x52, 0xc1, 0xd1, 0x08, 0x24,
                    0x33, 0x62, 0xe1, 0xf0, 0xf1, 0x16, 0x43, 0x72,
                    0x25, 0x26, 0x34, 0x82, 0x17, 0x53, 0x83, 0x93,
                }.CopyTo(new Span<byte>(ac_values, 6 * 8));
            }
            
            fixed(byte* num_dc_codes = this.huffmanTable.huffman_table.e1.num_dc_codes)
            fixed(byte* dc_values = this.huffmanTable.huffman_table.e1.dc_values)
            fixed(byte* num_ac_codes = this.huffmanTable.huffman_table.e1.num_ac_codes)
            fixed(byte* ac_values = this.huffmanTable.huffman_table.e1.ac_values)
            {
                new byte[] 
                {
                    0x00, 0x02, 0x03, 0x01, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
                }.CopyTo(new Span<byte>(num_dc_codes, 2 * 8));
                new byte[]
                {
                    0x02, 0x03, 0x00, 0x01, 0x04, 0x05,
                }.CopyTo(new Span<byte>(dc_values, 6));
                new byte[] 
                {
                    0x00, 0x02, 0x02, 0x01, 0x04, 0x02, 0x02, 0x02,
                    0x01, 0x03, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00
                }.CopyTo(new Span<byte>(num_ac_codes, 2 * 8));
                new byte[]
                {
                    0x00, 0x01, 0x02, 0x11, 0x03, 0x04, 0x12, 0x21,
                    0x31, 0x22, 0x41, 0x13, 0x32, 0x05, 0x51, 0x14,
                    0x23, 0x42, 0x61, 0x33, 0x52, 0x91, 0xb1, 0xc1,
                }.CopyTo(new Span<byte>(ac_values, 3 * 8));
            }
        }

        [DrmFact, SupportedOSPlatform("linux")]
        public void JpegDecoding_Works()
        {
            const VAProfile Profile = VAProfile.VAProfileJPEGBaseline;
            const VAFormat Format = VAFormat.VA_RT_FORMAT_YUV420;

            var imageBytes = new Span<byte>(File.ReadAllBytes("jpeg.jpg"));
            var sliceBytes = imageBytes.Slice(DataOffset, DataSize);

            var provider = this.GetServiceProvider();
            using var display = provider.GetRequiredService<VADisplay>();
            using var decoder = provider.GetRequiredService<VADecoder>();

            decoder.Initialize(
                Profile,
                Format,
                Width,
                Height);

            Assert.NotNull(decoder.Context);
            var pictureParameterBuffer = decoder.Context.CreateBuffer(VABufferType.VAPictureParameterBufferType, ref pictureParameters);
            var iqMatrixBuffer = decoder.Context.CreateBuffer(VABufferType.VAIQMatrixBufferType, ref iqMatrix);
            var huffmanTableBuffer = decoder.Context.CreateBuffer(VABufferType.VAHuffmanTableBufferType, ref huffmanTable);
            var sliceParameterBuffer = decoder.Context.CreateBuffer(VABufferType.VASliceParameterBufferType, ref sliceParameters);

            fixed (byte* sliceData = sliceBytes)
            {
                var sliceDataBuffer = decoder.Context.CreateBuffer(
                    VABufferType.VASliceDataBufferType,
                    sliceData,
                    sliceBytes.Length);

                decoder.Render(
                    pictureParameterBuffer,
                    iqMatrixBuffer,
                    huffmanTableBuffer,
                    sliceParameterBuffer,
                    sliceDataBuffer);
            }

            var image = display.DeriveImage(decoder.Surface);
            Assert.NotEqual(Methods.VA_INVALID_ID, image.image_id);
            Assert.NotEqual(Methods.VA_INVALID_ID, image.buf);
            Assert.Equal((uint)Methods.VA_FOURCC_NV12, image.format.fourcc);
            Assert.Equal(Height, image.height);
            Assert.Equal(Width, image.width);
            
            this.SaveImage(display, image, "jpeg.rgb");

            display.DestroyImage(image);
        }
    }
}