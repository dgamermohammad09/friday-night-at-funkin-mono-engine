using System;

namespace FNF_MonoGame.Platforms.Video
{
    /// <summary>
    /// YUV 4:2:0 -> RGBA8888 for the Android MediaCodec video path (Image planes from getOutputImage, YUV_420_888):
    /// handles planar (I420, pixelStride 1) and semi-planar (NV12 / NV21, pixelStride 2) chroma, row strides, crop
    /// offsets and nearest-neighbour scaling to the destination size. BT.601 or BT.709 matrix, limited (TV) or full
    /// range, 16.16 fixed point. Pure C# so it is unit-tested on the PC (scratch/yuvtest) against ffmpeg's converter.
    /// </summary>
    public static class YuvToRgba
    {
        public static void Convert(byte[] y, int yRow, int yPix, byte[] u, int uRow, int uPix, byte[] v, int vRow, int vPix,
                                   int cropX, int cropY, int srcW, int srcH, byte[] dst, int dstW, int dstH, bool bt709, bool fullRange)
        {
            // coefficients * 65536
            int cr_r, cb_g, cr_g, cb_b, ys, yoff;
            if (bt709) { cr_r = Fx(1.5748); cb_g = Fx(0.1873); cr_g = Fx(0.4681); cb_b = Fx(1.8556); }
            else { cr_r = Fx(1.4020); cb_g = Fx(0.3441); cr_g = Fx(0.7141); cb_b = Fx(1.7720); }
            if (fullRange) { ys = 65536; yoff = 0; }
            else
            {   // limited range: Y 16..235, C 16..240
                ys = (int)(65536 * 255.0 / 219.0 + 0.5); yoff = 16;
                double cs = 255.0 / 224.0;
                cr_r = (int)(cr_r * cs + 0.5); cb_g = (int)(cb_g * cs + 0.5); cr_g = (int)(cr_g * cs + 0.5); cb_b = (int)(cb_b * cs + 0.5);
            }
            bool same = dstW == srcW && dstH == srcH;
            for (int dy = 0; dy < dstH; dy++)
            {
                int sy = (same ? dy : (int)((long)dy * srcH / dstH)) + cropY;
                int yRowOff = sy * yRow, cRowU = (sy >> 1) * uRow, cRowV = (sy >> 1) * vRow;
                int o = dy * dstW * 4;
                for (int dx = 0; dx < dstW; dx++, o += 4)
                {
                    int sx = (same ? dx : (int)((long)dx * srcW / dstW)) + cropX;
                    int Y = (y[yRowOff + sx * yPix] - yoff) * ys;
                    int cx = sx >> 1;
                    int Cb = u[cRowU + cx * uPix] - 128, Cr = v[cRowV + cx * vPix] - 128;
                    int r = (Y + cr_r * Cr + 32768) >> 16;
                    int g = (Y - cb_g * Cb - cr_g * Cr + 32768) >> 16;
                    int b = (Y + cb_b * Cb + 32768) >> 16;
                    dst[o] = (byte)(r < 0 ? 0 : r > 255 ? 255 : r);
                    dst[o + 1] = (byte)(g < 0 ? 0 : g > 255 ? 255 : g);
                    dst[o + 2] = (byte)(b < 0 ? 0 : b > 255 ? 255 : b);
                    dst[o + 3] = 255;
                }
            }
        }

        private static int Fx(double d) => (int)(d * 65536 + 0.5);
    }
}
