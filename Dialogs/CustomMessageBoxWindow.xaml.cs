using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AIBridge.Dialogs
{
    public partial class CustomMessageBoxWindow : Window
    {
        public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

        public CustomMessageBoxWindow(string message, string caption, MessageBoxButton button, MessageBoxImage image)
        {
            InitializeComponent();
            TitleText.Text = caption;
            MessageText.Text = message;

            if (button == MessageBoxButton.OK)
            {
                AddButton(Unclassified.TxLib.Tx.T("OK"), MessageBoxResult.OK, true);
            }
            else if (button == MessageBoxButton.OKCancel)
            {
                AddButton(Unclassified.TxLib.Tx.T("OK"), MessageBoxResult.OK, true);
                AddButton(Unclassified.TxLib.Tx.T("Cancel"), MessageBoxResult.Cancel, false);
            }
            else if (button == MessageBoxButton.YesNo)
            {
                AddButton(Unclassified.TxLib.Tx.T("Yes"), MessageBoxResult.Yes, true);
                AddButton(Unclassified.TxLib.Tx.T("No"), MessageBoxResult.No, false);
            }
            else if (button == MessageBoxButton.YesNoCancel)
            {
                AddButton(Unclassified.TxLib.Tx.T("Yes"), MessageBoxResult.Yes, true);
                AddButton(Unclassified.TxLib.Tx.T("No"), MessageBoxResult.No, false);
                AddButton(Unclassified.TxLib.Tx.T("Cancel"), MessageBoxResult.Cancel, false);
            }
        }

        private void AddButton(string text, MessageBoxResult result, bool isDefault)
        {
            var btn = new Button
            {
                Content = text,
                Width = 80,
                Height = 35,
                Margin = new Thickness(10, 0, 0, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4A90E2")),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                IsDefault = isDefault
            };
            
            // Stile per arrotondare e togliere bordo blu strano WPF
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(cp);
            template.VisualTree = border;
            btn.Template = template;

            btn.Click += (s, e) =>
            {
                Result = result;
                this.Close();
            };
            ButtonsPanel.Children.Add(btn);
        }
    }

    public static class CustomMessageBox
    {
        public static MessageBoxResult Show(string messageBoxText, string caption = "", MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
        {
            return Application.Current.Dispatcher.Invoke(() =>
            {
                string safeCaption = string.IsNullOrEmpty(caption) ? Unclassified.TxLib.Tx.T("Message") : caption;
                var window = new CustomMessageBoxWindow(messageBoxText, safeCaption, button, icon);
                
                if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsVisible)
                {
                    window.Owner = Application.Current.MainWindow;
                }
                else
                {
                    window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
                
                window.ShowDialog();
                return window.Result;
            });
        }
    }
}
