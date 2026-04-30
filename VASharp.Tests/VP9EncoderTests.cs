using Microsoft.Extensions.DependencyInjection;
using System.Buffers.Binary;
using System.Diagnostics;
using VASharp.Native;
using Xunit;

namespace VASharp.Tests
{
    public class VP9EncoderTests : DecoderTests
    {
        const int SURFACE_NUM = 8;
        const int SID_INPUT_PICTURE_0 = 0;
        const int SID_INPUT_PICTURE_1 = 1;
        const int SID_NUMBER = 2;

        uint[] surface_ids = new uint[SID_NUMBER];
        uint[] ref_surfaces = new uint[SURFACE_NUM + SID_NUMBER];
        bool[] use_slot = new bool[SURFACE_NUM];

        int picture_width = 640;
        int picture_height = 480;
        int frame_size;
        MemoryStream yuvStream;

        VADisplay display;

        [Fact]
        public void Encode()
        {
            // https://github.com/intel/libva-utils/blob/1747b4d227ed90cfb815297b40a064140c00f154/encode/vp9enc.c
            var provider = this.GetServiceProvider();
            this.display = provider.GetRequiredService<VADisplay>();

            this.yuvStream = new MemoryStream(YuvgaPic.yuvga_pic());

            int rc_mode = Methods.VA_RC_CQP;
            int frame_rate = 30;
            int qp_value = 60;
            int intra_period = 30;
            int frame_bit_rate = -1;
            int vbr_max = -1;
            int lf_level = 10;

            if (rc_mode != Methods.VA_RC_CQP && (frame_bit_rate < 0))
            {
                throw new InvalidOperationException("Please specifiy the bit rate for CBR/VBR");
            }

            if (rc_mode == Methods.VA_RC_VBR)
            {
                if (vbr_max < 0)
                {
                    vbr_max = frame_bit_rate;
                    frame_bit_rate = (vbr_max * 95 / 100);
                }

                if (vbr_max < frame_bit_rate)
                {
                    throw new InvalidOperationException("Under VBR, the max bit rate should be greater than or equal to fb");
                }

                if (vbr_max > (frame_bit_rate * 2))
                {
                    throw new InvalidOperationException("under VBR, the max bit rate is too much greater than the average bit");
                }
            }

            this.frame_size = picture_width * picture_height + ((picture_width * picture_height) >> 1);

            if (frame_size <= 0)
            {
                throw new InvalidOperationException("Frame size is not correct");
            }

            int file_size = (int)yuvStream.Length;

            if ((file_size < frame_size) || (file_size % frame_size != 0))
            {
                throw new InvalidOperationException("The YUV file's size is not correct");
            }

            int frame_number = file_size / frame_size;
            yuvStream.Seek(0, SeekOrigin.Begin);

            int fn_num = -1;
            if (fn_num > 0 && fn_num <= frame_number)
                frame_number = fn_num;

            using Stream vp9Stream = File.Open("test.vp9", FileMode.Create, FileAccess.Write);

            if (intra_period == 0)
            {
                intra_period = frame_number;
            }

            int ip_period = 1;
            if (intra_period == 1)
            {
                ip_period = 0;
            }

            vp9enc_write_ivf_header(vp9Stream, picture_width, picture_height,
                                    frame_number, frame_rate);

            vp9enc_context_init(rc_mode, (uint)intra_period, (uint)frame_bit_rate, (byte)qp_value, (byte)lf_level);
            vp9enc_create_encode_pipe(yuvStream, display, frame_size);
            throw new NotImplementedException();
        }

        const int VP9_FOURCC = 0x30395056;

