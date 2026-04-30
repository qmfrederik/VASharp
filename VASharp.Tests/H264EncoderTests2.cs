using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.InteropServices;
using VASharp.Native;
using Xunit;

namespace VASharp.Tests
{
    public class H264EncoderTests2 : DecoderTests
    {
        private readonly ILogger logger = NullLogger.Instance;

        uint frame_width_mbaligned;
        uint frame_height_mbaligned;

        // https://github.com/intel/libva-utils/blob/master/encode/h264encode.c
        [Fact]
        public void Foo()
        {
            var provider = this.GetServiceProvider();
            var display = provider.GetRequiredService<VADisplay>();

            var frame_width = 64;
            var frame_height = 64;

            frame_width_mbaligned = (uint)((frame_width + 15) & (~15));
            frame_height_mbaligned = (uint)((frame_height + 15) & (~15));

            this.init_va(display);
            this.setup_encode(display);
            this.encode_frames(display);
        }

        const int SURFACE_NUM = 16;

        VAEntrypoint selected_entrypoint;
        VAProfile h264_profile;
        int constraint_set_flag = 0;
        int ip_period = 1;
        _VAConfigAttrib[] config_attrib = new _VAConfigAttrib[(int)VAConfigAttribType.VAConfigAttribTypeMax];
        int config_attrib_num = 0;
        bool h264_packedheader = false;
        int enc_packed_header_idx;
        uint h264_maxref = (1 << 16 | 1);

