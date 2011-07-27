/////////////////////////////////////////////////////////////////////////////////
//
// Photoshop PSD FileType Plugin for Paint.NET
// http://psdplugin.codeplex.com/
//
// This software is provided under the MIT License:
//   Copyright (c) 2006-2007 Frank Blumenberg
//   Copyright (c) 2010-2011 Tao Yue
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
  public class AlphaChannels : ImageResource
  {
    private List<string> m_channelNames=new List<string>();
    public List<string> ChannelNames
    {
      get { return m_channelNames; }
    }

    public AlphaChannels(): base((short)ResourceID.AlphaChannelNames)
    {
    }

    public AlphaChannels(ImageResource imgRes)
      : base(imgRes)
    {
      
      BinaryReverseReader reader = imgRes.DataReader;
      // the names are pascal strings without padding!!!
      while ((reader.BaseStream.Length - reader.BaseStream.Position) > 0)
      {
        byte stringLength = reader.ReadByte();
        string s = new string(reader.ReadChars(stringLength));
        if (s.Length > 0)
          m_channelNames.Add(s);
      }
      reader.Close();
    }

    protected override void StoreData()
    {
      System.IO.MemoryStream stream = new System.IO.MemoryStream();
      BinaryReverseWriter writer = new BinaryReverseWriter(stream);

      foreach (string name in m_channelNames)
      {
        writer.Write((byte)name.Length);
        writer.Write(name.ToCharArray());
      }

      writer.Close();
      stream.Close();

      Data = stream.ToArray();
    }
  }
}
