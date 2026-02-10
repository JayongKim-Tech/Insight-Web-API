using Cognex.InSight.Web;
using System;
using System.Threading.Tasks;
using System.Windows.Input;
using VisionCore.Models;
using VisionCore.ViewModels;

public class MainViewModel : ViewModelBase
{
    public DisplayViewModel DisplayVM { get; set; } = new DisplayViewModel();
    private CvsInSight _isInSightSensor = new CvsInSight();
    CameraControlModel model = new CameraControlModel();

    public CvsInSight IsInSightSensor
    {
        get => _isInSightSensor;
        set { _isInSightSensor = value; OnPropertyChanged(); }
    }

    public ICommand ConnectCommand { get; }
    public ICommand CloseCommand { get; }

    public MainViewModel()
    {
        ConnectCommand = new RelayCommand(async o => await ExecuteConnect());

        CloseCommand = new RelayCommand(async o => await ExecuteClose());
    }

    private async Task ExecuteConnect()
    {
        try
        {
            await model.ConnectAsync(IsInSightSensor, "127.0.0.1:61863", "admin", "");

            if (IsInSightSensor.Connected)
            {
                // 카메라 Online 상태 동기화
                IsOnline = IsInSightSensor.Online;
                OnPropertyChanged(nameof(IsInSightSensor));
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"연결 실패: {ex.Message}");
        }
    }


    private async Task ExecuteClose()
    {
        if (IsInSightSensor != null && IsInSightSensor.Connected)
        {
            await model.DisconnectAsync(IsInSightSensor);
        }
        System.Windows.Application.Current.Shutdown();
    }

    private bool _isOnline;
    public bool IsOnline
    {
        get => _isOnline;
        set
        {
            _isOnline = value;

            OnPropertyChanged(nameof(IsOnline));

            ChangeOnlineStateAsync(_isOnline);

        }
    }
    private async void ChangeOnlineStateAsync(bool targetValue)
    {
        if (IsInSightSensor == null || !IsInSightSensor.Connected)
        {
            _isOnline = !targetValue;
            OnPropertyChanged(nameof(IsOnline));
            return;
        }

        try
        {
            var result = await IsInSightSensor.SetSoftOnlineAsync(targetValue);

            if (result == null)
            {
                throw new Exception("API Response Error");
            }

        }
        catch (Exception ex)
        {
            _isOnline = !targetValue;
            OnPropertyChanged(nameof(IsOnline));
        }
    }

}