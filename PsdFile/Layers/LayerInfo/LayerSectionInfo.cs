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

    public string BlendModeKey { get; set; }

    public LayerSectionInfo(PsdBinaryReader reader, int dataLength)
    {
      SectionType = (LayerSectionType)reader.ReadInt32();
      if (dataLength == 12)
      {
        var signature = new string(reader.ReadChars(4));
        if (signature == "8BIM")
        {
          BlendModeKey = new string(reader.ReadChars(4));
        }
      }
    }

    protected override void WriteData(PsdBinaryWriter writer)
    {
      writer.Write((Int32)SectionType);
      if (BlendModeKey != null)
      {
        writer.Write(Util.SIGNATURE_8BIM);
        writer.Write(BlendModeKey.ToCharArray());
      }
    }
  }
}
