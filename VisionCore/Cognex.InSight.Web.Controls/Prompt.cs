using System.Windows.Forms;

namespace Cognex.InSight.Web.Controls
{
  public static class Prompt
  {
    /// <summary>
    /// Generic prompt for input.
    /// </summary>
    /// <param name="text">The text for the field to input.</param>
    /// <param name="startValue">The initial value.</param>
    /// <param name="caption">The dialog title.</param>
    /// <returns>The string that was input .</returns>
    public static string ShowDialog(string text, string startValue, string caption)
    {
      Form prompt = new Form()
      {
        Width = 450,
        Height = 350,
        FormBorderStyle = FormBorderStyle.FixedDialog,
        Text = caption,
        StartPosition = FormStartPosition.CenterScreen
      };
      Label textLabel = new Label() { Left = 20, Top = 10, Width = 400, Text = text };
      TextBox textBox = new TextBox() { Left = 20, Top = 30, Width = 400, Height = 250, Text = startValue, Multiline = true };
      Button confirmation = new Button() { Text = "OK", Left = 320, Width = 100, Top = 284, DialogResult = DialogResult.OK };
      confirmation.Click += (sender, e) => { prompt.Close(); };
      prompt.Controls.Add(textBox);
      prompt.Controls.Add(confirmation);
      prompt.Controls.Add(textLabel);
      prompt.AcceptButton = confirmation;

      return prompt.ShowDialog() == DialogResult.OK ? textBox.Text : "";
    }
  }
}