        private unsafe int init_va(VADisplay display)
        {
            VAProfile[] profile_list = { VAProfile.VAProfileH264High, VAProfile.VAProfileH264Main, VAProfile.VAProfileH264ConstrainedBaseline };
            int num_entrypoints, slice_entrypoint;
            bool support_encode = false;
            int va_status;
            uint i;

            foreach (var profile in profile_list)
            {
                var entrypoints = display.QueryConfigEntrypoints(profile);

                foreach (var entrypoint in entrypoints)
                {
                    if (entrypoint == VAEntrypoint.VAEntrypointEncSlice
                        || entrypoint == VAEntrypoint.VAEntrypointEncSliceLP)
                    {
                        support_encode = true;
                        selected_entrypoint = entrypoint;
                        h264_profile = profile;

                        this.logger.LogInformation($"Using entrypoint {entrypoint} for profile {profile}");
                        break;
                    }
                }
            }

            if (!support_encode)
            {
                throw new InvalidOperationException();
            }

            switch (h264_profile)
            {
                case VAProfile.VAProfileH264ConstrainedBaseline:
                    constraint_set_flag |= (1 << 0 | 1 << 1); /* Annex A.2.2 */
                    ip_period = 1;
                    break;

                case VAProfile.VAProfileH264Main:
                    this.logger.LogInformation("Use profile VAProfileH264Main");
                    constraint_set_flag |= (1 << 1); /* Annex A.2.2 */
                    break;

                case VAProfile.VAProfileH264High:
                    constraint_set_flag |= (1 << 3); /* Annex A.2.4 */
                    this.logger.LogInformation("Use profile VAProfileH264High");
                    break;

                default:
                    this.logger.LogInformation("unknow profile. Set to Constrained Baseline");
                    h264_profile = VAProfile.VAProfileH264ConstrainedBaseline;
                    constraint_set_flag |= (1 << 0 | 1 << 1); /* Annex A.2.1 & A.2.2 */
                    ip_period = 1;
                    break;
            }

            /* find out the format for the render target, and rate control mode */
            _VAConfigAttrib[] attrib = new _VAConfigAttrib[(int)VAConfigAttribType.VAConfigAttribTypeMax];
            for (i = 0; i < attrib.Length; i++)
            {
                attrib[i].type = (VAConfigAttribType)i;
                attrib[i].value = display.GetConfigAttribute(h264_profile, selected_entrypoint, attrib[i].type);
            }

            /* check the interested configattrib */
            if ((attrib[(int)VAConfigAttribType.VAConfigAttribRTFormat].value & Methods.VA_RT_FORMAT_YUV420) == 0)
            {
                this.logger.LogInformation("Not find desired YUV420 RT format");
                throw new NotImplementedException();
            }
            else
            {
                this.config_attrib[config_attrib_num].type = VAConfigAttribType.VAConfigAttribRTFormat;
                this.config_attrib[config_attrib_num].value = Methods.VA_RT_FORMAT_YUV420;
                this.config_attrib_num++;
            }

            if (attrib[(int)VAConfigAttribType.VAConfigAttribRateControl].value != Methods.VA_ATTRIB_NOT_SUPPORTED)
            {
                int tmp = (int)attrib[(int)VAConfigAttribType.VAConfigAttribRateControl].value;

                this.logger.LogInformation($"Support rate control mode ({tmp:x}):");

                this.config_attrib[config_attrib_num].type = VAConfigAttribType.VAConfigAttribRateControl;

                if ((tmp & Methods.VA_RC_VBR) != 0)
                {
                    this.config_attrib[config_attrib_num].value = Methods.VA_RC_VBR;
                    this.logger.LogInformation("VBR ");
                }
                else if ((tmp & Methods.VA_RC_CQP) != 0)
                {
                    this.config_attrib[config_attrib_num].value = Methods.VA_RC_CQP;
                    this.logger.LogInformation("CQP ");
                }
                else if ((tmp & Methods.VA_RC_VBR_CONSTRAINED) != 0)
                {
                    this.config_attrib[config_attrib_num].value = Methods.VA_RC_VBR_CONSTRAINED;
                    this.logger.LogInformation("VBR_CONSTRAINED ");
                }
                else if ((tmp & Methods.VA_RC_CBR) != 0)
                {
                    this.config_attrib[config_attrib_num].value = Methods.VA_RC_CBR;
                    this.logger.LogInformation("CBR ");
                }
                else if ((tmp & Methods.VA_RC_VCM) != 0)
                {
                    this.config_attrib[config_attrib_num].value = Methods.VA_RC_VCM;
                    this.logger.LogInformation("VCM ");
                }
                else if ((tmp & Methods.VA_RC_NONE) != 0)
                {
                    this.config_attrib[config_attrib_num].value = Methods.VA_RC_NONE;
                    this.logger.LogInformation("NONE ");
                }
                else
                {
                    throw new NotSupportedException();
                }

                this.config_attrib_num++;
            }


            if (attrib[(int)VAConfigAttribType.VAConfigAttribEncPackedHeaders].value != Methods.VA_ATTRIB_NOT_SUPPORTED)
            {
                uint tmp = attrib[(int)VAConfigAttribType.VAConfigAttribEncPackedHeaders].value;

                this.logger.LogInformation("Support VAConfigAttribEncPackedHeaders");

                h264_packedheader = true;
                config_attrib[config_attrib_num].type = VAConfigAttribType.VAConfigAttribEncPackedHeaders;
                config_attrib[config_attrib_num].value = Methods.VA_ENC_PACKED_HEADER_NONE;

                if ((tmp & Methods.VA_ENC_PACKED_HEADER_SEQUENCE) != 0)
                {
                    this.logger.LogInformation("Support packed sequence headers");
                    config_attrib[config_attrib_num].value |= Methods.VA_ENC_PACKED_HEADER_SEQUENCE;
                }

                if ((tmp & Methods.VA_ENC_PACKED_HEADER_PICTURE) != 0)
                {
                    this.logger.LogInformation("Support packed picture headers");
                    config_attrib[config_attrib_num].value |= Methods.VA_ENC_PACKED_HEADER_PICTURE;
                }

                if ((tmp & Methods.VA_ENC_PACKED_HEADER_SLICE) != 0)
                {
                    this.logger.LogInformation("Support packed slice headers");
                    config_attrib[config_attrib_num].value |= Methods.VA_ENC_PACKED_HEADER_SLICE;
                }

                if ((tmp & Methods.VA_ENC_PACKED_HEADER_MISC) != 0)
                {
                    this.logger.LogInformation("Support packed misc headers");
                    config_attrib[config_attrib_num].value |= Methods.VA_ENC_PACKED_HEADER_MISC;
                }

                enc_packed_header_idx = config_attrib_num;
                config_attrib_num++;
            }

            if (attrib[(int)VAConfigAttribType.VAConfigAttribEncInterlaced].value != Methods.VA_ATTRIB_NOT_SUPPORTED)
            {
                uint tmp = attrib[(int)VAConfigAttribType.VAConfigAttribEncInterlaced].value;

                this.logger.LogInformation("Support VAConfigAttribEncInterlaced");

                if ((tmp & Methods.VA_ENC_INTERLACED_FRAME) != 0)
                    this.logger.LogInformation("support VA_ENC_INTERLACED_FRAME");
                if ((tmp & Methods.VA_ENC_INTERLACED_FIELD) != 0)
                    this.logger.LogInformation("Support VA_ENC_INTERLACED_FIELD");
                if ((tmp & Methods.VA_ENC_INTERLACED_MBAFF) != 0)
                    this.logger.LogInformation("Support VA_ENC_INTERLACED_MBAFF");
                if ((tmp & Methods.VA_ENC_INTERLACED_PAFF) != 0)
                    this.logger.LogInformation("Support VA_ENC_INTERLACED_PAFF");

                config_attrib[config_attrib_num].type = VAConfigAttribType.VAConfigAttribEncInterlaced;
                config_attrib[config_attrib_num].value = Methods.VA_ENC_PACKED_HEADER_NONE;
                config_attrib_num++;
            }

            if (attrib[(int)VAConfigAttribType.VAConfigAttribEncMaxRefFrames].value != Methods.VA_ATTRIB_NOT_SUPPORTED)
            {
                h264_maxref = attrib[(int)VAConfigAttribType.VAConfigAttribEncMaxRefFrames].value;

                this.logger.LogInformation($"Support {h264_maxref & 0xffff} RefPicList0 and {(h264_maxref >> 16) & 0xffff} RefPicList1");
            }

            if (attrib[(int)VAConfigAttribType.VAConfigAttribEncMaxSlices].value != Methods.VA_ATTRIB_NOT_SUPPORTED)
            {
                this.logger.LogInformation($"Support {attrib[(int)VAConfigAttribType.VAConfigAttribEncMaxSlices].value} slices");
            }

            if (attrib[(int)VAConfigAttribType.VAConfigAttribEncSliceStructure].value != Methods.VA_ATTRIB_NOT_SUPPORTED)
            {
                uint tmp = attrib[(int)VAConfigAttribType.VAConfigAttribEncSliceStructure].value;

                this.logger.LogInformation("Support VAConfigAttribEncSliceStructure");

                if ((tmp & Methods.VA_ENC_SLICE_STRUCTURE_ARBITRARY_ROWS) != 0)
                {
                    this.logger.LogInformation("Support VA_ENC_SLICE_STRUCTURE_ARBITRARY_ROWS");
                }
                if ((tmp & Methods.VA_ENC_SLICE_STRUCTURE_POWER_OF_TWO_ROWS) != 0)
                {
                    this.logger.LogInformation("Support VA_ENC_SLICE_STRUCTURE_POWER_OF_TWO_ROWS");
                }
                if ((tmp & Methods.VA_ENC_SLICE_STRUCTURE_ARBITRARY_MACROBLOCKS) != 0)
                {
                    this.logger.LogInformation("Support VA_ENC_SLICE_STRUCTURE_ARBITRARY_MACROBLOCKS");
                }
            }

            if (attrib[(int)VAConfigAttribType.VAConfigAttribEncMacroblockInfo].value != Methods.VA_ATTRIB_NOT_SUPPORTED)
            {
                this.logger.LogInformation("Support VAConfigAttribEncMacroblockInfo");
            }

            return 0;
        }

