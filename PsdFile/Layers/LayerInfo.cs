/////////////////////////////////////////////////////////////////////////////////
//
// Photoshop PSD FileType Plugin for Paint.NET
// http://psdplugin.codeplex.com/
//
// This software is provided under the MIT License:
//   Copyright (c) 2006-2007 Frank Blumenberg
//   Copyright (c) 2010-2014 Tao Yue
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
    /// <summary>
    /// Loads the next LayerInfo record.
    /// </summary>
    /// <param name="reader">The file reader</param>
    /// <param name="globalLayerInfo">True if the LayerInfo record is being
    ///   loaded from the end of the Layer and Mask Information section;
    ///   false if it is being loaded from the end of a Layer record.</param>
    public static LayerInfo Load(PsdBinaryReader reader, bool globalLayerInfo)
    {
      Util.DebugMessage(reader.BaseStream, "Load, Begin, LayerInfo");
      
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
      // Photoshop writes data that is always 4-padded, even when the stated
      // length is not a multiple of 4.  The length mismatch seems to occur
      // only on global layer info.  We do not read extra padding in other
      // cases because third-party programs are likely to follow the spec.

      if (globalLayerInfo)
      {
        reader.ReadPadding(startPosition, 4);
      }

      Util.DebugMessage(reader.BaseStream, "Load, End, LayerInfo, {0}",
        result.Key);
      return result;
    }
  }

  public abstract class LayerInfo
  {
    public abstract string Key { get; }

    protected abstract void WriteData(PsdBinaryWriter writer);

    public void Save(PsdBinaryWriter writer, bool globalLayerInfo)
    {
      Util.DebugMessage(writer.BaseStream, "Save, Begin, LayerInfo");

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

      // Data for global layer info is always padded to a multiple of 4,
      // even if this causes the stated length to be incorrect.
      if (globalLayerInfo)
      {
        writer.WritePadding(startPosition, 4);
      }

      Util.DebugMessage(writer.BaseStream, "Save, End, LayerInfo, {0}", Key);
    }
  }
}
