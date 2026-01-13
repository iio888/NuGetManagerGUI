using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Win32;
using NugetManagerGUI.ViewModels;

namespace NugetManagerGUI;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        ProjectsList.ItemsSource = _vm.Projects;
        PackagesList.ItemsSource = _vm.Packages;
        LoadSolutionButton.Click += (s, e) =>
        {
            var dlg = new OpenFileDialog();
            dlg.Filter = "Solution Files|*.sln";
            if (dlg.ShowDialog() == true)
            {
                _vm.LoadSolution(dlg.FileName);
            }
        };

        SettingsButton.Click += (s, e) =>
        {
            var win = new SettingsWindow();
            win.Owner = this;
            win.ShowDialog();
            // reload settings after the settings window closes
            _vm.LoadSettings();
            // reload packages after settings change
            _ = _vm.LoadAllPackagesPublic();
        };

        // Load persisted settings on startup
        _vm.LoadSettings();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Load all packages on startup
        await _vm.LoadAllPackagesPublic();
    }

    private void ProjectsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Update ViewModel.SelectedProjects to reflect current selection
        _vm.SelectedProjects.Clear();
        foreach (var item in ProjectsList.SelectedItems)
        {
            if (item is string s)
                _vm.SelectedProjects.Add(s);
        }
    }
}