        private uint config_id;
        private VAContext context_id;
        private uint[] src_surface;
        private uint[] ref_surface;

        private unsafe int setup_encode(VADisplay display)
        {
            uint codedbuf_size, i;

            var attributes = this.config_attrib.Take(this.config_attrib_num).ToArray();

            this.config_id = display.CreateConfig(
                this.h264_profile,
                this.selected_entrypoint,
                attributes);

            /* create source surfaces */
            this.src_surface = display.CreateSurfaces(
                VAFormat.VA_RT_FORMAT_YUV420,
                this.frame_width_mbaligned,
                this.frame_height_mbaligned,
                SURFACE_NUM);

            /* create reference surfaces */
            this.ref_surface = display.CreateSurfaces(
                 VAFormat.VA_RT_FORMAT_YUV420,
                 this.frame_width_mbaligned,
                 this.frame_height_mbaligned,
                 SURFACE_NUM);

            uint[] tmp_surfaceid = new uint[2 * SURFACE_NUM];

            this.context_id = display.CreateContext(
                this.config_id,
                (int)this.frame_width_mbaligned,
                (int)this.frame_height_mbaligned,
                VAContextFlags.VA_PROGRESSIVE,
                tmp_surfaceid);

            codedbuf_size = (this.frame_width_mbaligned * this.frame_height_mbaligned * 400) / (16 * 16);
            uint[] coded_buf = new uint[SURFACE_NUM];

            for (i = 0; i < SURFACE_NUM; i++)
            {
                /* create coded buffer once for all
                 * other VA buffers which won't be used again after vaRenderPicture.
                 * so APP can always vaCreateBuffer for every frame
                 * but coded buffer need to be mapped and accessed after vaRenderPicture/vaEndPicture
                 * so VA won't maintain the coded buffer
                 */
                coded_buf[i] = context_id.CreateBuffer(
                    VABufferType.VAEncCodedBufferType,
                    codedbuf_size);
            }
            return 0;
        }

