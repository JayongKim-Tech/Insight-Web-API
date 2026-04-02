
namespace Cognex.InSight.Web.Controls
{
  partial class CvsDisplay
  {
    /// <summary> 
    /// Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary> 
    /// Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
      if (disposing && (components != null))
      {
        components.Dispose();
      }
      base.Dispose(disposing);
    }

    #region Component Designer generated code

    /// <summary> 
    /// Required method for Designer support - do not modify 
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
      this.picBox = new System.Windows.Forms.PictureBox();
      ((System.ComponentModel.ISupportInitialize)(this.picBox)).BeginInit();
      this.SuspendLayout();
      // 
      // picBox
      // 
      this.picBox.BackColor = System.Drawing.SystemColors.ControlDarkDark;
      this.picBox.Dock = System.Windows.Forms.DockStyle.Fill;
      this.picBox.Location = new System.Drawing.Point(0, 0);
      this.picBox.Margin = new System.Windows.Forms.Padding(0);
      this.picBox.Name = "picBox";
      this.picBox.Size = new System.Drawing.Size(680, 480);
      this.picBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
      this.picBox.TabIndex = 2;
      this.picBox.TabStop = false;
      this.picBox.LoadCompleted += new System.ComponentModel.AsyncCompletedEventHandler(this.picBox_LoadCompleted);
      this.picBox.Paint += new System.Windows.Forms.PaintEventHandler(this.picBox_Paint);
      // 
      // CvsDisplay
      // 
      this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
      this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
      this.Controls.Add(this.picBox);
      this.DoubleBuffered = true;
      this.Margin = new System.Windows.Forms.Padding(0);
      this.Name = "CvsDisplay";
      this.Size = new System.Drawing.Size(680, 480);
      ((System.ComponentModel.ISupportInitialize)(this.picBox)).EndInit();
      this.ResumeLayout(false);

    }

    #endregion

    private System.Windows.Forms.PictureBox picBox;
  }
}
