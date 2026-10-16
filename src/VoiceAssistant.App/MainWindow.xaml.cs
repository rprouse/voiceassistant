using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using VoiceAssistant.App.ViewModels;

namespace VoiceAssistant.App
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private ScrollViewer? _scrollViewer;
        private bool _followLatest = true;
        private bool _closing;

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = _viewModel = viewModel;
            CaptionList.Loaded += OnCaptionListLoaded;
        }

        // Stop cleanly (committing the utterance in progress) before the window goes away.
        protected override async void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (_closing)
                return;

            e.Cancel = true;
            _closing = true;
            await _viewModel.DisposeAsync();

            // Close can't be called from inside OnClosing, which is where we still are if
            // nothing needed stopping; queue it instead.
            await Dispatcher.BeginInvoke(Close);
        }

        private void OnCaptionListLoaded(object sender, RoutedEventArgs e)
        {
            CaptionList.ApplyTemplate();
            _scrollViewer = (ScrollViewer)CaptionList.Template.FindName("PART_ScrollViewer", CaptionList);
            _scrollViewer.ScrollChanged += OnScrollChanged;
        }

        // Follow new captions unless the user has scrolled up to read; resume once they
        // scroll back to the bottom.
        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_scrollViewer is null)
                return;

            if (e.ExtentHeightChange == 0)
                _followLatest = _scrollViewer.VerticalOffset >= _scrollViewer.ScrollableHeight - 1;
            else if (_followLatest)
                _scrollViewer.ScrollToEnd();
        }

        private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }
}
