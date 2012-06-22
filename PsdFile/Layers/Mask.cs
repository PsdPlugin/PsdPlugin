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

using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;

namespace PhotoshopFile
{
  public class Mask
  {
    private Layer m_layer;
    /// <summary>
    /// The layer to which this mask belongs
    /// </summary>
    public Layer Layer
    {
      get { return m_layer; }
    }

    private Rectangle m_rect = Rectangle.Empty;
    /// <summary>
    /// The rectangle enclosing the mask.
    /// </summary>
    public Rectangle Rect
    {
      get { return m_rect; }
      set { m_rect = value; }
    }

    private byte m_defaultColor;
    public byte DefaultColor
    {
      get { return m_defaultColor; }
      set { m_defaultColor = value; }
    }

    private static int positionIsRelativeBit = BitVector32.CreateMask();
    private static int disabledBit = BitVector32.CreateMask(positionIsRelativeBit);
    private static int invertOnBlendBit = BitVector32.CreateMask(disabledBit);

    private BitVector32 m_flags = new BitVector32();
    /// <summary>
    /// If true, the position of the mask is relative to the layer.
    /// </summary>
    public bool PositionIsRelative
    {
      get
      {
        return m_flags[positionIsRelativeBit];
      }
      set
      {
        m_flags[positionIsRelativeBit] = value;
      }
    }

    public bool Disabled
    {
      get { return m_flags[disabledBit]; }
      set { m_flags[disabledBit] = value; }
    }

    /// <summary>
    /// if true, invert the mask when blending.
    /// </summary>
    public bool InvertOnBlendBit
    {
      get { return m_flags[invertOnBlendBit]; }
      set { m_flags[invertOnBlendBit] = value; }
    }

    ///////////////////////////////////////////////////////////////////////////

    internal Mask(Layer layer)
    {
      m_layer = layer;
      m_layer.MaskData = this;
    }

    ///////////////////////////////////////////////////////////////////////////

    internal Mask(PsdBinaryReader reader, Layer layer)
    {
      Debug.WriteLine("Mask started at " + reader.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      m_layer = layer;

      uint maskLength = reader.ReadUInt32();

      if (maskLength <= 0)
        return;

      long startPosition = reader.BaseStream.Position;

      //-----------------------------------------------------------------------

      m_rect = new Rectangle();
      m_rect.Y = reader.ReadInt32();
      m_rect.X = reader.ReadInt32();
      m_rect.Height = reader.ReadInt32() - m_rect.Y;
      m_rect.Width = reader.ReadInt32() - m_rect.X;

      m_defaultColor = reader.ReadByte();

      //-----------------------------------------------------------------------

      byte flags = reader.ReadByte();
      m_flags = new BitVector32(flags);

      //-----------------------------------------------------------------------

      if (maskLength == 36)
      {
        var realFlags = new BitVector32(reader.ReadByte());

        byte realUserMaskBackground = reader.ReadByte();

        Rectangle rect = new Rectangle();
        rect.Y = reader.ReadInt32();
        rect.X = reader.ReadInt32();
        rect.Height = reader.ReadInt32() - m_rect.Y;
        rect.Width = reader.ReadInt32() - m_rect.X;
      }

      // there is other stuff following, but we will ignore this.
      reader.BaseStream.Position = startPosition + maskLength;
    }

    ///////////////////////////////////////////////////////////////////////////

    public void Save(PsdBinaryWriter writer)
    {
      Debug.WriteLine("Mask Save started at " + writer.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      if (m_rect.IsEmpty)
      {
        writer.Write((uint)0);
        return;
      }

      using (new PsdBlockLengthWriter(writer))
      {
        writer.Write(m_rect.Top);
        writer.Write(m_rect.Left);
        writer.Write(m_rect.Bottom);
        writer.Write(m_rect.Right);

        writer.Write(m_defaultColor);

        writer.Write((byte)m_flags.Data);

        // padding 2 bytes so that size is 20
        writer.Write((int)0);
      }
    }

    //////////////////////////////////////////////////////////////////

    /// <summary>
    /// The raw image data from the channel.
    /// </summary>
    public byte[] m_imageData;

    public byte[] ImageData
    {
      get { return m_imageData; }
      set { m_imageData = value; }
    }
  }
}
