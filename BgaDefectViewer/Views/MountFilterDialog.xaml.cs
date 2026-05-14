using System.Windows;
using BgaDefectViewer.Helpers;
using BgaDefectViewer.Models;

namespace BgaDefectViewer.Views;

public partial class MountFilterDialog : Window
{
    public class VM : ViewModelBase
    {
        private MountMode _mode = MountMode.Off;
        public MountMode Mode
        {
            get => _mode;
            set
            {
                if (SetProperty(ref _mode, value))
                {
                    OnPropertyChanged(nameof(IsOff));
                    OnPropertyChanged(nameof(IsDual));
                    OnPropertyChanged(nameof(IsTriple));
                    OnPropertyChanged(nameof(ModeIsFilter));
                }
            }
        }

        public bool IsOff
        {
            get => Mode == MountMode.Off;
            set { if (value) Mode = MountMode.Off; }
        }

        public bool IsDual
        {
            get => Mode == MountMode.Dual;
            set { if (value) Mode = MountMode.Dual; }
        }

        public bool IsTriple
        {
            get => Mode == MountMode.Triple;
            set { if (value) Mode = MountMode.Triple; }
        }

        public bool ModeIsFilter => Mode != MountMode.Off;

        private bool _m1 = true;
        public bool Mount1Enabled { get => _m1; set => SetProperty(ref _m1, value); }
        private bool _m2 = true;
        public bool Mount2Enabled { get => _m2; set => SetProperty(ref _m2, value); }
        private bool _m3 = true;
        public bool Mount3Enabled { get => _m3; set => SetProperty(ref _m3, value); }
    }

    public VM Model { get; } = new();

    public MountFilterDialog(MountFilter current)
    {
        InitializeComponent();
        DataContext = Model;
        Model.Mode = current.Mode;
        Model.Mount1Enabled = current.Mount1Enabled;
        Model.Mount2Enabled = current.Mount2Enabled;
        Model.Mount3Enabled = current.Mount3Enabled;
    }

    public MountFilter Result => new()
    {
        Mode = Model.Mode,
        Mount1Enabled = Model.Mount1Enabled,
        Mount2Enabled = Model.Mount2Enabled,
        Mount3Enabled = Model.Mount3Enabled,
    };

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
