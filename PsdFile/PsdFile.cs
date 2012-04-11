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
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;


namespace PhotoshopFile
{
  public enum PsdColorMode
  {
    Bitmap = 0, Grayscale = 1, Indexed = 2, RGB = 3, CMYK = 4, Multichannel = 7, Duotone = 8, Lab = 9
  };


  public class PsdFile
  {

    public Layer BaseLayer { get; set; }

    public ImageCompression ImageCompression { get; set; }


    ///////////////////////////////////////////////////////////////////////////

    public PsdFile()
    {
      this.BaseLayer = new Layer(this);
      this.BaseLayer.Rect = new Rectangle(0, 0, 0, 0);
    }

    ///////////////////////////////////////////////////////////////////////////

    public void Load(string fileName)
    {
      using (FileStream stream = new FileStream(fileName, FileMode.Open))
      {
        Load(stream);
      }
    }

    public void Load(Stream stream)
    {
      PsdBinaryReader reader = new PsdBinaryReader(stream);

      LoadHeader(reader);
      LoadColorModeData(reader);
      LoadImageResources(reader);
      LoadLayerAndMaskInfo(reader);

      LoadImage(reader);
      DecompressImages();
    }

    public void Save(string fileName)
    {
      using (FileStream stream = new FileStream(fileName, FileMode.Create))
      {
        Save(stream);

      }
    }

    public void Save(Stream stream)
    {
      if (Depth != 8)
        throw new NotImplementedException("Only 8-bit color has been implemented for saving.");

      var writer = new PsdBinaryWriter(stream);

      writer.AutoFlush = true;

      PrepareSave();

      SaveHeader(writer);
      SaveColorModeData(writer);
      SaveImageResources(writer);
      SaveLayerAndMaskInfo(writer);
      SaveImage(writer);
    }

    ///////////////////////////////////////////////////////////////////////////

    #region Header

    /// <summary>
    /// Always equal to 1.
    /// </summary>
    private short m_version = 1;
    public short Version
    {
      get { return m_version; }
    }


    private short m_channels;
    /// <summary>
    /// The number of channels in the image, including any alpha channels.
    /// </summary>
    public short Channels
    {
      get { return m_channels; }
      set
      {
        if (value < 1 || value > 56)
          throw new ArgumentException("Number of channels must be from 1 to 56.");
        m_channels = value;
      }
    }

    /// <summary>
    /// The height of the image in pixels.
    /// </summary>
    public int Rows
    {
      get { return this.BaseLayer.Rect.Height; }
      set
      {
        if (value < 0 || value > 30000)
          throw new ArgumentException("Number of rows must be from 1 to 30000.");
        this.BaseLayer.Rect = new Rectangle(0, 0, this.BaseLayer.Rect.Width, value);
      }
    }


    /// <summary>
    /// The width of the image in pixels. 
    /// </summary>
    public int Columns
    {
      get { return this.BaseLayer.Rect.Width; }
      set
      {
        if (value < 0 || value > 30000)
          throw new ArgumentException("Number of columns must be from 1 to 30000.");
        this.BaseLayer.Rect = new Rectangle(0, 0, value, this.BaseLayer.Rect.Height);
      }
    }


    /// <summary>
    /// The number of pixels to advance for each row.
    /// </summary>
    public int RowPixels
    {
      get
      {
        if (m_colorMode == PsdColorMode.Bitmap)
          return Util.RoundUp(this.Columns, 8);
        else
          return this.Columns;
      }
    }

    private int m_depth;
    /// <summary>
    /// The number of bits per channel. Supported values are 1, 8, 16, and 32.
    /// </summary>
    public int Depth
    {
      get { return m_depth; }
      set
      {
        switch (value)
        {
          case 1:
          case 8:
          case 16:
          case 32:
            m_depth = value;
            break;
          default:
            throw new NotImplementedException("Invalid bit depth.");
        }
      }
    }
    private PsdColorMode m_colorMode;
    /// <summary>
    /// The color mode of the file.
    /// </summary>
    public PsdColorMode ColorMode
    {
      get { return m_colorMode; }
      set { m_colorMode = value; }
    }


    ///////////////////////////////////////////////////////////////////////////

