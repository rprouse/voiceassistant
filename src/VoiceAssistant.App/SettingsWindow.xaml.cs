using System.Windows;
using VoiceAssistant.App.ViewModels;

namespace VoiceAssistant.App
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsViewModel _viewModel;

        public SettingsWindow(SettingsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = _viewModel = viewModel;
        }

        // Closing mid-download cancels it; the partial file is cleaned up.
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_viewModel.IsDownloading)
                _viewModel.DownloadCancelCommand.Execute(null);
            base.OnClosing(e);
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (_viewModel.Validate())
                DialogResult = true;
        }
    }
}
