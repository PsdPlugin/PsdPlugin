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

using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;

using PhotoshopFile;

namespace PaintDotNet.Data.PhotoshopFileType
{
  internal static class PsdSave
  {
    public static void Save(Document input, Stream output, PsdSaveConfigToken psdToken,
      Surface scratchSurface, ProgressEventHandler callback)
    {
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
        PsdSave.StoreLayer(layer, psdFile, input, psdLayer, psdToken);
      }
    }

  }

}
