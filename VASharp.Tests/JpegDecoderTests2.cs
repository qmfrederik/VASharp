#if HAVE_JPEG
using Microsoft.Extensions.DependencyInjection;
using VASharp.Native;
using Xunit;

namespace VASharp.Tests
{
    public class JpegDecoderTests2 : DecoderTests
    {
        const int Width = 320;
        const int Height = 240;

        private readonly int[] natural_order = {
            0,  1,  8, 16,  9,  2,  3, 10,
            17, 24, 32, 25, 18, 11,  4,  5,
            12, 19, 26, 33, 40, 48, 41, 34,
            27, 20, 13,  6,  7, 14, 21, 28,
            35, 42, 49, 56, 57, 50, 43, 36,
            29, 22, 15, 23, 30, 37, 44, 51,
            58, 59, 52, 45, 38, 31, 39, 46,
            53, 60, 61, 54, 47, 55, 62, 63,
        };

        // Tests the decoding of a JPEG file, while using libjpeg(-turbo) to read all the JPEG parameters.
        [DrmFact]
        public unsafe void JpegDecoding_Works()
        {
            var data = File.ReadAllBytes("jpeg.jpg");

            jpeg_decompress_struct cinfo;
            jpeg_error_mgr jerr;
            cinfo.err = Jpeg.jpeg_std_error(&jerr);	
            Jpeg.jpeg_CreateDecompress(&cinfo, 80, (uint)sizeof(jpeg_decompress_struct));

            fixed(byte* ptr = data)
            {
                Jpeg.jpeg_mem_src(&cinfo, ptr, (nuint)data.Length);
            }

	        var ret = Jpeg.jpeg_read_header(&cinfo, require_image: 1);

            // See https://github.com/JdeRobot/ThirdParty/blob/e4172b83bd56e498c5013c304e06194226f21c63/libfreenect2/src/vaapi_rgb_packet_processor.cpp#L340
            // and various other examples
            var pictureParameters = new _VAPictureParameterBufferJPEGBaseline()
            {
                picture_width = (ushort)cinfo.image_width,
                picture_height = (ushort)cinfo.image_height,
                num_components = (byte)cinfo.num_components,
            };

            for (int i = 0; i < cinfo.num_components; i++)
            {
                pictureParameters.components[i].component_id = (byte)cinfo.comp_info[i].component_id;
                pictureParameters.components[i].h_sampling_factor = (byte)cinfo.comp_info[i].h_samp_factor;
                pictureParameters.components[i].v_sampling_factor = (byte)cinfo.comp_info[i].v_samp_factor;
                pictureParameters.components[i].quantiser_table_selector = (byte)cinfo.comp_info[i].quant_tbl_no;
            }

            int mcu_h_size = cinfo.max_h_samp_factor * Jpeg.DCTSIZE;
            int mcu_v_size = cinfo.max_v_samp_factor * Jpeg.DCTSIZE;
            int mcus_per_row = ((int)cinfo.image_width /* WIDTH */ + mcu_h_size - 1) / mcu_h_size;
            int mcu_rows_in_scan = ((int)cinfo.image_height /* HEIGHT */ + mcu_v_size - 1) / mcu_v_size;
            uint num_mcus = (uint)(mcus_per_row * mcu_rows_in_scan);

            var sliceParameters = new _VASliceParameterBufferJPEGBaseline()
            {
                slice_data_size = (uint)cinfo.src->bytes_in_buffer,
                slice_data_offset = 0,
                slice_horizontal_position = 0,
                slice_vertical_position = 0,
                num_components = (byte)cinfo.num_components,
                restart_interval = (byte)cinfo.restart_interval,
                num_mcus = num_mcus,
            };

            for (int i = 0; i < cinfo.num_components; i++)
            {
                sliceParameters.components[i].component_selector = (byte)cinfo.cur_comp_info[i]->component_id;
                sliceParameters.components[i].dc_table_selector = (byte)cinfo.cur_comp_info[i]->dc_tbl_no;
                sliceParameters.components[i].ac_table_selector = (byte)cinfo.cur_comp_info[i]->ac_tbl_no;
            }

            var iqMatrix = new _VAIQMatrixBufferJPEGBaseline();
            for(int i = 0; i < Jpeg.NUM_QUANT_TBLS; i++)
            {
                if (cinfo.quant_tbl_ptrs[i] == null)
                {
                    continue;
                }

                iqMatrix.load_quantiser_table[i] = 1;
                
                for (int j = 0; j < Jpeg.DCTSIZE2; j++)
                {
                    iqMatrix.quantiser_table[i * Jpeg.DCTSIZE2 + j] =
                        (byte)cinfo.quant_tbl_ptrs[i]->quantval[natural_order[j]];
                }
            }

            var huffmanTable = new _VAHuffmanTableBufferJPEGBaseline();
            const int num_huffman_tables = 2;
            for (int i = 0; i < num_huffman_tables; i++)
            {
                if (cinfo.dc_huff_tbl_ptrs[i] == null
                    || cinfo.ac_huff_tbl_ptrs[i] == null)
                {
                    huffmanTable.load_huffman_table[i] = 0;
                    continue;
                }

                huffmanTable.load_huffman_table[i] = 1;

                fixed (byte* num_dc_codes = huffmanTable.huffman_table[i].num_dc_codes)
                {
                    new Span<byte>(
                        &cinfo.dc_huff_tbl_ptrs[i]->bits[1],
                        16)
                    .CopyTo(
                        new Span<byte>(
                            num_dc_codes,
                            16));
                }

                fixed (byte* dc_values = huffmanTable.huffman_table[i].dc_values)
                {
                    new Span<byte>(
                        cinfo.dc_huff_tbl_ptrs[i]->huffval,
                        12)
                    .CopyTo(
                        new Span<byte>(
                            dc_values,
                            12));
                }

                fixed (byte* num_ac_codes = huffmanTable.huffman_table[i].num_ac_codes)
                {
                    new Span<byte>(
                        &cinfo.ac_huff_tbl_ptrs[i]->bits[1],
                        16)
                    .CopyTo(
                        new Span<byte>(
                            num_ac_codes,
                            16));
                }
                fixed (byte* ac_values = huffmanTable.huffman_table[i].ac_values)
                {
                    new Span<byte>(
                        cinfo.ac_huff_tbl_ptrs[i]->huffval,
                        162)
                    .CopyTo(
                        new Span<byte>(
                            ac_values,
                            162));
                }
            }
            
            const VAProfile Profile = VAProfile.VAProfileJPEGBaseline;
            const VAFormat Format = VAFormat.VA_RT_FORMAT_YUV420;

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

            var sliceDataBuffer = decoder.Context.CreateBuffer(
                VABufferType.VASliceDataBufferType,
                new Span<byte>(cinfo.src->next_input_byte, (int)cinfo.src->bytes_in_buffer));

            decoder.Render(
                pictureParameterBuffer,
                iqMatrixBuffer,
                huffmanTableBuffer,
                sliceParameterBuffer,
                sliceDataBuffer);

            var image = display.DeriveImage(decoder.Surface);
            Assert.NotEqual(Methods.VA_INVALID_ID, image.image_id);
            Assert.NotEqual(Methods.VA_INVALID_ID, image.buf);
            Assert.Equal((uint)Methods.VA_FOURCC_NV12, image.format.fourcc);
            Assert.Equal(Height, image.height);
            Assert.Equal(Width, image.width);
            
            this.SaveImage(display, image, "jpeg2.rgb");

            display.DestroyImage(image);
        }
    }
}
#endif
