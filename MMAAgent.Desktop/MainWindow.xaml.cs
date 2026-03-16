using MMAAgent.Desktop.ViewModels;
using System.Windows;

namespace MMAAgent.Desktop
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }
    }
}