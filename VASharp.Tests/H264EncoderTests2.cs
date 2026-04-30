using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
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

            // Not doing packed headers for now, because this requires bitstream manipulations
#if false
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
#endif

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
        private uint[] src_surface = new uint[SURFACE_NUM];
        private uint[] ref_surface = new uint[SURFACE_NUM];
        private uint[] coded_buf = new uint[SURFACE_NUM];

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
            coded_buf = new uint[SURFACE_NUM];

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

        private int[] srcsurface_status = new int[SURFACE_NUM];
        const int SRC_SURFACE_IN_ENCODING = 0;

        _VAEncSequenceParameterBufferH264 seq_param;
        _VAEncPictureParameterBufferH264 pic_param;
        _VAEncSliceParameterBufferH264 slice_param;

        const uint frame_count = 60;
        const int intra_period = 30;
        const int intra_idr_period = 60;
        uint numShortTerm = 0;
        uint current_frame_num = 0;
        ulong current_IDR_display = 0;

        static int current_slot(ulong current_frame_display)
        {
            return (int)(current_frame_display % SURFACE_NUM);
        }

        private unsafe int encode_frames(VADisplay display)
        {
            ulong current_frame_display = 0;
            int current_frame_type;

            // Not reading YUV data from file for now, just encode dummy frames.
            /*
            if (srcyuv_fp != NULL) {
                for (i = 0; i < SURFACE_NUM; i++)
                    load_surface(src_surface[i], i);
            } else*/

            upload_source_YUV_once_for_all(display);

            // Ready for encoding
            srcsurface_status.AsSpan().Fill(SRC_SURFACE_IN_ENCODING);

            this.seq_param = default;
            this.pic_param = default;
            this.slice_param = default;

            // skip encode_syncmode

            for (ulong current_frame_encoding = 0; current_frame_encoding < frame_count; current_frame_encoding++)
            {
                encoding2display_order(current_frame_encoding, intra_period, intra_idr_period, ip_period,
                               &current_frame_display, &current_frame_type);

                if (current_frame_type == FRAME_IDR)
                {
                    numShortTerm = 0;
                    current_frame_num = 0;
                    current_IDR_display = current_frame_display;
                }

                /* check if the source frame is ready */
                while (srcsurface_status[current_slot(current_frame_display)] != SRC_SURFACE_IN_ENCODING)
                {
                    Thread.Sleep(10);
                    Debug.Assert(false);
                }

                context_id.BeginPicture(src_surface[current_slot(current_frame_display)]);

                if (current_frame_type == FRAME_IDR)
                {
                    render_sequence(display, context_id);
                    render_picture(display, context_id, current_frame_display, current_frame_type);
                    if (h264_packedheader)
                    {
                        render_packedsequence(display, context_id);
                        render_packedpicture();
                    }
                    //if (rc_mode == VA_RC_CBR)
                    //    render_packedsei();
                    //render_hrd();
                }
                else
                {
                    //render_sequence();
                    render_picture(display, context_id, current_frame_display, current_frame_type);
                    //if (rc_mode == VA_RC_CBR)
                    //    render_packedsei();
                    //render_hrd();
                }

                render_slice(display, context_id, current_frame_display, current_frame_type);

                //RenderPictureTicks += GetTickCount() - tmp;

                //tmp = GetTickCount();
                context_id.EndPicture();
                //EndPictureTicks += GetTickCount() - tmp;

                // Skip encode_syncmode
                storage_task(display, current_frame_display, current_frame_encoding);

                throw new NotImplementedException();
                //update_ReferenceFrames();
            }

            throw new NotImplementedException();
        }

        private void storage_task(VADisplay display, ulong display_order, ulong encode_order)
        {
            throw new NotImplementedException();
        }


        private unsafe int render_slice(VADisplay display, VAContext context, ulong current_frame_display, int current_frame_type)
        {
            uint slice_param_buf;
            uint va_status;
            int i;

            update_RefPicList(current_frame_type);

            /* one frame, one slice */
            slice_param.macroblock_address = 0;
            slice_param.num_macroblocks = frame_width_mbaligned * frame_height_mbaligned / (16 * 16); /* Measured by MB */
            slice_param.slice_type = (byte)((current_frame_type == FRAME_IDR) ? 2 : current_frame_type);
            if (current_frame_type == FRAME_IDR)
            {
                if (current_frame_encoding != 0)
                    ++slice_param.idr_pic_id;
            }
            else if (current_frame_type == FRAME_P)
            {
                int refpiclist0_max = (int)(h264_maxref & 0xffff);
                RefPicList0_P.AsSpan(0, ((refpiclist0_max > 32) ? 32 : refpiclist0_max)).CopyTo(slice_param.RefPicList0.AsSpan());

                for (i = refpiclist0_max; i < 32; i++)
                {
                    slice_param.RefPicList0[i].picture_id = Methods.VA_INVALID_SURFACE;
                    slice_param.RefPicList0[i].flags = Methods.VA_PICTURE_H264_INVALID;
                }
            }
            else if (current_frame_type == FRAME_B)
            {
                int refpiclist0_max = (int)(h264_maxref & 0xffff);
                int refpiclist1_max = (int)((h264_maxref >> 16) & 0xffff);

                RefPicList0_B.AsSpan(0, ((refpiclist0_max > 32) ? 32 : refpiclist0_max)).CopyTo(slice_param.RefPicList0.AsSpan());
                for (i = refpiclist0_max; i < 32; i++)
                {
                    slice_param.RefPicList0[i].picture_id = Methods.VA_INVALID_SURFACE;
                    slice_param.RefPicList0[i].flags = Methods.VA_PICTURE_H264_INVALID;
                }

                RefPicList1_B.AsSpan(((refpiclist1_max > 32) ? 32 : refpiclist1_max)).CopyTo(slice_param.RefPicList1.AsSpan());
                for (i = refpiclist1_max; i < 32; i++)
                {
                    slice_param.RefPicList1[i].picture_id = Methods.VA_INVALID_SURFACE;
                    slice_param.RefPicList1[i].flags = Methods.VA_PICTURE_H264_INVALID;
                }
            }

            slice_param.slice_alpha_c0_offset_div2 = 0;
            slice_param.slice_beta_offset_div2 = 0;
            slice_param.direct_spatial_mv_pred_flag = 1;
            slice_param.pic_order_cnt_lsb = (ushort)((current_frame_display - current_IDR_display) % MaxPicOrderCntLsb);

            Debug.Assert(!h264_packedheader);

            /*
            if (h264_packedheader &&
                config_attrib[enc_packed_header_idx].value & Methods.VA_ENC_PACKED_HEADER_SLICE)
                render_packedslice();*/

            slice_param_buf = context.CreateBuffer(
                VABufferType.VAEncSliceParameterBufferType,
                ref slice_param);

            context.RenderPicture(slice_param_buf);
            return 0;
        }
        private _VAPictureH264[] RefPicList0_P = new _VAPictureH264[32];
        private _VAPictureH264[] RefPicList0_B = new _VAPictureH264[32];
        private _VAPictureH264[] RefPicList1_B = new _VAPictureH264[32];

        private unsafe int update_RefPicList(int current_frame_type)
        {
            int current_poc = CurrentCurrPic.TopFieldOrderCnt;

            if (current_frame_type == FRAME_P)
            {
                ReferenceFrames.AsSpan(0, (int)numShortTerm).CopyTo(RefPicList0_P.AsSpan());
                sort_one(RefPicList0_P, 0, (int)(numShortTerm - 1), 0, 1);
            }

            if (current_frame_type == FRAME_B)
            {
                ReferenceFrames.AsSpan(0, (int)numShortTerm).CopyTo(RefPicList0_B.AsSpan());
                sort_two(RefPicList0_B, 0, (int)(numShortTerm - 1), (uint)current_poc, 0,
                         1, 0, 1);

                ReferenceFrames.AsSpan(0, (int)numShortTerm).CopyTo(RefPicList1_B.AsSpan());
                sort_two(RefPicList1_B, 0, (int)(numShortTerm - 1), (uint)current_poc, 0,
                         0, 1, 0);
            }

            return 0;
        }
        static void partition(ref int i, ref int j, _VAPictureH264[] references, Func<_VAPictureH264, uint> field, uint key, int ascending)
        {
            _VAPictureH264 tmp;
            while (i <= j)
            {
                if (ascending != 0)
                {
                    while (field(references[i]) < key)
                        i++;
                    while (field(references[j]) > key)
                        j--;
                }
                else
                {
                    while (field(references[i]) > key)
                        i++;
                    while (field(references[j]) < key)
                        j--;
                }
                if (i <= j)
                {
                    tmp = references[i];
                    references[i] = references[j];
                    references[j] = tmp;
                    i++;
                    j--;
                }
            }
        }

        static void sort_one(_VAPictureH264[] references, int left, int right,
                             int ascending, int frame_idx)
        {
            int i = left, j = right;
            uint key;
            _VAPictureH264 tmp;

            if (frame_idx != 0)
            {
                key = references[(left + right) / 2].frame_idx;
                partition(ref i, ref j, references, (s) => s.frame_idx, key, ascending);
            }
            else
            {
                key = (uint)references[(left + right) / 2].TopFieldOrderCnt;
                partition(ref i, ref j, references, (s) => (uint)s.TopFieldOrderCnt, key, ascending);
            }

            /* recursion */
            if (left < j)
                sort_one(references, left, j, ascending, frame_idx);

            if (i < right)
                sort_one(references, i, right, ascending, frame_idx);
        }

        static void sort_two(_VAPictureH264[] references, int left, int right, uint key, uint frame_idx,
                             int partition_ascending, int list0_ascending, int list1_ascending)
        {
            int i = left, j = right;

            if (frame_idx != 0)
            {
                partition(ref i, ref j, references, (r) =>r.frame_idx, key, partition_ascending);
            }
            else
            {
                partition(ref i, ref j, references, (r) => (uint)r.TopFieldOrderCnt, key, partition_ascending);
            }


            sort_one(references, left, i - 1, list0_ascending, (int)frame_idx);
            sort_one(references, j + 1, right, list1_ascending, (int)frame_idx);
        }

        private int render_packedpicture()
        {
            throw new NotImplementedException();
        }

        private unsafe int render_packedsequence(VADisplay display, VAContext context)
        {
            _VAEncPackedHeaderParameterBuffer packedheader_param_buffer;
            uint packedseq_para_bufid;
            uint packedseq_data_bufid;
            uint[] render_id = new uint[2];

            uint length_in_bits;
            byte* packedseq_buffer = null;
            uint va_status;

            length_in_bits = build_packed_seq_buffer(&packedseq_buffer);

            packedheader_param_buffer.type = (uint)VAEncPackedHeaderType.VAEncPackedHeaderSequence;

            packedheader_param_buffer.bit_length = length_in_bits; /*length_in_bits*/
            packedheader_param_buffer.has_emulation_bytes = 0;
            packedseq_para_bufid = context.CreateBuffer(VABufferType.VAEncPackedHeaderParameterBufferType, ref packedheader_param_buffer);

            packedseq_data_bufid = context.CreateBuffer(
                VABufferType.VAEncPackedHeaderDataBufferType,
                new Span<byte>(packedseq_buffer, (int)((length_in_bits + 7) / 8)));

            render_id[0] = packedseq_para_bufid;
            render_id[1] = packedseq_data_bufid;
            context.RenderPicture(render_id);

            // TODO: Free these buffers?

            return 0;
        }

        static unsafe uint build_packed_seq_buffer(byte** header_buffer)
        {
            throw new NotImplementedException();
        }

        uint frame_bitrate = 0;
        uint num_ref_frames = 2;
        uint Log2MaxPicOrderCntLsb = 8;
        uint Log2MaxFrameNum = 16;

        const int frame_width = 176;
        const int frame_height = 144;

        const int initial_qp = 26;
        const int minimal_qp = 0;
        const int misc_priv_type = 0;

        private unsafe int render_sequence(VADisplay display, VAContext context)
        {
            uint seq_param_buf;
            uint rc_param_buf;
            uint misc_param_tmpbuf;
            uint[] render_id = new uint[2];
            int va_status;
            _VAEncMiscParameterBuffer* misc_param;
            _VAEncMiscParameterBuffer* misc_param_tmp;
            _VAEncMiscParameterRateControl* misc_rate_ctrl;

            seq_param.level_idc = 41 /*SH_LEVEL_3*/;
            seq_param.picture_width_in_mbs = (ushort)(frame_width_mbaligned / 16);
            seq_param.picture_height_in_mbs = (ushort)(frame_height_mbaligned / 16);
            seq_param.bits_per_second = frame_bitrate;

            seq_param.intra_period = intra_period;
            seq_param.intra_idr_period = intra_idr_period;
            seq_param.ip_period = (uint)ip_period;

            seq_param.max_num_ref_frames = num_ref_frames;
            seq_param.seq_fields.bits.frame_mbs_only_flag = 1;
            seq_param.time_scale = 900;
            seq_param.num_units_in_tick = 15; /* Tc = num_units_in_tick / time_sacle */
            Log2MaxPicOrderCntLsb = (uint)Math.Ceiling(Math.Log2(intra_idr_period * 2));
            seq_param.seq_fields.bits.log2_max_pic_order_cnt_lsb_minus4 = Log2MaxPicOrderCntLsb - 4;
            if (seq_param.seq_fields.bits.log2_max_pic_order_cnt_lsb_minus4 > 12)
            {
                seq_param.seq_fields.bits.log2_max_pic_order_cnt_lsb_minus4 = 12;
            }
            seq_param.seq_fields.bits.log2_max_frame_num_minus4 = Log2MaxFrameNum - 4; ;
            seq_param.seq_fields.bits.frame_mbs_only_flag = 1;
            seq_param.seq_fields.bits.chroma_format_idc = 1;
            seq_param.seq_fields.bits.direct_8x8_inference_flag = 1;

            if (frame_width != frame_width_mbaligned ||
                frame_height != frame_height_mbaligned)
            {
                seq_param.frame_cropping_flag = 1;
                seq_param.frame_crop_left_offset = 0;
                seq_param.frame_crop_right_offset = (frame_width_mbaligned - frame_width) / 2;
                seq_param.frame_crop_top_offset = 0;
                seq_param.frame_crop_bottom_offset = (frame_height_mbaligned - frame_height) / 2;
            }

            seq_param_buf = context.CreateBuffer<_VAEncSequenceParameterBufferH264>(VABufferType.VAEncSequenceParameterBufferType, ref seq_param);
            rc_param_buf = context.CreateBuffer(
                VABufferType.VAEncMiscParameterBufferType,
                (uint)(sizeof(_VAEncMiscParameterBuffer) + sizeof(_VAEncMiscParameterRateControl)));

            int ret = Methods.vaMapBuffer((void*)display.DisplayId, rc_param_buf, (void**)&misc_param);
            VAException.ThrowOnError(ret);
            misc_param->type = VAEncMiscParameterType.VAEncMiscParameterTypeRateControl;
            misc_rate_ctrl = (_VAEncMiscParameterRateControl*)misc_param->data;
            misc_rate_ctrl->bits_per_second = frame_bitrate;
            misc_rate_ctrl->target_percentage = 66;
            misc_rate_ctrl->window_size = 1000;
            misc_rate_ctrl->initial_qp = initial_qp;
            misc_rate_ctrl->min_qp = minimal_qp;
            misc_rate_ctrl->basic_unit_size = 0;
            ret = Methods.vaUnmapBuffer((void*)display.DisplayId, rc_param_buf);
            VAException.ThrowOnError(ret);

            render_id[0] = seq_param_buf;
            render_id[1] = rc_param_buf;

            context.RenderPicture(render_id);

            if (misc_priv_type != 0)
            {
                throw new NotSupportedException();

                /*
                va_status = vaCreateBuffer(va_dpy, context_id,
                                           VAEncMiscParameterBufferType,
                                           sizeof(VAEncMiscParameterBuffer),
                                           1, NULL, &misc_param_tmpbuf);
                CHECK_VASTATUS(va_status, "vaCreateBuffer");
                vaMapBuffer(va_dpy, misc_param_tmpbuf, (void**)&misc_param_tmp);
                misc_param_tmp->type = misc_priv_type;
                misc_param_tmp->data[0] = misc_priv_value;
                vaUnmapBuffer(va_dpy, misc_param_tmpbuf);

                context.RenderPicture(&misc_param_tmpbuf, 1);*/
            }

            return 0;
        }
        static uint MaxPicOrderCntLsb = (2 << 8);

        private _VAPictureH264 CurrentCurrPic;
        private _VAPictureH264[] ReferenceFrames = new _VAPictureH264[16];
        private int h264_entropy_mode = 1; /* cabac */
        private ulong current_frame_encoding = 0;

        private int render_picture(VADisplay display, VAContext context, ulong current_frame_display, int current_frame_type)
        {
            int pic_param_buf;
            int va_status;
            int i = 0;

            pic_param.CurrPic.picture_id = ref_surface[current_slot(current_frame_display)];
            pic_param.CurrPic.frame_idx = current_frame_num;
            pic_param.CurrPic.flags = 0;
            pic_param.CurrPic.TopFieldOrderCnt = calc_poc((int)((current_frame_display - current_IDR_display) % MaxPicOrderCntLsb), current_frame_type);
            pic_param.CurrPic.BottomFieldOrderCnt = pic_param.CurrPic.TopFieldOrderCnt;
            CurrentCurrPic = pic_param.CurrPic;

            if (Environment.GetEnvironmentVariable("TO_DEL") != null)
            {
                throw new NotImplementedException();

                /* set RefPicList into ReferenceFrames */
                //update_RefPicList(); /* calc RefPicList */
                //memset(pic_param.ReferenceFrames, 0xff, 16 * sizeof(VAPictureH264)); /* invalid all */
                //if (current_frame_type == FRAME_P)
                //{
                //    pic_param.ReferenceFrames[0] = RefPicList0_P[0];
                //}
                //else if (current_frame_type == FRAME_B)
                //{
                //    pic_param.ReferenceFrames[0] = RefPicList0_B[0];
                //    pic_param.ReferenceFrames[1] = RefPicList1_B[0];
                //}
            }
            else
            {
                ReferenceFrames.AsSpan(0, (int)numShortTerm).CopyTo(
                    pic_param.ReferenceFrames.AsSpan());

                for (i = (int)numShortTerm; i < SURFACE_NUM; i++)
                {
                    pic_param.ReferenceFrames[i].picture_id = Methods.VA_INVALID_SURFACE;
                    pic_param.ReferenceFrames[i].flags = Methods.VA_PICTURE_H264_INVALID;
                }
            }

            pic_param.pic_fields.bits.idr_pic_flag = (current_frame_type == FRAME_IDR) ? 1u : 0u;
            pic_param.pic_fields.bits.reference_pic_flag = (current_frame_type != FRAME_B) ? 1u : 0u;
            pic_param.pic_fields.bits.entropy_coding_mode_flag = (uint)h264_entropy_mode;
            pic_param.pic_fields.bits.deblocking_filter_control_present_flag = 1;
            pic_param.frame_num = (ushort)current_frame_num;
            pic_param.coded_buf = coded_buf[current_slot(current_frame_display)];
            pic_param.last_picture = (current_frame_encoding == frame_count) ? (byte)1 : (byte)0;
            pic_param.pic_init_qp = initial_qp;

            // TODO: double check
            pic_param_buf = (int)context.CreateBuffer<_VAEncPictureParameterBufferH264>(
                VABufferType.VAEncPictureParameterBufferType,
                ref pic_param);
            //va_status = vaCreateBuffer(va_dpy, context_id, VAEncPictureParameterBufferType,
            //                           sizeof(pic_param), 1, &pic_param, &pic_param_buf);

            context.RenderPicture((uint)pic_param_buf);

            return 0;
        }

        static int calc_poc(int pic_order_cnt_lsb, int current_frame_type)
        {
            // TODO: these are static?
            int PicOrderCntMsb_ref = 0, pic_order_cnt_lsb_ref = 0;
            int prevPicOrderCntMsb, prevPicOrderCntLsb;
            int PicOrderCntMsb, TopFieldOrderCnt;

            if (current_frame_type == FRAME_IDR)
                prevPicOrderCntMsb = prevPicOrderCntLsb = 0;
            else
            {
                prevPicOrderCntMsb = PicOrderCntMsb_ref;
                prevPicOrderCntLsb = pic_order_cnt_lsb_ref;
            }

            if ((pic_order_cnt_lsb < prevPicOrderCntLsb) &&
                ((prevPicOrderCntLsb - pic_order_cnt_lsb) >= (int)(MaxPicOrderCntLsb / 2)))
                PicOrderCntMsb = (int)(prevPicOrderCntMsb + MaxPicOrderCntLsb);
            else if ((pic_order_cnt_lsb > prevPicOrderCntLsb) &&
                     ((pic_order_cnt_lsb - prevPicOrderCntLsb) > (int)(MaxPicOrderCntLsb / 2)))
                PicOrderCntMsb = (int)(prevPicOrderCntMsb - MaxPicOrderCntLsb);
            else
                PicOrderCntMsb = prevPicOrderCntMsb;

            TopFieldOrderCnt = PicOrderCntMsb + pic_order_cnt_lsb;

            if (current_frame_type != FRAME_B)
            {
                PicOrderCntMsb_ref = PicOrderCntMsb;
                pic_order_cnt_lsb_ref = pic_order_cnt_lsb;
            }

            return TopFieldOrderCnt;
        }

        /*
          Assume frame sequence is: Frame#0,#1,#2,...,#M,...,#X,... (encoding order)
          1) period between Frame #X and Frame #N = #X - #N
          2) 0 means infinite for intra_period/intra_idr_period, and 0 is invalid for ip_period
          3) intra_idr_period % intra_period (intra_period > 0) and intra_period % ip_period must be 0
          4) intra_period and intra_idr_period take precedence over ip_period
          5) if ip_period > 1, intra_period and intra_idr_period are not  the strict periods
             of I/IDR frames, see bellow examples
          -------------------------------------------------------------------
          intra_period intra_idr_period ip_period frame sequence (intra_period/intra_idr_period/ip_period)
          0            ignored          1          IDRPPPPPPP ...     (No IDR/I any more)
          0            ignored        >=2          IDR(PBB)(PBB)...   (No IDR/I any more)
          1            0                ignored    IDRIIIIIII...      (No IDR any more)
          1            1                ignored    IDR IDR IDR IDR...
          1            >=2              ignored    IDRII IDRII IDR... (1/3/ignore)
          >=2          0                1          IDRPPP IPPP I...   (3/0/1)
          >=2          0              >=2          IDR(PBB)(PBB)(IBB) (6/0/3)
                                                      (PBB)(IBB)(PBB)(IBB)...
          >=2          >=2              1          IDRPPPPP IPPPPP IPPPPP (6/18/1)
                                                   IDRPPPPP IPPPPP IPPPPP...
          >=2          >=2              >=2        {IDR(PBB)(PBB)(IBB)(PBB)(IBB)(PBB)} (6/18/3)
                                                   {IDR(PBB)(PBB)(IBB)(PBB)(IBB)(PBB)}...
                                                   {IDR(PBB)(PBB)(IBB)(PBB)}           (6/12/3)
                                                   {IDR(PBB)(PBB)(IBB)(PBB)}...
                                                   {IDR(PBB)(PBB)}                     (6/6/3)
                                                   {IDR(PBB)(PBB)}.
        */


        /*
         * Return displaying order with specified periods and encoding order
         * displaying_order: displaying order
         * frame_type: frame type
         */
        const int FRAME_P = 0;
        const int FRAME_B = 1;
        const int FRAME_I = 2;
        const int FRAME_IDR = 7;

        unsafe void encoding2display_order(
            ulong encoding_order, int intra_period,
            int intra_idr_period, int ip_period,
            ulong* displaying_order,
            int* frame_type)
        {
            int encoding_order_gop = 0;

            if (intra_period == 1)
            { /* all are I/IDR frames */
                *displaying_order = encoding_order;
                if (intra_idr_period == 0)
                    *frame_type = (encoding_order == 0) ? FRAME_IDR : FRAME_I;
                else
                    *frame_type = (encoding_order % (ulong)intra_idr_period == 0) ? FRAME_IDR : FRAME_I;
                return;
            }

            if (intra_period == 0)
                intra_idr_period = 0;

            /* new sequence like
             * IDR PPPPP IPPPPP
             * IDR (PBB)(PBB)(IBB)(PBB)
             */
            encoding_order_gop = (intra_idr_period == 0) ? (int)encoding_order :
                                 ((int)encoding_order % (intra_idr_period + ((ip_period == 1) ? 0 : 1)));

            if (encoding_order_gop == 0)
            { /* the first frame */
                *frame_type = FRAME_IDR;
                *displaying_order = encoding_order;
            }
            else if (((encoding_order_gop - 1) % ip_period) != 0)
            { /* B frames */
                *frame_type = FRAME_B;
                *displaying_order = encoding_order - 1;
            }
            else if ((intra_period != 0) && /* have I frames */
                       (encoding_order_gop >= 2) &&
                       ((ip_period == 1 && encoding_order_gop % intra_period == 0) || /* for IDR PPPPP IPPPP */
                        /* for IDR (PBB)(PBB)(IBB) */
                        (ip_period >= 2 && ((encoding_order_gop - 1) / ip_period % (intra_period / ip_period)) == 0)))
            {
                *frame_type = FRAME_I;
                *displaying_order = encoding_order + (ulong)ip_period - 1;
            }
            else
            {
                *frame_type = FRAME_P;
                *displaying_order = encoding_order + (ulong)ip_period - 1;
            }
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
