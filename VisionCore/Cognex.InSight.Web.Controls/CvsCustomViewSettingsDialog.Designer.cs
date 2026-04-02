
namespace Cognex.InSight.Web.Controls
{
  partial class CvsCustomViewSettingsDialog
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

    #region Windows Form Designer generated code

    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
      this.lblCellRange = new System.Windows.Forms.Label();
      this.tbCellRange = new System.Windows.Forms.TextBox();
      this.btnOK = new System.Windows.Forms.Button();
      this.btnCancel = new System.Windows.Forms.Button();
      this.btnNext = new System.Windows.Forms.Button();
      this.btnPrev = new System.Windows.Forms.Button();
      this.label1 = new System.Windows.Forms.Label();
      this.label2 = new System.Windows.Forms.Label();
      this.label3 = new System.Windows.Forms.Label();
      this.label4 = new System.Windows.Forms.Label();
      this.label5 = new System.Windows.Forms.Label();
      this.numX = new System.Windows.Forms.NumericUpDown();
      this.numY = new System.Windows.Forms.NumericUpDown();
      this.numWidth = new System.Windows.Forms.NumericUpDown();
      this.numHeight = new System.Windows.Forms.NumericUpDown();
      ((System.ComponentModel.ISupportInitialize)(this.numX)).BeginInit();
      ((System.ComponentModel.ISupportInitialize)(this.numY)).BeginInit();
      ((System.ComponentModel.ISupportInitialize)(this.numWidth)).BeginInit();
      ((System.ComponentModel.ISupportInitialize)(this.numHeight)).BeginInit();
      this.SuspendLayout();
      // 
      // lblCellRange
      // 
      this.lblCellRange.AutoSize = true;
      this.lblCellRange.Location = new System.Drawing.Point(36, 9);
      this.lblCellRange.Name = "lblCellRange";
      this.lblCellRange.Size = new System.Drawing.Size(71, 15);
      this.lblCellRange.TabIndex = 1;
      this.lblCellRange.Text = "Cell Range:";
      // 
      // tbCellRange
      // 
      this.tbCellRange.Location = new System.Drawing.Point(39, 25);
      this.tbCellRange.MaxLength = 20;
      this.tbCellRange.Name = "tbCellRange";
      this.tbCellRange.Size = new System.Drawing.Size(129, 20);
      this.tbCellRange.TabIndex = 2;
      // 
      // btnOK
      // 
      this.btnOK.DialogResult = System.Windows.Forms.DialogResult.OK;
      this.btnOK.Location = new System.Drawing.Point(102, 250);
      this.btnOK.Name = "btnOK";
      this.btnOK.Size = new System.Drawing.Size(75, 23);
      this.btnOK.TabIndex = 3;
      this.btnOK.Text = "OK";
      this.btnOK.UseVisualStyleBackColor = true;
      // 
      // btnCancel
      // 
      this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
      this.btnCancel.Location = new System.Drawing.Point(183, 250);
      this.btnCancel.Name = "btnCancel";
      this.btnCancel.Size = new System.Drawing.Size(75, 23);
      this.btnCancel.TabIndex = 4;
      this.btnCancel.Text = "Cancel";
      this.btnCancel.UseVisualStyleBackColor = true;
      // 
      // btnNext
      // 
      this.btnNext.Location = new System.Drawing.Point(134, 193);
      this.btnNext.Name = "btnNext";
      this.btnNext.Size = new System.Drawing.Size(75, 23);
      this.btnNext.TabIndex = 3;
      this.btnNext.Text = "Next ->";
      this.btnNext.UseVisualStyleBackColor = true;
      this.btnNext.Click += new System.EventHandler(this.btnNext_Click);
      // 
      // btnPrev
      // 
      this.btnPrev.Location = new System.Drawing.Point(12, 193);
      this.btnPrev.Name = "btnPrev";
      this.btnPrev.Size = new System.Drawing.Size(75, 23);
      this.btnPrev.TabIndex = 3;
      this.btnPrev.Text = "<- Prev";
      this.btnPrev.UseVisualStyleBackColor = true;
      this.btnPrev.Click += new System.EventHandler(this.btnPrev_Click);
      // 
      // label1
      // 
      this.label1.AutoSize = true;
      this.label1.Location = new System.Drawing.Point(36, 59);
      this.label1.Name = "label1";
      this.label1.Size = new System.Drawing.Size(79, 15);
      this.label1.TabIndex = 5;
      this.label1.Text = "View Region:";
      // 
      // label2
      // 
      this.label2.Location = new System.Drawing.Point(36, 76);
      this.label2.Name = "label2";
      this.label2.Size = new System.Drawing.Size(43, 17);
      this.label2.TabIndex = 10;
      this.label2.Text = "X:";
      this.label2.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
      // 
      // label3
      // 
      this.label3.Location = new System.Drawing.Point(38, 102);
      this.label3.Name = "label3";
      this.label3.Size = new System.Drawing.Size(43, 17);
      this.label3.TabIndex = 11;
      this.label3.Text = "Y:";
      this.label3.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
      // 
      // label4
      // 
      this.label4.Location = new System.Drawing.Point(36, 130);
      this.label4.Name = "label4";
      this.label4.Size = new System.Drawing.Size(43, 13);
      this.label4.TabIndex = 12;
      this.label4.Text = "Width:";
      this.label4.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
      // 
      // label5
      // 
      this.label5.Location = new System.Drawing.Point(38, 154);
      this.label5.Name = "label5";
      this.label5.Size = new System.Drawing.Size(41, 17);
      this.label5.TabIndex = 13;
      this.label5.Text = "Height:";
      this.label5.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
      // 
      // numX
      // 
      this.numX.Location = new System.Drawing.Point(85, 76);
      this.numX.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
      this.numX.Name = "numX";
      this.numX.Size = new System.Drawing.Size(83, 20);
      this.numX.TabIndex = 14;
      // 
      // numY
      // 
      this.numY.Location = new System.Drawing.Point(85, 102);
      this.numY.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
      this.numY.Name = "numY";
      this.numY.Size = new System.Drawing.Size(83, 20);
      this.numY.TabIndex = 15;
      // 
      // numWidth
      // 
      this.numWidth.Location = new System.Drawing.Point(85, 128);
      this.numWidth.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
      this.numWidth.Name = "numWidth";
      this.numWidth.Size = new System.Drawing.Size(83, 20);
      this.numWidth.TabIndex = 16;
      // 
      // numHeight
      // 
      this.numHeight.Location = new System.Drawing.Point(85, 154);
      this.numHeight.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
      this.numHeight.Name = "numHeight";
      this.numHeight.Size = new System.Drawing.Size(83, 20);
      this.numHeight.TabIndex = 17;
      // 
      // CvsCustomViewSettingsDialog
      // 
      this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
      this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
      this.ClientSize = new System.Drawing.Size(262, 276);
      this.Controls.Add(this.numHeight);
      this.Controls.Add(this.numWidth);
      this.Controls.Add(this.numY);
      this.Controls.Add(this.numX);
      this.Controls.Add(this.label5);
      this.Controls.Add(this.label4);
      this.Controls.Add(this.label3);
      this.Controls.Add(this.label2);
      this.Controls.Add(this.label1);
      this.Controls.Add(this.btnCancel);
      this.Controls.Add(this.btnOK);
      this.Controls.Add(this.btnNext);
      this.Controls.Add(this.btnPrev);
      this.Controls.Add(this.tbCellRange);
      this.Controls.Add(this.lblCellRange);
      this.MaximizeBox = false;
      this.MinimizeBox = false;
      this.Name = "CvsCustomViewSettingsDialog";
      this.ShowInTaskbar = false;
      this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
      this.Text = "Custom View Settings";
      this.Load += new System.EventHandler(this.CvsCustomViewSettingsDialog_Load);
      ((System.ComponentModel.ISupportInitialize)(this.numX)).EndInit();
      ((System.ComponentModel.ISupportInitialize)(this.numY)).EndInit();
      ((System.ComponentModel.ISupportInitialize)(this.numWidth)).EndInit();
      ((System.ComponentModel.ISupportInitialize)(this.numHeight)).EndInit();
      this.ResumeLayout(false);
      this.PerformLayout();

    }

    #endregion

    private System.Windows.Forms.Label lblCellRange;
    private System.Windows.Forms.TextBox tbCellRange;
    private System.Windows.Forms.Button btnOK;
    private System.Windows.Forms.Button btnCancel;
    private System.Windows.Forms.Button btnNext;
    private System.Windows.Forms.Button btnPrev;
    private System.Windows.Forms.Label label1;
    private System.Windows.Forms.Label label2;
    private System.Windows.Forms.Label label3;
    private System.Windows.Forms.Label label4;
    private System.Windows.Forms.Label label5;
    private System.Windows.Forms.NumericUpDown numX;
    private System.Windows.Forms.NumericUpDown numY;
    private System.Windows.Forms.NumericUpDown numWidth;
    private System.Windows.Forms.NumericUpDown numHeight;
  }
}
