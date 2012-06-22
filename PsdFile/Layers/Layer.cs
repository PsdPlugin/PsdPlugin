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
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace PhotoshopFile
{
  public class Layer
  {
    ///////////////////////////////////////////////////////////////////////////

    private PsdFile m_psdFile;
    internal PsdFile PsdFile
    {
      get { return m_psdFile; }
    }

    private Rectangle m_rect = Rectangle.Empty;
    /// <summary>
    /// The rectangle containing the contents of the layer.
    /// </summary>
    public Rectangle Rect
    {
      get { return m_rect; }
      set { m_rect = value; }
    }

    private ChannelList m_channels = new ChannelList();

    /// <summary>
    /// Channel information.
    /// </summary>
    public ChannelList Channels
    {
      get { return m_channels; }
    }

    /// <summary>
    /// Returns alpha channel if it exists, otherwise null.
    /// </summary>
    public Channel AlphaChannel
    {
      get
      {
        if (Channels.ContainsId(-1))
          return this.Channels.GetId(-1);
        else
          return null;
      }
    }

    private string m_blendModeKey = "norm";
    /// <summary>
    /// The blend mode key for the layer
    /// </summary>
    /// <remarks>
    /// <list type="table">
    /// <term>norm</term><description>normal</description>
    /// <term>dark</term><description>darken</description>
    /// <term>lite</term><description>lighten</description>
    /// <term>hue </term><description>hue</description>
    /// <term>sat </term><description>saturation</description>
    /// <term>colr</term><description>color</description>
    /// <term>lum </term><description>luminosity</description>
    /// <term>mul </term><description>multiply</description>
    /// <term>scrn</term><description>screen</description>
    /// <term>diss</term><description>dissolve</description>
    /// <term>over</term><description>overlay</description>
    /// <term>hLit</term><description>hard light</description>
    /// <term>sLit</term><description>soft light</description>
    /// <term>diff</term><description>difference</description>
    /// <term>smud</term><description>exclusion</description>
    /// <term>div </term><description>color dodge</description>
    /// <term>idiv</term><description>color burn</description>
    /// </list>
    /// </remarks>
    public string BlendModeKey
    {
      get { return m_blendModeKey; }
      set
      {
        if (value.Length != 4) throw new ArgumentException("Key length must be 4");
        m_blendModeKey = value;
      }
    }


    private byte m_opacity;
    /// <summary>
    /// 0 = transparent ... 255 = opaque
    /// </summary>
    public byte Opacity
    {
      get { return m_opacity; }
      set { m_opacity = value; }
    }


    private bool m_clipping;
    /// <summary>
    /// false = base, true = non-base
    /// </summary>
    public bool Clipping
    {
      get { return m_clipping; }
      set { m_clipping = value; }
    }

    private static int protectTransBit = BitVector32.CreateMask();
    private static int visibleBit = BitVector32.CreateMask(protectTransBit);

    BitVector32 m_flags = new BitVector32();

    /// <summary>
    /// If true, the layer is visible.
    /// </summary>
    public bool Visible
    {
      get { return !m_flags[visibleBit]; }
      set { m_flags[visibleBit] = !value; }
    }


    /// <summary>
    /// Protect the transparency
    /// </summary>
    public bool ProtectTrans
    {
      get { return m_flags[protectTransBit]; }
      set { m_flags[protectTransBit] = value; }
    }


    private string m_name;
    /// <summary>
    /// The descriptive layer name
    /// </summary>
    public string Name
    {
      get { return m_name; }
      set { m_name = value; }
    }

    private BlendingRanges m_blendingRangesData;
    public BlendingRanges BlendingRangesData
    {
      get { return m_blendingRangesData; }
      set { m_blendingRangesData = value; }
    }

    private Mask m_maskData;
    public Mask MaskData
    {
      get { return m_maskData; }
      set { m_maskData = value; }
    }

    private List<LayerInfo> additionalInfo = new List<LayerInfo>();
    public List<LayerInfo> AdditionalInfo
    {
      get { return additionalInfo; }
      set { additionalInfo = value; }
    }

    ///////////////////////////////////////////////////////////////////////////

    public Layer(PsdFile psdFile)
    {
      m_psdFile = psdFile;
    }

    public Layer(PsdBinaryReader reader, PsdFile psdFile)
    {
      Debug.WriteLine("Layer started at " + reader.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      m_psdFile = psdFile;
      m_rect = new Rectangle();
      m_rect.Y = reader.ReadInt32();
      m_rect.X = reader.ReadInt32();
      m_rect.Height = reader.ReadInt32() - m_rect.Y;
      m_rect.Width = reader.ReadInt32() - m_rect.X;

      //-----------------------------------------------------------------------

      int numberOfChannels = reader.ReadUInt16();
      this.m_channels.Clear();
      for (int channel = 0; channel < numberOfChannels; channel++)
      {
        Channel ch = new Channel(reader, this);
        m_channels.Add(ch);
      }

      //-----------------------------------------------------------------------

      string signature = new string(reader.ReadChars(4));
      if (signature != "8BIM")
        throw (new IOException("Layer ChannelHeader error!"));

      m_blendModeKey = new string(reader.ReadChars(4));
      m_opacity = reader.ReadByte();

      m_clipping = reader.ReadByte() > 0;

      //-----------------------------------------------------------------------

      byte flags = reader.ReadByte();
      m_flags = new BitVector32(flags);

      //-----------------------------------------------------------------------

      reader.ReadByte(); //padding

      //-----------------------------------------------------------------------

      Debug.WriteLine("Layer extraDataSize started at " + reader.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      // this is the total size of the MaskData, the BlendingRangesData, the 
      // Name and the AdjustmentLayerInfo
      uint extraDataSize = reader.ReadUInt32();

      // remember the start position for calculation of the 
      // AdjustmentLayerInfo size
      long extraDataStartPosition = reader.BaseStream.Position;

      m_maskData = new Mask(reader, this);
      m_blendingRangesData = new BlendingRanges(reader, this);

      //-----------------------------------------------------------------------

      long namePosition = reader.BaseStream.Position;

      m_name = reader.ReadPascalString();

      int paddingBytes = (int)((reader.BaseStream.Position - namePosition) % 4);

      Debug.Print("Layer {0} padding bytes after name", paddingBytes);
      reader.ReadBytes(paddingBytes);

      //-----------------------------------------------------------------------
      // Process Additional Layer Information

      long adjustmentLayerEndPos = extraDataStartPosition + extraDataSize;
      try
      {
        while (reader.BaseStream.Position < adjustmentLayerEndPos)
          additionalInfo.Add(LayerInfoFactory.CreateLayerInfo(reader));
      }
      catch
      {
        // An exception would leave us in the wrong stream position.  We must
        // therefore reset the position to continue parsing the file.
        reader.BaseStream.Position = adjustmentLayerEndPos;
      }

      foreach (var adjustmentInfo in additionalInfo)
      {
        switch (adjustmentInfo.Key)
        {
          case "luni":
            m_name = ((LayerUnicodeName)adjustmentInfo).Name;
            break;
        }
      }

    }

    ///////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// Create ImageData for any missing channels.
    /// </summary>
    public void CreateMissingChannels()
    {
      var channelCount = this.PsdFile.ColorMode.ChannelCount();
      for (short id = 0; id < channelCount; id++)
      {
        if (!this.Channels.ContainsId(id))
        {
          var size = this.Rect.Height * this.Rect.Width;

          var ch = new Channel(id, this);
          ch.ImageData = new byte[size];
          unsafe
          {
            fixed (byte* ptr = &ch.ImageData[0])
            {
              Util.Fill(ptr, 255, size);
            }
          }

          this.Channels.Add(ch);
        }
      }
    }

    ///////////////////////////////////////////////////////////////////////////

    public void PrepareSave(PaintDotNet.Threading.PrivateThreadPool threadPool)
    {
      foreach (Channel ch in m_channels)
      {
        CompressChannelContext ccc = new CompressChannelContext(ch);
        WaitCallback waitCallback = new WaitCallback(ccc.CompressChannel);
        threadPool.QueueUserWorkItem(waitCallback);
      }
      
      // Create or update the Unicode layer name to be consistent with the
      // ANSI layer name.
      var layerUnicodeNames = AdditionalInfo.Where(x => x is LayerUnicodeName);
      if (layerUnicodeNames.Count() > 1)
        throw new Exception("Layer has more than one LayerUnicodeName.");

      var layerUnicodeName = (LayerUnicodeName) layerUnicodeNames.FirstOrDefault();
      if (layerUnicodeName == null)
      {
        layerUnicodeName = new LayerUnicodeName(Name);
        AdditionalInfo.Add(layerUnicodeName);
      }
      else if (layerUnicodeName.Name != Name)
      {
        layerUnicodeName.Name = Name;
      }
    }

    public void Save(PsdBinaryWriter writer)
    {
      Debug.WriteLine("Layer Save started at " + writer.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      writer.Write(m_rect.Top);
      writer.Write(m_rect.Left);
      writer.Write(m_rect.Bottom);
      writer.Write(m_rect.Right);

      //-----------------------------------------------------------------------

      writer.Write((short)m_channels.Count);
      foreach (Channel ch in m_channels)
        ch.Save(writer);

      //-----------------------------------------------------------------------

      writer.Write(Util.SIGNATURE_8BIM);
      writer.Write(m_blendModeKey.ToCharArray());
      writer.Write(m_opacity);
      writer.Write((byte)(m_clipping ? 1 : 0));

      writer.Write((byte)m_flags.Data);

      //-----------------------------------------------------------------------

      writer.Write((byte)0);

      //-----------------------------------------------------------------------

      using (new PsdBlockLengthWriter(writer))
      {
        m_maskData.Save(writer);
        m_blendingRangesData.Save(writer);

        long namePosition = writer.BaseStream.Position;

        writer.WritePascalString(m_name);

        int paddingBytes = (int)((writer.BaseStream.Position - namePosition) % 4);
        Debug.Print("Layer {0} write padding bytes after name", paddingBytes);

        for (int i = 0; i < paddingBytes;i++ )
          writer.Write((byte)0);

        foreach (LayerInfo info in additionalInfo)
        {
          info.Save(writer);
        }
      }
    }

    private class CompressChannelContext
    {
      private Channel ch;

      public CompressChannelContext(Channel ch)
      {
        this.ch = ch;
      }

      public void CompressChannel(object context)
      {
        ch.CompressImageData();
      }
    }
  }
}
