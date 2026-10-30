#if ANDROID
// Android video cutscenes: MediaExtractor + MediaCodec decode the mp4 (H.264 video, AAC audio) on two background threads.
// Video frames come out as YUV_420_888 Images (getOutputImage), are converted to RGBA on the CPU (YuvToRgba, BT.601/709,
// limited/full range from the output format) into the same double buffer the ffmpeg path uses (PortableVideo.cs), and are
// shown at their presentation time against the player clock. Audio is decoded to 16-bit PCM and streamed into a
// DynamicSoundEffectInstance. Any failure (no decoder, odd colour format, exception) returns false / ends the video, so the
// cutscene is skipped exactly like before (the song / story flow continues).
// UNTESTED ON A DEVICE (no emulator on the build PC); compiled into the APK, YUV conversion unit-tested on the PC.
using System;
using System.Threading;
using Android.Media;
using Microsoft.Xna.Framework.Audio;

namespace Microsoft.Xna.Framework.Media
{
    public sealed partial class VideoPlayer
    {
        private Thread _acThread;
        private const long TimeoutUs = 10000;

        private bool AndroidStart(Video video)
        {
            if (Environment.GetEnvironmentVariable("FNF_ANDROID_VIDEO") == "0") return false;
            MediaExtractor probe = null;
            int vTrack = -1, aTrack = -1;
            try
            {
                probe = new MediaExtractor();
                probe.SetDataSource(video.FileName);
                for (int i = 0; i < probe.TrackCount; i++)
                {
                    string mime = probe.GetTrackFormat(i).GetString(MediaFormat.KeyMime) ?? "";
                    if (vTrack < 0 && mime.StartsWith("video/")) vTrack = i;
                    else if (aTrack < 0 && mime.StartsWith("audio/")) aTrack = i;
                }
            }
            catch (Exception ex) { Console.WriteLine("[VIDEO] MediaExtractor failed: " + ex.Message); return false; }
            finally { try { probe?.Release(); } catch { } }
            if (vTrack < 0) { Console.WriteLine("[VIDEO] no video track: " + video.FileName); return false; }

            _quit = false; _videoEnded = false; _frontIndex = -1; _uploadedIndex = -1;
            _front = new byte[_w * _h * 4]; _back = new byte[_w * _h * 4];
            if (aTrack >= 0) { /* audio instance is created by the audio thread once the PCM format is known */ }
            _state = MediaState.Playing;
            _clock.Restart();
            string path = video.FileName;
            _vthread = new Thread(() => DecodeVideo(path, vTrack)) { IsBackground = true, Name = "mediacodec-video" }; _vthread.Start();
            if (aTrack >= 0) { _acThread = new Thread(() => DecodeAudio(path, aTrack)) { IsBackground = true, Name = "mediacodec-audio" }; _acThread.Start(); }
            Console.WriteLine($"[VIDEO] MediaCodec playback started: {System.IO.Path.GetFileName(path)} {_w}x{_h} (video track {vTrack}, audio track {aTrack})");
            return true;
        }

        private void AndroidStop()
        {
            try { _acThread?.Join(500); } catch { }
            _acThread = null;
        }

        private static int GetInt(MediaFormat f, string key, int def)
        {
            try { return f != null && f.ContainsKey(key) ? f.GetInteger(key) : def; } catch { return def; }
        }

