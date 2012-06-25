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

using PaintDotNet;
using PaintDotNet.Threading;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Threading;

using PhotoshopFile;

namespace PaintDotNet.Data.PhotoshopFileType
{
  public class PhotoshopFileTypes
      : IFileTypeFactory
  {
    public static readonly FileType Psd = new PhotoshopFileType();

    private static FileType[] fileTypes = new FileType[] { Psd };

    public FileType[] GetFileTypeInstances()
    {
      return (FileType[])fileTypes.Clone();
    }
  }


  public class PhotoshopFileType : FileType
  {
    public PhotoshopFileType()
      : base("Photoshop",
             FileTypeFlags.SupportsLoading |
               FileTypeFlags.SupportsSaving |
               FileTypeFlags.SavesWithProgress |
               FileTypeFlags.SupportsLayers,
             new string[] { ".psd" })
    {
    }

    public override SaveConfigWidget CreateSaveConfigWidget()
    {
      return new PsdSaveConfigWidget();
    }

    protected override SaveConfigToken OnCreateDefaultSaveConfigToken()
    {
      return new PsdSaveConfigToken(true);
    }

    protected override void OnSave(Document input, System.IO.Stream output, SaveConfigToken token, Surface scratchSurface, ProgressEventHandler callback)
    {
      var psdToken = (PsdSaveConfigToken)token;
      var psdFile = new PsdFile();

      //-----------------------------------------------------------------------

      psdFile.RowCount = input.Height;
      psdFile.ColumnCount = input.Width;

      // We only save in 8 bits per channel RGBA format, which corresponds to
      // Paint.NET's internal representation.
      psdFile.ChannelCount = 4; 
      psdFile.ColorMode = PsdColorMode.RGB;
      psdFile.BitDepth = 8;

      //-----------------------------------------------------------------------
      // No color mode data is necessary for RGB
      //-----------------------------------------------------------------------

      var resInfo = new ResolutionInfo();

      resInfo.HeightDisplayUnit = ResolutionInfo.Unit.Inches;
      resInfo.WidthDisplayUnit = ResolutionInfo.Unit.Inches;

      if (input.DpuUnit == MeasurementUnit.Inch)
      {
        resInfo.HResDisplayUnit = ResolutionInfo.ResUnit.PxPerInch;
        resInfo.VResDisplayUnit = ResolutionInfo.ResUnit.PxPerInch;

        resInfo.HDpi = new UFixed16_16(input.DpuX);
        resInfo.VDpi = new UFixed16_16(input.DpuY);
      }
      else
      {
        resInfo.HResDisplayUnit = ResolutionInfo.ResUnit.PxPerCm;
        resInfo.VResDisplayUnit = ResolutionInfo.ResUnit.PxPerCm;

        // Always stored as pixels/inch even if the display unit is
        // pixels/centimeter.
        resInfo.HDpi = new UFixed16_16(input.DpuX * 2.54);
        resInfo.VDpi = new UFixed16_16(input.DpuY * 2.54);
      }

      psdFile.Resolution = resInfo;
      psdFile.ImageCompression = psdToken.RleCompress ? ImageCompression.Rle : ImageCompression.Raw;

      //-----------------------------------------------------------------------
      // Set document image data from the fully-rendered image
      //-----------------------------------------------------------------------
      
      int imageSize = psdFile.RowCount * psdFile.ColumnCount;

      psdFile.Layers.Clear();
      for (short i = 0; i < psdFile.ChannelCount; i++)
      {
        var channel = new Channel(i, psdFile.BaseLayer);
        channel.ImageData = new byte[imageSize];
        channel.ImageCompression = psdFile.ImageCompression;
        psdFile.BaseLayer.Channels.Add(channel);
      }
      
      using (var ra = new RenderArgs(scratchSurface))
      {
        input.Flatten(scratchSurface);
      }

      var channelsArray = psdFile.BaseLayer.Channels.ToIdArray();
      unsafe
      {
        for (int y = 0; y < psdFile.RowCount; y++)
        {
          int rowIndex = y * psdFile.ColumnCount;
          ColorBgra* srcRow = scratchSurface.GetRowAddress(y);
          ColorBgra* srcPixel = srcRow;

          for (int x = 0; x < psdFile.ColumnCount; x++)
          {
            int pos = rowIndex + x;

            channelsArray[0].ImageData[pos] = srcPixel->R;
            channelsArray[1].ImageData[pos] = srcPixel->G;
            channelsArray[2].ImageData[pos] = srcPixel->B;
            channelsArray[3].ImageData[pos] = srcPixel->A;
            srcPixel++;
          }
        }
      }

      //-----------------------------------------------------------------------
      // Set the image data for all the layers
      //-----------------------------------------------------------------------

      var threadPool = new PaintDotNet.Threading.PrivateThreadPool();
      foreach (BitmapLayer layer in input.Layers)
      {
        var psdLayer = new PhotoshopFile.Layer(psdFile);
        psdLayer.BlendModeKey = layer.BlendOp.ToPsdBlendMode();
        psdLayer.Visible = layer.Visible;
        psdFile.Layers.Add(psdLayer);

        var slc = new StoreLayerContext(layer, psdFile, input, psdLayer, psdToken);
        var waitCallback = new WaitCallback(slc.StoreLayer);
        threadPool.QueueUserWorkItem(waitCallback);
      }
      threadPool.Drain();

      psdFile.Save(output);
    }

