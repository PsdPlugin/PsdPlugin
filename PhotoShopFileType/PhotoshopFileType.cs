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
      PsdSaveConfigToken psdToken = (PsdSaveConfigToken)token;
      PsdFile psdFile = new PsdFile();

      //-----------------------------------------------------------------------

      psdFile.Rows = input.Height;
      psdFile.Columns = input.Width;

      // We only save in 8 bits per channel RGBA format, which corresponds to
      // Paint.NET's internal representation.
      psdFile.Channels = 4; 
      psdFile.ColorMode = PsdColorMode.RGB;
      psdFile.Depth = 8;

      //-----------------------------------------------------------------------
      // No color mode data is necessary for RGB
      //-----------------------------------------------------------------------

      ResolutionInfo resInfo = new ResolutionInfo();

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
      
      int imageSize = psdFile.Rows * psdFile.Columns;

      psdFile.Layers.Clear();
      for (short i = 0; i < psdFile.Channels; i++)
      {
        var channel = new PhotoshopFile.Layer.Channel(i, psdFile.BaseLayer);
        channel.ImageData = new byte[imageSize];
        channel.ImageCompression = psdFile.ImageCompression;
        psdFile.BaseLayer.Channels.Add(channel);
      }
      
      using (RenderArgs ra = new RenderArgs(scratchSurface))
      {
        input.Flatten(scratchSurface);
      }

      var channelsArray = psdFile.BaseLayer.Channels.ToIdArray();
      unsafe
      {
        for (int y = 0; y < psdFile.Rows; y++)
        {
          int rowIndex = y * psdFile.Columns;
          ColorBgra* srcRow = scratchSurface.GetRowAddress(y);
          ColorBgra* srcPixel = srcRow;

          for (int x = 0; x < psdFile.Columns; x++)
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

      PaintDotNet.Threading.PrivateThreadPool threadPool = new PaintDotNet.Threading.PrivateThreadPool();
      foreach (BitmapLayer layer in input.Layers)
      {
        var psdLayer = new PhotoshopFile.Layer(psdFile);
        BlendOpToBlendModeKey(layer.BlendOp, psdLayer);
        psdLayer.Visible = layer.Visible;
        psdFile.Layers.Add(psdLayer);

        StoreLayerContext slc = new StoreLayerContext(layer, psdFile, input, psdLayer, psdToken);
        WaitCallback waitCallback = new WaitCallback(slc.StoreLayer);
        threadPool.QueueUserWorkItem(waitCallback);
      }
      threadPool.Drain();

      psdFile.Save(output);
    }

    /// <summary>
    /// Determine the real size of the layer, i.e., the smallest rectangle
    /// that includes all non-transparent pixels.
    /// </summary>
    private static Rectangle FindImageRectangle(BitmapLayer layer, PsdFile psdFile, Document input, PhotoshopFile.Layer psdLayer)
    {
      Surface surface = layer.Surface;

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
          for (int y = psdFile.Rows - 1; y > rectPos.Bottom; y--)
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

      var result = new Rectangle(rectPos.Left, rectPos.Top, rectPos.Right - rectPos.Left + 1, rectPos.Bottom - rectPos.Top + 1);
      return result;
    }

    unsafe private static bool CheckImageRow(Surface surface, int y, int xStart, int xEnd, ref Util.RectanglePosition rectPos)
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
      psdLayer.MaskData = new PhotoshopFile.Layer.Mask(psdLayer);
      psdLayer.BlendingRangesData = new PhotoshopFile.Layer.BlendingRanges(psdLayer);

      // Preserve Unicode layer name as Additional Layer Information
      var luniLayerInfo = new PhotoshopFile.Layer.AdjustmentLayerInfo("luni");
      var luniData = Encoding.BigEndianUnicode.GetBytes("\u0000\u0000" + layer.Name);
      Util.SetBigEndianInt32(luniData, 0, psdLayer.Name.Length);
      luniLayerInfo.Data = luniData;
      psdLayer.AdjustmentInfo.Add(luniLayerInfo);

      // Store channel metadata
      int layerSize = psdLayer.Rect.Width * psdLayer.Rect.Height;
      for (int i = -1; i < 3; i++)
      {
        PhotoshopFile.Layer.Channel ch = new PhotoshopFile.Layer.Channel((short)i, psdLayer);
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

    private static void BlendOpToBlendModeKey(UserBlendOp op, PhotoshopFile.Layer layer)
    {
      switch (op.ToString())
      {
        case "Normal":
          layer.BlendModeKey = "norm";
          break;
        case "Additive":
          layer.BlendModeKey = "lddg";
          break;
        case "Color Burn":
          layer.BlendModeKey = "idiv";
          break;
        case "Color Dodge":
          layer.BlendModeKey = "div ";
          break;
        case "Darken":
          layer.BlendModeKey = "dark";
          break;
        case "Difference":
          layer.BlendModeKey = "diff";
          break;
        case "Lighten":
          layer.BlendModeKey = "lite";
          break;
        case "Multiply":
          layer.BlendModeKey = "mul ";
          break;
        case "Overlay":
          layer.BlendModeKey = "over";
          break;
        case "Screen":
          layer.BlendModeKey = "scrn";
          break;

        // Paint.NET blend modes without a Photoshop equivalent are saved as Normal
        case "Glow":
          layer.BlendModeKey = "norm";
          break;
        case "Negation":
          layer.BlendModeKey = "norm";
          break;
        case "Reflect":
          layer.BlendModeKey = "norm";
          break;
        case "Xor":
          layer.BlendModeKey = "norm";
          break;
        default:
          layer.BlendModeKey = "norm";
          break;
      }
    }

    private static UserBlendOp BlendModeKeyToBlendOp(PhotoshopFile.Layer layer)
    {
      UserBlendOp blendOp = UserBlendOps.CreateBlendOp(typeof(UserBlendOps.NormalBlendOp));
      switch (layer.BlendModeKey)
      {
        case "norm":
          blendOp = UserBlendOps.CreateBlendOp(typeof(UserBlendOps.NormalBlendOp));
          break;
        case "dark":
          blendOp = UserBlendOps.CreateBlendOp(typeof(UserBlendOps.DarkenBlendOp));
          break;
        case "diff":
          blendOp = UserBlendOps.CreateBlendOp(typeof(UserBlendOps.DifferenceBlendOp));
          break;
        case "div ":
          blendOp = UserBlendOps.CreateBlendOp(typeof(UserBlendOps.ColorDodgeBlendOp));
          break;
        case "idiv":
          blendOp = UserBlendOps.CreateBlendOp(typeof(UserBlendOps.ColorBurnBlendOp));
          break;
        case "lddg":
          blendOp = UserBlendOps.CreateBlendOp(typeof(UserBlendOps.AdditiveBlendOp));
          break;
        case "lite":
          blendOp = UserBlendOps.CreateBlendOp(typeof(UserBlendOps.LightenBlendOp));
          break;
        case "mul ":
          blendOp = UserBlendOps.CreateBlendOp(typeof(UserBlendOps.MultiplyBlendOp));
          break;
        case "over":
          blendOp = UserBlendOps.CreateBlendOp(typeof(UserBlendOps.OverlayBlendOp));
          break;
        case "scrn":
          blendOp = UserBlendOps.CreateBlendOp(typeof(UserBlendOps.ScreenBlendOp));
          break;

        // Photoshop blend modes without a Paint.NET equivalent are loaded as Normal
        case "colr":
          break;
        case "diss":
          break;
        case "hLit":
          break;
        case "hue ":
          break;
        case "lbrn":
          break;
        case "lum ":
          break;
        case "sat ":
          break;
        case "sLit":
          break;
        case "smud":
          break;
      }
      return blendOp;
    }

    protected override Document OnLoad(System.IO.Stream input)
    {
      // Load and decompress Photoshop file structures
      PsdFile psdFile = new PsdFile();
      psdFile.Load(input);
      CheckSufficientMemory(psdFile);

      // Convert into Paint.NET internal representation
      Document document = new Document(psdFile.Columns, psdFile.Rows);

      if (psdFile.Resolution != null)
      {
        // PSD files always specify the resolution in DPI.  When loading and
        // saving cm, we will have to round-trip the conversion, but doubles
        // have plenty of precision to spare vs. PSD's 16/16 fixed-point.

        if ((psdFile.Resolution.HResDisplayUnit == ResolutionInfo.ResUnit.PxPerCm)
          && (psdFile.Resolution.VResDisplayUnit == ResolutionInfo.ResUnit.PxPerCm))
        {
          document.DpuUnit = MeasurementUnit.Centimeter;

          // HACK: Add 0.0005 because Paint.NET truncates to three decimal
          // places when you set DpuX and DpuY on a Document.
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
        BitmapLayer layer = ImageDecoderPdn.DecodeImage(psdFile.BaseLayer, true);
        document.Layers.Add(layer);
      }
      else
      {
        PaintDotNet.Threading.PrivateThreadPool threadPool = new PaintDotNet.Threading.PrivateThreadPool();
        var layersList = new List<Layer>();
        foreach (PhotoshopFile.Layer l in psdFile.Layers)
        {
          layersList.Add(null);
          LoadLayerContext llc = new LoadLayerContext(l, BlendModeKeyToBlendOp(l), layersList, layersList.Count - 1, false);
          WaitCallback waitCallback = new WaitCallback(llc.LoadLayer);
          threadPool.QueueUserWorkItem(waitCallback); 
        }
        threadPool.Drain();

        foreach (var layer in layersList)
        {
          document.Layers.Add(layer);
        }

      }
      return document;
    }

    private void CheckSufficientMemory(PsdFile psdFile)
    {
      // Memory for layers, plus scratch, composite, and background
      var numLayers = psdFile.Layers.Count + 2;
      if (psdFile.Layers.Count == 0)
        numLayers++;
      long numPixels = psdFile.Columns * psdFile.Rows;
      ulong bytesRequired = (ulong)(4 * numPixels * numLayers);

      // Check that the file will fit entirely into physical memory.
      // Otherwise, we will thrash and make the Windows UI nonresponsive.
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
      UserBlendOp blendOp;
      List<Layer> layersList;
      int idxLayersList;
      bool isBackground;

      public LoadLayerContext(PhotoshopFile.Layer psdLayer, UserBlendOp blendOp,
        List<Layer> layersList, int idxLayersList, bool isBackground)
      {
        this.psdLayer = psdLayer;
        this.blendOp = blendOp;
        this.layersList = layersList;
        this.idxLayersList = idxLayersList;
        this.isBackground = isBackground;
      }

      public void LoadLayer(object context)
      {
        var layer = ImageDecoderPdn.DecodeImage(psdLayer, isBackground);
        layer.Name = psdLayer.Name;
        layer.Opacity = psdLayer.Opacity;
        layer.Visible = psdLayer.Visible;
        layer.SetBlendOp(blendOp);

        layersList[idxLayersList] = layer;
      }
    }
  }

}
