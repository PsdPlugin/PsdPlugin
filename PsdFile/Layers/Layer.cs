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
    internal PsdFile PsdFile { get; private set; }

    /// <summary>
    /// The rectangle containing the contents of the layer.
    /// </summary>
    public Rectangle Rect { get; set; }

    /// <summary>
    /// Image channels.
    /// </summary>
    public ChannelList Channels { get; private set; }

    /// <summary>
    /// Returns alpha channel if it exists, otherwise null.
    /// </summary>
    public Channel AlphaChannel
    {
      get
      {
        if (Channels.ContainsId(-1))
          return Channels.GetId(-1);
        else
          return null;
      }
    }

    private string blendModeKey;
    /// <summary>
    /// Photoshop blend mode key for the layer
    /// </summary>
    public string BlendModeKey
    {
      get { return blendModeKey; }
      set
      {
        if (value.Length != 4) throw new ArgumentException("Key length must be 4");
        blendModeKey = value;
      }
    }

    /// <summary>
    /// 0 = transparent ... 255 = opaque
    /// </summary>
    public byte Opacity { get; set; }

    /// <summary>
    /// false = base, true = non-base
    /// </summary>
    public bool Clipping { get; set; }

    private static int protectTransBit = BitVector32.CreateMask();
    private static int visibleBit = BitVector32.CreateMask(protectTransBit);
    BitVector32 flags = new BitVector32();

    /// <summary>
    /// If true, the layer is visible.
    /// </summary>
    public bool Visible
    {
      get { return !flags[visibleBit]; }
      set { flags[visibleBit] = !value; }
    }

    /// <summary>
    /// Protect the transparency
    /// </summary>
    public bool ProtectTrans
    {
      get { return flags[protectTransBit]; }
      set { flags[protectTransBit] = value; }
    }

    /// <summary>
    /// The descriptive layer name
    /// </summary>
    public string Name { get; set; }

    public BlendingRanges BlendingRangesData { get; set; }

    public Mask MaskData { get; set; }

    public List<LayerInfo> AdditionalInfo { get; set; }

    ///////////////////////////////////////////////////////////////////////////

    public Layer(PsdFile psdFile)
    {
      PsdFile = psdFile;
      Rect = Rectangle.Empty;
      Channels = new ChannelList();
      BlendModeKey = PsdBlendMode.Normal;
      AdditionalInfo = new List<LayerInfo>();
    }

    public Layer(PsdBinaryReader reader, PsdFile psdFile)
    {
      Debug.WriteLine("Layer started at " + reader.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      PsdFile = psdFile;
      var rect = new Rectangle();
      rect.Y = reader.ReadInt32();
      rect.X = reader.ReadInt32();
      rect.Height = reader.ReadInt32() - rect.Y;
      rect.Width = reader.ReadInt32() - rect.X;
      Rect = rect;

      //-----------------------------------------------------------------------

      int numberOfChannels = reader.ReadUInt16();
      Channels = new ChannelList();
      for (int channel = 0; channel < numberOfChannels; channel++)
      {
        Channel ch = new Channel(reader, this);
        Channels.Add(ch);
      }

      //-----------------------------------------------------------------------

      string signature = new string(reader.ReadChars(4));
      if (signature != "8BIM")
        throw (new IOException("Layer ChannelHeader error!"));

      BlendModeKey = new string(reader.ReadChars(4));
      Opacity = reader.ReadByte();
      Clipping = reader.ReadBoolean();

      //-----------------------------------------------------------------------

      var flagsByte = reader.ReadByte();
      flags = new BitVector32(flagsByte);

      //-----------------------------------------------------------------------

      reader.ReadByte(); //padding

      //-----------------------------------------------------------------------

      Debug.WriteLine("Layer extraDataSize started at " + reader.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      // This is the total size of the MaskData, the BlendingRangesData, the 
      // Name and the AdjustmentLayerInfo.
      var extraDataSize = reader.ReadUInt32();
      var extraDataStartPosition = reader.BaseStream.Position;

      MaskData = new Mask(reader, this);
      BlendingRangesData = new BlendingRanges(reader, this);

      //-----------------------------------------------------------------------

      // Read layer name, padded to length multiple of 4
      long namePosition = reader.BaseStream.Position;
      Name = reader.ReadPascalString();

      // Calculation works because ReadPascalString has already padded to even
      int paddingBytes = (int)((reader.BaseStream.Position - namePosition) % 4);
      Debug.Print("Layer {0} padding bytes after name", paddingBytes);
      reader.ReadBytes(paddingBytes);

      //-----------------------------------------------------------------------
      // Process Additional Layer Information

      long adjustmentLayerEndPos = extraDataStartPosition + extraDataSize;
      AdditionalInfo = new List<LayerInfo>();
      try
      {
        while (reader.BaseStream.Position < adjustmentLayerEndPos)
          AdditionalInfo.Add(LayerInfoFactory.CreateLayerInfo(reader));
      }
      catch
      {
        // An exception would leave us in the wrong stream position.  We must
        // therefore reset the position to continue parsing the file.
        reader.BaseStream.Position = adjustmentLayerEndPos;
      }

      foreach (var adjustmentInfo in AdditionalInfo)
      {
        switch (adjustmentInfo.Key)
        {
          case "luni":
            Name = ((LayerUnicodeName)adjustmentInfo).Name;
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
      foreach (var ch in Channels)
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

      writer.Write(Rect.Top);
      writer.Write(Rect.Left);
      writer.Write(Rect.Bottom);
      writer.Write(Rect.Right);

      //-----------------------------------------------------------------------

      writer.Write((short)Channels.Count);
      foreach (var ch in Channels)
        ch.Save(writer);

      //-----------------------------------------------------------------------

      writer.Write(Util.SIGNATURE_8BIM);
      writer.Write(BlendModeKey.ToCharArray());
      writer.Write(Opacity);
      writer.Write(Clipping);

      writer.Write((byte)flags.Data);

      //-----------------------------------------------------------------------

      writer.Write((byte)0);

      //-----------------------------------------------------------------------

      using (new PsdBlockLengthWriter(writer))
      {
        MaskData.Save(writer);
        BlendingRangesData.Save(writer);

        var namePosition = writer.BaseStream.Position;
        writer.WritePascalString(Name);

        // Calculation works because WritePascalString has already padded to even
        int paddingBytes = (int)((writer.BaseStream.Position - namePosition) % 4);
        Debug.Print("Layer {0} write padding bytes after name", paddingBytes);
        for (int i = 0; i < paddingBytes; i++)
          writer.Write((byte)0);

        foreach (LayerInfo info in AdditionalInfo)
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
