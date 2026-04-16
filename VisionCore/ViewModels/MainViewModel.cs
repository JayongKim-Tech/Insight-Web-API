using Cognex.InSight.Web;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using VisionCore;
using VisionCore.Models;
using VisionCore.ViewModels;
using VisionCore.Views;

public class MainViewModel : ViewModelBase
{

    //Config Model
    public ConfigModel Settings => ConfigModel.Instance;
    //Cmaera Control Model
    public CameraControlModel controlModel => CameraControlModel.Instance;
    public FileManagerModel fileManagerModel => FileManagerModel.Instance;

    public DisplayViewModel DisplayVM { get; set; } = new DisplayViewModel();

    public LoggerService logger => LoggerService.Instance;


    public ICommand ConnectCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand OpenImageCommand { get; }

    public ICommand OpenJobCommand { get; }
    public ICommand SaveJobCommand { get; }
    public ICommand SaveJob { get; }

    public ICommand OpenConfigCommand { get; }
    public ICommand TriggerCommand { get; }

    private string _imgUri;
    private string selectedPath;

    private bool _isLive = false;

    public bool IsLive
    {
        get { return _isLive; }

        set
        {
            _isLive = value;
            OnPropertyChanged();
            ExcuteLive(_isLive);
        }
    }

    public MainViewModel()
    {
        ConnectCommand = new RelayCommand(async o => await ExecuteConnect());

        CloseCommand = new RelayCommand(async o => await ExecuteClose());

        //다른이름으로 저장
        SaveJobCommand = new RelayCommand(async o => await ExecuteSaveAsJob());

        // ctrl + s 저장
        SaveJob = new RelayCommand(async o => await ExcuteSaveJob());

        OpenJobCommand = new RelayCommand(o => ExcuteLoadJob());

        OpenImageCommand = new RelayCommand(o => ExcuteOpenImg());

        OpenConfigCommand = new RelayCommand(o => ExecuteOpenConfig());

        TriggerCommand = new RelayCommand(o => ExcuteTrigger());


    }

    private async Task ExecuteConnect()
    {
        try
        {

            Logger.Info($"센서 연결 시도 중...{Settings.IP}:{Settings.Port}");

            await controlModel.ConnectAsync($"{Settings.IP}:{Settings.Port}", $"{Settings.User}", $"{Settings.Password}");

            if (controlModel.IsInSightSensor.Connected)
            {
                // 카메라 Online 상태 동기화
                IsOnline = controlModel.IsInSightSensor.Online;

                //연결 성공시 카메라 Event 등록
                controlModel.IsInSightSensor.ResultsChanged += OnSensorResultsChanged;

                // Cam Online/Offline 변경시 Event 등록
                controlModel.IsInSightSensor.StateChanged += OnSenorOnlineChanged;

                OnPropertyChanged(nameof(controlModel.IsInSightSensor));

                Logger.Success($"센서 연결 성공!");

            }
        }
        catch (Exception ex)
        {
            Logger.Error($"연결 실패: {ex.ToString()}"); // 에러 로그
            System.Windows.MessageBox.Show($"연결 실패: {ex.Message}");
        }
    }