        private void vp9enc_create_encode_pipe(Stream yuv_fp, VADisplay display, int frame_size)
        {
            VAEntrypoint[] vp9enc_entrypoint_lists = {
                VAEntrypoint.VAEntrypointEncSlice,
                VAEntrypoint.VAEntrypointEncSliceLP
            };

            int select_entrypoint = -1;
            int num_entrypoints, slice_entrypoint;
            _VAConfigAttrib[] attrib = new _VAConfigAttrib[2];
            int major_ver, minor_ver;
            uint va_status;
            int i;

            var entrypoints = display.QueryConfigEntrypoints(vp9enc_context.profile);

            for (slice_entrypoint = 0; slice_entrypoint < entrypoints.Length; slice_entrypoint++)
            {
                if (select_entrypoint == -1)
                {
                    for (i = 0; i < 2; i++)
                    {
                        if (entrypoints[slice_entrypoint] == vp9enc_entrypoint_lists[i])
                            break;
                    }

                    if (i < 2)
                    {
                        select_entrypoint = i;
                        break;
                    }
                }
                else
                {
                    Debug.Assert(select_entrypoint == 0 || select_entrypoint == 1);

                    if (entrypoints[slice_entrypoint] == vp9enc_entrypoint_lists[select_entrypoint])
                        break;
                }
            }

            if (slice_entrypoint == entrypoints.Length)
            {
                /* not find Slice entry point */
                throw new NotSupportedException();
            }

            /* find out the format for the render target, and rate control mode */
            attrib[0].type = VAConfigAttribType.VAConfigAttribRTFormat;
            attrib[1].type = VAConfigAttribType.VAConfigAttribRateControl;
            for (i = 0; i < 2; i++)
            {
                attrib[i].value = display.GetConfigAttribute(vp9enc_context.profile, vp9enc_entrypoint_lists[select_entrypoint], attrib[i].type);
            }

            if ((attrib[0].value & Methods.VA_RT_FORMAT_YUV420) == 0)
            {
                /* not find desired YUV420 RT format */
                throw new NotSupportedException();
            }

            if ((attrib[1].value & vp9enc_context.rate_control_method) == 0)
            {
                /* Can't find matched RC mode */
                throw new NotSupportedException("Can't find the desired RC mode, exit");
            }

            attrib[0].value = Methods.VA_RT_FORMAT_YUV420; /* set to desired RT format */
            attrib[1].value = (uint)vp9enc_context.rate_control_method; /* set to desired RC mode */

            vp9enc_context.config_id = display.CreateConfig(vp9enc_context.profile, vp9enc_entrypoint_lists[select_entrypoint], attrib);

            vp9enc_alloc_encode_resource(yuv_fp, display, frame_size);

            /* Create a context for this decode pipe */
            /* the surface is added to the render_target list when creating the context */
            vp9enc_context.context_id = display.CreateContext(vp9enc_context.config_id, picture_width, picture_height, VAContextFlags.VA_PROGRESSIVE, ref_surfaces);
        }

        private byte[] newImageBuffer;

        private void vp9enc_alloc_encode_resource(Stream yuv_fp, VADisplay display, int frame_size)
        {
            uint va_status;
            _VASurfaceAttrib attrib = default;
            int i;

            attrib.type = VASurfaceAttribType.VASurfaceAttribPixelFormat;
            attrib.flags = Methods.VA_SURFACE_ATTRIB_SETTABLE;
            attrib.value.type = VAGenericValueType.VAGenericValueTypeInteger;
            attrib.value.value.i = Methods.VA_FOURCC_NV12;

            // Create surface
            surface_ids = display.CreateSurfaces(VAFormat.VA_RT_FORMAT_YUV420, (uint)this.picture_width, (uint)this.picture_height, SID_NUMBER, attrib);

            // Create surface
            ref_surfaces = display.CreateSurfaces(VAFormat.VA_RT_FORMAT_YUV420, (uint)this.picture_width, (uint)this.picture_height, SURFACE_NUM, attrib);

            for (i = 0; i < SID_NUMBER; i++)
            {
                ref_surfaces[i + SURFACE_NUM] = surface_ids[i];
            }

            newImageBuffer = new byte[frame_size];

            /* firstly upload YUV data to SID_INPUT_PICTURE_0 */
            // vp9enc_context.thread = new Thread(vp9enc_upload_thread_function);
            this.vp9enc_upload_thread_function();
        }

        private void vp9enc_upload_thread_function()
        {
            vp9enc_upload_yuv_to_surface();
        }

