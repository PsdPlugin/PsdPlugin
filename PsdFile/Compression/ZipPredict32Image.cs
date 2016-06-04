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
  public class ZipPredict32Image : ImageData
  {
    private ImageData zipImage;

    public ZipPredict32Image(byte[] zipData, Size size)
      : base(size, 32)
    {
      zipImage = new ZipImage(zipData, size, 32);
    }

    internal override void Read(byte[] buffer)
    {
      var tempBuffer = new byte[buffer.Length];

      zipImage.Read(tempBuffer);
      unsafe
      {
        fixed (byte* ptrData = &tempBuffer[0])
        fixed (byte* ptrOutput = &buffer[0])
        {
          Unpredict(ptrData, (Int32*)ptrOutput);
        }
      }
    }

    /// <summary>
    /// Unpredicts the raw decompressed image data into a 32-bpp bitmap with
    /// native endianness.
    /// </summary>
    unsafe private void Unpredict(byte* ptrData, Int32* ptrOutput)
    {
      for (int i = 0; i < Size.Height; i++)
      {
        byte* ptrDataRow = ptrData;
        byte* ptrDataRowEnd = ptrDataRow + BytesPerRow;

        // Delta-decode each row
        ptrData++;
        while (ptrData < ptrDataRowEnd)
        {
          *ptrData += *(ptrData - 1);
          ptrData++;
        }

        // Within each row, the individual bytes of the 32-bit words are
        // packed together, high-order bytes before low-order bytes.
        // We now unpack them into words.
        int offset1 = Size.Width;
        int offset2 = 2 * offset1;
        int offset3 = 3 * offset1;

        ptrData = ptrDataRow;
        Int32* ptrOutputRowEnd = ptrOutput + Size.Width;
        while (ptrOutput < ptrOutputRowEnd)
        {
          *ptrOutput = *(ptrData) << 24
            | *(ptrData + offset1) << 16
            | *(ptrData + offset2) << 8
            | *(ptrData + offset3);

          ptrData++;
          ptrOutput++;
        }

        // Advance pointer to next row
        ptrData = ptrDataRowEnd;
      }
    }
  }
}