    /// <summary>
    /// Determine the real size of the layer, i.e., the smallest rectangle
    /// that includes all non-transparent pixels.
    /// </summary>
    private static Rectangle FindImageRectangle(BitmapLayer layer,
      PsdFile psdFile, Document input, PhotoshopFile.Layer psdLayer)
    {
      var surface = layer.Surface;

      var rectPos = new Util.RectanglePosition
      {
        Left = input.Width,
        Top = input.Height,
        Right = 0,
        Bottom = 0
      };

      unsafe
      {
        // Search for top non-transparent pixel
        bool fFound = false;
        for (int y = 0; y < input.Height; y++)
        {
          if (CheckImageRow(surface, y, 0, input.Width, ref rectPos))
          {
            fFound = true;
            break;
          }
        }

        // If layer is non-empty, then search the remaining space to expand
        // the rectangle as necessary.
        if (fFound)
        {
          // Search for bottom non-transparent pixel
          for (int y = psdFile.RowCount - 1; y > rectPos.Bottom; y--)
          {
            if (CheckImageRow(surface, y, 0, input.Width, ref rectPos))
              break;
          }

          // Search for left and right non-transparent pixels
          for (int y = rectPos.Top + 1; y < rectPos.Bottom; y++)
          {
            CheckImageRow(surface, y, 0, rectPos.Left, ref rectPos);
            CheckImageRow(surface, y, rectPos.Right + 1, input.Width, ref rectPos);
          }
        }
        else
        {
          rectPos.Left = 0;
          rectPos.Top = 0;
        }
      }

      Debug.Assert(rectPos.Left <= rectPos.Right);
      Debug.Assert(rectPos.Top <= rectPos.Bottom);

      var result = new Rectangle(rectPos.Left, rectPos.Top,
        rectPos.Right - rectPos.Left + 1, rectPos.Bottom - rectPos.Top + 1);
      return result;
    }

    unsafe private static bool CheckImageRow(Surface surface, int y,
      int xStart, int xEnd, ref Util.RectanglePosition rectPos)
    {
      bool fFound = false;

      ColorBgra* rowStart = surface.GetRowAddress(y);
      ColorBgra* pixel = rowStart + xStart;
      for (int x = xStart; x < xEnd; x++)
      {
        if (pixel->A > 0)
        {
          // Expand the rectangle to include the specified point.  
          if (x < rectPos.Left)
            rectPos.Left = x;
          if (x > rectPos.Right)
            rectPos.Right = x;
          if (y < rectPos.Top)
            rectPos.Top = y;
          if (y > rectPos.Bottom)
            rectPos.Bottom = y;
          fFound = true;
        }
        pixel++;
      }

      return fFound;
    }

    public static void StoreLayer(BitmapLayer layer, PsdFile psdFile,
        Document input, PhotoshopFile.Layer psdLayer, PsdSaveConfigToken psdToken)
    {      
      Surface surface = layer.Surface;

      // Set layer metadata
      psdLayer.Rect = FindImageRectangle(layer, psdFile, input, psdLayer);
      psdLayer.Name = layer.Name;
      psdLayer.Opacity = layer.Opacity;
      psdLayer.Visible = layer.Visible;
      psdLayer.MaskData = new Mask(psdLayer);
      psdLayer.BlendingRangesData = new BlendingRanges(psdLayer);

      // Store channel metadata
      int layerSize = psdLayer.Rect.Width * psdLayer.Rect.Height;
      for (int i = -1; i < 3; i++)
      {
        var ch = new Channel((short)i, psdLayer);
        ch.ImageCompression = psdToken.RleCompress ? ImageCompression.Rle : ImageCompression.Raw;
        ch.ImageData = new byte[layerSize];
        psdLayer.Channels.Add(ch);
      }

      // Store image data into channels
      var channels = psdLayer.Channels.ToIdArray();
      var alphaChannel = psdLayer.AlphaChannel;
      unsafe
      {
        int rowIndex = 0;
        for (int y = 0; y < psdLayer.Rect.Height; y++)
        {
          ColorBgra* srcRow = surface.GetRowAddress(y + psdLayer.Rect.Top);
          ColorBgra* srcPixel = srcRow + psdLayer.Rect.Left;

          for (int x = 0; x < psdLayer.Rect.Width; x++)
          {
            int pos = rowIndex + x;

            channels[0].ImageData[pos] = srcPixel->R;
            channels[1].ImageData[pos] = srcPixel->G;
            channels[2].ImageData[pos] = srcPixel->B;
            alphaChannel.ImageData[pos] = srcPixel->A;
            srcPixel++;
          }
          rowIndex += psdLayer.Rect.Width;
        }
      }
    }

