
namespace Cognex.InSight.Web.Controls
{
  partial class CvsFilmstrip
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
      this.btnFreeze = new System.Windows.Forms.Button();
      this.SuspendLayout();
      // 
      // btnFreeze
      // 
      this.btnFreeze.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
      this.btnFreeze.Location = new System.Drawing.Point(0, 0);
      this.btnFreeze.Margin = new System.Windows.Forms.Padding(1);
      this.btnFreeze.Name = "btnFreeze";
      this.btnFreeze.Size = new System.Drawing.Size(70, 48);
      this.btnFreeze.TabIndex = 0;
      this.btnFreeze.Text = "Freeze";
      this.btnFreeze.UseVisualStyleBackColor = true;
      this.btnFreeze.Click += new System.EventHandler(this.btnFreeze_Click);
      // 
      // CvsFilmstrip
      // 
      this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
      this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
      this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
      this.Controls.Add(this.btnFreeze);
      this.DoubleBuffered = true;
      this.Margin = new System.Windows.Forms.Padding(0);
      this.Name = "CvsFilmstrip";
      this.Size = new System.Drawing.Size(122, 50);
      this.MouseClick += new System.Windows.Forms.MouseEventHandler(this.CvsFilmstrip_MouseClick);
      this.ResumeLayout(false);

    }

    #endregion

    private System.Windows.Forms.Button btnFreeze;
  }
}
