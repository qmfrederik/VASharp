using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using VASharp.Native;
using Xunit;

namespace VASharp.Tests
{
    public unsafe class H264DecoderTests
    {
        static H264DecoderTests()
        {
            const string YuvPath = "../../../../vcpkg_installed/x64-windows/bin/libyuv.dll";

            // When on Windows, load libyuv if available
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                && File.Exists(YuvPath))
            {
                NativeLibrary.TryLoad(YuvPath, out nint _);
            }
        }

        const int Width = 320;
        const int Height = 240;

        _VAPictureParameterBufferH264 pictureParameters = new _VAPictureParameterBufferH264()
        {
            picture_width_in_mbs_minus1 = (Width + 15) / 16 - 1,
            picture_height_in_mbs_minus1 = (Height + 15) / 16 - 1,
            bit_depth_luma_minus8 = 0,
            bit_depth_chroma_minus8 = 0,
            num_ref_frames = 1,
            seq_fields =
            {
                bits =
                {
                    chroma_format_idc = 1,
                    residual_colour_transform_flag = 0,
                    frame_mbs_only_flag = 1,
                    mb_adaptive_frame_field_flag = 0,
                    direct_8x8_inference_flag = 1,
                    MinLumaBiPredSize8x8 = 0,
                    log2_max_frame_num_minus4 = 1,
                    pic_order_cnt_type = 0,
                    log2_max_pic_order_cnt_lsb_minus4 = 2,
                    delta_pic_order_always_zero_flag = 0,
                }
            },
            num_slice_groups_minus1 = 0,
            slice_group_map_type = 0,
            pic_init_qp_minus26 = 0,
            chroma_qp_index_offset = -2,
            second_chroma_qp_index_offset = -2,
            pic_fields =
            {
                bits =
                {
                    entropy_coding_mode_flag = 1,
                    weighted_pred_flag = 0,
                    weighted_bipred_idc = 0,
                    transform_8x8_mode_flag = 0,
                    field_pic_flag = 0,
                    constrained_intra_pred_flag = 0,
                    pic_order_present_flag = 0,
                    deblocking_filter_control_present_flag = 1,
                    redundant_pic_cnt_present_flag = 0,
                    reference_pic_flag = 1,
                }
            },
            frame_num = 0,
        };

        /*
         *  Fields not copied over:
            .profile_idc = 77,
            .level_idc = 13,
            .width = H264_CLIP_WIDTH,
            .height = H264_CLIP_HEIGHT,
            .seq_fields = {
                .bits = {
                    .gaps_in_frame_num_value_allowed_flag = 0,
                },
            },
            .slice_group_change_rate_minus1 = 0,
            .pic_init_qs_minus26 = 0,
            .pic_fields = {
                .bits = 
                    .bottom_field_flag = 0,
                },
            },*/

        _VASliceParameterBufferH264 sliceParameter = new _VASliceParameterBufferH264()
        {
            slice_data_bit_offset = 39,
            first_mb_in_slice = 0,
            slice_type = 2,
            direct_spatial_mv_pred_flag = 0,
            num_ref_idx_l0_active_minus1 = 0,
            num_ref_idx_l1_active_minus1 = 0,
            cabac_init_idc = 0,
            slice_qp_delta = 2,
            disable_deblocking_filter_idc = 1,
            slice_alpha_c0_offset_div2 = 0,
            slice_beta_offset_div2 = 0,
            luma_log2_weight_denom = 0,
            chroma_log2_weight_denom = 0,
            luma_weight_l0_flag = 0,
            chroma_weight_l0_flag = 0,
            luma_weight_l1_flag = 0,
            chroma_weight_l1_flag = 0,
        };

