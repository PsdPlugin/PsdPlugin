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
    Bitmap = 0,
    Grayscale = 1,
    Indexed = 2,
    RGB = 3,
    CMYK = 4,
    Multichannel = 7,
    Duotone = 8,
    Lab = 9
  };


  public class PsdFile
  {

    public Layer BaseLayer { get; set; }

    public ImageCompression ImageCompression { get; set; }

    ///////////////////////////////////////////////////////////////////////////

    public PsdFile()
    {
      Version = 1;
      BaseLayer = new Layer(this);
      BaseLayer.Rect = new Rectangle(0, 0, 0, 0);

      ImageResources = new List<ImageResource>();
      Layers = new List<Layer>();
    }

    ///////////////////////////////////////////////////////////////////////////

    public void Load(string fileName)
    {
      using (var stream = new FileStream(fileName, FileMode.Open))
      {
        Load(stream);
      }
    }

    public void Load(Stream stream)
    {
      var reader = new PsdBinaryReader(stream);

      LoadHeader(reader);
      LoadColorModeData(reader);
      LoadImageResources(reader);
      LoadLayerAndMaskInfo(reader);

      LoadImage(reader);
      DecompressImages();
    }

    public void Save(string fileName)
    {
      using (var stream = new FileStream(fileName, FileMode.Create))
      {
        Save(stream);
      }
    }

    public void Save(Stream stream)
    {
      if (BitDepth != 8)
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
    public Int16 Version { get; private set; }

    private Int16 channelCount;
    /// <summary>
    /// The number of channels in the image, including any alpha channels.
    /// </summary>
    public Int16 ChannelCount
    {
      get { return channelCount; }
      set
      {
        if (value < 1 || value > 56)
          throw new ArgumentException("Number of channels must be from 1 to 56.");
        channelCount = value;
      }
    }

    /// <summary>
    /// The height of the image in pixels.
    /// </summary>
    public int RowCount
    {
      get { return this.BaseLayer.Rect.Height; }
      set
      {
        if (value < 0 || value > 30000)
          throw new ArgumentException("Number of rows must be from 1 to 30000.");
        BaseLayer.Rect = new Rectangle(0, 0, BaseLayer.Rect.Width, value);
      }
    }


    /// <summary>
    /// The width of the image in pixels. 
    /// </summary>
    public int ColumnCount
    {
      get { return this.BaseLayer.Rect.Width; }
      set
      {
        if (value < 0 || value > 30000)
          throw new ArgumentException("Number of columns must be from 1 to 30000.");
        this.BaseLayer.Rect = new Rectangle(0, 0, value, this.BaseLayer.Rect.Height);
      }
    }

    private int bitDepth;
    /// <summary>
    /// The number of bits per channel. Supported values are 1, 8, 16, and 32.
    /// </summary>
    public int BitDepth
    {
      get { return bitDepth; }
      set
      {
        switch (value)
        {
          case 1:
          case 8:
          case 16:
          case 32:
            bitDepth = value;
            break;
          default:
            throw new NotImplementedException("Invalid bit depth.");
        }
      }
    }

    /// <summary>
    /// The color mode of the file.
    /// </summary>
    public PsdColorMode ColorMode { get; set; }

    ///////////////////////////////////////////////////////////////////////////

    private void LoadHeader(PsdBinaryReader reader)
    {
      Debug.WriteLine("LoadHeader started at " + reader.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      var signature = new string(reader.ReadChars(4));
      if (signature != "8BPS")
        throw new PsdInvalidException("The given stream is not a valid PSD file");

      Version = reader.ReadInt16();
      if (Version != 1)
        throw new PsdInvalidException("The PSD file has an unknown version");

      //6 bytes reserved
      reader.BaseStream.Position += 6;

      this.ChannelCount = reader.ReadInt16();
      this.RowCount = reader.ReadInt32();
      this.ColumnCount = reader.ReadInt32();
      BitDepth = reader.ReadInt16();
      ColorMode = (PsdColorMode)reader.ReadInt16();
    }

    ///////////////////////////////////////////////////////////////////////////

    private void SaveHeader(PsdBinaryWriter writer)
    {
      Debug.WriteLine("SaveHeader started at " + writer.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      string signature = "8BPS";
      writer.Write(signature.ToCharArray());
      writer.Write(Version);
      writer.Write(new byte[] { 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, });
      writer.Write(ChannelCount);
      writer.Write(RowCount);
      writer.Write(ColumnCount);
      writer.Write((Int16)BitDepth);
      writer.Write((Int16)ColorMode);
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

      var paletteLength = reader.ReadUInt32();
      if (paletteLength > 0)
      {
        ColorModeData = reader.ReadBytes((int)paletteLength);
      }
    }

    private void SaveColorModeData(PsdBinaryWriter writer)
    {
      Debug.WriteLine("SaveColorModeData started at " + writer.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      writer.Write((UInt32)ColorModeData.Length);
      writer.Write(ColorModeData);
    }

    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region ImageResources

    /// <summary>
    /// The Image resource blocks for the file
    /// </summary>
    public List<ImageResource> ImageResources { get; set; }

    public ResolutionInfo Resolution
    {
      get
      {
        return (ResolutionInfo)ImageResources.Find(
          x => x.ID == ResourceID.ResolutionInfo);
      }

      set
      {
        ImageResources.RemoveAll(x => x.ID == ResourceID.ResolutionInfo);
        ImageResources.Add(value);
      }
    }


    ///////////////////////////////////////////////////////////////////////////

    private void LoadImageResources(PsdBinaryReader reader)
    {
      Debug.WriteLine("LoadImageResources started at " + reader.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      ImageResources.Clear();

      var imageResourcesLength = reader.ReadUInt32();
      if (imageResourcesLength <= 0)
        return;

      var startPosition = reader.BaseStream.Position;
      var endPosition = startPosition + imageResourcesLength;
      while (reader.BaseStream.Position < endPosition)
      {
        var imageResource = ImageResourceFactory.CreateImageResource(reader);
        ImageResources.Add(imageResource);
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
        foreach (var imgRes in ImageResources)
          imgRes.Save(writer);
      }
    }

    #endregion

    ///////////////////////////////////////////////////////////////////////////

    #region LayerAndMaskInfo

    public List<Layer> Layers { get; private set; }

    public bool AbsoluteAlpha { get; set; }

    ///////////////////////////////////////////////////////////////////////////

    private void LoadLayerAndMaskInfo(PsdBinaryReader reader)
    {
      Debug.WriteLine("LoadLayerAndMaskInfo started at " + reader.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      var layersAndMaskLength = reader.ReadUInt32();

      if (layersAndMaskLength <= 0)
        return;

      var startPosition = reader.BaseStream.Position;

      LoadLayers(reader);
      LoadGlobalLayerMask(reader);

      //-----------------------------------------------------------------------

      // Higher bit depth images store an empty layers section for backcompat,
      // followed by the real layers section (which is undocumented but
      // appears largely identical).
      if ((this.BitDepth > 8) &&
        (reader.BaseStream.Position < startPosition + layersAndMaskLength))
      {
        var signature = new string(reader.ReadChars(8));
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

      var layersInfoSectionLength = reader.ReadUInt32();
      if (layersInfoSectionLength <= 0)
        return;

      var startPosition = reader.BaseStream.Position;
      var numLayers = reader.ReadInt16();

      // If numLayers < 0, then number of layers is absolute value,
      // and the first alpha channel contains the transparency data for
      // the merged result.
      if (numLayers < 0)
      {
        AbsoluteAlpha = true;
        numLayers = Math.Abs(numLayers);
      }

      Layers.Clear();
      if (numLayers == 0)
        return;

      for (int i = 0; i < numLayers; i++)
      {
        Layers.Add(new Layer(reader, this));
      }

      //-----------------------------------------------------------------------

      // We will load pixel data as we progress.
      // TODO: Load in parallel, queuing decompress/decoding tasks as we go.

      foreach (var layer in Layers)
      {
        foreach (var channel in layer.Channels)
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
      var threadPool = new PaintDotNet.Threading.PrivateThreadPool();

      var imageLayers = Layers.Concat(new List<Layer>() { this.BaseLayer });
      foreach (var layer in imageLayers)
      {
        foreach (var channel in layer.Channels)
        {
          Rectangle rect = (channel.ID == -2)
            ? layer.MaskData.Rect
            : layer.Rect;

          var dcc = new DecompressChannelContext(channel, rect);

          var waitCallback = new WaitCallback(dcc.DecompressChannel);
          threadPool.QueueUserWorkItem(waitCallback);
        }
      }
      threadPool.Drain();

      foreach (var layer in Layers)
      {
        if (layer.Channels.ContainsId(-2))
          layer.MaskData.ImageData = layer.Channels.GetId(-2).ImageData;
      }
    }

    /// <summary>
    /// Check the validity of the PSD file and generate necessary data.
    /// </summary>
    public void PrepareSave()
    {
      var imageLayers = Layers.Concat(new List<Layer>() { this.BaseLayer }).ToList();

      foreach (var layer in imageLayers)
      {
        layer.PrepareSave();
      }

      SetVersionInfo();
    }

    /// <summary>
    /// Set the VersionInfo resource on the file.
    /// </summary>
    public void SetVersionInfo()
    {
      var versionInfos = ImageResources.Where(x => x.ID == ResourceID.VersionInfo);
      if (versionInfos.Count() > 1)
        throw new PsdInvalidException("Image has more than one VersionInfo resource.");

      var versionInfo = (VersionInfo)versionInfos.SingleOrDefault();
      if (versionInfo == null)
      {
        versionInfo = new VersionInfo();
        ImageResources.Add(versionInfo);
      }

      // Get the version string.  We don't use the fourth part (revision).
      var assembly = System.Reflection.Assembly.GetExecutingAssembly();
      var version = assembly.GetName().Version;
      var versionString = version.Major + "." + version.Minor + "." + version.Build;

      // Strings are not localized since they are not shown to the user.
      versionInfo.Version = 1;
      versionInfo.HasRealMergedData = true;
      versionInfo.ReaderName = "Paint.NET PSD Plugin";
      versionInfo.WriterName = "Paint.NET PSD Plugin " + versionString;
      versionInfo.FileVersion = 1;
    }

    private void SaveLayers(PsdBinaryWriter writer)
    {
      Debug.WriteLine("SaveLayers started at " + writer.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      using (new PsdBlockLengthWriter(writer))
      {
        var numberOfLayers = (Int16)Layers.Count;
        if (AbsoluteAlpha)
          numberOfLayers = (Int16)(-numberOfLayers);

        writer.Write(numberOfLayers);

        foreach (var layer in Layers)
        {
          layer.Save(writer);
        }

        foreach (var layer in Layers)
        {
          foreach (var channel in layer.Channels)
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

      var maskLength = reader.ReadUInt32();
      if (maskLength <= 0)
        return;

      GlobalLayerMaskData = reader.ReadBytes((int)maskLength);
    }

    ///////////////////////////////////////////////////////////////////////////

    private void SaveGlobalLayerMask(PsdBinaryWriter writer)
    {
      Debug.WriteLine("SaveGlobalLayerMask started at " + writer.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      writer.Write((UInt32)GlobalLayerMaskData.Length);
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

      BaseLayer.Rect = new Rectangle(0, 0, ColumnCount, RowCount);
      ImageCompression = (ImageCompression)reader.ReadInt16();
      switch (ImageCompression)
      {
        case ImageCompression.Raw:
          var length = this.RowCount * Util.BytesPerRow(BaseLayer.Rect, BitDepth);
          for (Int16 i = 0; i < ChannelCount; i++)
          {
            var channel = new Channel(i, this.BaseLayer);
            channel.ImageCompression = ImageCompression;
            channel.Length = length;
            channel.ImageData = reader.ReadBytes(length);
            BaseLayer.Channels.Add(channel);
          }
          break;

        case ImageCompression.Rle:
          // Store RLE data length
          for (Int16 i = 0; i < ChannelCount; i++)
          {
            int totalRleLength = 0;
            for (int j = 0; j < RowCount; j++)
              totalRleLength += reader.ReadUInt16();

            var channel = new Channel(i, this.BaseLayer);
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
      if (ChannelCount == ColorMode.ChannelCount() + 1)
      {
        var alphaChannel = BaseLayer.Channels.Last();
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
      private Channel ch;
      private Rectangle rect;

      public DecompressChannelContext(Channel ch, Rectangle rect)
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

}
