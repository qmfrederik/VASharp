using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VASharp.Native;
using Xunit;

namespace VASharp.Tests
{
    public class H264EncoderTests2 : DecoderTests
    {
        private readonly ILogger logger = NullLogger.Instance;

        // https://github.com/intel/libva-utils/blob/master/encode/h264encode.c
        [Fact]
        public void Foo()
        {
            var provider = this.GetServiceProvider();
            var display = provider.GetRequiredService<VADisplay>();

            this.init_va(display);
        }

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
    }
}
