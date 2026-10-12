using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using FamilyLibraryTools.Core;
using FamilyLibraryTools.Media;

namespace FamilyLibraryTools.Views;

public partial class MediaPreview : UserControl
{
    int generation;
    double zoom = 1;
    Vector panRange;
    Point lastPointer;
    bool dragging;
    public BitmapSource? LoadedPhoto { get; private set; }
    public Point Pan { get; private set; }
    public event Action<Point>? PanChanged;

    public MediaPreview() => InitializeComponent();

    public async Task ShowAsync(MediaFile file)
    {
        Clear();
        int request = generation;
        ShowFileDetails(file);
        ResolutionText.Text = "Reading resolution...";
        if (file.Kind == MediaKind.Video)
        {
            VideoControls.Visibility = Visibility.Visible;
            Video.Visibility = Visibility.Visible;
            PreviewMessage.Text = "Loading video preview...";
            Video.Source = new Uri(file.Path);
            Video.Pause();
            return;
        }
        PreviewMessage.Text = "Loading photo preview...";
        try
        {
            BitmapSource bitmap = await Task.Run(() => PhotoPreview.Load(file.Path));
            if (request != generation) return;
            LoadedPhoto = bitmap;
            Photo.Source = bitmap;
            SetResolution(bitmap.PixelWidth, bitmap.PixelHeight);
            UpdateView();
            PreviewMessage.Visibility = Visibility.Collapsed;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or COMException)
        {
            if (request == generation)
            {
                ResolutionText.Text = "Resolution unavailable";
                PreviewMessage.Text = "Preview unavailable with installed Windows codecs.\n" + error.Message;
            }
        }
    }

    public void Clear()
    {
        generation++;
        Video.Close();
        Video.Source = null;
        Video.Visibility = Visibility.Collapsed;
        VideoControls.Visibility = Visibility.Collapsed;
        Photo.Source = null;
        LoadedPhoto = null;
        FileTitle.Text = "";
        FileDetails.Text = "";
        ResolutionText.Text = "";
        PreviewMessage.Text = "Select a file to preview";
        PreviewMessage.Visibility = Visibility.Visible;
        Seek.Value = 0;
        DurationText.Text = "";
        EndPan();
        SetView(1, new Point());
    }

    /// <summary>Shows pixels already decoded from a hash-verified read handle.</summary>
    public void ShowVerifiedPhoto(MediaFile file, BitmapSource bitmap)
    {
        Clear();
        ShowFileDetails(file);
        LoadedPhoto = bitmap;
        Photo.Source = bitmap;
        SetResolution(bitmap.PixelWidth, bitmap.PixelHeight);
        UpdateView();
        PreviewMessage.Visibility = Visibility.Collapsed;
    }

    void ShowFileDetails(MediaFile file)
    {
        FileTitle.Text = file.Name;
        FileTitle.ToolTip = file.Path;
        FileDetails.Text = $"{file.Length:N0} bytes ({file.Length / 1048576.0:N2} MB) | {file.Kind}\n" +
            $"{(file.InOutput ? "Already in output" : "Input file")}\n" +
            $"Date: {(file.Date.Value.HasValue ? file.Date.Value.Value.ToString("yyyy-MM-dd HH:mm:ss") : "Unknown")} | {file.Date.Source}\n" +
            $"Modified: {file.LastWriteUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n{file.Path}";
    }

    /// <summary>Zoom is relative to fit; pan ranges from -1 to 1 between each image's edges.</summary>
    public void SetView(double scale, Point pan)
    {
        zoom = scale;
        Pan = pan;
        UpdateView();
    }

    public void PanBy(Vector movement)
    {
        SetView(zoom, new Point(
            panRange.X > 0 ? Math.Clamp(Pan.X + movement.X / panRange.X, -1, 1) : Pan.X,
            panRange.Y > 0 ? Math.Clamp(Pan.Y + movement.Y / panRange.Y, -1, 1) : Pan.Y));
        PanChanged?.Invoke(Pan);
    }

    void UpdateView()
    {
        double width = LoadedPhoto?.PixelWidth ?? Video.NaturalVideoWidth;
        double height = LoadedPhoto?.PixelHeight ?? Video.NaturalVideoHeight;
        double fit = width > 0 && height > 0 ? Math.Min(Viewport.ActualWidth / width, Viewport.ActualHeight / height) : 0;
        panRange = new Vector(Math.Max(0, (width * fit * zoom - Viewport.ActualWidth) / 2),
            Math.Max(0, (height * fit * zoom - Viewport.ActualHeight) / 2));
        ViewScale.ScaleX = ViewScale.ScaleY = zoom;
        ViewOffset.X = Pan.X * panRange.X;
        ViewOffset.Y = Pan.Y * panRange.Y;
        Viewport.Cursor = zoom > 1 ? Cursors.Hand : Cursors.Arrow;
    }

    void OnViewportSizeChanged(object sender, SizeChangedEventArgs e) => UpdateView();

    void OnPanStart(object sender, MouseButtonEventArgs e)
    {
        if (zoom <= 1 || (LoadedPhoto == null && Video.Source == null)) return;
        lastPointer = e.GetPosition(Viewport);
        dragging = Viewport.CaptureMouse();
        e.Handled = dragging;
    }

    void OnPanMove(object sender, MouseEventArgs e)
    {
        if (!dragging) return;
        if (e.LeftButton != MouseButtonState.Pressed) { EndPan(); return; }
        Point pointer = e.GetPosition(Viewport);
        PanBy(pointer - lastPointer);
        lastPointer = pointer;
        e.Handled = true;
    }

    void EndPan() { dragging = false; if (Viewport.IsMouseCaptured) Viewport.ReleaseMouseCapture(); }
    void OnPanEnd(object sender, MouseButtonEventArgs e) => EndPan();
    void OnPanLost(object sender, MouseEventArgs e) => dragging = false;

    void SetResolution(int width, int height) => ResolutionText.Text = width > 0 && height > 0
        ? $"Resolution: {width} × {height} px" : "Resolution unavailable";

    void OnPlay(object sender, RoutedEventArgs e) => Video.Play();
    void OnPause(object sender, RoutedEventArgs e) => Video.Pause();
    void OnMediaOpened(object sender, RoutedEventArgs e)
    {
        if (Video.Source == null) return;
        PreviewMessage.Visibility = Visibility.Collapsed;
        SetResolution(Video.NaturalVideoWidth, Video.NaturalVideoHeight);
        UpdateView();
        if (Video.NaturalDuration.HasTimeSpan)
        {
            Seek.Maximum = Video.NaturalDuration.TimeSpan.TotalSeconds;
            DurationText.Text = Video.NaturalDuration.TimeSpan.ToString("g");
        }
    }
    void OnMediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (Video.Source == null) return;
        ResolutionText.Text = "Resolution unavailable";
        PreviewMessage.Visibility = Visibility.Visible;
        PreviewMessage.Text = "Video preview unavailable with installed Windows codecs.\n" + e.ErrorException.Message;
    }
    void OnSeek(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Video.Source != null) Video.Position = TimeSpan.FromSeconds(e.NewValue);
    }
}