    private void LoadHeader(PsdBinaryReader reader)
    {
      Debug.WriteLine("LoadHeader started at " + reader.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      string signature = new string(reader.ReadChars(4));
      if (signature != "8BPS")
        throw new IOException("The given stream is not a valid PSD file");

      m_version = reader.ReadInt16();
      if (m_version != 1)
        throw new IOException("The PSD file has an unknown version");

      //6 bytes reserved
      reader.BaseStream.Position += 6;

      this.Channels = reader.ReadInt16();
      this.Rows = reader.ReadInt32();
      this.Columns = reader.ReadInt32();
      m_depth = reader.ReadInt16();
      m_colorMode = (PsdColorMode)reader.ReadInt16();
    }

    ///////////////////////////////////////////////////////////////////////////

    private void SaveHeader(PsdBinaryWriter writer)
    {
      Debug.WriteLine("SaveHeader started at " + writer.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      string signature = "8BPS";
      writer.Write(signature.ToCharArray());
      writer.Write(Version);
      writer.Write(new byte[] { 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, });
      writer.Write(Channels);
      writer.Write(Rows);
      writer.Write(Columns);
      writer.Write((short)m_depth);
      writer.Write((short)m_colorMode);
    }

    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region ColorModeData

    /// <summary>
    /// If ColorMode is ColorModes.Indexed, the following 768 bytes will contain 
    /// a 256-color palette. If the ColorMode is ColorModes.Duotone, the data 
    /// following presumably consists of screen parameters and other related information. 
    /// Unfortunately, it is intentionally not documented by Adobe, and non-Photoshop 
    /// readers are advised to treat duotone images as gray-scale images.
    /// </summary>
    public byte[] ColorModeData = new byte[0];

    private void LoadColorModeData(PsdBinaryReader reader)
    {
      Debug.WriteLine("LoadColorModeData started at " + reader.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      uint paletteLength = reader.ReadUInt32();
      if (paletteLength > 0)
      {
        ColorModeData = reader.ReadBytes((int)paletteLength);
      }
    }

    private void SaveColorModeData(PsdBinaryWriter writer)
    {
      Debug.WriteLine("SaveColorModeData started at " + writer.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      writer.Write((uint)ColorModeData.Length);
      writer.Write(ColorModeData);
    }

    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region ImageResources

    private List<ImageResource> m_imageResources = new List<ImageResource>();

    /// <summary>
    /// The Image resource blocks for the file
    /// </summary>
    public List<ImageResource> ImageResources
    {
      get { return m_imageResources; }
    }


    // This method implements the test condition for 
    // finding the ResolutionInfo.
    private static bool IsResolutionInfo(ImageResource res)
    {
      return res.ID == ResourceID.ResolutionInfo;
    }

    public ResolutionInfo Resolution
    {
      get
      {
        return (ResolutionInfo)m_imageResources.Find(IsResolutionInfo);
      }

      set
      {
        ImageResource oldValue = m_imageResources.Find(IsResolutionInfo);
        if (oldValue != null)
          m_imageResources.Remove(oldValue);

        m_imageResources.Add(value);
      }
    }


    ///////////////////////////////////////////////////////////////////////////

    private void LoadImageResources(PsdBinaryReader reader)
    {
      Debug.WriteLine("LoadImageResources started at " + reader.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      m_imageResources.Clear();

      var imageResourcesLength = reader.ReadUInt32();
      if (imageResourcesLength <= 0)
        return;

      var startPosition = reader.BaseStream.Position;
      var endPosition = startPosition + imageResourcesLength;
      while (reader.BaseStream.Position < endPosition)
      {
        var imageResource = ImageResourceFactory.CreateImageResource(reader);
        m_imageResources.Add(imageResource);
      }

      //-----------------------------------------------------------------------
      // make sure we are not on a wrong offset, so set the stream position 
      // manually
      reader.BaseStream.Position = startPosition + imageResourcesLength;
    }

    ///////////////////////////////////////////////////////////////////////////

    private void SaveImageResources(PsdBinaryWriter writer)
    {
     Debug.WriteLine("SaveImageResources started at " + writer.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      using (new PsdBlockLengthWriter(writer))
      {
        foreach (ImageResource imgRes in m_imageResources)
          imgRes.Save(writer);
      }
    }

    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region LayerAndMaskInfo

    List<Layer> m_layers = new List<Layer>();
    public List<Layer> Layers
    {
      get
      {
        return m_layers;
      }
    }

    private bool m_absoluteAlpha;
    public bool AbsoluteAlpha
    {
      get { return m_absoluteAlpha; }
      set { m_absoluteAlpha = value; }
    }


    ///////////////////////////////////////////////////////////////////////////

    private void LoadLayerAndMaskInfo(PsdBinaryReader reader)
    {
      Debug.WriteLine("LoadLayerAndMaskInfo started at " + reader.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      uint layersAndMaskLength = reader.ReadUInt32();

      if (layersAndMaskLength <= 0)
        return;

      long startPosition = reader.BaseStream.Position;

      LoadLayers(reader);
      LoadGlobalLayerMask(reader);

      //-----------------------------------------------------------------------

      // Higher bit depth images store an empty layers section for backcompat,
      // followed by the real layers section (which is undocumented but
      // appears largely identical).
      if ((this.Depth > 8) && (reader.BaseStream.Position < startPosition + layersAndMaskLength))
      {
        string signature = new string(reader.ReadChars(8));
        if ((signature == "8BIMLr16") || (signature == "8BIMLr32"))
        {
          LoadLayers(reader);
          LoadGlobalLayerMask(reader);
        }
      }
      

      //-----------------------------------------------------------------------
      // make sure we are not on a wrong offset, so set the stream position 
      // manually
      reader.BaseStream.Position = startPosition + layersAndMaskLength;
    }

    ///////////////////////////////////////////////////////////////////////////

    private void SaveLayerAndMaskInfo(PsdBinaryWriter writer)
    {
      Debug.WriteLine("SaveLayerAndMaskInfo started at " + writer.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      using (new PsdBlockLengthWriter(writer))
      {
        SaveLayers(writer);
        SaveGlobalLayerMask(writer);
      }
    }

    ///////////////////////////////////////////////////////////////////////////

    private void LoadLayers(PsdBinaryReader reader)
    {
      Debug.WriteLine("LoadLayers started at " + reader.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      uint layersInfoSectionLength = reader.ReadUInt32();

      if (layersInfoSectionLength <= 0)
        return;

      long startPosition = reader.BaseStream.Position;

      short numberOfLayers = reader.ReadInt16();

      // If <0, then number of layers is absolute value,
      // and the first alpha channel contains the transparency data for
      // the merged result.
      if (numberOfLayers < 0)
      {
        AbsoluteAlpha = true;
        numberOfLayers = Math.Abs(numberOfLayers);
      }

      m_layers.Clear();

      if (numberOfLayers == 0)
        return;

      for (int i = 0; i < numberOfLayers; i++)
      {
        m_layers.Add(new Layer(reader, this));
      }

      //-----------------------------------------------------------------------

      foreach (Layer layer in m_layers)
      {
        foreach (Layer.Channel channel in layer.Channels)
        {
          Rectangle rect = (channel.ID == -2)
            ? layer.MaskData.Rect
            : layer.Rect;
          channel.LoadPixelData(reader, rect);
        }
      }

      //-----------------------------------------------------------------------

      if (reader.BaseStream.Position % 2 == 1)
        reader.ReadByte();

      //-----------------------------------------------------------------------
      // make sure we are not on a wrong offset, so set the stream position 
      // manually
      reader.BaseStream.Position = startPosition + layersInfoSectionLength;
    }

    ///////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// Decompress the document image data and all the layers' image data, in parallel.
    /// </summary>
    private void DecompressImages()
    {
      PaintDotNet.Threading.PrivateThreadPool threadPool = new PaintDotNet.Threading.PrivateThreadPool();

      var imageLayers = m_layers.Concat(new List<Layer>() { this.BaseLayer });
      foreach (Layer layer in imageLayers)
      {
        foreach (Layer.Channel channel in layer.Channels)
        {
          Rectangle rect = (channel.ID == -2)
            ? layer.MaskData.Rect
            : layer.Rect;

          DecompressChannelContext dcc = new DecompressChannelContext(channel, rect);

          WaitCallback waitCallback = new WaitCallback(dcc.DecompressChannel);
          threadPool.QueueUserWorkItem(waitCallback);
        }
      }

      threadPool.Drain();

      foreach (Layer layer in m_layers)
      {
        if (layer.Channels.ContainsId(-2))
          layer.MaskData.ImageData = layer.Channels.GetId(-2).ImageData;
      }
    }

    /// <summary>
    /// Prepare to save the document image data and all the layers' image data,
    /// compressing in parallel.
    /// </summary>
    public void PrepareSave()
    {
      PaintDotNet.Threading.PrivateThreadPool threadPool = new PaintDotNet.Threading.PrivateThreadPool();
      var imageLayers = m_layers.Concat(new List<Layer>() { this.BaseLayer });
      foreach (Layer layer in imageLayers)
      {
        layer.PrepareSave(threadPool);
      }
      threadPool.Drain();
    }

    private void SaveLayers(PsdBinaryWriter writer)
    {
      Debug.WriteLine("SaveLayers started at " + writer.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      using (new PsdBlockLengthWriter(writer))
      {
        short numberOfLayers = (short)m_layers.Count;
        if (AbsoluteAlpha)
          numberOfLayers = (short)-numberOfLayers;

        writer.Write(numberOfLayers);

        foreach (Layer layer in m_layers)
        {
          layer.Save(writer);
        }

        foreach (Layer layer in m_layers)
        {
          foreach (Layer.Channel channel in layer.Channels)
          {
            channel.SavePixelData(writer);
          }
        }

        if (writer.BaseStream.Position % 2 == 1)
          writer.Write((byte)0);
      }
    }

    ///////////////////////////////////////////////////////////////////////////

    byte[] GlobalLayerMaskData = new byte[0];

    private void LoadGlobalLayerMask(PsdBinaryReader reader)
    {
      Debug.WriteLine("LoadGlobalLayerMask started at " + reader.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      uint maskLength = reader.ReadUInt32();

      if (maskLength <= 0)
        return;

      GlobalLayerMaskData = reader.ReadBytes((int)maskLength);
    }

    ///////////////////////////////////////////////////////////////////////////

    private void SaveGlobalLayerMask(PsdBinaryWriter writer)
    {
      Debug.WriteLine("SaveGlobalLayerMask started at " + writer.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      writer.Write((uint)GlobalLayerMaskData.Length);
      writer.Write(GlobalLayerMaskData);
    }

    ///////////////////////////////////////////////////////////////////////////

    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region ImageData

    ///////////////////////////////////////////////////////////////////////////

    private void LoadImage(PsdBinaryReader reader)
    {
      Debug.WriteLine("LoadImage started at " + reader.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      this.BaseLayer.Rect = new Rectangle(0, 0, this.Columns, this.Rows);
      this.ImageCompression = (ImageCompression)reader.ReadInt16();
      switch (this.ImageCompression)
      {
        case ImageCompression.Raw:
          var length = this.Rows * Util.BytesPerRow(this.BaseLayer.Rect, this.Depth);
          for (short i = 0; i < this.Channels; i++)
          {
            var channel = new Layer.Channel(i, this.BaseLayer);
            channel.ImageCompression = this.ImageCompression;
            channel.Length = length;
            channel.ImageData = reader.ReadBytes(length);
            this.BaseLayer.Channels.Add(channel);
          }
          break;

        case ImageCompression.Rle:
          // Store RLE data length
          for (short i = 0; i < this.Channels; i++)
          {
            int totalRleLength = 0;
            for (int j = 0; j < this.Rows; j++)
              totalRleLength += reader.ReadUInt16();

            var channel = new Layer.Channel(i, this.BaseLayer);
            channel.ImageCompression = this.ImageCompression;
            channel.Length = (int)totalRleLength;
            this.BaseLayer.Channels.Add(channel);
          }
          
          foreach (var channel in this.BaseLayer.Channels)
          {
            channel.Data = reader.ReadBytes(channel.Length);
          }
          break;
      }

      // If there is one more channel than we need, then it is the alpha channel
      if (this.Channels == this.ColorMode.ChannelCount() + 1)
      {
        var alphaChannel = this.BaseLayer.Channels.Last();
        alphaChannel.ID = -1;
      }
    }

    ///////////////////////////////////////////////////////////////////////////

    private void SaveImage(PsdBinaryWriter writer)
    {
      Debug.WriteLine("SaveImage started at " + writer.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      writer.Write((short)this.ImageCompression);
      if (this.ImageCompression == PhotoshopFile.ImageCompression.Rle)
      {
        foreach (var channel in this.BaseLayer.Channels)
          writer.Write(channel.RleHeader);
      }
      foreach (var channel in this.BaseLayer.Channels)
      {
        writer.Write(channel.Data);
      }
    }

    ///////////////////////////////////////////////////////////////////////////

    private class DecompressChannelContext
    {
      private PhotoshopFile.Layer.Channel ch;
      private Rectangle rect;

      public DecompressChannelContext(PhotoshopFile.Layer.Channel ch, Rectangle rect)
      {
        this.ch = ch;
        this.rect = rect;
      }

      public void DecompressChannel(object context)
      {
        ch.DecompressImageData(rect);
      }
    }

    #endregion
  }



  /// <summary>
  /// The possible Compression methods.
  /// </summary>
  public enum ImageCompression
  {
    /// <summary>
    /// Raw data
    /// </summary>
    Raw = 0,
    /// <summary>
    /// RLE compressed
    /// </summary>
    Rle = 1,
    /// <summary>
    /// ZIP without prediction.
    /// </summary>
    Zip = 2,
    /// <summary>
    /// ZIP with prediction.
    /// </summary>
    ZipPrediction = 3
  }


  class RleHelper
  {
    ////////////////////////////////////////////////////////////////////////

    private class RlePacketStateMachine
    {
      private bool m_rlePacket = false;
      private byte lastValue;
      private int idxPacketData;
      private int packetLength;
      private int maxPacketLength = 128;
      private Stream m_stream;
      private byte[] data;

      internal void Flush()
      {
        byte header;
        if (m_rlePacket)
        {
          header = (byte)(-(packetLength - 1));
          m_stream.WriteByte(header);
          m_stream.WriteByte(lastValue);
        }
        else
        {
          header = (byte)(packetLength - 1);
          m_stream.WriteByte(header);
          m_stream.Write(data, idxPacketData, packetLength);
        }

        packetLength = 0;
      }

      internal void PushRow(byte[] imgData, int startIdx, int endIdx)
      {
        data = imgData;
        for (int i = startIdx; i < endIdx; i++)
        {
          byte color = imgData[i];
          if (packetLength == 0)
          {
            // Starting a fresh packet.
            m_rlePacket = false;
            lastValue = color;
            idxPacketData = i;
            packetLength = 1;
          }
          else if (packetLength == 1)
          {
            // 2nd byte of this packet... decide RLE or non-RLE.
            m_rlePacket = (color == lastValue);
            lastValue = color;
            packetLength = 2;
          }
          else if (packetLength == maxPacketLength)
          {
            // Packet is full. Start a new one.
            Flush();
            m_rlePacket = false;
            lastValue = color;
            idxPacketData = i;
            packetLength = 1;
          }
          else if (packetLength >= 2 && m_rlePacket && color != lastValue)
          {
            // We were filling in an RLE packet, and we got a non-repeated color.
            // Emit the current packet and start a new one.
            Flush();
            m_rlePacket = false;
            lastValue = color;
            idxPacketData = i;
            packetLength = 1;
          }
          else if (packetLength >= 2 && m_rlePacket && color == lastValue)
          {
            // We are filling in an RLE packet, and we got another repeated color.
            // Add the new color to the current packet.
            ++packetLength;
          }
          else if (packetLength >= 2 && !m_rlePacket && color != lastValue)
          {
            // We are filling in a raw packet, and we got another random color.
            // Add the new color to the current packet.
            lastValue = color;
            ++packetLength;
          }
          else if (packetLength >= 2 && !m_rlePacket && color == lastValue)
          {
            // We were filling in a raw packet, but we got a repeated color.
            // Emit the current packet without its last color, and start a
            // new RLE packet that starts with a length of 2.
            --packetLength;
            Flush();
            m_rlePacket = true;
            packetLength = 2;
            lastValue = color;
          }
        }

        Flush();
      }

      internal RlePacketStateMachine(Stream stream)
      {
        m_stream = stream;
      }
    }

    ////////////////////////////////////////////////////////////////////////

    public static int EncodedRow(Stream stream, byte[] imgData, int startIdx, int columns)
    {
      long startPosition = stream.Position;

      RlePacketStateMachine machine = new RlePacketStateMachine(stream);
      machine.PushRow(imgData, startIdx, startIdx + columns);

      return (int)(stream.Position - startPosition);
    }

    ////////////////////////////////////////////////////////////////////////

    public static void DecodedRow(Stream stream, byte[] imgData, int startIdx, int columns)
    {
      int count = 0;
      while (count < columns)
      {
        byte byteValue = (byte)stream.ReadByte();

        int len = (int)byteValue;
        if (len < 128)
        {
          len++;
          while (len != 0 && (startIdx + count) < imgData.Length)
          {
            byteValue = (byte)stream.ReadByte();

            imgData[startIdx + count] = byteValue;
            count++;
            len--;
          }
        }
        else if (len > 128)
        {
          // Next -len+1 bytes in the dest are replicated from next source byte.
          // (Interpret len as a negative 8-bit int.)
          len ^= 0x0FF;
          len += 2;
          byteValue = (byte)stream.ReadByte();

          while (len != 0 && (startIdx + count) < imgData.Length)
          {
            imgData[startIdx + count] = byteValue;
            count++;
            len--;
          }
        }
        else if (128 == len)
        {
          // Do nothing
        }
      }

    }
  }

}
