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

namespace PhotoshopFile
{
  public enum LayerSectionType
  {
    Layer = 0,
    OpenFolder = 1,
    ClosedFolder = 2,
    SectionDivider = 3
  }

  /// <summary>
  /// Layer sections are known as Groups in the Photoshop UI.
  /// </summary>
  public class LayerSectionInfo : LayerInfo
  {
    public override string Key
    {
      get { return "lsct"; }
    }

    public LayerSectionType SectionType { get; set; }

    private string blendModeKey;
    public string BlendModeKey
    {
      get { return blendModeKey; }
      set
      {
        if (value.Length != 4)
          throw new ArgumentException("Blend mode key must have a length of 4.");
        blendModeKey = value;
      }
    }

    public LayerSectionInfo(PsdBinaryReader reader, int dataLength)
    {
      SectionType = (LayerSectionType)reader.ReadInt32();
      if (dataLength >= 12)
      {
        var signature = reader.ReadAsciiChars(4);
        if (signature == "8BIM")
        {
          BlendModeKey = reader.ReadAsciiChars(4);
        }
      }
    }

    protected override void WriteData(PsdBinaryWriter writer)
    {
      writer.Write((Int32)SectionType);
      if (BlendModeKey != null)
      {
        writer.WriteAsciiChars("8BIM");
        writer.WriteAsciiChars(BlendModeKey);
      }

      // 12-byte length, no additional padding necessary.
    }
  }
}
