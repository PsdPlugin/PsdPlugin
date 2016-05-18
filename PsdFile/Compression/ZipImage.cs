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
using System.IO;
using System.IO.Compression;

namespace PhotoshopFile.Compression
{
  public class ZipImage : ImageData
  {
    private DeflateStream zipStream;

    public ZipImage(byte[] zipData, Size size, int bitDepth)
      : base(size, bitDepth)
    {
      var memoryStream = new MemoryStream(zipData);

      // .NET implements Deflate (RFC 1951) but not zlib (RFC 1950),
      // so we have to skip the first two bytes.
      memoryStream.ReadByte();
      memoryStream.ReadByte();
      zipStream = new DeflateStream(memoryStream, CompressionMode.Decompress);
    }

    internal override void Read(byte[] buffer)
    {
      var bytesToRead = (long)Size.Height * BytesPerRow;
      Util.CheckByteArrayLength(bytesToRead);

      var bytesRead = zipStream.Read(buffer, 0, (int)bytesToRead);
      if (bytesRead != bytesToRead)
      {
        throw new Exception("ZIP stream was not fully decompressed.");
      }
    }
  }
}
