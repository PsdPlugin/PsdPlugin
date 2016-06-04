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
          Unpredict((UInt16*)ptrData);
        }
      }
    }

    /// <summary>
    /// Unpredicts the decompressed, native-endian image data.
    /// </summary>
    unsafe private void Unpredict(UInt16* ptrData)
    {
      // Delta-decode each row
      for (int i = 0; i < Size.Height; i++)
      {
        UInt16* ptrDataRowEnd = ptrData + Size.Width;

        // Start with column index 1 on each row
        ptrData++;
        while (ptrData < ptrDataRowEnd)
        {
          *ptrData += *(ptrData - 1);
          ptrData++;
        }

        // Advance pointer to the next row
        ptrData = ptrDataRowEnd;
      }
    }
  }
}
