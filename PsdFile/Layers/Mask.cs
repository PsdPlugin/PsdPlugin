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
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;

namespace PhotoshopFile
{
  public class Mask
  {
    /// <summary>
    /// The layer to which this mask belongs
    /// </summary>
    public Layer Layer { get; private set; }

    /// <summary>
    /// The rectangle enclosing the mask.
    /// </summary>
    public Rectangle Rect { get; private set; }

    public byte DefaultColor { get; set; }

    private static int positionIsRelativeBit = BitVector32.CreateMask();
    private static int disabledBit = BitVector32.CreateMask(positionIsRelativeBit);
    private static int invertOnBlendBit = BitVector32.CreateMask(disabledBit);

    private BitVector32 flags = new BitVector32();
    /// <summary>
    /// If true, the position of the mask is relative to the layer.
    /// </summary>
    public bool PositionIsRelative
    {
      get { return flags[positionIsRelativeBit]; }
      set { flags[positionIsRelativeBit] = value; }
    }

    public bool Disabled
    {
      get { return flags[disabledBit]; }
      set { flags[disabledBit] = value; }
    }

    /// <summary>
    /// if true, invert the mask when blending.
    /// </summary>
    public bool InvertOnBlendBit
    {
      get { return flags[invertOnBlendBit]; }
      set { flags[invertOnBlendBit] = value; }
    }

    /// <summary>
    /// Mask image data.
    /// </summary>
    public byte[] ImageData { get; set; }

    ///////////////////////////////////////////////////////////////////////////

    internal Mask(Layer layer)
    {
      Layer = layer;
    }

    ///////////////////////////////////////////////////////////////////////////

    internal Mask(PsdBinaryReader reader, Layer layer)
    {
      Debug.WriteLine("Mask started at " + reader.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      Layer = layer;

      var maskLength = reader.ReadUInt32();
      if (maskLength <= 0)
        return;

      var startPosition = reader.BaseStream.Position;

      //-----------------------------------------------------------------------

      var rect = new Rectangle();
      rect.Y = reader.ReadInt32();
      rect.X = reader.ReadInt32();
      rect.Height = reader.ReadInt32() - rect.Y;
      rect.Width = reader.ReadInt32() - rect.X;
      Rect = rect;

      DefaultColor = reader.ReadByte();

      //-----------------------------------------------------------------------

      var flagsByte = reader.ReadByte();
      flags = new BitVector32(flagsByte);

      //-----------------------------------------------------------------------

      if (maskLength == 36)
      {
        var realFlags = new BitVector32(reader.ReadByte());
        byte realUserMaskBackground = reader.ReadByte();

        var realRect = new Rectangle();
        realRect.Y = reader.ReadInt32();
        realRect.X = reader.ReadInt32();
        realRect.Height = reader.ReadInt32() - rect.Y;
        realRect.Width = reader.ReadInt32() - rect.X;
      }

      // 20-byte mask data will end with padding.
      reader.BaseStream.Position = startPosition + maskLength;
    }

    ///////////////////////////////////////////////////////////////////////////

    public void Save(PsdBinaryWriter writer)
    {
      Debug.WriteLine("Mask Save started at " + writer.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      if (Rect.IsEmpty)
      {
        writer.Write((UInt32)0);
        return;
      }

      using (new PsdBlockLengthWriter(writer))
      {
        writer.Write(Rect.Top);
        writer.Write(Rect.Left);
        writer.Write(Rect.Bottom);
        writer.Write(Rect.Right);

        writer.Write(DefaultColor);

        writer.Write((byte)flags.Data);

        // Padding by 2 bytes to make the block length 20
        writer.Write((byte)0);
        writer.Write((byte)0);
      }
    }

  }
}