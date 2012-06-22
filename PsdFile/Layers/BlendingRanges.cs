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

using System.Diagnostics;
using System.Globalization;

namespace PhotoshopFile
{
  public class BlendingRanges
  {
    private Layer m_layer;
    /// <summary>
    /// The layer to which this channel belongs
    /// </summary>
    public Layer Layer
    {
      get { return m_layer; }
    }

    private byte[] m_data = new byte[0];

    public byte[] Data
    {
      get { return m_data; }
      set { m_data = value; }
    }

    ///////////////////////////////////////////////////////////////////////////

    public BlendingRanges(Layer layer)
    {
      m_layer = layer;
    }

    ///////////////////////////////////////////////////////////////////////////

    public BlendingRanges(PsdBinaryReader reader, Layer layer)
    {
      Debug.WriteLine("BlendingRanges started at " + reader.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      m_layer = layer;
      int dataLength = reader.ReadInt32();
      if (dataLength <= 0)
        return;

      m_data = reader.ReadBytes(dataLength);
    }

    ///////////////////////////////////////////////////////////////////////////

    public void Save(PsdBinaryWriter writer)
    {
      Debug.WriteLine("BlendingRanges Save started at " + writer.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      writer.Write((uint)m_data.Length);
      writer.Write(m_data);
    }
  }
}