        private int encode_frames(VADisplay display)
        {
            // Not reading YUV data from file for now, just encode dummy frames.
            /*
            if (srcyuv_fp != NULL) {
                for (i = 0; i < SURFACE_NUM; i++)
                    load_surface(src_surface[i], i);
            } else*/

            upload_source_YUV_once_for_all(display);

            throw new NotImplementedException();
        }

        private int upload_source_YUV_once_for_all(VADisplay display)
        {
            int box_width = 8;
            int row_shift = 0;
            int i;

            for (i = 0; i < SURFACE_NUM; i++)
            {
                this.logger.LogInformation($"Loading data into surface {i}.....");
                upload_surface(display, src_surface[i], box_width, row_shift, 0);

                row_shift++;
                if (row_shift == (2 * box_width)) row_shift = 0;
            }
            this.logger.LogInformation("Complete surface loading");

            return 0;
        }

        static unsafe int upload_surface(VADisplay display, uint surface_id,
                                  int box_width, int row_shift,
                                  int field)
        {
            _VAImage surface_image;
            void* surface_p = null;
            void* U_start = null;
            void* V_start = null;
            int va_status;
            uint[] pitches = { 0, 0, 0 };

            va_status = Methods.vaDeriveImage((void*)display.DisplayId, surface_id, &surface_image);
            VAException.ThrowOnError(va_status);

            Methods.vaMapBuffer((void*)display.DisplayId, surface_image.buf, &surface_p);
            VAException.ThrowOnError(va_status);

            pitches[0] = surface_image.pitches[0];
            switch (surface_image.format.fourcc)
            {
                case Methods.VA_FOURCC_NV12:
                    U_start = (byte*)surface_p + surface_image.offsets[1];
                    V_start = (byte*)U_start + 1;
                    pitches[1] = surface_image.pitches[1];
                    pitches[2] = surface_image.pitches[1];
                    break;
                case Methods.VA_FOURCC_IYUV:
                    U_start = (byte*)surface_p + surface_image.offsets[1];
                    V_start = (byte*)surface_p + surface_image.offsets[2];
                    pitches[1] = surface_image.pitches[1];
                    pitches[2] = surface_image.pitches[2];
                    break;
                case Methods.VA_FOURCC_YV12:
                    U_start = (byte*)surface_p + surface_image.offsets[2];
                    V_start = (byte*)surface_p + surface_image.offsets[1];
                    pitches[1] = surface_image.pitches[2];
                    pitches[2] = surface_image.pitches[1];
                    break;
                case Methods.VA_FOURCC_YUY2:
                    U_start = (byte*)surface_p + 1;
                    V_start = (byte*)surface_p + 3;
                    pitches[1] = surface_image.pitches[0];
                    pitches[2] = surface_image.pitches[0];
                    break;
                default:
                    throw new NotImplementedException();
            }

            /* assume surface is planar format */
            yuvgen_planar(surface_image.width, surface_image.height,
                        (byte*)surface_p, (int)pitches[0],
                        (byte*)U_start, (int)pitches[1],
                        (byte*)V_start, (int)pitches[2],
                        surface_image.format.fourcc,
                        box_width, row_shift, field);

            va_status = Methods.vaUnmapBuffer((void*)display.DisplayId, surface_image.buf);
            VAException.ThrowOnError(va_status);

            va_status = Methods.vaDestroyImage((void*)display.DisplayId, surface_image.image_id);
            VAException.ThrowOnError(va_status);

            return 0;
        }
        static unsafe int yuvgen_planar(int width, int height,
                         byte* Y_start, int Y_pitch,
                         byte* U_start, int U_pitch,
                         byte* V_start, int V_pitch,
                         uint fourcc, int box_width, int row_shift,
                         int field)
        {
            int row, alpha;
            byte uv_value = 0x80;

            /* copy Y plane */
            int y_factor = 1;
            if (fourcc == Methods.VA_FOURCC_YUY2) y_factor = 2;
            for (row = 0; row < height; row++)
            {
                byte* Y_row = Y_start + row * Y_pitch;
                int jj, xpos, ypos;

                ypos = (row / box_width) & 0x1;

                /* fill garbage data into the other field */
                if (((field == Methods.VA_TOP_FIELD) && (row & 1) != 0)
                    || ((field == Methods.VA_BOTTOM_FIELD) && ((row & 1) == 0)))
                {
                    new Span<byte>(Y_row, width).Fill(0xff);
                    continue;
                }

                for (jj = 0; jj < width; jj++)
                {
                    xpos = ((row_shift + jj) / box_width) & 0x1;
                    if (xpos == ypos)
                        Y_row[jj * y_factor] = 0xeb;
                    else
                        Y_row[jj * y_factor] = 0x10;

                    if (fourcc == Methods.VA_FOURCC_YUY2)
                    {
                        Y_row[jj * y_factor + 1] = uv_value; // it is for UV
                    }
                }
            }

            /* copy UV data */
            for (row = 0; row < height / 2; row++)
            {
                /* fill garbage data into the other field */
                if (((field == Methods.VA_TOP_FIELD) && (row & 1) != 0)
                    || ((field == Methods.VA_BOTTOM_FIELD) && ((row & 1) == 0)))
                {
                    uv_value = 0xff;
                }

                byte* U_row = U_start + row * U_pitch;
                byte* V_row = V_start + row * V_pitch;
                switch (fourcc)
                {
                    case Methods.VA_FOURCC_NV12:
                        new Span<byte>(U_row, width).Fill(uv_value);
                        break;
                    case Methods.VA_FOURCC_YV12:
                        new Span<byte>(U_row, width / 2).Fill(uv_value);
                        new Span<byte>(V_row, width / 2).Fill(uv_value);
                        break;
                    case Methods.VA_FOURCC_YUY2:
                        // see above. it is set with Y update.
                        break;
                    default:
                        throw new NotSupportedException("unsupported fourcc in loadsurface.h");
                }
            }

            if (Environment.GetEnvironmentVariable("AUTO_NOUV") != null)
            {
                return 0;
            }

            if (Environment.GetEnvironmentVariable("AUTO_ALPHA") != null)
            {
                alpha = 0;
            }
            else
            {
                alpha = 70;
            }

            YUV_blend_with_pic(width, height,
                               Y_start, Y_pitch,
                               U_start, U_pitch,
                               V_start, V_pitch,
                               fourcc, alpha);

            return 0;
        }