        private unsafe void vp9enc_upload_yuv_to_surface()
        {
            var surface_id = surface_ids[SID_INPUT_PICTURE_0];
            _VAImage surface_image;
            int y_size = this.picture_width * this.picture_height;
            int u_size = (this.picture_width >> 1) * (this.picture_height >> 1);
            int row, col;
            nint n_items;

            this.yuvStream.ReadExactly(this.newImageBuffer, 0, this.frame_size);

            surface_image = this.display.DeriveImage(surface_id);

            Span<byte> surface_p = this.display.MapBuffer(surface_image);

            Span<byte> y_src = new Span<byte>(newImageBuffer, 0, y_size);
            Span<byte> u_src = new Span<byte>(newImageBuffer, y_size, u_size);
            Span<byte> v_src = new Span<byte>(newImageBuffer, y_size + u_size, newImageBuffer.Length - y_size - u_size);

            Span<byte> y_dst = surface_p.Slice((int)surface_image.offsets[0]);
            Span<byte> u_dst = surface_p.Slice((int)surface_image.offsets[1]); /* UV offset for NV12 */
            Span<byte> v_dst = surface_p.Slice((int)surface_image.offsets[2]);

            /* Y plane */
            for (row = 0; row < surface_image.height; row++)
            {
                y_src.Slice(0, surface_image.width).CopyTo(y_dst);
                y_dst = y_dst.Slice((int)surface_image.pitches[0]);
                y_src = y_src.Slice(picture_width);
            }

            if (surface_image.format.fourcc == Methods.VA_FOURCC_NV12)
            { /* UV plane */
                for (row = 0; row < surface_image.height / 2; row++)
                {
                    for (col = 0; col < surface_image.width / 2; col++)
                    {
                        u_dst[col * 2] = u_src[col];
                        u_dst[col * 2 + 1] = v_src[col];
                    }

                    u_dst = u_dst.Slice((int)surface_image.pitches[1]);
                    u_src = u_src.Slice(picture_width / 2);
                    v_src = v_src.Slice(picture_width / 2);
                }
            }
            else if (surface_image.format.fourcc == Methods.VA_FOURCC_YV12 ||
                       surface_image.format.fourcc == Methods.VA_FOURCC_I420)
            {
                int U = surface_image.format.fourcc == Methods.VA_FOURCC_I420 ? 1 : 2;
                int V = surface_image.format.fourcc == Methods.VA_FOURCC_I420 ? 2 : 1;

                u_dst = surface_p.Slice((int)surface_image.offsets[U]);
                v_dst = surface_p.Slice((int)surface_image.offsets[V]);

                for (row = 0; row < surface_image.height / 2; row++)
                {
                    u_src.Slice(surface_image.width / 2).CopyTo(u_dst);
                    v_src.Slice(surface_image.width / 2).CopyTo(v_dst);
                    u_dst =  u_dst.Slice((int)surface_image.pitches[U]);
                    v_dst = v_dst.Slice((int)surface_image.pitches[V]);
                    u_src = u_src.Slice(picture_width / 2);
                    v_src = v_src.Slice(picture_width / 2);
                }
            }

            this.display.UnmapBuffer(surface_image);
            this.display.DestroyImage(surface_image);
        }

        private void vp9enc_write_ivf_header(Stream vp9_file, int width, int height, int frame_num, int frame_rate)
        {
            byte[] header = new byte[32];

            header[0] = (byte)'D';
            header[1] = (byte)'K';
            header[2] = (byte)'I';
            header[3] = (byte)'F';

            vp9enc_write_word(header, 4, 0);
            vp9enc_write_word(header, 6, 32);
            vp9enc_write_dword(header, 8, VP9_FOURCC);
            vp9enc_write_word(header, 12, width);
            vp9enc_write_word(header, 14, height);
            vp9enc_write_dword(header, 16, 1);
            vp9enc_write_dword(header, 20, frame_rate);
            vp9enc_write_dword(header, 24, frame_num);
            vp9enc_write_dword(header, 28, 0);

            vp9_file.Write(header.AsSpan());
            vp9_file.Flush();
        }

        struct vp9encode_context
        {
            public VAProfile profile;
            public _VAEncSequenceParameterBufferVP9 seq_param;
            public _VAEncPictureParameterBufferVP9 pic_param;
            public VAContext context_id;
            public uint config_id;
            public uint seq_param_buf_id;                /* Sequence level parameter */
            public uint pic_param_buf_id;                /* Picture level parameter */
            public uint codedbuf_buf_id;                 /* Output buffer, compressed data */
            public uint misc_parameter_hrd_buf_id;
            /* for VAEncMiscParameterTypeVP9PerSegmantParam. VAQMatrixBufferType */
            public uint qmatrix_buf_id;
            /* the buffer for VP9 super block. VAEncMacroblockMapBufferType */
            public uint mb_seg_buf_id;
            public uint raw_data_header_buf_id;
            public uint raw_data_buf_id;
            public uint misc_fr_buf_id;
            public uint misc_rc_buf_id;

            public int codedbuf_i_size;
            public int codedbuf_pb_size;
            public int current_input_surface;
            public int rate_control_method;

            public Thread thread;
        };

        vp9encode_context vp9enc_context;

