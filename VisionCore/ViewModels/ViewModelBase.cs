using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace VisionCore.ViewModels
{
    public class ViewModelBase : INotifyPropertyChanged
    {
        // 화면과 연결된 '종' (이벤트)
        public event PropertyChangedEventHandler PropertyChanged;

        // 종을 울리는 '행위' (함수)
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            // 나를 지켜보고 있는 화면이 있다면, "값이 바뀌었어!"라고 알려줌
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
