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

    public Stream BaseStream
    {
      get { return writer.BaseStream; }
    }

    public bool AutoFlush { get; set; }

    public PsdBinaryWriter(Stream stream)
    {
      writer = new BinaryWriter(stream, Encoding.Default);
    }

    public void Flush()
    {
      writer.Flush();
    }

    /// <summary>
    /// Writes a Pascal string using the system's current Windows code page.
    /// </summary>
    /// <param name="s">Unicode string to convert to a Windows code page encoding.</param>
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