    protected override Document OnLoad(System.IO.Stream input)
    {
      // Load and decompress Photoshop file structures
      var psdFile = new PsdFile();
      psdFile.Load(input);
      CheckSufficientMemory(psdFile);

      // Convert into Paint.NET internal representation
      var document = new Document(psdFile.ColumnCount, psdFile.RowCount);

      if (psdFile.Resolution != null)
      {
        // PSD files always specify the resolution in DPI.  When loading and
        // saving cm, we will have to round-trip the conversion, but doubles
        // have plenty of precision to spare vs. PSD's 16/16 fixed-point.

        if ((psdFile.Resolution.HResDisplayUnit == ResolutionInfo.ResUnit.PxPerCm)
          && (psdFile.Resolution.VResDisplayUnit == ResolutionInfo.ResUnit.PxPerCm))
        {
          document.DpuUnit = MeasurementUnit.Centimeter;

          // HACK: Paint.NET truncates DpuX and DpuY to three decimal places,
          // so add 0.0005 to get a rounded value instead.
          document.DpuX = psdFile.Resolution.HDpi / 2.54 + 0.0005;
          document.DpuY = psdFile.Resolution.VDpi / 2.54 + 0.0005;
        }
        else
        {
          document.DpuUnit = MeasurementUnit.Inch;
          document.DpuX = psdFile.Resolution.HDpi;
          document.DpuY = psdFile.Resolution.VDpi;
        }
      }

      if (psdFile.Layers.Count == 0)
      {
        psdFile.BaseLayer.CreateMissingChannels();
        var layer = ImageDecoderPdn.DecodeImage(psdFile.BaseLayer, true);
        document.Layers.Add(layer);
      }
      else
      {
        var threadPool = new PaintDotNet.Threading.PrivateThreadPool();
        var pdnLayers = new Layer[psdFile.Layers.Count];

        for (int i = 0; i < psdFile.Layers.Count; i++)
        {
          var psdLayer = psdFile.Layers[i];
          psdLayer.CreateMissingChannels();

          var context = new LoadLayerContext(psdLayer, pdnLayers, i);
          var waitCallback = new WaitCallback(context.LoadLayer);
          threadPool.QueueUserWorkItem(waitCallback);
        }
        threadPool.Drain();

        document.Layers.AddRange(pdnLayers);
      }
      return document;
    }

    /// <summary>
    /// Verify that the PSD file will fit into physical memory once loaded
    /// and converted to Paint.NET format.
    /// 
    /// <remarks>
    /// This check is necessary because layers in Paint.NET have the same
    /// dimensions as the canvas.  Thus, PSD files that contain lots of
    /// tiny adjustment layers may blow up in size by several
    /// orders of magnitude.</remarks>
    /// </summary>
    private void CheckSufficientMemory(PsdFile psdFile)
    {
      // Memory for layers, plus scratch, composite, and background
      var numLayers = psdFile.Layers.Count + 2;
      if (psdFile.Layers.Count == 0)
        numLayers++;
      long numPixels = psdFile.ColumnCount * psdFile.RowCount;
      ulong bytesRequired = (ulong)(4 * numPixels * numLayers);

      // Check that the file will fit entirely into physical memory.
      // Otherwise, we will thrash and make the Paint.NET UI nonresponsive.
      var computerInfo = new Microsoft.VisualBasic.Devices.ComputerInfo();
      if (bytesRequired > computerInfo.TotalPhysicalMemory)
        throw new OutOfMemoryException();
    }

    private class StoreLayerContext
    {
      private BitmapLayer layer;
      private PsdFile psdFile;
      private Document input;
      private PsdSaveConfigToken psdToken;
      PhotoshopFile.Layer psdLayer;

      public StoreLayerContext(BitmapLayer layer, PsdFile psdFile,
        Document input, PhotoshopFile.Layer psdLayer, PsdSaveConfigToken psdToken)
      {
        this.layer = layer;
        this.psdFile = psdFile;
        this.input = input;
        this.psdToken = psdToken;
        this.psdLayer = psdLayer;
      }

      public void StoreLayer(object context)
      {
        PhotoshopFileType.StoreLayer(layer, psdFile, input, psdLayer, psdToken);
      }
    }

    private class LoadLayerContext
    {
      PhotoshopFile.Layer psdLayer;

      Layer[] pdnLayers;
      int idxPdnLayer;

      public LoadLayerContext(PhotoshopFile.Layer psdLayer,
        Layer[] pdnLayers, int idxPdnLayer)
      {
        this.psdLayer = psdLayer;
        this.pdnLayers = pdnLayers;
        this.idxPdnLayer = idxPdnLayer;
      }

      public void LoadLayer(object context)
      {
        var pdnLayer = ImageDecoderPdn.DecodeImage(psdLayer, isBackground: false);
        pdnLayer.Name = psdLayer.Name;
        pdnLayer.Opacity = psdLayer.Opacity;
        pdnLayer.Visible = psdLayer.Visible;
        pdnLayer.SetBlendOp(BlendOpMapping.FromPsdBlendMode(psdLayer.BlendModeKey));

        pdnLayers[idxPdnLayer] = pdnLayer;
      }
    }
  }

}
