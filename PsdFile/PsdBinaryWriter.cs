/////////////////////////////////////////////////////////////////////////////////
//
// Photoshop PSD FileType Plugin for Paint.NET
// http://psdplugin.codeplex.com/
//
// This software is provided under the MIT License:
//   Copyright (c) 2006-2007 Frank Blumenberg
//   Copyright (c) 2010-2012 Tao Yue
//
// Portions of this file are provided under the BSD 3-clause License:
//   Copyright (c) 2006, Jonas Beckeman
//
// See LICENSE.txt for complete licensing and attribution information.
//
/////////////////////////////////////////////////////////////////////////////////

using System;
using System.IO;
using System.Text;

namespace PhotoshopFile
{
  /// <summary>
  /// Writes PSD data types in big-endian byte order.
  /// </summary>
  public class PsdBinaryWriter
  {
    private BinaryWriter writer;

    public Stream BaseStream
    {
      get { return writer.BaseStream; }
    }

    public bool AutoFlush { get; set; }

    public PsdBinaryWriter(Stream stream)
    {
      writer = new BinaryWriter(stream);
    }

    public void Flush()
    {
      writer.Flush();
    }

    /// <summary>
    /// Writes a Pascal string to the stream using the current ANSI code page.
    /// </summary>
    /// <param name="s">Unicode string to write</param>
    public void WritePascalString(string s)
    {
      string str = (s.Length > 255) ? s.Substring(0, 255) : s;
      byte[] bytesArray = Encoding.Default.GetBytes(str);

      Write((byte)bytesArray.Length);
      Write(bytesArray);

      // Original string length is even, so Pascal string length is odd
      if ((bytesArray.Length % 2) == 0)
        Write((byte)0);

      if (AutoFlush)
        Flush();
    }

    /// <summary>
    /// Write a Unicode string to the stream.
    /// </summary>
    public void WriteUnicodeString(string s)
    {
      Write(s.Length);
      var data = Encoding.BigEndianUnicode.GetBytes(s);
      Write(data);
    }

    public void Write(bool value)
    {
      writer.Write(value);

      if (AutoFlush)
        Flush();
    }

    public void Write(char[] value)
    {
      writer.Write(value);

      if (AutoFlush)
        Flush();
    }

    public void Write(byte[] value)
    {
      writer.Write(value);

      if (AutoFlush)
        Flush();
    }

    public void Write(byte value)
    {
      writer.Write(value);

      if (AutoFlush)
        Flush();
    }

    public void Write(short value)
    {
      unsafe
      {
        Util.SwapBytes2((byte*)&value);
      }
      writer.Write(value);

      if (AutoFlush)
        Flush();
    }

    public void Write(int value)
    {
      unsafe
      {
        Util.SwapBytes4((byte*)&value);
      }
      writer.Write(value);

      if (AutoFlush)
        Flush();
    }

    public void Write(long value)
    {
      unsafe
      {
        Util.SwapBytes((byte*)&value, 8);
      }
      writer.Write(value);

      if (AutoFlush)
        Flush();
    }

    public void Write(ushort value)
    {
      unsafe
      {
        Util.SwapBytes2((byte*)&value);
      }
      writer.Write(value);

      if (AutoFlush)
        Flush();
    }

    public void Write(uint value)
    {
      unsafe
      {
        Util.SwapBytes4((byte*)&value);
      }
      writer.Write(value);

      if (AutoFlush)
        Flush();
    }

    public void Write(ulong value)
    {
      unsafe
      {
        Util.SwapBytes((byte*)&value, 8);
      }
      writer.Write(value);

      if (AutoFlush)
        Flush();
    }
  }

  /// <summary>
  /// Writes the actual length in front of the data block upon disposal.
  /// </summary>
  class PsdBlockLengthWriter : IDisposable
  {
    private bool disposed = false;

    long lengthPosition;
    long startPosition;
    PsdBinaryWriter writer;

    public PsdBlockLengthWriter(PsdBinaryWriter writer)
    {
      this.writer = writer;

      // Store position so that we can return to it when the length is known.
      lengthPosition = writer.BaseStream.Position;

      // Write a sentinel value as a placeholder for the length.
      writer.Write((uint)0xFEEDFEED);

      // Store the start position of the data block so that we can calculate
      // its length when we're done writing.
      startPosition = writer.BaseStream.Position;
    }

    public void Write()
    {
      var endPosition = writer.BaseStream.Position;

      writer.BaseStream.Position = lengthPosition;
      long length = endPosition - startPosition;
      writer.Write((uint)length);

      writer.BaseStream.Position = endPosition;
    }

    public void Dispose()
    {
      if (!this.disposed)
      {
        Write();
        this.disposed = true;
      }
    }
  }

}