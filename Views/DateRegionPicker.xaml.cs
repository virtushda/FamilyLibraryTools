using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace FamilyLibraryTools.Views;

/// <summary>Selects pixels in the already oriented preview, independent of image DPI and letterboxing.</summary>
public partial class DateRegionPicker : UserControl
{
    BitmapSource? image;
    Point start;
    bool dragging;
    Int32Rect region;
    public event Action<Int32Rect?>? Completed;

    public DateRegionPicker() => InitializeComponent();

    public void Show(BitmapSource photo)
    {
        Clear();
        image = photo;
        Photo.Source = photo;
        Visibility = Visibility.Visible;
        LayoutImage();
    }

    public void Clear()
    {
        dragging = false;
        Surface.ReleaseMouseCapture();
        image = null;
        Photo.Source = null;
        region = Int32Rect.Empty;
        Outline.Visibility = Visibility.Collapsed;
        ReadRegion.IsEnabled = false;
        Visibility = Visibility.Collapsed;
    }

    static Rect ImageBounds(BitmapSource photo, Size viewport)
    {
        double scale = Math.Min(viewport.Width / photo.PixelWidth, viewport.Height / photo.PixelHeight);
        return new Rect((viewport.Width - photo.PixelWidth * scale) / 2, (viewport.Height - photo.PixelHeight * scale) / 2,
            photo.PixelWidth * scale, photo.PixelHeight * scale);
    }

    public static Int32Rect MapSelection(BitmapSource photo, Size viewport, Point first, Point last)
    {
        Rect bounds = ImageBounds(photo, viewport);
        if (bounds.Width <= 0 || bounds.Height <= 0 || !bounds.Contains(first)) return Int32Rect.Empty;
        Rect selected = Rect.Intersect(bounds, new Rect(first, last));
        if (selected.IsEmpty || selected.Width < 3 || selected.Height < 3) return Int32Rect.Empty;
        int left = Math.Clamp((int)Math.Floor((selected.Left - bounds.Left) * photo.PixelWidth / bounds.Width), 0, photo.PixelWidth - 1);
        int top = Math.Clamp((int)Math.Floor((selected.Top - bounds.Top) * photo.PixelHeight / bounds.Height), 0, photo.PixelHeight - 1);
        int right = Math.Clamp((int)Math.Ceiling((selected.Right - bounds.Left) * photo.PixelWidth / bounds.Width), left + 1, photo.PixelWidth);
        int bottom = Math.Clamp((int)Math.Ceiling((selected.Bottom - bounds.Top) * photo.PixelHeight / bounds.Height), top + 1, photo.PixelHeight);
        return new Int32Rect(left, top, right - left, bottom - top);
    }

    void LayoutImage()
    {
        if (image == null) return;
        Rect bounds = ImageBounds(image, Surface.RenderSize);
        Canvas.SetLeft(Photo, bounds.Left); Canvas.SetTop(Photo, bounds.Top);
        Photo.Width = bounds.Width; Photo.Height = bounds.Height;
        // A resized viewport invalidates a drag expressed in its old coordinates.
        dragging = false;
        Surface.ReleaseMouseCapture();
        region = Int32Rect.Empty;
        ReadRegion.IsEnabled = false;
        Outline.Visibility = Visibility.Collapsed;
    }

    void Select(Point first, Point last)
    {
        if (image == null) return;
        region = MapSelection(image, Surface.RenderSize, first, last);
        ReadRegion.IsEnabled = !region.IsEmpty;
        Outline.Visibility = region.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        if (region.IsEmpty) return;
        Rect selected = Rect.Intersect(ImageBounds(image, Surface.RenderSize), new Rect(first, last));
        Canvas.SetLeft(Outline, selected.Left); Canvas.SetTop(Outline, selected.Top);
        Outline.Width = selected.Width; Outline.Height = selected.Height;
    }

    void OnStart(object sender, MouseButtonEventArgs e)
    {
        if (image == null || !ImageBounds(image, Surface.RenderSize).Contains(e.GetPosition(Surface))) return;
        start = e.GetPosition(Surface);
        Select(start, start);
        dragging = Surface.CaptureMouse();
        e.Handled = true;
    }
    void OnMove(object sender, MouseEventArgs e)
    {
        if (dragging && e.LeftButton == MouseButtonState.Pressed) Select(start, e.GetPosition(Surface));
    }
    void OnEnd(object sender, MouseButtonEventArgs e)
    {
        if (!dragging) return;
        Select(start, e.GetPosition(Surface));
        dragging = false;
        Surface.ReleaseMouseCapture();
        e.Handled = true;
    }
    void OnCaptureLost(object sender, MouseEventArgs e)
    {
        if (!dragging) return;
        dragging = false;
        region = Int32Rect.Empty;
        ReadRegion.IsEnabled = false;
        Outline.Visibility = Visibility.Collapsed;
    }
    void OnSizeChanged(object sender, SizeChangedEventArgs e) => LayoutImage();
    void OnRead(object sender, RoutedEventArgs e) { if (!region.IsEmpty) Completed?.Invoke(region); }
    void OnCancel(object sender, RoutedEventArgs e) => Completed?.Invoke(null);
}
