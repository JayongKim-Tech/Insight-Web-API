using System.Windows.Forms;
using System.Windows.Input;
using VisionCore.Models;

namespace VisionCore.ViewModels
{
    public class ConfigViewModel : ViewModelBase
    {
        public ConfigModel Settings => ConfigModel.Instance;

        public ICommand SaveCommand { get; }
        public ICommand BrowseSavePathCommand { get; }
        public ICommand AddCustomFieldCommand { get; }
        public ICommand RemoveCustomFieldCommand { get; }

        public ConfigViewModel()
        {
            LoadConfig();
            SaveCommand = new RelayCommand(o => ExecuteSave());
            BrowseSavePathCommand = new RelayCommand(o => ExecuteBrowseSavePath());
            AddCustomFieldCommand = new RelayCommand(o => ExecuteAddCustomField());
            RemoveCustomFieldCommand = new RelayCommand(o => ExecuteRemoveCustomField(o as CustomFieldDefinition));
        }

        private void LoadConfig()
        {
            Settings.Load();
        }

        private void ExecuteSave()
        {
            Settings.Save();
        }

        private void ExecuteBrowseSavePath()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.SelectedPath = Settings.SaveRootPath;
                dlg.Description = "검사 결과 이미지를 저장할 폴더를 선택하세요";

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    Settings.SaveRootPath = dlg.SelectedPath;
                }
            }
        }

        private void ExecuteAddCustomField()
        {
            Settings.CustomFields.Add(new CustomFieldDefinition { Name = "New Field", CellLocation = "A0" });
        }

        private void ExecuteRemoveCustomField(CustomFieldDefinition field)
        {
            if (field != null)
            {
                Settings.CustomFields.Remove(field);
            }
        }
    }
}