        static unsafe int YUV_blend_with_pic(int width, int height,
                                      byte* Y_start, int Y_pitch,
                                      byte* U_start, int U_pitch,
                                      byte* V_start, int V_pitch,
                                      uint fourcc, int fixed_alpha)
        {
            /* PIC YUV format */
            byte[] old = YuvgaPic.yuvga_pic();

            fixed (byte* yugva_pic = old)
            {
                byte* pic_y_old = yugva_pic;
                byte* pic_u_old = pic_y_old + 640 * 480;
                byte* pic_v_old = pic_u_old + 640 * 480 / 4;
                byte* pic_y;
                byte* pic_u;
                byte* pic_v;

                int[] alpha_values = { 100, 90, 80, 70, 60, 50, 40, 30, 20, 30, 40, 50, 60, 70, 80, 90 };

                int alpha_idx = 0;
                int alpha;
                bool allocated = false;

                int row, col;

                if (fixed_alpha == 0)
                {
                    alpha = alpha_values[alpha_idx % 16];
                    alpha_idx++;
                }
                else
                    alpha = fixed_alpha;

                //alpha = 0;

                pic_y = pic_y_old;
                pic_u = pic_u_old;
                pic_v = pic_v_old;

                if (width != 640 || height != 480)
                { /* need to scale the pic */
                    pic_y = (byte*)Marshal.AllocHGlobal(width * height);
                    pic_u = (byte*)Marshal.AllocHGlobal(width * height / 4);
                    pic_v = (byte*)Marshal.AllocHGlobal(width * height / 4);
                    allocated = true;

                    new Span<byte>(pic_y, width * height).Fill(0);
                    new Span<byte>(pic_u, width * height / 4).Fill(0);
                    new Span<byte>(pic_v, width * height / 4).Fill(0);

                    scale_2dimage(pic_y_old, 640, 480,
                                  pic_y, width, height);
                    scale_2dimage(pic_u_old, 320, 240,
                                  pic_u, width / 2, height / 2);
                    scale_2dimage(pic_v_old, 320, 240,
                                  pic_v, width / 2, height / 2);
                }

                /* begin blend */

                /* Y plane */
                int Y_pixel_stride = 1;
                if (fourcc == Methods.VA_FOURCC_YUY2)
                    Y_pixel_stride = 2;

                for (row = 0; row < height; row++)
                {
                    byte* p = Y_start + row * Y_pitch;
                    byte* q = pic_y + row * width;
                    for (col = 0; col < width; col++, q++)
                    {
                        *p = (byte)(*p * (100 - alpha) / 100 + *q * alpha / 100);
                        p += Y_pixel_stride;
                    }
                }

                /* U/V plane */
                int U_pixel_stride = 0, V_pixel_stride = 0;
                int v_factor_to_nv12 = 1;
                switch (fourcc)
                {
                    case Methods.VA_FOURCC_YV12:
                        U_pixel_stride = V_pixel_stride = 1;
                        break;
                    case Methods.VA_FOURCC_NV12:
                        U_pixel_stride = V_pixel_stride = 2;
                        break;
                    case Methods.VA_FOURCC_YUY2:
                        U_pixel_stride = V_pixel_stride = 4;
                        v_factor_to_nv12 = 2;
                        break;
                    default:
                        break;
                }
                for (row = 0; row < height / 2 * v_factor_to_nv12; row++)
                {
                    byte* pU = U_start + row * U_pitch;
                    byte* pV = V_start + row * V_pitch;
                    byte* qU = pic_u + row / v_factor_to_nv12 * width / 2;
                    byte* qV = pic_v + row / v_factor_to_nv12 * width / 2;

                    for (col = 0; col < width / 2; col++, qU++, qV++)
                    {
                        *pU = (byte)(*pU * (100 - alpha) / 100 + *qU * alpha / 100);
                        *pV = (byte)(*pV * (100 - alpha) / 100 + *qV * alpha / 100);

                        pU += U_pixel_stride;
                        pV += V_pixel_stride;
                    }
                }

                if (allocated)
                {
                    Marshal.FreeHGlobal((nint)pic_y);
                    Marshal.FreeHGlobal((nint)pic_u);
                    Marshal.FreeHGlobal((nint)pic_v);
                }

                return 0;
            }
        }

        static unsafe int scale_2dimage(byte* src_img, int src_imgw, int src_imgh,
                                 byte* dst_img, int dst_imgw, int dst_imgh)
        {
            int row = 0, col = 0;

            for (row = 0; row < dst_imgh; row++)
            {
                for (col = 0; col < dst_imgw; col++)
                {
                    *(dst_img + row * dst_imgw + col) = *(src_img + (row * src_imgh / dst_imgh) * src_imgw + col * src_imgw / dst_imgw);
                }
            }

            return 0;
        }
    }
}
