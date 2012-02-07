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
  /// Reads primitive data types as binary values in big-endian byte order.
  /// </summary>
  public class BinaryReverseReader : BinaryReader
  {
    public BinaryReverseReader(Stream a_stream)
      : base(a_stream, Encoding.Default)
    {
    }

    public override short ReadInt16()
    {
      short val = base.ReadInt16();
      unsafe
      {
        Util.SwapBytes((byte*)&val, 2);
      }
      return val;
    }

    public override int ReadInt32()
    {
      int val = base.ReadInt32();
      unsafe
      {
        Util.SwapBytes((byte*)&val, 4);
      }
      return val;
    }

    public override long ReadInt64()
    {
      long val = base.ReadInt64();
      unsafe
      {
        Util.SwapBytes((byte*)&val, 8);
      }
      return val;
    }

    public override ushort ReadUInt16()
    {
      ushort val = base.ReadUInt16();
      unsafe
      {
        Util.SwapBytes((byte*)&val, 2);
      }
      return val;
    }

    public override uint ReadUInt32()
    {
      uint val = base.ReadUInt32();
      unsafe
      {
        Util.SwapBytes((byte*)&val, 4);
      }
      return val;
    }

    public override ulong ReadUInt64()
    {
      ulong val = base.ReadUInt64();
      unsafe
      {
        Util.SwapBytes((byte*)&val, 8);
      }
      return val;
    }

    //////////////////////////////////////////////////////////////////

    public string ReadPascalString()
    {
      byte stringLength = base.ReadByte();

      char[] c = base.ReadChars(stringLength);

      if ((stringLength % 2) == 0)
        base.ReadByte();

      return new string(c);
    }
  }

  //////////////////////////////////////////////////////////////////

  /// <summary>
  /// Writes primitive data types as binary values in big-endian format
  /// </summary>
  public class BinaryReverseWriter : BinaryWriter
  {
    public BinaryReverseWriter(Stream a_stream)
      : base(a_stream)
    {
    }

    public bool AutoFlush;

    /// <summary>
    /// Writes a Pascal string to the stream using the current ANSI code page.
    /// </summary>
    /// <param name="s">Unicode string to write</param>
    public void WritePascalString(string s)
    {
      string str = (s.Length > 255) ? s.Substring(0, 255) : s;
      byte[] bytesArray = Encoding.Default.GetBytes(str);

      base.Write((byte)bytesArray.Length);
      base.Write(bytesArray);

      // Original string length is even, so Pascal string length is odd
      if ((bytesArray.Length % 2) == 0)
        base.Write((byte)0);

      if (AutoFlush)
        Flush();
    }

    public override void Write(short value)
    {
      unsafe
      {
        SwapBytes((byte*)&value, 2);
      }
      base.Write(value);

      if (AutoFlush)
        Flush();
    }
    public override void Write(int value)
    {
      unsafe
      {
        SwapBytes((byte*)&value, 4);
      }
      base.Write(value);

      if (AutoFlush)
        Flush();
    }
    public override void Write(long value)
    {
      unsafe
      {
        SwapBytes((byte*)&value, 8);
      }
      base.Write(value);

      if (AutoFlush)
        Flush();
    }

    public override void Write(ushort value)
    {
      unsafe
      {
        SwapBytes((byte*)&value, 2);
      }
      base.Write(value);

      if (AutoFlush)
        Flush();
    }

    public override void Write(uint value)
    {
      unsafe
      {
        SwapBytes((byte*)&value, 4);
      }
      base.Write(value);

      if (AutoFlush)
        Flush();
    }

    public override void Write(ulong value)
    {
      unsafe
      {
        SwapBytes((byte*)&value, 8);
      }
      base.Write(value);

      if (AutoFlush)
        Flush();
    }

    //////////////////////////////////////////////////////////////////

    unsafe static protected void SwapBytes(byte* ptr, int nLength)
    {
      for (long i = 0; i < nLength / 2; ++i)
      {
        byte t = *(ptr + i);
        *(ptr + i) = *(ptr + nLength - i - 1);
        *(ptr + nLength - i - 1) = t;
      }
    }
  }


  class LengthWriter : IDisposable
  {
    long m_lengthPosition = long.MinValue;
    long m_startPosition;
    BinaryReverseWriter m_writer;

    public LengthWriter(BinaryReverseWriter writer)
    {
      m_writer = writer;

      // we will write the correct length later, so remember 
      // the position
      m_lengthPosition = m_writer.BaseStream.Position;
      m_writer.Write((uint)0xFEEDFEED);

      // remember the start  position for calculation Image 
      // resources length
      m_startPosition = m_writer.BaseStream.Position;
    }

    public void Write()
    {
      if (m_lengthPosition != long.MinValue)
      {
        long endPosition = m_writer.BaseStream.Position;

        m_writer.BaseStream.Position = m_lengthPosition;
        long length=endPosition - m_startPosition;
        m_writer.Write((uint)length);
        m_writer.BaseStream.Position = endPosition;

        m_lengthPosition = long.MinValue;
      }
    }

    public void Dispose()
    {
      Write();
    }
  }

}

