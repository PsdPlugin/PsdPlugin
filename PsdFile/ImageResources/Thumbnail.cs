/////////////////////////////////////////////////////////////////////////////////
//
// Photoshop PSD FileType Plugin for Paint.NET
// http://psdplugin.codeplex.com/
//
// This software is provided under the MIT License:
//   Copyright (c) 2006-2007 Frank Blumenberg
//   Copyright (c) 2010-2011 Tao Yue
//
// Portions of this file are provided under the BSD 3-clause License:
//   Copyright (c) 2006, Jonas Beckeman
//
// See LICENSE.txt for complete licensing and attribution information.
//
/////////////////////////////////////////////////////////////////////////////////

using System;
using System.IO;
using System.Drawing;

namespace PhotoshopFile
{
  /// <summary>
  /// Summary description for Thumbnail.
  /// </summary>
  public class Thumbnail : ImageResource
  {
    private Bitmap m_thumbnailImage;
    public Bitmap Image
    {
      get { return m_thumbnailImage; }
      set { m_thumbnailImage = value; }
    }

    public Thumbnail(ImageResource imgRes):base(imgRes)
    {
      using (BinaryReverseReader reader = DataReader)
      {
        int format = reader.ReadInt32();
        int width = reader.ReadInt32();
        int height = reader.ReadInt32();
        int widthBytes = reader.ReadInt32();
        int size = reader.ReadInt32();
        int compressedSize = reader.ReadInt32();
        short bitPerPixel = reader.ReadInt16();
        short planes = reader.ReadInt16();

        if (format == 1)
        {

          byte[] imgData = reader.ReadBytes((int)(reader.BaseStream.Length - reader.BaseStream.Position));

          using (MemoryStream strm = new MemoryStream(imgData))
          {
            m_thumbnailImage = (Bitmap)(Bitmap.FromStream(strm).Clone());
          }

          if (this.ID == 1033)
          {
            //// BGR
            //for(int y=0;y<m_thumbnailImage.Height;y++)
            //  for (int x = 0; x < m_thumbnailImage.Width; x++)
            //  {
            //    Color c=m_thumbnailImage.GetPixel(x,y);
            //    Color c2=Color.FromArgb(c.B, c.G, c.R);
            //    m_thumbnailImage.SetPixel(x, y, c);
            //  }
          }

        }
        else
        {
          m_thumbnailImage = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        }
      }
    }
  }
}
