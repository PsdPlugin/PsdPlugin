﻿/////////////////////////////////////////////////////////////////////////////////
//
// Photoshop PSD FileType Plugin for Paint.NET
//
// This software is provided under the MIT License:
//   Copyright (c) 2006-2007 Frank Blumenberg
//   Copyright (c) 2010-2017 Tao Yue
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
    private MemoryStream zipDataStream;
    private DeflateStream zipStream;

    protected override bool AltersWrittenData => false;

    public ZipImage(byte[] data, Size size, int bitDepth)
      : base(size, bitDepth)
    {
      if (data == null)
      {
        InitCompress();
      }
      else
      {
        InitDecompress(data);
      }
    }

    private void InitCompress()
    {
      zipDataStream = new MemoryStream();

      // Write 2-byte zlib (RFC 1950) header
      //
      // CMF Compression Method and flags:
      //   CM     0:3 = 8 = deflate
      //   CINFO  4:7 = 4 = undefined, RFC 1950 only defines CINFO = 8
      //
      // FLG Flags:
      //   FCHECK  0:4 = 9 = check bits for CMF and FLG
      //   FDICT     5 = 0 = no preset dictionary
      //   FLEVEL  6:7 = 2 = default compression level

      zipDataStream.WriteByte(0x48);
      zipDataStream.WriteByte(0x89);
      zipStream = new DeflateStream(zipDataStream, CompressionMode.Compress,
        true);
    }

    private void InitDecompress(byte[] data)
    {
      zipDataStream = new MemoryStream(data);

      // .NET implements Deflate (RFC 1951) but not zlib (RFC 1950),
      // so we have to skip the first two bytes.
      zipDataStream.ReadByte();
      zipDataStream.ReadByte();
      zipStream = new DeflateStream(zipDataStream, CompressionMode.Decompress,
        true);
    }

    internal override void Read(byte[] buffer)
    {
      var bytesToRead = (long)Size.Height * BytesPerRow;
      Util.CheckByteArrayLength(bytesToRead);

      var totalRead = 0;
      while (totalRead < bytesToRead)
      {
        var bytesRead = zipStream.Read(buffer, totalRead, (int)bytesToRead - totalRead);
        if(bytesRead == 0) { break; }
        totalRead += bytesRead;
      }
      
      if (totalRead != bytesToRead)
      {
        throw new Exception("ZIP stream was not fully decompressed.");
      }
    }

    public override byte[] ReadCompressed()
    {
      // Write out the last block.  (Flush leaves the last block open.)
      zipStream.Close();

      // Do not write the zlib header when the image data is empty
      var result = (zipDataStream.Length == 2)
        ? new byte[0]
        : zipDataStream.ToArray();
      return result;
    }

    internal override void WriteInternal(byte[] array)
    {
      zipStream.Write(array, 0, array.Length);
    }
  }
}
