/////////////////////////////////////////////////////////////////////////////////
//
// Photoshop PSD FileType Plugin for Paint.NET
// http://psdplugin.codeplex.com/
//
// This software is provided under the MIT License:
//   Copyright (c) 2006-2007 Frank Blumenberg
//   Copyright (c) 2010-2011 Tao Yue
//
// See LICENSE.txt for complete licensing and attribution information.
//
/////////////////////////////////////////////////////////////////////////////////

using System;

using PaintDotNet;

namespace PaintDotNet.Data.PhotoshopFileType
{
  [Serializable]
  public class PsdSaveConfigToken
      : SaveConfigToken
  {
    public override object Clone()
    {
      return new PsdSaveConfigToken(this);
    }

    private bool m_rleCompress;
    public bool RleCompress
    {
      get
      {
        return this.m_rleCompress;
      }

      set
      {
        this.m_rleCompress = value;
      }
    }

    public PsdSaveConfigToken(bool rleCompress)
    {
      this.RleCompress = rleCompress;
    }

    protected PsdSaveConfigToken(PsdSaveConfigToken copyMe)
    {
      this.m_rleCompress = copyMe.m_rleCompress;
    }

    public override void Validate()
    {
      base.Validate();
    }

  }
}
