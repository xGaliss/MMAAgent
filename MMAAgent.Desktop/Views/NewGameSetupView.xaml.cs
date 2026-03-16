using System.Windows;
using System.Windows.Controls;
using MMAAgent.Desktop.ViewModels;

namespace MMAAgent.Desktop.Views
{
    public partial class NewGameSetupView : UserControl
    {
        public NewGameSetupView()
        {
            InitializeComponent();
        }

        private async void Create_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is NewGameSetupViewModel vm)
                await vm.CreateAsync();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is NewGameSetupViewModel vm)
                vm.Cancel();
        }
    }
}