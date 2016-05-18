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
          Unpredict(ptrData, ptrOutput);
        }
      }
    }

    /// <summary>
    /// Unpredicts the raw decompressed image data into a little-endian
    /// scanline bitmap.
    /// </summary>
    unsafe private void Unpredict(byte* ptrData, byte* ptrOutput)
    {
      // Delta-decode each row
      for (int iRow = 0; iRow < Size.Height; iRow++)
      {
        byte* ptr = ptrData + iRow * Size.Width * 4;
        byte* ptrEnd = ptrData + (iRow + 1) * Size.Width * 4;

        // Start with column index 1 on each row
        ptr++;
        while (ptr < ptrEnd)
        {
          *ptr = (byte)(*ptr + *(ptr - 1));
          ptr++;
        }
      }

      // Within each row, the individual bytes of the 32-bit words are
      // packed together, high-order bytes before low-order bytes.
      // We now unpack them into words and reverse to little-endian.
      int offset1 = Size.Width;
      int offset2 = 2 * offset1;
      int offset3 = 3 * offset1;
      for (int iRow = 0; iRow < Size.Height; iRow++)
      {
        byte* dstPtr = ptrOutput + iRow * Size.Width * 4;
        byte* dstPtrEnd = ptrOutput + (iRow + 1) * Size.Width * 4;

        byte* srcPtr = ptrData + iRow * Size.Width * 4;

        // Reverse to little-endian as we do the unpacking.
        while (dstPtr < dstPtrEnd)
        {
          *(dstPtr++) = *(srcPtr + offset3);
          *(dstPtr++) = *(srcPtr + offset2);
          *(dstPtr++) = *(srcPtr + offset1);
          *(dstPtr++) = *srcPtr;

          srcPtr++;
        }
      }
    }
  }
}
