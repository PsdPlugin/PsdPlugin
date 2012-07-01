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
  public static class PsdSave
  {
    public static void Save(Document input, Stream output, PsdSaveConfigToken psdToken,
      Surface scratchSurface, ProgressEventHandler callback)
    {
      double renderProgress = 30.0;
      double storeProgress = 90.0;
      
      var psdFile = new PsdFile();
      psdFile.RowCount = input.Height;
      psdFile.ColumnCount = input.Width;

      // We only save in RGBA format, 8 bits per channel, which corresponds to
      // Paint.NET's internal representation.  No color mode data is necessary,
      // since PSD files default to RGB.

      psdFile.ChannelCount = 4; 
      psdFile.ColorMode = PsdColorMode.RGB;
      psdFile.BitDepth = 8;
      psdFile.Resolution = GetResolutionInfo(input);
      psdFile.ImageCompression = psdToken.RleCompress
        ? ImageCompression.Rle
        : ImageCompression.Raw;

      //-----------------------------------------------------------------------
      // Render and store the full composite image
      //-----------------------------------------------------------------------

      // Allocate space for the image data
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

      // Prepare to store image data.
      callback(null, new ProgressEventArgs(renderProgress));
      var storeProgressNotifier = new DiscreteProgressNotifier(callback,
        input.Layers.Count + 1, renderProgress, storeProgress);

      // Store composite image data.
      var channelsArray = psdFile.BaseLayer.Channels.ToIdArray();
      StoreLayerImage(channelsArray, channelsArray[3],
        scratchSurface, psdFile.BaseLayer.Rect);
      storeProgressNotifier.NotifyIncrement();

      //-----------------------------------------------------------------------
      // Store layer image data.
      //-----------------------------------------------------------------------

      var threadPool = new PaintDotNet.Threading.PrivateThreadPool();
      foreach (BitmapLayer layer in input.Layers)
      {
        var psdLayer = new PhotoshopFile.Layer(psdFile);
        psdFile.Layers.Add(psdLayer);

        var slc = new StoreLayerContext(layer, psdFile, input, psdLayer,
          psdToken, storeProgressNotifier);
        var waitCallback = new WaitCallback(slc.StoreLayer);
        threadPool.QueueUserWorkItem(waitCallback);
      }
      threadPool.Drain();

      psdFile.Save(output);
    }

    private static ResolutionInfo GetResolutionInfo(Document input)
    {
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

      return resInfo;
    }

    /// <summary>
    /// Determine the real size of the layer, i.e., the smallest rectangle
    /// that includes all non-transparent pixels.
    /// </summary>
    private static Rectangle FindImageRectangle(Surface surface)
    {
      var rectPos = new Util.RectanglePosition
      {
        Left = surface.Width,
        Top = surface.Height,
        Right = 0,
        Bottom = 0
      };

      unsafe
      {
        // Search for top non-transparent pixel
        bool fPixelFound = false;
        for (int y = 0; y < surface.Height; y++)
        {
          if (ExpandImageRectangle(surface, y, 0, surface.Width, ref rectPos))
          {
            fPixelFound = true;
            break;
          }
        }

        // Narrow down the other dimensions of the image rectangle
        if (fPixelFound)
        {
          // Search for bottom non-transparent pixel
          for (int y = surface.Height - 1; y > rectPos.Bottom; y--)
          {
            if (ExpandImageRectangle(surface, y, 0, surface.Width, ref rectPos))
              break;
          }

          // Search for left and right non-transparent pixels.  Because we
          // scan horizontally, we can't just break, but we can examine fewer
          // candidate pixels on the remaining rows.
          for (int y = rectPos.Top + 1; y < rectPos.Bottom; y++)
          {
            ExpandImageRectangle(surface, y, 0, rectPos.Left, ref rectPos);
            ExpandImageRectangle(surface, y, rectPos.Right + 1, surface.Width, ref rectPos);
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

    /// <summary>
    /// Check for non-transparent pixels in a row, or portion of a row.
    /// Expands the size of the image rectangle if any were found.
    /// </summary>
    /// <returns>True if non-transparent pixels were found, false otherwise.</returns>
    unsafe private static bool ExpandImageRectangle(Surface surface, int y,
      int xStart, int xEnd, ref Util.RectanglePosition rectPos)
    {
      bool fPixelFound = false;

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
          fPixelFound = true;
        }
        pixel++;
      }

      return fPixelFound;
    }

    /// <summary>
    /// Store layer metadata and image data.
    /// </summary>
    public static void StoreLayer(BitmapLayer layer, PsdFile psdFile,
        Document input, PhotoshopFile.Layer psdLayer, PsdSaveConfigToken psdToken)
    {
      // Set layer metadata
      psdLayer.Name = layer.Name;
      psdLayer.Rect = FindImageRectangle(layer.Surface);
      psdLayer.BlendModeKey = layer.BlendOp.ToPsdBlendMode();
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

      // Store and compress channel image data
      var channelsArray = psdLayer.Channels.ToIdArray();
      StoreLayerImage(channelsArray, psdLayer.AlphaChannel, layer.Surface, psdLayer.Rect);
    }

    /// <summary>
    /// Store and compress layer image data.
    /// </summary>
    /// <param name="channels">Destination channels.</param>
    /// <param name="alphaChannel">Destination alpha channel.</param>
    /// <param name="surface">Source image from Paint.NET.</param>
    /// <param name="rect">Image rectangle to store.</param>
    unsafe private static void StoreLayerImage(Channel[] channels, Channel alphaChannel,
      Surface surface, Rectangle rect)
    {
      unsafe
      {
        for (int y = 0; y < rect.Height; y++)
        {
          int destRowIndex = y * rect.Width;
          ColorBgra* srcRow = surface.GetRowAddress(y + rect.Top);
          ColorBgra* srcPixel = srcRow + rect.Left;

          for (int x = 0; x < rect.Width; x++)
          {
            int destIndex = destRowIndex + x;

            channels[0].ImageData[destIndex] = srcPixel->R;
            channels[1].ImageData[destIndex] = srcPixel->G;
            channels[2].ImageData[destIndex] = srcPixel->B;
            alphaChannel.ImageData[destIndex] = srcPixel->A;
            srcPixel++;
          }
        }
      }

      channels[0].CompressImageData();
      channels[1].CompressImageData();
      channels[2].CompressImageData();
      alphaChannel.CompressImageData();
    }

    private class StoreLayerContext
    {
      private BitmapLayer layer;
      private PsdFile psdFile;
      private Document input;
      private PsdSaveConfigToken psdToken;
      PhotoshopFile.Layer psdLayer;
      DiscreteProgressNotifier progress;

      public StoreLayerContext(BitmapLayer layer, PsdFile psdFile,
        Document input, PhotoshopFile.Layer psdLayer,
        PsdSaveConfigToken psdToken, DiscreteProgressNotifier progress)
      {
        this.layer = layer;
        this.psdFile = psdFile;
        this.input = input;
        this.psdToken = psdToken;
        this.psdLayer = psdLayer;
        this.progress = progress;
      }

      public void StoreLayer(object context)
      {
        PsdSave.StoreLayer(layer, psdFile, input, psdLayer, psdToken);
        progress.NotifyIncrement();
      }
    }

    private class DiscreteProgressNotifier
    {
      private ProgressEventHandler callback;
      private double totalIncrements;
      private int completedIncrements;
      private double progressStart;
      private double progressEnd;

      public DiscreteProgressNotifier(ProgressEventHandler callback,
        int numIncrements, double progressStart, double progressEnd)
      {
        this.callback = callback;
        this.totalIncrements = (double)numIncrements;
        this.progressStart = progressStart;
        this.progressEnd = progressEnd;

        this.completedIncrements = 0;
        callback(null, new ProgressEventArgs(progressStart));
      }

      /// <summary>
      /// Complete an increment.
      /// </summary>
      public void NotifyIncrement()
      {
        lock (this)
        {
          completedIncrements++;
          var progressDelta = completedIncrements / totalIncrements
            * (progressEnd - progressStart);
          var progress = progressStart + progressDelta;

          callback(null, new ProgressEventArgs(progress));
          Debug.WriteLine("Reporting save progress " + progress);
        }
      }
    }

  }

}
