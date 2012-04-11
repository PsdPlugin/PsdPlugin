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
  /// Reads PSD data types in big-endian byte order.
  /// </summary>
  public class PsdBinaryReader
  {
    private BinaryReader reader;

    public Stream BaseStream
    {
      get { return reader.BaseStream; }
    }
    
    public PsdBinaryReader(Stream stream)
    {
      reader = new BinaryReader(stream, Encoding.Default);
    }

    public byte ReadByte()
    {
      return reader.ReadByte();
    }

    public byte[] ReadBytes(int count)
    {
      return reader.ReadBytes(count);
    }

    public char[] ReadChars(int count)
    {
      return reader.ReadChars(count);
    }

    public bool ReadBoolean()
    {
      return reader.ReadBoolean();
    }

    public short ReadInt16()
    {
      short val = reader.ReadInt16();
      unsafe
      {
        Util.SwapBytes((byte*)&val, 2);
      }
      return val;
    }

    public int ReadInt32()
    {
      int val = reader.ReadInt32();
      unsafe
      {
        Util.SwapBytes((byte*)&val, 4);
      }
      return val;
    }

    public long ReadInt64()
    {
      long val = reader.ReadInt64();
      unsafe
      {
        Util.SwapBytes((byte*)&val, 8);
      }
      return val;
    }

    public ushort ReadUInt16()
    {
      ushort val = reader.ReadUInt16();
      unsafe
      {
        Util.SwapBytes((byte*)&val, 2);
      }
      return val;
    }

    public uint ReadUInt32()
    {
      uint val = reader.ReadUInt32();
      unsafe
      {
        Util.SwapBytes((byte*)&val, 4);
      }
      return val;
    }

    public ulong ReadUInt64()
    {
      ulong val = reader.ReadUInt64();
      unsafe
      {
        Util.SwapBytes((byte*)&val, 8);
      }
      return val;
    }

    //////////////////////////////////////////////////////////////////

    public string ReadPascalString()
    {
      byte stringLength = ReadByte();
      char[] c = ReadChars(stringLength);

      // Padded to even length
      if ((stringLength % 2) == 0)
        ReadByte();

      return new string(c);
    }

    //////////////////////////////////////////////////////////////////

    public string ReadUnicodeString()
    {
      var numChars = ReadInt32();
      var length = 2 * numChars;
      var data = ReadBytes(length);
      var str = Encoding.BigEndianUnicode.GetString(data, 0, length);

      return str;
    }
  }

}