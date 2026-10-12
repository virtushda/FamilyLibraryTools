using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FamilyLibraryTools.Core;

namespace FamilyLibraryTools.Media;

public static class PhotoPreview
{
    public static BitmapSource Load(string path)
    {
        using var stream = FileContents.OpenRead(path);
        return Load(stream);
    }

    /// <summary>Decodes the same open, verified file used by the read-only benchmark.</summary>
    public static BitmapSource Load(Stream stream)
    {
        stream.Position = 0;
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        BitmapFrame frame = decoder.Frames[0];
        ushort orientation = 1;
        if (frame.Metadata is BitmapMetadata metadata)
        {
            string query = metadata.Format == "tiff" ? "/ifd/{ushort=274}" : "/app1/ifd/{ushort=274}";
            if (metadata.ContainsQuery(query) && metadata.GetQuery(query) is ushort value) orientation = value;
        }
        stream.Position = 0;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        // Keep original pixels so zoom reveals real detail and resolution stays accurate.
        bitmap.EndInit();
        bitmap.Freeze();
        Transform? transform = orientation switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => new MatrixTransform(0, 1, 1, 0, 0, 0),
            6 => new RotateTransform(90),
            7 => new MatrixTransform(0, -1, -1, 0, 0, 0),
            8 => new RotateTransform(270),
            _ => null
        };
        if (transform == null) return bitmap;
        var oriented = new TransformedBitmap(bitmap, transform);
        oriented.Freeze();
        return oriented;
    }

    /// <summary>A conservative visual hint; never used to consolidate files or preselect a winner.</summary>
    public static bool LooksSimilar(BitmapSource left, BitmapSource right)
    {
        double ratioLeft = (double)left.PixelWidth / left.PixelHeight;
        double ratioRight = (double)right.PixelWidth / right.PixelHeight;
        if (Math.Abs(ratioLeft / ratioRight - 1) > 0.01) return false;
        byte[] first = Sample(left), second = Sample(right);
        long difference = 0;
        int largeDifferences = 0;
        for (int index = 0; index < first.Length; index++)
        {
            int delta = Math.Abs(first[index] - second[index]);
            difference += delta;
            if (delta > 32) largeDifferences++;
        }
        return difference / (double)first.Length < 9 && largeDifferences < first.Length / 25;
    }

    static byte[] Sample(BitmapSource image)
    {
        var scaled = new TransformedBitmap(image, new ScaleTransform(32.0 / image.PixelWidth, 32.0 / image.PixelHeight));
        var rgb = new FormatConvertedBitmap(scaled, PixelFormats.Bgr24, null, 0);
        byte[] pixels = new byte[32 * 32 * 3];
        rgb.CopyPixels(pixels, 32 * 3, 0);
        return pixels;
    }
}
