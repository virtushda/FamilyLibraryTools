using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using FamilyLibraryTools.Core;

namespace FamilyLibraryTools.Media;

public static class CaptureMetadata
{
    public static MediaDate Read(string path, MediaKind kind)
    {
        if (kind == MediaKind.Video) return ReadVideoDate(path);
        try
        {
            using var stream = FileContents.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (decoder.Frames[0].Metadata is BitmapMetadata metadata)
            {
                string? taken = metadata.DateTaken;
                if (DateTime.TryParse(taken, CultureInfo.CurrentCulture, DateTimeStyles.None, out DateTime value))
                {
                    bool questionable = value.Year < 1800 || value.Year > DateTime.Now.Year + 1;
                    return new(value, "Embedded date taken", questionable, questionable ? "Capture date is outside the expected range. Please confirm it." : "");
                }
            }
            return new(null, "No capture date", true, "No date taken metadata was found. File timestamps are shown only as hints.");
        }
        catch (Exception error) when (error is IOException or NotSupportedException or ArgumentException or COMException)
        {
            return new(null, "Capture metadata unavailable", true, error.Message);
        }
    }

    static MediaDate ReadVideoDate(string path)
    {
        int initialized = CoInitializeEx(IntPtr.Zero, 0);
        try
        {
            Guid interfaceId = typeof(IPropertyStore).GUID;
            int result = SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 0, ref interfaceId, out IPropertyStore store);
            if (result < 0) return new(null, "No video capture date", true, "Windows could not read the video's date properties.");
            try
            {
                var key = new PropertyKey { Format = new Guid("2E4B640D-5019-46D8-8881-55414CC5CAA0"), Id = 100 };
                store.GetValue(ref key, out PropVariant value);
                try
                {
                    if (value.Type == 64 && value.FileTime > 0)
                    {
                        DateTime date = DateTime.FromFileTimeUtc(value.FileTime);
                        return new(date, "Video encoding date (UTC; unconfirmed)", true,
                            "Encoding time may differ from recording time. Confirm the capture month if no filename date is available.");
                    }
                }
                finally { PropVariantClear(ref value); }
            }
            finally { Marshal.ReleaseComObject(store); }
            return new(null, "No video capture date", true, "Choose the recording month. File timestamps are hints only.");
        }
        catch (Exception error) when (error is COMException or ArgumentOutOfRangeException)
        {
            return new(null, "Video date unavailable", true, error.Message);
        }
        finally { if (initialized >= 0) CoUninitialize(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public long FileTime;
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    static extern int SHGetPropertyStoreFromParsingName(string path, IntPtr bindContext, uint flags, ref Guid interfaceId, out IPropertyStore store);
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant value);
    [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr reserved, uint apartment);
    [DllImport("ole32.dll")] static extern void CoUninitialize();
}
