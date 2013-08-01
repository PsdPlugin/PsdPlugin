/////////////////////////////////////////////////////////////////////////////////
//
// Photoshop PSD FileType Plugin for Paint.NET
// http://psdplugin.codeplex.com/
//
// This software is provided under the MIT License:
//   Copyright (c) 2006-2007 Frank Blumenberg
//   Copyright (c) 2010-2013 Tao Yue
//
// See LICENSE.txt for complete licensing and attribution information.
//
/////////////////////////////////////////////////////////////////////////////////

using System;
using System.Diagnostics;
using System.IO;

namespace PhotoshopFile
{
  public static class LayerInfoFactory
  {
    public static LayerInfo Load(PsdBinaryReader reader)
    {
      Debug.WriteLine("LayerInfoFactory.Load started at " + reader.BaseStream.Position);
      
      var signature = reader.ReadAsciiChars(4);
      if (signature != "8BIM")
        throw new PsdInvalidException("Could not read LayerInfo due to signature mismatch.");

      var key = reader.ReadAsciiChars(4);
      var length = reader.ReadInt32();
      var startPosition = reader.BaseStream.Position;

      LayerInfo result;
      switch (key)
      {
        case "lsct":
        case "lsdk":
          result = new LayerSectionInfo(reader, key, length);
          break;
        case "luni":
          result = new LayerUnicodeName(reader);
          break;
        default:
          result = new RawLayerInfo(reader, key, length);
          break;
      }

      // May have additional padding applied.
      var endPosition = startPosition + length;
      if (reader.BaseStream.Position < endPosition)
        reader.BaseStream.Position = endPosition;

      // Documentation states that the length is even-padded.  Actually:
      //   1. Most keys have 4-padded lengths.
      //   2. However, some keys (LMsk) have even-padded lengths.
      //   3. Other keys (Txt2, Lr16, Lr32) have unpadded lengths.
      //
      // The data is always 4-padded, regardless of the stated length.

      reader.ReadPadding(startPosition, 4);

      return result;
    }
  }

  public abstract class LayerInfo
  {
    public abstract string Key { get; }

    protected abstract void WriteData(PsdBinaryWriter writer);

    public void Save(PsdBinaryWriter writer)
    {
      Debug.WriteLine("LayerInfo.Save started at " + writer.BaseStream.Position);

      writer.WriteAsciiChars("8BIM");
      writer.WriteAsciiChars(Key);

      var startPosition = writer.BaseStream.Position;
      using (var lengthWriter = new PsdBlockLengthWriter(writer))
      {
        // Depending on the key, the length may be unpadded, 2-padded, or
        // 4-padded.  Thus, it is up to each implementation of WriteData to
        // pad the length correctly.
        WriteData(writer);
      }

      // Regardless of how the length is padded, the data is always padded to
      // a multiple of 4.
      writer.WritePadding(startPosition, 4);
    }
  }
}