    private async Task ExecuteClose()
    {
        if (controlModel.IsInSightSensor != null && controlModel.IsInSightSensor.Connected)
        {
            Logger.Info("센서 연결 해제 중...");

            //메모리 리크 방지
            controlModel.IsInSightSensor.ResultsChanged -= OnSensorResultsChanged;
            controlModel.IsInSightSensor.StateChanged -= OnSenorOnlineChanged;

            await controlModel.DisconnectAsync(controlModel.IsInSightSensor);
            Logger.Info("센서 연결 해제 완료.");
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
        if (controlModel.IsInSightSensor == null || !controlModel.IsInSightSensor.Connected)
        {
            Logger.Warning("연결된 센서가 없어 상태를 변경할 수 없습니다.");


            _isOnline = !targetValue;
            OnPropertyChanged(nameof(IsOnline));
            return;
        }

        try
        {
            await controlModel.IsInSightSensor.SetSoftOnlineAsync(targetValue);

            //if (targetValue)
            //    Logger.Success("센서 상태: [ONLINE]");
            //else
            //    Logger.Info("센서 상태: [OFFLINE]");

        }
        catch (Exception ex)
        {
            Logger.Error($"상태 변경 오류: {ex.ToString()}");

            _isOnline = !targetValue;

            OnPropertyChanged(nameof(IsOnline));
        }
    }



    private void ExecuteOpenConfig()
    {
        try
        {
            Logger.Info("시스템 설정 창을 엽니다.");

            var configWin = new VisionCore.Views.ConfigWindow();
            var configVM = new VisionCore.ViewModels.ConfigViewModel();

            configWin.DataContext = configVM;
            configWin.Owner = System.Windows.Application.Current.MainWindow; // 부모 창 중앙에 띄우기

            bool? result = configWin.ShowDialog(); // 설정을 다 하고 닫을 때까지 대기

            if (result == true || !configWin.IsVisible)
            {
                Logger.Info("설정 창이 닫혔습니다.");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("설정 창을 여는 중 오류 발생: " + ex.ToString());
        }
    }



    #region JobLoad Window 이후 커스텀 예정..
    //private void ExcuteLoadJob()
    //{
    //    try
    //    {
    //        Logger.Info("JobFile Explorer Open");

    //        var win = new JobBrowserWindow();
    //        var vm = new JobExplorerViewModel();

    //        FileItem fileitem = new FileItem();

    //        win.DataContext = vm;
    //        win.Owner = System.Windows.Application.Current.MainWindow;

    //        bool? result = win.ShowDialog();

    //        if (result == true || !win.IsVisible)
    //        {
    //            Logger.Info("JobFile Explorer Close");
    //        }
    //    }
    //    catch (Exception ex)
    //    {
    //        Logger.Error("JobFile Explorer Open Error: " + ex.Message);
    //    }

    //}
    #endregion


    private async void ExcuteLoadJob()
    {
        using (OpenFileDialog openFileDialog = new OpenFileDialog())
        {
            openFileDialog.Title = "로드할 Job 파일을 선택하세요";
            openFileDialog.InitialDirectory = AppDomain.CurrentDomain.BaseDirectory; // 실행 위치에서 시작
            openFileDialog.Filter = "In-Sight Job Files (*.jobx)|*.jobx|모든 파일 (*.*)|*.*";
            openFileDialog.FilterIndex = 1;
            openFileDialog.RestoreDirectory = true;

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                selectedPath = openFileDialog.FileName;
                logger.Info($"선택된 파일: {selectedPath}");

                try
                {
                    await controlModel.IsInSightSensor.LoadJob(selectedPath);
                }
                catch (Exception ex)
                {
                    logger.Error($"Job 로드 중 예외 발생: {ex.Message}");
                }
            }
        }
    }

    private async Task ExecuteSaveAsJob()
    {
        string targetPath = string.Empty;

        using (SaveFileDialog saveFileDialog = new SaveFileDialog())
        {
            saveFileDialog.Title = "Job 파일을 저장할 경로를 선택하세요";
            saveFileDialog.InitialDirectory = AppDomain.CurrentDomain.BaseDirectory;
            saveFileDialog.Filter = "In-Sight Job Files (*.jobx)|*.jobx|모든 파일 (*.*)|*.*";
            saveFileDialog.FilterIndex = 1;
            saveFileDialog.RestoreDirectory = true;

            saveFileDialog.FileName = "NewJob.jobx";

            if (saveFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                targetPath = saveFileDialog.FileName;
                await controlModel.IsInSightSensor.SaveJob(targetPath);
                logger.Info($"해당 경로에 Job 저장 완료 : {targetPath}");
            }
        }
    }

    private async Task ExcuteSaveJob()
    {
        if(controlModel.IsInSightSensor.Connected)
        {
            try
            {
                await controlModel.IsInSightSensor.SaveJob(selectedPath);
                logger.Info($"해당 경로에 Job 저장 완료 : {selectedPath}");
            }
            catch (Exception ex)
            {
                logger.Error($"Job Save 실패: {ex.ToString()}");
            }
        }

    }


    private void ExcuteOpenImg()
    {
        if(controlModel.IsInSightSensor.Connected)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Title = "이미지 파일을 선택하세요";
                openFileDialog.InitialDirectory = AppDomain.CurrentDomain.BaseDirectory; // 실행 위치에서 시작
                openFileDialog.Filter = "이미지 파일 (*.jpg; *.jpeg; *.png; *.bmp; *.tif)|*.jpg; *.jpeg; *.png; *.bmp; *.tif|" +
                    "모든 파일 (*.*)|*.*";
                openFileDialog.FilterIndex = 1;
                openFileDialog.RestoreDirectory = true;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    string Path = openFileDialog.FileName;
                    logger.Info($"선택된 이미지 파일: {Path}");

                    try
                    {
                        controlModel.IsInSightSensor.LoadImage(Path);
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"이미지 오픈중 예외 발생: {ex.ToString()}");
                    }
                }
            }

        }
    }
    private async void ExcuteTrigger()
    {
        try
        {
            if (controlModel.IsInSightSensor.Connected)
            {
                await controlModel.IsInSightSensor.ManualAcquire();
            }
        }
        catch (Exception ex)
        {
            logger.Error($"Trigger 실패 : {ex.Message}");
        }

    }

    private async void ExcuteLive(bool isLive)
    {
        try
        {
            if (controlModel.IsInSightSensor.Connected)
            {
                await controlModel.IsInSightSensor.SetLiveModeAsync(isLive);

                logger.Info($"{controlModel.IsInSightSensor.LiveMode}");
                if (isLive) logger.Info("Live Mode ON");

                else
                {
                    await controlModel.IsInSightSensor.SendReady();
                    logger.Info("Live Mode OFF");
                }


            }
        }
        catch (Exception ex)
        {
            logger.Error($"Trigger 실패 : {ex.ToString()}");
        }

    }

    private async void OnSensorResultsChanged(object sender, EventArgs e)
    {

        try
        {
            if (controlModel.IsInSightSensor.LiveMode) return;

            // 이미지가 변경되었을 경우 Trigger
            if (_imgUri != controlModel.IsInSightSensor.GetMainImageUrl())
            {
                _imgUri = controlModel.IsInSightSensor.GetMainImageUrl();

                await fileManagerModel.GetCellValueAsync(_imgUri);
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex.ToString());
        }


    }

    private void OnSenorOnlineChanged(object sender, EventArgs e)
    {
        if(controlModel.IsInSightSensor.Connected)
        {
            IsOnline = controlModel.IsInSightSensor.Online;
        }
    }


}