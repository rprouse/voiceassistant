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

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (_viewModel.Validate())
                DialogResult = true;
        }
    }
}
