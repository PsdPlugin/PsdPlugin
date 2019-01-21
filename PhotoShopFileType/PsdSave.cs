﻿/////////////////////////////////////////////////////////////////////////////////
//
// Photoshop PSD FileType Plugin for Paint.NET
// http://psdplugin.codeplex.com/
//
// This software is provided under the MIT License:
//   Copyright (c) 2006-2007 Frank Blumenberg
//   Copyright (c) 2010-2014 Tao Yue
//
// See LICENSE.txt for complete licensing and attribution information.
//
/////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PhotoshopFile;

namespace PaintDotNet.Data.PhotoshopFileType
{
  public static class PsdSave
  {
    public static void Save(Document input, Stream output, PsdSaveConfigToken psdToken,
      Surface scratchSurface, ProgressEventHandler progressCallback)
    {
      var psdVersion = ((input.Height > 30000) || (input.Width > 30000))
        ? PsdFileVersion.PsbLargeDocument
        : PsdFileVersion.Psd;
      var psdFile = new PsdFile(psdVersion);

      psdFile.RowCount = input.Height;
      psdFile.ColumnCount = input.Width;

      // We only save in RGBA format, 8 bits per channel, which corresponds to
      // Paint.NET's internal representation.

      psdFile.ChannelCount = 4; 
      psdFile.ColorMode = PsdColorMode.RGB;
      psdFile.BitDepth = 8;
      psdFile.Resolution = GetResolutionInfo(input);
      psdFile.ImageCompression = psdToken.RleCompress
        ? ImageCompression.Rle
        : ImageCompression.Raw;

      // Treat the composite image as another layer when reporting progress.
      var progress = new ProgressNotifier(progressCallback);
      var percentPerLayer = percentStoreImages
        / (input.Layers.Count + 1);

      // Render the composite image.  This operation is parallelized within
      // Paint.NET using its own private thread pool.
      using (var ra = new RenderArgs(scratchSurface))
      {
        input.Flatten(scratchSurface);
        progress.Notify(percentRenderComposite);
      }

      // Delegate to store the composite
      Action storeCompositeAction = () =>
      {
        // Allocate space for the composite image data
        int imageSize = psdFile.RowCount * psdFile.ColumnCount;
        for (short i = 0; i < psdFile.ChannelCount; i++)
        {
          var channel = new Channel(i, psdFile.BaseLayer);
          channel.ImageData = new byte[imageSize];
          channel.ImageCompression = psdFile.ImageCompression;
          psdFile.BaseLayer.Channels.Add(channel);
        }

        var channelsArray = psdFile.BaseLayer.Channels.ToIdArray();
        StoreLayerImage(channelsArray, channelsArray[3],
          scratchSurface, psdFile.BaseLayer.Rect);

        progress.Notify(percentPerLayer);
      };

      // Delegate to store the layers
      Action storeLayersAction = () =>
      {
        // LayerList is an ArrayList, so we have to cast to get a generic
        // IEnumerable that works with LINQ.
        var pdnLayers = input.Layers.Cast<BitmapLayer>();

        // Create folders/groups before actual image data will be saved.
        var layerInfos = PrepareLayers(pdnLayers, psdToken.SaveLayers);


        var psdLayers = layerInfos.AsParallel().AsOrdered().Select(pdnLayer =>
        {
          var psdLayer = new PhotoshopFile.Layer(psdFile);
          StoreLayer(pdnLayer, psdLayer, psdToken);

          progress.Notify(percentPerLayer);
          return psdLayer;
        });
        psdFile.Layers.AddRange(psdLayers);
      };

      // Process composite and layers in parallel
      Parallel.Invoke(storeCompositeAction, storeLayersAction);

      psdFile.Save(output, Encoding.Default);
    }


