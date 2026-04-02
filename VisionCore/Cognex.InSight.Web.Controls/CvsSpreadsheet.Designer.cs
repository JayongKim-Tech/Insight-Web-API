
namespace Cognex.InSight.Web.Controls
{
  partial class CvsSpreadsheet
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
      System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
      System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle2 = new System.Windows.Forms.DataGridViewCellStyle();
      System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle3 = new System.Windows.Forms.DataGridViewCellStyle();
      System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle4 = new System.Windows.Forms.DataGridViewCellStyle();
      System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle5 = new System.Windows.Forms.DataGridViewCellStyle();
      this.gridView = new System.Windows.Forms.DataGridView();
      ((System.ComponentModel.ISupportInitialize)(this.gridView)).BeginInit();
      this.SuspendLayout();
      // 
      // gridView
      // 
      this.gridView.AllowDrop = true;
      this.gridView.AllowUserToAddRows = false;
      this.gridView.AllowUserToDeleteRows = false;
      dataGridViewCellStyle1.BackColor = System.Drawing.SystemColors.ControlLight;
      this.gridView.AlternatingRowsDefaultCellStyle = dataGridViewCellStyle1;
      this.gridView.BackgroundColor = System.Drawing.SystemColors.ControlDark;
      dataGridViewCellStyle2.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
      dataGridViewCellStyle2.BackColor = System.Drawing.SystemColors.ControlDark;
      dataGridViewCellStyle2.Font = new System.Drawing.Font("Arial", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
      dataGridViewCellStyle2.ForeColor = System.Drawing.SystemColors.WindowText;
      dataGridViewCellStyle2.SelectionBackColor = System.Drawing.SystemColors.Highlight;
      dataGridViewCellStyle2.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
      dataGridViewCellStyle2.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
      this.gridView.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle2;
      this.gridView.ColumnHeadersHeight = 20;
      dataGridViewCellStyle3.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
      dataGridViewCellStyle3.BackColor = System.Drawing.SystemColors.ControlLight;
      dataGridViewCellStyle3.Font = new System.Drawing.Font("Arial", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
      dataGridViewCellStyle3.ForeColor = System.Drawing.Color.Navy;
      dataGridViewCellStyle3.SelectionBackColor = System.Drawing.SystemColors.Highlight;
      dataGridViewCellStyle3.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
      dataGridViewCellStyle3.WrapMode = System.Windows.Forms.DataGridViewTriState.False;
      this.gridView.DefaultCellStyle = dataGridViewCellStyle3;
      this.gridView.Dock = System.Windows.Forms.DockStyle.Fill;
      this.gridView.Location = new System.Drawing.Point(0, 0);
      this.gridView.Margin = new System.Windows.Forms.Padding(0);
      this.gridView.MultiSelect = false;
      this.gridView.Name = "gridView";
      this.gridView.ReadOnly = true;
      dataGridViewCellStyle4.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
      dataGridViewCellStyle4.BackColor = System.Drawing.SystemColors.ControlDark;
      dataGridViewCellStyle4.Font = new System.Drawing.Font("Arial Narrow", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
      dataGridViewCellStyle4.ForeColor = System.Drawing.SystemColors.WindowText;
      dataGridViewCellStyle4.SelectionBackColor = System.Drawing.SystemColors.Highlight;
      dataGridViewCellStyle4.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
      dataGridViewCellStyle4.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
      this.gridView.RowHeadersDefaultCellStyle = dataGridViewCellStyle4;
      this.gridView.RowHeadersVisible = false;
      this.gridView.RowHeadersWidth = 62;
      dataGridViewCellStyle5.BackColor = System.Drawing.SystemColors.ControlLight;
      this.gridView.RowsDefaultCellStyle = dataGridViewCellStyle5;
      this.gridView.RowTemplate.Height = 20;
      this.gridView.RowTemplate.ReadOnly = true;
      this.gridView.ScrollBars = System.Windows.Forms.ScrollBars.None;
      this.gridView.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.CellSelect;
      this.gridView.Size = new System.Drawing.Size(150, 162);
      this.gridView.TabIndex = 16;
      this.gridView.CellDoubleClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.gridView_CellDoubleClick);
      // 
      // CvsSpreadsheet
      // 
      this.AllowDrop = true;
      this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
      this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
      this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
      this.BackColor = System.Drawing.SystemColors.ControlDark;
      this.Controls.Add(this.gridView);
      this.DoubleBuffered = true;
      this.Font = new System.Drawing.Font("Arial", 9.0F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
      this.Margin = new System.Windows.Forms.Padding(0);
      this.Name = "CvsSpreadsheet";
      this.Size = new System.Drawing.Size(150, 162);
      ((System.ComponentModel.ISupportInitialize)(this.gridView)).EndInit();
      this.ResumeLayout(false);

    }

    #endregion

    private System.Windows.Forms.DataGridView gridView;
  }
}
