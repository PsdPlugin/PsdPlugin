/////////////////////////////////////////////////////////////////////////////////
//
// Photoshop PSD FileType Plugin for Paint.NET
// http://psdplugin.codeplex.com/
//
// This software is provided under the MIT License:
//   Copyright (c) 2006-2007 Frank Blumenberg
//   Copyright (c) 2010-2013 Tao Yue
//
// Portions of this file are provided under the BSD 3-clause License:
//   Copyright (c) 2006, Jonas Beckeman
//
// See LICENSE.txt for complete licensing and attribution information.
//
/////////////////////////////////////////////////////////////////////////////////

using System;
using System.Drawing;
using System.IO;
using System.Text;

namespace PhotoshopFile
{
  /// <summary>
  /// Writes PSD data types in big-endian byte order.
  /// </summary>
  public class PsdBinaryWriter : IDisposable
  {
    private BinaryWriter writer;
    private Encoding encoding;

    public Stream BaseStream
    {
      get { return writer.BaseStream; }
    }

    public bool AutoFlush { get; set; }

    public PsdBinaryWriter(Stream stream, Encoding encoding)
    {
      this.encoding = encoding;

      // BinaryWriter.Write(String) cannot be used, as it writes a UTF-7
      // (variable-sized) length integer, while PSD strings have a fixed-size
      // length field.  Encoding is set to ASCII to catch any accidental usage.
      writer = new BinaryWriter(stream, Encoding.ASCII);
    }

    public void Flush()
    {
      writer.Flush();
    }

    public void Write(Rectangle rect)
    {
      Write(rect.Top);
      Write(rect.Left);
      Write(rect.Bottom);
      Write(rect.Right);
    }

    /// <summary>
    /// Pad the length of a block to a multiple.
    /// </summary>
    /// <param name="startPosition">Starting position of the padded block.</param>
    /// <param name="padMultiple">Byte multiple to pad to.</param>
    public void WritePadding(long startPosition, int padMultiple)
    {
      var length = writer.BaseStream.Position - startPosition;
      var padBytes = Util.GetPadding((int)length, padMultiple);
      for (long i = 0; i < padBytes; i++)
        writer.Write((byte)0);

      if (AutoFlush)
        Flush();
    }

    /// <summary>
    /// Write string as ASCII characters, without a length prefix.
    /// </summary>
    public void WriteAsciiChars(string s)
    {
      var bytes = Encoding.ASCII.GetBytes(s);
      writer.Write(bytes);

      if (AutoFlush)
        Flush();
    }


    /// <summary>
    /// Writes a Pascal string using the specified encoding.
    /// </summary>
    /// <param name="s">Unicode string to convert to the encoding.</param>
    /// <param name="padMultiple">Byte multiple that the Pascal string is padded to.</param>
    /// <param name="maxBytes">Maximum number of bytes to write.</param>
    public void WritePascalString(string s, int padMultiple, byte maxBytes = 255)
    {
      var startPosition = writer.BaseStream.Position;

      byte[] bytesArray = encoding.GetBytes(s);
      if (bytesArray.Length > maxBytes)
      {
        var tempArray = new byte[maxBytes];
        Array.Copy(bytesArray, tempArray, maxBytes);
        bytesArray = tempArray;
      }

      writer.Write((byte)bytesArray.Length);
      writer.Write(bytesArray);
      WritePadding(startPosition, padMultiple);
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

    public void Write(Int16 value)
    {
      unsafe
      {
        Util.SwapBytes2((byte*)&value);
      }
      writer.Write(value);

      if (AutoFlush)
        Flush();
    }

    public void Write(Int32 value)
    {
      unsafe
      {
        Util.SwapBytes4((byte*)&value);
      }
      writer.Write(value);

      if (AutoFlush)
        Flush();
    }

    public void Write(Int64 value)
    {
      unsafe
      {
        Util.SwapBytes((byte*)&value, 8);
      }
      writer.Write(value);

      if (AutoFlush)
        Flush();
    }

    public void Write(UInt16 value)
    {
      unsafe
      {
        Util.SwapBytes2((byte*)&value);
      }
      writer.Write(value);

      if (AutoFlush)
        Flush();
    }

    public void Write(UInt32 value)
    {
      unsafe
      {
        Util.SwapBytes4((byte*)&value);
      }
      writer.Write(value);

      if (AutoFlush)
        Flush();
    }

    public void Write(UInt64 value)
    {
      unsafe
      {
        Util.SwapBytes((byte*)&value, 8);
      }
      writer.Write(value);

      if (AutoFlush)
        Flush();
    }


    //////////////////////////////////////////////////////////////////

    # region IDisposable

    private bool disposed = false;

    public void Dispose()
    {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
      // Check to see if Dispose has already been called. 
      if (disposed)
        return;

      if (disposing)
      {
        if (writer != null)
        {
          // BinaryWriter.Dispose() is protected.
          writer.Close();
          writer = null;
        }
      }

      disposed = true;
    }

    #endregion

  }
}