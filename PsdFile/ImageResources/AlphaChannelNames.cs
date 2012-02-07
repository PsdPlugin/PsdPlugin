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
using System.Collections.Generic;

namespace PhotoshopFile
{
  /// <summary>
  /// The names of the alpha channels
  /// </summary>
  public class AlphaChannelNames : ImageResource
  {
    public override ResourceID ID
    {
      get { return ResourceID.AlphaChannelNames; }
    }

    private List<string> channelNames = new List<string>();
    public List<string> ChannelNames
    {
      get { return channelNames; }
    }

    public AlphaChannelNames() : base(String.Empty)
    {
    }

    public AlphaChannelNames(BinaryReverseReader reader, string name, int resourceDataLength)
      : base(name)
    {
      var endPosition = reader.BaseStream.Position + resourceDataLength;

      // Alpha channel names are Pascal strings, with no padding.
      while (reader.BaseStream.Position < endPosition)
      {
        var stringLength = reader.ReadByte();
        var channelName = new string(reader.ReadChars(stringLength));
        if (channelName.Length > 0)
          channelNames.Add(channelName);
      }
    }

    protected override void WriteData(BinaryReverseWriter writer)
    {
      foreach (var channelName in channelNames)
      {
        writer.Write((byte)channelName.Length);
        writer.Write(channelName.ToCharArray());
      }
    }
  }
}