        [Fact]
        public void H264Decoding_Works()
        {
            const VAProfile Profile = VAProfile.VAProfileH264High;
            const VAFormat Format = VAFormat.VA_RT_FORMAT_YUV420;

            var videoBytes = new Span<byte>(File.ReadAllBytes("h264.mp4"));
            var sliceBytes = videoBytes.Slice(52, 12071);

            using var provider = new ServiceCollection()
                .AddLogging()
                .AddVideoAcceleration(
                (options) =>
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        // For the time being, hardcode the paths to:
                        // - The va.dll and va_win32.dll libraries which can be installed via vcpkg
                        // - The drivers which can be downloaded at https://www.nuget.org/packages/Microsoft.Direct3D.VideoAccelerationCompatibilityPack/
                        options.LibraryPath = Path.GetFullPath("../../../../vcpkg_installed/x64-windows/bin/");
                        options.DriverPath = Path.GetFullPath("../../../../");
                    }
                })
                .BuildServiceProvider();

            using var display = provider.GetRequiredService<VADisplay>();

            var profiles = display.QueryConfigProfiles();
            Assert.Contains(Profile, profiles);

            var entryPoints = display.QueryConfigEntrypoints(Profile);
            Assert.Contains(VAEntrypoint.VAEntrypointVLD, entryPoints);

            var format = (VAFormat)display.GetConfigAttribute(Profile, VAEntrypoint.VAEntrypointVLD, VAConfigAttribType.VAConfigAttribRTFormat);
            Assert.True(format.HasFlag(Format));

            var config = display.CreateConfig(Profile, VAEntrypoint.VAEntrypointVLD, Array.Empty<_VAConfigAttrib>());

            var surface = display.CreateSurfaces(
                Format,
                Width,
                Height);

            pictureParameters.frame_num = 0;
            pictureParameters.CurrPic.picture_id = surface;
            pictureParameters.CurrPic.TopFieldOrderCnt = 0;
            pictureParameters.CurrPic.BottomFieldOrderCnt = 0;

            for (int i = 0; i < 16; i++)
            {
                InitPicture(ref pictureParameters.ReferenceFrames[i]);
            }

            _VAIQMatrixBufferH264 iqMatrix = new _VAIQMatrixBufferH264();

            // scaling lists, corresponds to same HEVC spec syntax element ScalingList[ i ][ MatrixID ][ j ].
            // 4x4 scaling, correspongs i = 0, MatrixID is in the range of 0 to 5, inclusive.And j is in the range of 0 to 15, inclusive.
            for (int i = 0; i < 6 * 16; i++)
            {
                iqMatrix.ScalingList4x4[i] = 0x10;
            }

            // 8x8 scaling, correspongs i = 1, MatrixID is in the range of 0 to 5, inclusive. And j is in the range of 0 to 63, inclusive.
            for (int i = 0; i < 2 * 64; i++)
            {
                iqMatrix.ScalingList8x8[i] = 0x10;
            }

            for (int i = 0; i < 32; i++)
            {
                InitPicture(ref sliceParameter.RefPicList0[i]);
                InitPicture(ref sliceParameter.RefPicList1[i]);
            }

            sliceParameter.slice_data_offset = 0;
            sliceParameter.slice_data_flag = Methods.VA_SLICE_DATA_FLAG_ALL;
            sliceParameter.slice_data_size = (uint)sliceBytes.Length;

            var context = display.CreateContext(
                config,
                Width,
                ((Height + 15) / 16) * 16,
                VAContextFlags.VA_PROGRESSIVE,
                surface);

            var pictureParameterBuffer = context.CreateBuffer(VABufferType.VAPictureParameterBufferType, ref pictureParameters);
            var iqMatrixBuffer = context.CreateBuffer(VABufferType.VAIQMatrixBufferType, ref iqMatrix);
            var sliceParameterbuffer = context.CreateBuffer(VABufferType.VASliceParameterBufferType, ref sliceParameter);

            fixed (byte* sliceData = sliceBytes)
            {
                // TODO: rewrite this as Span<byte>
                var sliceDataBuffer = context.CreateBuffer(
                    VABufferType.VASliceDataBufferType,
                    sliceData,
                    sliceBytes.Length);

                context.BeginPicture(surface);
                context.RenderPicture(pictureParameterBuffer, iqMatrixBuffer);
                context.RenderPicture(sliceParameterbuffer, sliceDataBuffer);

                context.EndPicture();
            }

            display.SyncSurface(surface);

            var image = display.DeriveImage(surface);

            Assert.NotEqual(Methods.VA_INVALID_ID, image.image_id);
            Assert.NotEqual(Methods.VA_INVALID_ID, image.buf);
            Assert.Equal((uint)Methods.VA_FOURCC_NV12, image.format.fourcc);
            Assert.Equal(Height, image.height);
            Assert.Equal(Width, image.width);
            
            var bytes = display.MapBuffer(image);

#if HAVE_YUV
            Span<byte> argb = stackalloc byte[Width * 4 * Height];

            // Use libyuv to convert the pixel in nv12 format to ARGB format
            fixed(byte* raw = bytes)
            fixed(byte* rawArgb = argb)
            {
                int ret = Yuv.NV12ToARGB(
                    src_y: raw + image.offsets[0],
                    src_stride_y: (int)image.pitches[0],
                    src_uv: raw + image.offsets[1],
                    src_stride_uv: (int)image.pitches[1],
                    dst_argb: rawArgb,
                    dst_stride_argb: Width * 4,
                    width: Width,
                    height: Height);

#if NET9_0_OR_GREATER
                File.WriteAllBytes("data.rgb", argb);
#endif
            }
#endif

            display.UnmapBuffer(image);

            display.DestroyImage(image);

            display.DestroySurface(surface);
            display.DestroyConfig(config);
        }

        private static void InitPicture(ref _VAPictureH264 picture)
        {
            picture.picture_id = 0xffffffff;
            picture.flags = Methods.VA_PICTURE_H264_INVALID;
            picture.TopFieldOrderCnt = 0;
            picture.BottomFieldOrderCnt = 0;
        }
    }
}
