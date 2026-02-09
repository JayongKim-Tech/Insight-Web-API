using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

public class RelayCommand : ICommand
{
    // 1. 실제 실행할 '동작(함수)'을 저장하는 변수입니다. (Action = 리턴값이 없는 함수 타입)
    private readonly Action<object> _execute;

    // 2. 이 버튼이 눌릴 수 있는 '조건'을 저장하는 변수입니다. (Predicate = 참/거짓을 반환하는 함수 타입)
    private readonly Predicate<object> _canExecute;

    // 3. 생성자: 리모컨을 만들 때 "뭐 할지(_execute)"와 "언제 할지(_canExecute)"를 주입받습니다.
    public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    // 4. [ICommand 규칙] 현재 버튼이 활성화 상태인지 확인합니다.
    public bool CanExecute(object parameter) => _canExecute == null || _canExecute(parameter);

    // 5. [ICommand 규칙] 버튼이 눌렸을 때 실제로 실행되는 핵심 부분입니다.
    public void Execute(object parameter) => _execute(parameter);

    // 6. [ICommand 규칙] 버튼의 활성/비활성 상태가 바뀌어야 할 때 UI에 신호를 보냅니다.
    public event EventHandler CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }
}