/////////////////////////////////////////////////////////////////////////////////
//
// Photoshop PSD FileType Plugin for Paint.NET
// http://psdplugin.codeplex.com/
//
// This software is provided under the MIT License:
//   Copyright (c) 2006-2007 Frank Blumenberg
//   Copyright (c) 2010-2017 Tao Yue
//
// See LICENSE.txt for complete licensing and attribution information.
//
/////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Windows.Forms;

using PaintDotNet;

namespace PaintDotNet.Data.PhotoshopFileType
{
    /// <summary>
    /// Summary description for TgaSaveConfigWidget.
    /// </summary>
    public class PsdSaveConfigWidget
        : PaintDotNet.SaveConfigWidget
    {
        private System.Windows.Forms.CheckBox rleCompressCheckBox;
        private CheckBox cbLayers;
        private Label label1;
        private ToolTip toolTip1;
        private IContainer components;

        public PsdSaveConfigWidget()
        {
            // This call is required by the Windows.Forms Form Designer.
            InitializeComponent();

            //this.bpp24Radio.Text = PdnResources.GetString("TgaSaveConfigWidget.Bpp24Radio.Text");
            //this.bpp32Radio.Text = PdnResources.GetString("TgaSaveConfigWidget.Bpp32Radio.Text");
            //this.bppLabel.Text = PdnResources.GetString("TgaSaveConfigWidget.BppLabel.Text");
            //this.rleCompressCheckBox.Text = PdnResources.GetString("TgaSaveConfigWidget.RleCompressCheckBox.Text");
        }

        protected override void InitFileType()
        {
            this.fileType = new PhotoshopFileType();
        }

        protected override void InitTokenFromWidget()
        {
            var token = ((PsdSaveConfigToken)this.token);
            token.RleCompress = this.rleCompressCheckBox.Checked;
            token.SaveLayers = this.cbLayers.Checked;
        }

        protected override void InitWidgetFromToken(SaveConfigToken token)
        {
            if (token is PsdSaveConfigToken psdToken)
            {
                this.rleCompressCheckBox.Checked = psdToken.RleCompress;
                this.cbLayers.Checked = psdToken.SaveLayers;
            }
            else
            {
                this.rleCompressCheckBox.Checked = true;
                this.cbLayers.Checked = true;
            }
        }

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (components != null)
                {
                    components.Dispose();
                }
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
            this.components = new System.ComponentModel.Container();
            this.rleCompressCheckBox = new System.Windows.Forms.CheckBox();
            this.cbLayers = new System.Windows.Forms.CheckBox();
            this.label1 = new System.Windows.Forms.Label();
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this.SuspendLayout();
            // 
            // rleCompressCheckBox
            // 
            this.rleCompressCheckBox.Location = new System.Drawing.Point(0, 0);
            this.rleCompressCheckBox.Name = "rleCompressCheckBox";
            this.rleCompressCheckBox.Size = new System.Drawing.Size(184, 24);
            this.rleCompressCheckBox.TabIndex = 0;
            this.rleCompressCheckBox.Text = "RLE compression";
            this.rleCompressCheckBox.CheckedChanged += new System.EventHandler(this.OnCheckedChanged);
            // 
            // cbLayers
            // 
            this.cbLayers.Location = new System.Drawing.Point(0, 28);
            this.cbLayers.Name = "cbLayers";
            this.cbLayers.Size = new System.Drawing.Size(184, 24);
            this.cbLayers.TabIndex = 1;
            this.cbLayers.Text = "Save Layers Groups";
            this.toolTip1.SetToolTip(this.cbLayers, "Layers \"Layer Group:\" and \"End Layer Group:\" is about to be saved as a PSD group!" +
              " Note: Your drawings on a layer group image might be lost!");
            // 
            // label1
            // 
            this.label1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(30, 55);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(0, 13);
            this.label1.TabIndex = 2;
            // 
            // toolTip1
            // 
            this.toolTip1.AutomaticDelay = 100;
            this.toolTip1.AutoPopDelay = 3000;
            this.toolTip1.InitialDelay = 100;
            this.toolTip1.ReshowDelay = 20;
            this.toolTip1.ShowAlways = true;
            this.toolTip1.UseAnimation = false;
            // 
            // PsdSaveConfigWidget
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.Controls.Add(this.label1);
            this.Controls.Add(this.cbLayers);
            this.Controls.Add(this.rleCompressCheckBox);
            this.Name = "PsdSaveConfigWidget";
            this.Size = new System.Drawing.Size(180, 104);
            this.ResumeLayout(false);
            this.PerformLayout();

        }
        #endregion

        private void OnCheckedChanged(object sender, System.EventArgs e)
        {
            this.UpdateToken();
        }
    }
}