        void vp9enc_context_seq_param_init(ref _VAEncSequenceParameterBufferVP9 seq_param,
                                      int width, int height, uint intra_period, uint frame_bit_rate)
        {
            seq_param.intra_period = intra_period;
            seq_param.max_frame_width = (uint)width;
            seq_param.max_frame_height = (uint)height;
            seq_param.kf_auto = 0;
            seq_param.kf_min_dist = 1;
            seq_param.kf_max_dist = intra_period;

            if (frame_bit_rate > 0)
                seq_param.bits_per_second = 1000 * frame_bit_rate; /* use kbps as input */
            else
                seq_param.bits_per_second = 0;
        }

        unsafe void vp9enc_context_pic_param_init(ref _VAEncPictureParameterBufferVP9 pic_param,
                                      int width, int height, byte qp_value, byte lf_level)
        {
            int i, ref_num;

            pic_param = default;

            ref_num = 8; // sizeof(pic_param.reference_frames) / sizeof(_VASurfaceID);
            for (i = 0; i < ref_num; i++)
                pic_param.reference_frames[i] = Methods.VA_INVALID_ID;

            pic_param.coded_buf = Methods.VA_INVALID_ID;
            pic_param.reconstructed_frame = Methods.VA_INVALID_ID;

            pic_param.frame_width_src = (uint)width;
            pic_param.frame_height_src = (uint)height;
            pic_param.frame_width_dst = (uint)width;
            pic_param.frame_height_dst = (uint)height;

            pic_param.ref_flags.bits.force_kf = 0;
            /* always show it */
            pic_param.pic_flags.bits.show_frame = 1;
            /* Not use silience mode */
            pic_param.pic_flags.bits.error_resilient_mode = 0;
            /* Not use the frame parallel mode */
            pic_param.pic_flags.bits.frame_parallel_decoding_mode = 0;

            pic_param.luma_ac_qindex = qp_value;
            pic_param.luma_dc_qindex_delta = 1;
            pic_param.chroma_ac_qindex_delta = 1;
            pic_param.chroma_dc_qindex_delta = 1;
            /* use the zero sharpness_level/lf_ref&mode deltas */
            pic_param.filter_level = lf_level;

            for (i = 0; i < 4; i++)
                pic_param.ref_lf_delta[i] = 1;

            for (i = 0; i < 2; i++)
                pic_param.mode_lf_delta[i] = 1;
        }

        private void vp9enc_context_init(int rc_mode, uint intra_period, uint frame_bit_rate, byte qp_value, byte lf_level)
        {
            this.vp9enc_context = default;
            vp9enc_context.profile = VAProfile.VAProfileVP9Profile0;

            use_slot.AsSpan().Fill(false);

            vp9enc_context.seq_param_buf_id = Methods.VA_INVALID_ID;
            vp9enc_context.pic_param_buf_id = Methods.VA_INVALID_ID;
            vp9enc_context.mb_seg_buf_id = Methods.VA_INVALID_ID;
            vp9enc_context.misc_parameter_hrd_buf_id = Methods.VA_INVALID_ID;
            vp9enc_context.qmatrix_buf_id = Methods.VA_INVALID_ID;
            vp9enc_context.codedbuf_buf_id = Methods.VA_INVALID_ID;
            vp9enc_context.raw_data_header_buf_id = Methods.VA_INVALID_ID;
            vp9enc_context.raw_data_buf_id = Methods.VA_INVALID_ID;
            vp9enc_context.misc_fr_buf_id = Methods.VA_INVALID_ID;
            vp9enc_context.misc_rc_buf_id = Methods.VA_INVALID_ID;

            vp9enc_context.codedbuf_i_size = this.picture_width * this.picture_height;
            vp9enc_context.codedbuf_pb_size = this.picture_width * this.picture_height;
            vp9enc_context.current_input_surface = SID_INPUT_PICTURE_0;
            // vp9enc_context.upload_thread_value = -1;

            vp9enc_context.rate_control_method = rc_mode;

            vp9enc_context_seq_param_init(ref this.vp9enc_context.seq_param, 8192, 8192, intra_period, frame_bit_rate);
            vp9enc_context_pic_param_init(ref vp9enc_context.pic_param, this.picture_width, this.picture_height, qp_value, lf_level);
        }

        private void vp9enc_write_word(byte[] buffer, int offset, int value)
        {
            BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(offset), (short)value);
        }

        private void vp9enc_write_dword(byte[] buffer, int offset, int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), value);
        }
    }
}
