/////////////////////////////////////////////////////////////////////////////////
// Copyright (C) 2006, Frank Blumenberg
// 
// See License.txt for complete licensing and attribution information.
// Permission is hereby granted, free of charge, to any person obtaining a copy 
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell 
// copies of the Software, and to permit persons to whom the Software is 
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR 
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN 
// THE SOFTWARE.
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

    protected override void OnSave(Document input, System.IO.Stream output,
      SaveConfigToken token, Surface scratchSurface, ProgressEventHandler callback)
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

      resInfo.HeightUnit = ResolutionInfo.Unit.In;
      resInfo.WidthUnit = ResolutionInfo.Unit.In;

      if (input.DpuUnit == MeasurementUnit.Inch)
      {
        resInfo.HResUnit = ResolutionInfo.ResUnit.PxPerInch;
        resInfo.VResUnit = ResolutionInfo.ResUnit.PxPerInch;

        resInfo.HRes = (short)input.DpuX;
        resInfo.VRes = (short)input.DpuY;
      }
      else
      {
        resInfo.HResUnit = ResolutionInfo.ResUnit.PxPerCent;
        resInfo.VResUnit = ResolutionInfo.ResUnit.PxPerCent;


        resInfo.HRes = (short)(input.DpuX / 2.54);
        resInfo.VRes = (short)(input.DpuY / 2.54);
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
      }
      
      using (RenderArgs ra = new RenderArgs(scratchSurface))
      {
        input.Flatten(scratchSurface);
      }

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

            psdFile.BaseLayer.Channels[0].ImageData[pos] = srcPixel->R;
            psdFile.BaseLayer.Channels[1].ImageData[pos] = srcPixel->G;
            psdFile.BaseLayer.Channels[2].ImageData[pos] = srcPixel->B;
            psdFile.BaseLayer.Channels[3].ImageData[pos] = srcPixel->A;
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
        PhotoshopFile.Layer psdLayer = new PhotoshopFile.Layer(psdFile);
        BlendOpToBlendModeKey(layer.BlendOp, psdLayer);
        psdLayer.Visible = layer.Visible;

        SaveLayerPixelsContext slc = new SaveLayerPixelsContext(layer, psdFile, input, psdLayer, psdToken);
        WaitCallback waitCallback = new WaitCallback(slc.SaveLayer);
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

    public static void SaveLayerPixels(BitmapLayer layer, PsdFile psdFile,
        Document input, PhotoshopFile.Layer psdLayer, PsdSaveConfigToken psdToken)
    {      
      Surface surface = layer.Surface;

      psdLayer.Rect = FindImageRectangle(layer, psdFile, input, psdLayer);
      psdLayer.Name = layer.Name;
      psdLayer.Opacity = layer.Opacity;
      psdLayer.Visible = layer.Visible;
      psdLayer.MaskData = new PhotoshopFile.Layer.Mask(psdLayer);
      psdLayer.BlendingRangesData = new PhotoshopFile.Layer.BlendingRanges(psdLayer);

      int layerSize = psdLayer.Rect.Width * psdLayer.Rect.Height;

      for (int i = -1; i < 3; i++)
      {
        PhotoshopFile.Layer.Channel ch = new PhotoshopFile.Layer.Channel((short)i, psdLayer);

        ch.ImageCompression = psdToken.RleCompress ? ImageCompression.Rle : ImageCompression.Raw;
        ch.ImageData = new byte[layerSize];
      }

      var channels = psdLayer.ChannelsArray;
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
        document.DpuUnit = MeasurementUnit.Inch;
        document.DpuX = psdFile.Resolution.HRes;
        document.DpuY = psdFile.Resolution.VRes;
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

    private class SaveLayerPixelsContext
    {
      private BitmapLayer layer;
      private PsdFile psdFile;
      private Document input;
      private PsdSaveConfigToken psdToken;
      PhotoshopFile.Layer psdLayer;

      public SaveLayerPixelsContext(BitmapLayer layer, PsdFile psdFile,
        Document input, PhotoshopFile.Layer psdLayer, PsdSaveConfigToken psdToken)
      {
        this.layer = layer;
        this.psdFile = psdFile;
        this.input = input;
        this.psdToken = psdToken;
        this.psdLayer = psdLayer;
      }

      public void SaveLayer(object context)
      {
        SaveLayerPixels(layer, psdFile, input, psdLayer, psdToken);
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