        private void DecodeVideo(string path, int track)
        {
            MediaExtractor ex = null; MediaCodec codec = null;
            long frames = 0, dropped = 0;
            try
            {
                ex = new MediaExtractor(); ex.SetDataSource(path); ex.SelectTrack(track);
                var fmt = ex.GetTrackFormat(track);
                codec = MediaCodec.CreateDecoderByType(fmt.GetString(MediaFormat.KeyMime));
                fmt.SetInteger(MediaFormat.KeyColorFormat, (int)MediaCodecCapabilities.Formatyuv420flexible);
                codec.Configure(fmt, null, null, MediaCodecConfigFlags.None);
                codec.Start();
                var info = new MediaCodec.BufferInfo();
                bool inEos = false;
                bool bt709 = _h >= 720, full = false;
                byte[] yb = null, ub = null, vb = null;
                while (!_quit)
                {
                    if (_state == MediaState.Paused) { Thread.Sleep(5); continue; }
                    if (!inEos)
                    {
                        int ii = codec.DequeueInputBuffer(TimeoutUs);
                        if (ii >= 0)
                        {
                            var buf = codec.GetInputBuffer(ii);
                            int n = ex.ReadSampleData(buf, 0);
                            if (n < 0) { codec.QueueInputBuffer(ii, 0, 0, 0, MediaCodecBufferFlags.EndOfStream); inEos = true; }
                            else { codec.QueueInputBuffer(ii, 0, n, ex.SampleTime, MediaCodecBufferFlags.None); ex.Advance(); }
                        }
                    }
                    int oi = codec.DequeueOutputBuffer(info, TimeoutUs);
                    if (oi == (int)MediaCodecInfoState.OutputFormatChanged)
                    {
                        var of = codec.OutputFormat;
                        int std = GetInt(of, MediaFormat.KeyColorStandard, 0), rng = GetInt(of, MediaFormat.KeyColorRange, 0);
                        if (std == 1) bt709 = true; else if (std == 2 || std == 4) bt709 = false;   // COLOR_STANDARD_BT709 / BT601_PAL / BT601_NTSC
                        full = rng == 1;                                                               // COLOR_RANGE_FULL
                        Console.WriteLine($"[VIDEO] decoder output format: {of}");
                        continue;
                    }
                    if (oi < 0) continue;
                    bool eos = (info.Flags & MediaCodecBufferFlags.EndOfStream) != 0;
                    // late by more than ~2 frames (slow phone): drop it instead of converting, so the picture keeps up with the audio
                    bool late = info.PresentationTimeUs / 1e6 < _clock.Elapsed.TotalSeconds - 0.08 && frames > 0;
                    if (late) dropped++;
                    if (info.Size > 0 && !late)
                    {
                        using (var img = codec.GetOutputImage(oi))
                        {
                            if (img != null)
                            {
                                var pl = img.GetPlanes();
                                var crop = img.CropRect;
                                int cw = crop != null && crop.Width() > 0 ? crop.Width() : img.Width, ch = crop != null && crop.Height() > 0 ? crop.Height() : img.Height;
                                int cx = crop?.Left ?? 0, cy = crop?.Top ?? 0;
                                yb = Copy(pl[0].Buffer, yb); ub = Copy(pl[1].Buffer, ub); vb = Copy(pl[2].Buffer, vb);
                                FNF_MonoGame.Platforms.Video.YuvToRgba.Convert(yb, pl[0].RowStride, pl[0].PixelStride, ub, pl[1].RowStride, pl[1].PixelStride,
                                    vb, pl[2].RowStride, pl[2].PixelStride, cx, cy, cw, ch, _back, _w, _h, bt709, full);
                                // present at its timestamp
                                double due = info.PresentationTimeUs / 1e6;
                                while (!_quit && (_clock.Elapsed.TotalSeconds < due || _state == MediaState.Paused)) Thread.Sleep(2);
                                lock (_lock) { (_front, _back) = (_back, _front); _frontIndex = frames++; }
                            }
                        }
                    }
                    codec.ReleaseOutputBuffer(oi, false);
                    if (eos) break;
                }
            }
            catch (Exception e) { if (!_quit) Console.WriteLine($"[VIDEO] MediaCodec video error: {e.GetType().Name}: {e.Message}"); }
            finally
            {
                try { codec?.Stop(); } catch { }
                try { codec?.Release(); } catch { }
                try { ex?.Release(); } catch { }
            }
            Console.WriteLine($"[VIDEO] MediaCodec video done: {frames} frames shown, {dropped} dropped (late)");
            _videoEnded = true;
        }

        /// <summary>Copy a (direct) ByteBuffer plane into a reusable managed array.</summary>
        private static byte[] Copy(Java.Nio.ByteBuffer b, byte[] arr)
        {
            b.Rewind();
            int n = b.Remaining();
            if (arr == null || arr.Length < n) arr = new byte[n];
            b.Get(arr, 0, n);
            return arr;
        }

