using GraphicsEditor.ViewModels;
using System.Windows;

namespace GraphicsEditor.Views
{
    public partial class Main : Window
    {
        private MainViewModel _viewModel;

        public Main()
        {
            InitializeComponent();
            _viewModel = DataContext as MainViewModel;
        }

        private void SelectImage(object sender, RoutedEventArgs e)
        {
            _viewModel.OpenDialogForImageSelecting();
        }

        private void NegativeButtonClicked(object sender, RoutedEventArgs e)
        {
            _viewModel.TransformToNegative();
        }

        private void GrayscaleButtonClicked(object sender, RoutedEventArgs e)
        {
            _viewModel.TransformToGrayscale();
        }

        private void CancelOperationClick(object sender, RoutedEventArgs e)
        {
            _viewModel.CancelOperation();
        }

        private void ChangeColorsClick(object sender, RoutedEventArgs e)
        {
            _viewModel.ChangeColors();
        }
    }
}
