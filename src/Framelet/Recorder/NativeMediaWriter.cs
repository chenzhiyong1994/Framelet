using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Framelet;

/// <summary>Writes H.264 MP4 through Windows Media Foundation. Use and dispose on one MTA thread.</summary>
internal sealed class NativeMediaWriter : IDisposable
{
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private readonly int width;
    private readonly int height;
    private IntPtr writer;
    private uint stream;
    private bool comStarted;
    private bool mediaStarted;
    private bool completed;
    private bool disposed;
    private long previousTime = -1;

    public NativeMediaWriter(string path, int width, int height, int fps)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (width < 2 || height < 2 || width % 2 != 0 || height % 2 != 0)
            throw new ArgumentException("原生录像需要偶数宽高，且至少为 2 × 2 像素。");
        if (fps is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(fps));
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.MTA)
            throw new InvalidOperationException("原生录像编码器必须运行在专用 MTA 线程。");
        this.width = width;
        this.height = height;
        IntPtr attributes = IntPtr.Zero, output = IntPtr.Zero, input = IntPtr.Zero;
        try
        {
            Native.Check(Native.CoInitializeEx(IntPtr.Zero, 0), "初始化录屏线程");
            comStarted = true;
            Native.Check(Native.MFStartup(0x00020070, 0), "启动 Windows 媒体组件");
            mediaStarted = true;
            Native.Check(Native.MFCreateAttributes(out attributes, 1), "创建编码设置");
            Native.SetUInt32(attributes, Native.DisableThrottling, 1);
            Native.Check(Native.MFCreateSinkWriterFromURL(Path.GetFullPath(path), IntPtr.Zero, attributes, out writer), "创建 MP4 文件");

            output = CreateVideoType(Native.H264, fps);
            // Screen content benefits from a higher bitrate than camera video, especially small text.
            var bitrate = (uint)Math.Clamp((long)width * height * fps / 4, 2_000_000, 24_000_000);
            Native.SetUInt32(output, Native.AverageBitrate, bitrate);
            Native.Check(Native.Method<Native.AddStream>(writer, Native.SinkAddStream)(writer, output, out stream), "配置 H.264 编码");

            input = CreateVideoType(Native.Rgb32, fps);
            Native.SetUInt32(input, Native.DefaultStride, checked((uint)(width * 4)));
            Native.Check(Native.Method<Native.SetInputType>(writer, Native.SinkSetInputType)(writer, stream, input, IntPtr.Zero), "配置录屏画面格式");
            Native.Check(Native.Method<Native.NoArgs>(writer, Native.SinkBeginWriting)(writer), "开始 MP4 编码");
        }
        catch
        {
            Native.Release(ref input);
            Native.Release(ref output);
            Native.Release(ref attributes);
            Dispose();
            throw;
        }
        finally
        {
            Native.Release(ref input);
            Native.Release(ref output);
            Native.Release(ref attributes);
        }
    }

    private IntPtr CreateVideoType(Guid subtype, int fps)
    {
        Native.Check(Native.MFCreateMediaType(out var type), "创建视频格式");
        try
        {
            Native.SetGuid(type, Native.MajorType, Native.Video);
            Native.SetGuid(type, Native.Subtype, subtype);
            Native.SetUInt32(type, Native.InterlaceMode, 2); // MFVideoInterlace_Progressive
            Native.SetUInt64(type, Native.FrameSize, ((ulong)(uint)width << 32) | (uint)height);
            Native.SetUInt64(type, Native.FrameRate, ((ulong)(uint)fps << 32) | 1);
            Native.SetUInt64(type, Native.PixelAspectRatio, (1UL << 32) | 1);
            return type;
        }
        catch { Native.Release(ref type); throw; }
    }

    public void WriteFrame(Bitmap frame, long timestamp, long duration)
    {
        CheckThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (completed) throw new InvalidOperationException("录像已经保存。");
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Width != width || frame.Height != height) throw new ArgumentException("录像画面尺寸发生变化。", nameof(frame));
        if (timestamp < 0 || timestamp <= previousTime) throw new ArgumentOutOfRangeException(nameof(timestamp));
        if (duration <= 0) throw new ArgumentOutOfRangeException(nameof(duration));

        IntPtr buffer = IntPtr.Zero, sample = IntPtr.Zero;
        BitmapData? pixels = null;
        var locked = false;
        try
        {
            var length = checked((uint)(width * height * 4));
            Native.Check(Native.MFCreateMemoryBuffer(length, out buffer), "分配录屏画面");
            Native.Check(Native.Method<Native.BufferLock>(buffer, Native.MediaBufferLock)(buffer, out var target, IntPtr.Zero, IntPtr.Zero), "读取录屏缓冲区");
            locked = true;
            pixels = frame.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            // Positive MF_MT_DEFAULT_STRIDE is top-down; preserve Bitmap's logical row order.
            Native.Check(Native.MFCopyImage(target, checked(width * 4), pixels.Scan0, pixels.Stride, checked((uint)(width * 4)), (uint)height), "复制录屏画面");
            frame.UnlockBits(pixels);
            pixels = null;
            Native.Check(Native.Method<Native.NoArgs>(buffer, Native.MediaBufferUnlock)(buffer), "提交录屏缓冲区");
            locked = false;
            Native.Check(Native.Method<Native.SetLength>(buffer, Native.MediaBufferSetLength)(buffer, length), "设置画面长度");
            Native.Check(Native.MFCreateSample(out sample), "创建录屏帧");
            Native.Check(Native.Method<Native.AddBuffer>(sample, Native.SampleAddBuffer)(sample, buffer), "添加录屏画面");
            Native.Check(Native.Method<Native.SetTime>(sample, Native.SampleSetTime)(sample, timestamp), "设置录像时间");
            Native.Check(Native.Method<Native.SetTime>(sample, Native.SampleSetDuration)(sample, duration), "设置画面时长");
            Native.Check(Native.Method<Native.WriteSample>(writer, Native.SinkWriteSample)(writer, stream, sample), "编码录屏画面");
            previousTime = timestamp;
        }
        finally
        {
            if (pixels is not null) frame.UnlockBits(pixels);
            if (locked) Native.Method<Native.NoArgs>(buffer, Native.MediaBufferUnlock)(buffer);
            Native.Release(ref sample);
            Native.Release(ref buffer);
        }
    }

    public void Complete()
    {
        CheckThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (completed) return;
        Native.Check(Native.Method<Native.NoArgs>(writer, Native.SinkFinalize)(writer), "完成 MP4 文件");
        completed = true;
    }

    public void Dispose()
    {
        CheckThread();
        if (disposed) return;
        disposed = true;
        Native.Release(ref writer);
        if (mediaStarted) { Native.MFShutdown(); mediaStarted = false; }
        if (comStarted) { Native.CoUninitialize(); comStarted = false; }
    }

    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != ownerThread)
            throw new InvalidOperationException("原生录像编码器必须在创建它的线程上使用和释放。");
    }

    private static class Native
    {
        // ABI slots and GUIDs are defined by the Windows SDK's mfobjects.h, mfreadwrite.h and mfapi.h:
        // https://github.com/microsoft/win32metadata/tree/main/generation/WinSDK/RecompiledIdlHeaders/um
        // Keep inherited IMFAttributes slots in IMFSample; native COM pointers are exclusively owned here.
        internal const int AttributesSetUInt32 = 21, AttributesSetUInt64 = 22, AttributesSetGuid = 24;
        internal const int SinkAddStream = 3, SinkSetInputType = 4, SinkBeginWriting = 5, SinkWriteSample = 6, SinkFinalize = 11;
        internal const int MediaBufferLock = 3, MediaBufferUnlock = 4, MediaBufferSetLength = 6;
        internal const int SampleSetTime = 36, SampleSetDuration = 38, SampleAddBuffer = 42;
        internal static readonly Guid MajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        internal static readonly Guid Subtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        internal static readonly Guid Video = new("73646976-0000-0010-8000-00aa00389b71");
        internal static readonly Guid H264 = new("34363248-0000-0010-8000-00aa00389b71");
        internal static readonly Guid Rgb32 = new("00000016-0000-0010-8000-00aa00389b71");
        internal static readonly Guid AverageBitrate = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
        internal static readonly Guid InterlaceMode = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
        internal static readonly Guid FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
        internal static readonly Guid FrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
        internal static readonly Guid PixelAspectRatio = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
        internal static readonly Guid DefaultStride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        internal static readonly Guid DisableThrottling = new("08b845d8-2b74-4afe-9d53-be16d2d5ae4f");

        internal static T Method<T>(IntPtr instance, int slot) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));
        internal static void Release(ref IntPtr value)
        {
            if (value == IntPtr.Zero) return;
            Marshal.Release(value);
            value = IntPtr.Zero;
        }
        internal static void Check(int result, string operation)
        {
            if (result < 0) throw new COMException($"{operation}失败（0x{result:X8}）。请确认 Windows 媒体功能可用。", result);
        }
        internal static void SetUInt32(IntPtr value, Guid key, uint data) => Check(Method<AttributeUInt32>(value, AttributesSetUInt32)(value, in key, data), "设置视频参数");
        internal static void SetUInt64(IntPtr value, Guid key, ulong data) => Check(Method<AttributeUInt64>(value, AttributesSetUInt64)(value, in key, data), "设置视频参数");
        internal static void SetGuid(IntPtr value, Guid key, Guid data) => Check(Method<AttributeGuid>(value, AttributesSetGuid)(value, in key, in data), "设置视频格式");

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int NoArgs(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int AttributeUInt32(IntPtr self, in Guid key, uint value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int AttributeUInt64(IntPtr self, in Guid key, ulong value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int AttributeGuid(IntPtr self, in Guid key, in Guid value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int AddStream(IntPtr self, IntPtr mediaType, out uint streamIndex);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int SetInputType(IntPtr self, uint streamIndex, IntPtr mediaType, IntPtr parameters);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int WriteSample(IntPtr self, uint streamIndex, IntPtr sample);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int BufferLock(IntPtr self, out IntPtr data, IntPtr maxLength, IntPtr currentLength);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int SetLength(IntPtr self, uint length);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int AddBuffer(IntPtr self, IntPtr buffer);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int SetTime(IntPtr self, long value);

        [DllImport("ole32.dll", ExactSpelling = true)] internal static extern int CoInitializeEx(IntPtr reserved, uint flags);
        [DllImport("ole32.dll", ExactSpelling = true)] internal static extern void CoUninitialize();
        [DllImport("mfplat.dll", ExactSpelling = true)] internal static extern int MFStartup(uint version, uint flags);
        [DllImport("mfplat.dll", ExactSpelling = true)] internal static extern int MFShutdown();
        [DllImport("mfplat.dll", ExactSpelling = true)] internal static extern int MFCreateMediaType(out IntPtr mediaType);
        [DllImport("mfplat.dll", ExactSpelling = true)] internal static extern int MFCreateAttributes(out IntPtr attributes, uint initialSize);
        [DllImport("mfplat.dll", ExactSpelling = true)] internal static extern int MFCreateMemoryBuffer(uint length, out IntPtr buffer);
        [DllImport("mfplat.dll", ExactSpelling = true)] internal static extern int MFCreateSample(out IntPtr sample);
        [DllImport("mfplat.dll", ExactSpelling = true)] internal static extern int MFCopyImage(IntPtr destination, int destinationStride, IntPtr source, int sourceStride, uint widthInBytes, uint lines);
        [DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] internal static extern int MFCreateSinkWriterFromURL(string path, IntPtr byteStream, IntPtr attributes, out IntPtr sinkWriter);
    }
}
