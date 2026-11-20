using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AIBridge.Dialogs
{
    public partial class ImageViewerWindow : Window
    {
        private double _zoomValue = 1.0;
        private Point _scrollStartPoint;
        private Point _scrollStartOffset;
        private bool _isDragging = false;

        public ImageViewerWindow(ImageSource imageSource)
        {
            InitializeComponent();
            DisplayImage.Source = imageSource;
            this.PreviewKeyDown += ImageViewerWindow_PreviewKeyDown;
        }

        private void ImageViewerWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                this.Close();
            }
        }

        private void ImageGrid_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta > 0)
            {
                _zoomValue = Math.Min(10.0, _zoomValue + 0.1);
            }
            else if (e.Delta < 0)
            {
                _zoomValue = Math.Max(0.1, _zoomValue - 0.1);
            }

            ScaleTransform.ScaleX = _zoomValue;
            ScaleTransform.ScaleY = _zoomValue;
            ZoomLabel.Text = $"Zoom: {Math.Round(_zoomValue * 100)}%";
            e.Handled = true;
        }

        private void ImageGrid_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _isDragging = true;
                _scrollStartPoint = e.GetPosition(this);
                _scrollStartOffset = new Point(ScrollViewer.HorizontalOffset, ScrollViewer.VerticalOffset);
                ImageGrid.CaptureMouse();
            }
        }

        private void ImageGrid_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                Point currentPoint = e.GetPosition(this);
                double newX = _scrollStartOffset.X - (currentPoint.X - _scrollStartPoint.X);
                double newY = _scrollStartOffset.Y - (currentPoint.Y - _scrollStartPoint.Y);
                ScrollViewer.ScrollToHorizontalOffset(newX);
                ScrollViewer.ScrollToVerticalOffset(newY);
            }
        }

        private void ImageGrid_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _isDragging = false;
                ImageGrid.ReleaseMouseCapture();
            }
        }

        private void ResetZoom_Click(object sender, RoutedEventArgs e)
        {
            _zoomValue = 1.0;
            ScaleTransform.ScaleX = 1.0;
            ScaleTransform.ScaleY = 1.0;
            ZoomLabel.Text = "Zoom: 100%";
        }

        private void OpenInSystemApp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (DisplayImage.Source is BitmapSource bitmapSource)
                {
                    string tempPath = Path.Combine(Path.GetTempPath(), "dwnbridge_view_" + Guid.NewGuid().ToString("N") + ".png");

                    using (var fileStream = new FileStream(tempPath, FileMode.Create))
                    {
                        BitmapEncoder encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                        encoder.Save(fileStream);
                    }

                    Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true });
                }
                else
                {
                    MessageBox.Show("Sorgente immagine non supportata per l'apertura esterna.", "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante l'apertura dell'immagine: " + ex.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
