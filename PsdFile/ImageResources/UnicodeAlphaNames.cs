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
using System.Collections.Generic;

namespace PhotoshopFile
{
  /// <summary>
  /// The names of the alpha channels.
  /// </summary>
  public class UnicodeAlphaNames : ImageResource
  {
    public override ResourceID ID
    {
      get { return ResourceID.UnicodeAlphaNames; }
    }

    private List<string> channelNames = new List<string>();
    public List<string> ChannelNames
    {
      get { return channelNames; }
    }

    public UnicodeAlphaNames()
      : base(String.Empty)
    {
    }

    public UnicodeAlphaNames(PsdBinaryReader reader, string name, int resourceDataLength)
      : base(name)
    {
      var endPosition = reader.BaseStream.Position + resourceDataLength;

      while (reader.BaseStream.Position < endPosition)
      {
        var channelName = reader.ReadUnicodeString();
        ChannelNames.Add(channelName);
      }
    }

    protected override void WriteData(PsdBinaryWriter writer)
    {
      foreach (var channelName in ChannelNames)
      {
        writer.WriteUnicodeString(channelName);
      }
    }
  }
}