        private void DecodeAudio(string path, int track)
        {
            MediaExtractor ex = null; MediaCodec codec = null;
            try
            {
                ex = new MediaExtractor(); ex.SetDataSource(path); ex.SelectTrack(track);
                var fmt = ex.GetTrackFormat(track);
                codec = MediaCodec.CreateDecoderByType(fmt.GetString(MediaFormat.KeyMime));
                codec.Configure(fmt, null, null, MediaCodecConfigFlags.None);
                codec.Start();
                var info = new MediaCodec.BufferInfo();
                int rate = GetInt(fmt, MediaFormat.KeySampleRate, 44100), chans = GetInt(fmt, MediaFormat.KeyChannelCount, 2);
                bool inEos = false;
                byte[] pcm = null;
                while (!_quit)
                {
                    if (_state == MediaState.Paused || (_audio != null && _audio.PendingBufferCount >= 4)) { Thread.Sleep(10); continue; }
                    if (!inEos)
                    {
                        int ii = codec.DequeueInputBuffer(TimeoutUs);
                        if (ii >= 0)
                        {
                            var buf = codec.GetInputBuffer(ii);
                            int n = ex.ReadSampleData(buf, 0);
                            if (n < 0) { codec.QueueInputBuffer(ii, 0, 0, 0, MediaCodecBufferFlags.EndOfStream); inEos = true; }
                            else { codec.QueueInputBuffer(ii, 0, n, ex.SampleTime, MediaCodecBufferFlags.None); ex.Advance(); }
                        }
                    }
                    int oi = codec.DequeueOutputBuffer(info, TimeoutUs);
                    if (oi == (int)MediaCodecInfoState.OutputFormatChanged)
                    {
                        var of = codec.OutputFormat;
                        rate = GetInt(of, MediaFormat.KeySampleRate, rate); chans = GetInt(of, MediaFormat.KeyChannelCount, chans);
                        continue;
                    }
                    if (oi < 0) continue;
                    bool eos = (info.Flags & MediaCodecBufferFlags.EndOfStream) != 0;
                    if (info.Size > 0)
                    {
                        if (_audio == null)
                        {
                            _audio = new DynamicSoundEffectInstance(rate, chans >= 2 ? AudioChannels.Stereo : AudioChannels.Mono);
                            _audio.Volume = IsMuted ? 0f : _volume;
                            _audio.Play();
                        }
                        var ob = codec.GetOutputBuffer(oi);
                        ob.Position(info.Offset); ob.Limit(info.Offset + info.Size);
                        if (pcm == null || pcm.Length < info.Size) pcm = new byte[info.Size];
                        ob.Get(pcm, 0, info.Size);
                        int frameBytes = (chans >= 2 ? 2 : 1) * 2;
                        if (chans > 2) pcm = Downmix(pcm, info.Size, chans, out frameBytes);
                        int len = info.Size / (chans > 2 ? chans * 2 : frameBytes) * frameBytes;
                        if (len > 0) _audio.SubmitBuffer(pcm, 0, len);
                    }
                    codec.ReleaseOutputBuffer(oi, false);
                    if (eos) break;
                }
            }
            catch (Exception e) { if (!_quit) Console.WriteLine($"[VIDEO] MediaCodec audio error: {e.GetType().Name}: {e.Message}"); }
            finally
            {
                try { codec?.Stop(); } catch { }
                try { codec?.Release(); } catch { }
                try { ex?.Release(); } catch { }
            }
        }

        /// <summary>&gt;2 channels: keep front left/right (16-bit interleaved).</summary>
        private static byte[] Downmix(byte[] src, int size, int chans, out int frameBytes)
        {
            frameBytes = 4;
            int frames = size / (chans * 2);
            var dst = new byte[Math.Max(src.Length, frames * 4)];
            for (int f = 0; f < frames; f++) System.Array.Copy(src, f * chans * 2, dst, f * 4, 4);
            return dst;
        }
    }
}
#endif