    /// <summary>
    /// Prepare Paint.NET's flat layer list to be exported as Photoshop layers.
    /// Indicate where layer sections begin and end and create missing layers while user can break the structure.
    /// </summary>
    private static IEnumerable<BitmapLayerInfo> PrepareLayers(IEnumerable<BitmapLayer> paintLayers, bool saveGroupLayers)
    {
      var toReturn = new List<BitmapLayerInfo>();
      var layer = paintLayers.FirstOrDefault();
      var openedGroupsCount = 0;
      var reversed = paintLayers.Reverse();
      foreach (var paintLayer in reversed)
      {
        var layerInfo = new BitmapLayerInfo();
        layerInfo.Name = paintLayer.Name;
        layerInfo.Layer = paintLayer;
        // Render as default image
        layerInfo.RenderAsRegularLayer = true;

        var isGroupLayerStart = false;
        var layerGroup = string.Empty;

        if (saveGroupLayers && CheckIsGroupLayer(paintLayer, out isGroupLayerStart, out layerGroup))
        {
          layerInfo.Name = layerGroup ?? "Layer";
          layerInfo.IsGroupStart = isGroupLayerStart;
          layerInfo.IsGroupEnd = !isGroupLayerStart;
          layerInfo.RenderAsRegularLayer = false;
          if (isGroupLayerStart)
          {
            openedGroupsCount++;
          }
          else if (layerInfo.IsGroupEnd)
          {
            if (openedGroupsCount == 0)
            {
               // Structure is broken. Close layer tag is before any other start. 
               // In this case we should render group as regular layer.
               layerInfo.RenderAsRegularLayer = true;
             }
             else
             {
               openedGroupsCount--;
             }
           }
          }

          toReturn.Add(layerInfo);
          }

          toReturn.Reverse();
          // Add missing close groups.
          if (openedGroupsCount > 0)
          {
            for (int count = 0; count < openedGroupsCount; count++)
            {
              var emptyLayer = new BitmapLayer(layer.Width, layer.Height);
              var layerInfo = new BitmapLayerInfo() { Layer = emptyLayer, IsGroupEnd = true, Name = "Layer end" };
              toReturn.Insert(0, layerInfo);
            }
          }

      return toReturn;
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

        public static bool CheckIsGroupLayer(BitmapLayer layer, out bool isStart, out string groupName)
        {
            isStart = true;
            groupName = null;
            if (layer == null || string.IsNullOrEmpty(layer.Name) || !layer.Name.Contains(':'))
            {
                return false;
            }

            var splited = layer.Name.Split(':');
            var layerPrefix = splited.FirstOrDefault().ToLower().Trim();
            if(string.IsNullOrEmpty(layerPrefix))
            {
                return false;
            }

            var nameOfTheGroup = string.Join(":", splited.Range(1, splited.Length - 1));

            // This is part of the 
            if (!string.IsNullOrEmpty(nameOfTheGroup) && nameOfTheGroup.StartsWith(" "))
            {
                nameOfTheGroup = nameOfTheGroup.Substring(1, nameOfTheGroup.Length - 1);
            }

            var startGroupPrefixes = PsdPluginResources.GetAllLayerGroupNames(PsdPluginResources.LayersPalette_LayerGroupBegin);
            if (startGroupPrefixes.Any(p => p == layerPrefix || p.StartsWith(layerPrefix)))
            {
                isStart = true;
                groupName = nameOfTheGroup;
                return true;
            }

            var endGroupPrefixes = PsdPluginResources.GetAllLayerGroupNames(PsdPluginResources.LayersPalette_LayerGroupEnd);
            if (endGroupPrefixes.Any(p => p == layerPrefix || p.StartsWith(layerPrefix)))
            {
                isStart = false;
                groupName = nameOfTheGroup;
                return true;
            }

            return false;
        }

    /// <summary>
    /// Store layer metadata and image data.
    /// </summary>
    public static void StoreLayer(
      BitmapLayerInfo layerInfo,
      PhotoshopFile.Layer psdLayer, 
      PsdSaveConfigToken psdToken)
    {
      var layer = layerInfo.Layer;

      // Set layer metadata
      psdLayer.Name = layerInfo.Name;
      psdLayer.Rect = FindImageRectangle(layer.Surface);

      psdLayer.Opacity = layer.Opacity;
      psdLayer.Masks = new MaskInfo();
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

      psdLayer.BlendModeKey = layer.BlendMode.ToPsdBlendMode();
      psdLayer.Visible = layer.Visible;

      // Create folders/groups before actual image data will be saved.
      if (psdToken.SaveLayers && !layerInfo.RenderAsRegularLayer)
      {
          if (layerInfo.IsGroupStart)
          {
              var status = layer.Visible ? LayerSectionType.OpenFolder : LayerSectionType.ClosedFolder;
              psdLayer.AdditionalInfo.Add(new LayerSectionInfo(status));
              psdLayer.Name = layerInfo.Name;
              psdLayer.Visible = true;
          }
          else if (layerInfo.IsGroupEnd)
          {
              // End of the group
              psdLayer.Opacity = 255;
              psdLayer.BlendModeKey = PsdBlendMode.PassThrough;
              psdLayer.Name = "</Layer group>";
              psdLayer.AdditionalInfo.Add(new LayerSectionInfo(LayerSectionType.SectionDivider));
          }

          return;
      }

      // Store and compress channel image data
      var channelsArray = psdLayer.Channels.ToIdArray();
      StoreLayerImage(channelsArray, psdLayer.AlphaChannel, layer.Surface, psdLayer.Rect);
    }

    /// <summary>
    /// Stores and compresses the image data for the layer.
    /// </summary>
    /// <param name="channels">Destination channels.</param>
    /// <param name="alphaChannel">Destination alpha channel.</param>
    /// <param name="surface">Source image from Paint.NET.</param>
    /// <param name="rect">Image rectangle to store.</param>
    unsafe private static void StoreLayerImage(Channel[] channels, Channel alphaChannel,
      Surface surface, Rectangle rect)
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

      Parallel.ForEach(channels, channel =>
        channel.CompressImageData()
      );
    }

    #region Progress notification

    // We only report progress to 90%, reserving 10% for writing out to disk.
    private static double percentRenderComposite = 20.0;
    private static double percentStoreImages = 70.0;

    private class ProgressNotifier
    {
      private ProgressEventHandler callback;
      private double percent;

      internal ProgressNotifier(ProgressEventHandler progressCallback)
      {
        callback = progressCallback;
        percent = 0;
      }

      internal void Notify(double percentIncrement)
      {
        lock (this)
        {
          percent += percentIncrement;
          callback.Invoke(null, new ProgressEventArgs(percent));
        }
      }
    }

    #endregion
  }

}
