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
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace PhotoshopFile
{

  public enum ResourceID
  {
    Undefined = 0,
    MacPrintInfo = 1001,
    ResolutionInfo = 1005,
    AlphaChannelNames = 1006,
    DisplayInfo = 1007,
    Caption = 1008,
    BorderInfo = 1009,
    BgColor = 1010,
    PrintFlags = 1011,
    MultiChannelHalftoneInfo = 1012,
    ColorHalftoneInfo = 1013,
    DuotoneHalftoneInfo = 1014,
    MultiChannelTransferFunctions = 1015,
    ColorTransferFunctions = 1016,
    DuotoneTransferFunctions = 1017,
    DuotoneImageInfo = 1018,
    BlackWhiteRange = 1019,
    EPSOptions = 1021,
    QuickMaskInfo = 1022, //2 bytes containing Quick Mask channel ID, 1 byte boolean indicating whether the mask was initially empty.
    LayerStateInfo = 1024, //2 bytes containing the index of target layer. 0=bottom layer.
    WorkingPathUnsaved = 1025,
    LayersGroupInfo = 1026, //2 bytes per layer containing a group ID for the dragging groups. Layers in a group have the same group ID.
    IPTC_NAA = 1028,
    RawFormatImageMode = 1029,
    JPEGQuality = 1030,
    GridGuidesInfo = 1032,
    ThumbnailBgr = 1033,
    CopyrightInfo = 1034,
    URL = 1035,
    ThumbnailRgb = 1036,
    GlobalAngle = 1037,
    ColorSamplers = 1038,
    ICCProfile = 1039, //The raw bytes of an ICC format profile, see the ICC34.pdf and ICC34.h files from the Internation Color Consortium located in the documentation section
    Watermark = 1040,
    ICCUntagged = 1041, //1 byte that disables any assumed profile handling when opening the file. 1 = intentionally untagged.
    EffectsVisible = 1042, //1 byte global flag to show/hide all the effects layer. Only present when they are hidden.
    SpotHalftone = 1043, // 4 bytes for version, 4 bytes for length, and the variable length data.
    DocumentSpecific = 1044,
    UnicodeAlphaNames = 1045, // 4 bytes for length and the string as a unicode string
    IndexedColorTableCount = 1046, // 2 bytes for the number of colors in table that are actually defined
    TransparentIndex = 1047,
    GlobalAltitude = 1049,  // 4 byte entry for altitude
    Slices = 1050,
    WorkflowURL = 1051, //Unicode string, 4 bytes of length followed by unicode string
    JumpToXPEP = 1052, //2 bytes major version, 2 bytes minor version,
    //4 bytes count. Following is repeated for count: 4 bytes block size,
    //4 bytes key, if key = 'jtDd' then next is a Boolean for the dirty flag
    //otherwise it's a 4 byte entry for the mod date
    AlphaIdentifiers = 1053, //4 bytes of length, followed by 4 bytes each for every alpha identifier.
    URLList = 1054, //4 byte count of URLs, followed by 4 byte long, 4 byte ID, and unicode string for each count.
    VersionInfo = 1057, //4 byte version, 1 byte HasRealMergedData, unicode string of writer name, unicode string of reader name, 4 bytes of file version.
    Unknown4 = 1058, //pretty long, 302 bytes in one file. Holds creation date, maybe Photoshop license number
    XMLInfo = 1060, //some kind of XML definition of file. The xpacket tag seems to hold binary data
    Unknown = 1061, //seems to be common!
    Unknown2 = 1062, //seems to be common!
    Unknown3 = 1064, //seems to be common!
    PathInfo = 2000, //2000-2999 actually I think?
    ClippingPathName = 2999,
    PrintFlagsInfo = 10000
  }

  public struct ImageResourceInfo
  {
    public short ID;
    public string Name;
    public short OSType;
  }

  /// <summary>
  /// Abstract class for Image Resources
  /// </summary>
  public abstract class ImageResource
  {
    public string Name { get; set; }

    public abstract ResourceID ID { get; }

    public ImageResource(string name)
    {
      Name = name;
    }

    /// <summary>
    /// Write out the image resource block: header and data.
    /// </summary>
    public void Save(PsdBinaryWriter writer)
    {
      writer.Write(Util.SIGNATURE_8BIM);
      writer.Write((UInt16)ID);
      writer.WritePascalString(Name);

      // Write length placeholder and data block
      writer.Write((UInt32)0);
      var startPosition = writer.BaseStream.Position;
      WriteData(writer);
      
      // Back up and put in the actual size of the data block
      var endPosition = writer.BaseStream.Position;
      var dataLength = endPosition - startPosition;
      writer.BaseStream.Position = startPosition - 4;
      writer.Write((UInt32)dataLength);
      writer.BaseStream.Position = endPosition;


      if (writer.BaseStream.Position % 2 == 1)
        writer.Write((byte)0);
    }

    /// <summary>
    /// Write the data for this image resource.
    /// </summary>
    protected abstract void WriteData(PsdBinaryWriter writer);

    public override string ToString()
    {
      return String.Format(CultureInfo.InvariantCulture, "{0} {1}", (ResourceID)ID, Name);
    }
  }

  /// <summary>
  /// Creates the appropriate subclass of ImageResource.
  /// </summary>
  public static class ImageResourceFactory
  {
    public static ImageResource CreateImageResource(PsdBinaryReader reader)
    {
      var signature = new string(reader.ReadChars(4));
      var resourceIdInt = reader.ReadUInt16();
      var name = reader.ReadPascalString();
      var resourceDataLength = (int)reader.ReadUInt32();
      var endPosition = reader.BaseStream.Position + resourceDataLength;

      ImageResource resource = null;
      var resourceId = (ResourceID)resourceIdInt;
      switch (resourceId)
      {
        case ResourceID.ResolutionInfo:
          resource = new ResolutionInfo(reader, name);
          break;
        case ResourceID.ThumbnailRgb:
        case ResourceID.ThumbnailBgr:
          resource = new Thumbnail(reader, resourceId, name, resourceDataLength);
          break;
        case ResourceID.AlphaChannelNames:
          resource = new AlphaChannelNames(reader, name, resourceDataLength);
          break;
        default:
          resource = new RawImageResource(reader, name, resourceId, resourceDataLength);
          break;
      }

      if (reader.BaseStream.Position % 2 == 1)
        reader.ReadByte();

      // Reposition the reader if we do not consume the full resource block.
      // This preserves forward-compatibility in case a resource block is
      // later extended with additional properties.
      if (reader.BaseStream.Position < endPosition)
        reader.BaseStream.Position = endPosition;

      return resource;
    }
  }

}
