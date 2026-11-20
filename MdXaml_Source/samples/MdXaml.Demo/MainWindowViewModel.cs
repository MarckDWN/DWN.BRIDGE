using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Markup;

namespace MdXaml.Demo
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        public MainWindowViewModel()
        {
            Styles = new List<StyleInfo>
            {
                new StyleInfo("Plain", null),
                new StyleInfo("Standard", MarkdownStyle.Standard),
                new StyleInfo("Compact", MarkdownStyle.Compact),
                new StyleInfo("GithubLike", MarkdownStyle.GithubLike),
                new StyleInfo("Sasabune", MarkdownStyle.Sasabune),
                new StyleInfo("SasabuneStandard", MarkdownStyle.SasabuneStandard),
                new StyleInfo("SasabuneCompact", MarkdownStyle.SasabuneCompact)
            };

            SelectedStyleInfo = Styles[1];

            var subjectType = typeof(MainWindow);
            var subjectAssembly = GetType().Assembly;
            using (Stream stream = subjectAssembly.GetManifestResourceStream(subjectType.FullName + ".md"))
            {
                string ragText;
                if (stream == null)
                {
                    ragText = String.Format("Could not find sample text *{0}*.md", subjectType.FullName);
                }
                else
                {
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        ragText = reader.ReadToEnd();
                    }
                }
                _rawText = ragText;
            }

            _activeTabIndex = 0;

            ForegroundRed = 0x00;
            ForegroundGreen = 0x00;
            ForegroundBlue = 0x00;

            BackgroundRed = 0xFF;
            BackgroundGreen = 0xFF;
            BackgroundBlue = 0xFF;
        }

        #region Style & Colors

        public StyleInfo _selectedStyleInfo;
        public StyleInfo SelectedStyleInfo
        {
            get { return _selectedStyleInfo; }
            set
            {
                if (_selectedStyleInfo == value) return;
                _selectedStyleInfo = value;
                FirePropertyChanged();
            }
        }

        public List<StyleInfo> _styles;
        public List<StyleInfo> Styles
        {
            get { return _styles; }
            set
            {
                if (_styles == value) return;
                _styles = value;
                FirePropertyChanged();
            }
        }

        private byte _ForegroundRed;
        public byte ForegroundRed
        {
            get => _ForegroundRed;
            set
            {
                if (_ForegroundRed == value) return;
                _ForegroundRed = value;
                FirePropertyChanged();
            }
        }

        private byte _ForegroundGreen;
        public byte ForegroundGreen
        {
            get => _ForegroundGreen;
            set
            {
                if (_ForegroundGreen == value) return;
                _ForegroundGreen = value;
                FirePropertyChanged();
            }
        }

        private byte _ForegroundBlue;
        public byte ForegroundBlue
        {
            get => _ForegroundBlue;
            set
            {
                if (_ForegroundBlue == value) return;
                _ForegroundBlue = value;
                FirePropertyChanged();
            }
        }

        private byte _BackgroundRed;
        public byte BackgroundRed
        {
            get => _BackgroundRed;
            set
            {
                if (_BackgroundRed == value) return;
                _BackgroundRed = value;
                FirePropertyChanged();
            }
        }

        private byte _BackgroundGreen;
        public byte BackgroundGreen
        {
            get => _BackgroundGreen;
            set
            {
                if (_BackgroundGreen == value) return;
                _BackgroundGreen = value;
                FirePropertyChanged();
            }
        }

        private byte _BackgroundBlue;
        public byte BackgroundBlue
        {
            get => _BackgroundBlue;
            set
            {
                if (_BackgroundBlue == value) return;
                _BackgroundBlue = value;
                FirePropertyChanged();
            }
        }

        #endregion


        #region Markdown Text

        private Task _textXamlChangeEvent;
        private string _rawText;
        public string RawText
        {
            get => _rawText ?? string.Empty;
            set
            {
                if (_rawText == value) return;
                _rawText = value;
                if (_textXamlChangeEvent == null || _textXamlChangeEvent.Status >= TaskStatus.RanToCompletion)
                {
                    _textXamlChangeEvent = Task.Run(() =>
                    {
                        Task.Delay(100);
                    retry:
                        var oldVal = _rawText;
                        Thread.MemoryBarrier();
                        FirePropertyChanged(nameof(RawText));
                        Thread.MemoryBarrier();
                        if (oldVal != _rawText) goto retry;
                    });
                }
            }
        }

        private FlowDocument _renderingDocument;
        public FlowDocument RenderingDocument
        {
            get => _renderingDocument;
            set
            {
                if (_renderingDocument == value) return;
                _renderingDocument = value;
                FirePropertyChanged();
                RefreshVisibleOutput();
            }
        }

        private FlowDocument _requestingConvertXaml;
        public FlowDocument RequestingConvertXaml
        {
            get => _requestingConvertXaml;
            set
            {
                if (_requestingConvertXaml == value) return;
                _requestingConvertXaml = value;
                FirePropertyChanged();
            }
        }

        private FlowDocument _requestingConvertMatter;
        public FlowDocument RequestingConvertMatter
        {
            get => _requestingConvertMatter;
            set
            {
                if (_requestingConvertMatter == value) return;
                _requestingConvertMatter = value;
                FirePropertyChanged();
            }
        }


        #endregion

        private int _activeTabIndex;
        public int ActiveTabIndex
        {
            get => _activeTabIndex;
            set
            {
                if (_activeTabIndex == value) return;
                _activeTabIndex = value;
                FirePropertyChanged();
                RefreshVisibleOutput();
            }
        }

        private void RefreshVisibleOutput()
        {
            switch (_activeTabIndex)
            {
                case 1:
                    RequestingConvertXaml = RenderingDocument;
                    break;
                case 2:
                    RequestingConvertMatter = RenderingDocument;
                    break;
            }
        }


        /// <summary> <see cref="INotifyPropertyChanged"/> </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// <see cref="INotifyPropertyChanged"/>のイベント発火用
        /// </summary>
        /// <param name="propertyName"></param>
        protected void FirePropertyChanged([CallerMemberName] string propertyName = null)
        {
            if (PropertyChanged != null && propertyName != null)
            {
                var e = new PropertyChangedEventArgs(propertyName);
                PropertyChanged(this, e);
            }
        }
    }
    public class StyleInfo
    {
        public string Name { set; get; }
        public Style Style { set; get; }

        public StyleInfo(string name, Style style)
        {
            Name = name;
            Style = style;
        }

        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }

        public override bool Equals(object val)
        {
            if (val is StyleInfo sf)
            {
                return Name == sf.Name;
            }
            else return false;
        }

        public static bool operator ==(StyleInfo left, StyleInfo right)
        {
            if (Object.ReferenceEquals(left, right)) return true;
            if (Object.ReferenceEquals(left, null)) return false;
            return left.Equals(right);
        }

        public static bool operator !=(StyleInfo left, StyleInfo right)
        {
            return !(left == right);
        }
    }
}