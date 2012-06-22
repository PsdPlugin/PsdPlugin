/////////////////////////////////////////////////////////////////////////////////
//
// Photoshop PSD FileType Plugin for Paint.NET
// http://psdplugin.codeplex.com/
//
// This software is provided under the MIT License:
//   Copyright (c) 2006-2007 Frank Blumenberg
//   Copyright (c) 2010-2012 Tao Yue
//
// See LICENSE.txt for complete licensing and attribution information.
//
/////////////////////////////////////////////////////////////////////////////////

using System.Diagnostics;
using System.IO;

namespace PhotoshopFile
{
  public static class LayerInfoFactory
  {
    public static LayerInfo CreateLayerInfo(PsdBinaryReader reader)
    {
      Debug.WriteLine("LayerInfoFactory.Create started at " + reader.BaseStream.Position);
      
      var signature = new string(reader.ReadChars(4));
      if (signature != "8BIM")
        throw new IOException("Could not read LayerInfo due to signature mismatch.");

      var key = new string(reader.ReadChars(4));
      var length = reader.ReadInt32();
      var startPosition = reader.BaseStream.Position;

      LayerInfo result;
      switch (key)
      {
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

      writer.Write(Util.SIGNATURE_8BIM);
      writer.Write(Key.ToCharArray());
      using (var lengthWriter = new PsdBlockLengthWriter(writer))
      {
        WriteData(writer);
      }

    }
  }
}
