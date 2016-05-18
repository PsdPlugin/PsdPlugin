/////////////////////////////////////////////////////////////////////////////////
//
// Photoshop PSD FileType Plugin for Paint.NET
// http://psdplugin.codeplex.com/
//
// This software is provided under the MIT License:
//   Copyright (c) 2006-2007 Frank Blumenberg
//   Copyright (c) 2010-2016 Tao Yue
//
// See LICENSE.txt for complete licensing and attribution information.
//
/////////////////////////////////////////////////////////////////////////////////

using System;
using System.Drawing;

namespace PhotoshopFile.Compression
{
  public class ZipPredict16Image : ImageData
  {
    private ImageData zipImage;

    public ZipPredict16Image(byte[] zipData, Size size)
      : base(size, 16)
    {
      // 16-bitdepth images are delta-encoded word-by-word.  The deltas
      // are thus big-endian and must be reversed for further processing.
      var zipRawImage = new ZipImage(zipData, size, 16);
      zipImage = new EndianReverser(zipRawImage);
    }

    internal override void Read(byte[] buffer)
    {
      zipImage.Read(buffer);
      unsafe
      {
        fixed (byte* ptrData = &buffer[0])
        {
          Unpredict(ptrData);
        }
      }
    }

    /// <summary>
    /// Unpredicts the little-endian decompressed image data.
    /// </summary>
    unsafe private void Unpredict(byte* ptrData)
    {
      // Delta-decode each row
      for (int iRow = 0; iRow < Size.Height; iRow++)
      {
        UInt16* ptr = (UInt16*)(ptrData + iRow * Size.Width * 2);
        UInt16* ptrEnd = (UInt16*)(ptrData + (iRow + 1) * Size.Width * 2);

        // Start with column index 1 on each row
        ptr++;
        while (ptr < ptrEnd)
        {
          *ptr = (UInt16)(*ptr + *(ptr - 1));
          ptr++;
        }
      }
    }
  }
}
