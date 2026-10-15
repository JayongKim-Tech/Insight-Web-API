using Cognex.InSight.Remoting.Serialization;
using Cognex.InSight.Web;
using System.Drawing;
using System.Windows.Forms;

namespace Cognex.InSight.Web.Controls
{
  /// <summary>
  /// 스프레드시트의 EditPoint/EditRegion 등 버튼 → 도형 편집 팝업 (편집 화면은 ShapeEditorControl).
  /// Enter = 적용, Esc = 취소
  /// </summary>
  public class ShapeEditorForm : Form
  {
    private readonly ShapeEditorControl _editor;

    private ShapeEditorForm(CvsInSight inSight, string location, CvsCogShape shape)
    {
      Text = location + " " + shape.GetType().Name.Replace("CvsCog", "") + " 편집";
      StartPosition = FormStartPosition.CenterScreen;
      Size = new Size(1100, 800);
      MinimumSize = new Size(600, 450);
      ShowIcon = false;
      ShowInTaskbar = false;
      Font = new Font("Malgun Gothic", 9F);

      _editor = new ShapeEditorControl { Dock = DockStyle.Fill };
      _editor.SetSensor(inSight);
      _editor.SetShape(location, shape);

      var bottom = new Panel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(6) };
      var ok = new Button { Text = "적용", Dock = DockStyle.Right, Width = 90, DialogResult = DialogResult.OK };
      var cancel = new Button { Text = "취소", Dock = DockStyle.Right, Width = 90, DialogResult = DialogResult.Cancel };
      bottom.Controls.Add(cancel);
      bottom.Controls.Add(ok);
      AcceptButton = ok;
      CancelButton = cancel;

      Controls.Add(_editor);
      Controls.Add(bottom);

      // 창은 바로 띄우고 이미지/참고 그래픽은 도착하면 그림
      Shown += async (s, e) => await _editor.ReloadAsync();
    }

    /// <summary>편집 가능한 도형인지 (그 외는 기존 텍스트 입력창 사용)</summary>
    public static bool IsSupported(object value)
    {
      return ShapeEditorControl.IsSupported(value);
    }

    /// <summary>모달로 띄워서 편집. 적용하면 true (shape가 직접 수정됨)</summary>
    public static bool Edit(IWin32Window owner, CvsInSight inSight, string location, CvsCogShape shape)
    {
      using (var f = new ShapeEditorForm(inSight, location, shape))
      {
        if (f.ShowDialog(owner) != DialogResult.OK) return false;
        f._editor.FlushFields();
        return true;
      }
    }
  }
}
