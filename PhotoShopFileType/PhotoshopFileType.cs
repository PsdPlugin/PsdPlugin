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
using System;
using System.Collections.Generic;
using System.Text;
using System.Drawing;

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

      // we have an Alpha channel which will be saved, 
      // we have to add this to our image resources
      psdFile.Channels = 4;

      // for now we oly save the images as RGB
      psdFile.ColorMode = PsdFile.ColorModes.RGB;

      psdFile.Depth = 8;

      //-----------------------------------------------------------------------
      // no color mode Data

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
      //-----------------------------------------------------------------------

      psdFile.ImageCompression = psdToken.RleCompress ? ImageCompression.Rle : ImageCompression.Raw;

      int size = psdFile.Rows * psdFile.Columns;

      psdFile.ImageData = new byte[psdFile.Channels][];
      for (int i = 0; i < psdFile.Channels; i++)
      {
        psdFile.ImageData[i] = new byte[size];
      }

      using (Surface surface = new Surface(input.Width, input.Height))
      {
        surface.Clear(ColorBgra.FromBgra(255, 255, 255, 255));

        using (RenderArgs ra = new RenderArgs(surface))
        {
          input.Render(ra, true);
        }

        unsafe
        {
          for (int y = 0; y < psdFile.Rows; y++)
          {
            int rowIndex = y * psdFile.Columns;
            ColorBgra* srcRow = surface.GetRowAddressUnchecked(y);

            for (int x = 0; x < psdFile.Columns; x++)
            {
              int pos = rowIndex + x;

              psdFile.ImageData[0][pos] = srcRow[x].R;
              psdFile.ImageData[1][pos] = srcRow[x].G;
              psdFile.ImageData[2][pos] = srcRow[x].B;
              psdFile.ImageData[3][pos] = srcRow[x].A;
            }
          }
        }
      }

      foreach (BitmapLayer layer in input.Layers)
      {
        Surface surface = layer.Surface;

        PhotoshopFile.Layer psdLayer = new PhotoshopFile.Layer(psdFile);

        int rectLeft = input.Width;
        int rectTop = input.Height;
        int rectRight = 0;
        int rectBottom = 0;

        // Determine the real size of this layer, i.e., the smallest rectangle
        // that includes all all non-invisible pixels
        unsafe
        {
          for (int y = 0; y < psdFile.Rows; y++)
          {
            int rowIndex = y * psdFile.Columns;
            ColorBgra* srcRow = surface.GetRowAddressUnchecked(y);
            
            for (int x = 0; x < psdFile.Columns; x++)
            {
              int pos = rowIndex + x;

              // Found a non-transparent pixel, potentially increase the size of the rectangle
              if (srcRow[x].A > 0)
              {
                // Expand the rectangle
                if (x < rectLeft)
                  rectLeft = x;
                if (x > rectRight)
                  rectRight = x;
                if (y < rectTop)
                  rectTop = y;
                if (y > rectBottom)
                  rectBottom = y;
              }
            }
          }
        }

        psdLayer.Rect = new Rectangle(rectLeft, rectTop, rectRight - rectLeft + 1, rectBottom - rectTop + 1);
        psdLayer.Name = layer.Name;
        psdLayer.Opacity = layer.Opacity;
        psdLayer.Visible = layer.Visible;
        psdLayer.MaskData = new PhotoshopFile.Layer.Mask(psdLayer);
        psdLayer.BlendingRangesData = new PhotoshopFile.Layer.BlendingRanges(psdLayer);

        BlendOpToBlenModeKey(layer.BlendOp, psdLayer);

        int layerSize = psdLayer.Rect.Width * psdLayer.Rect.Height;

        for (int i = -1; i < 3; i++)
        {
          PhotoshopFile.Layer.Channel ch = new PhotoshopFile.Layer.Channel((short)i, psdLayer);

          ch.ImageCompression = ImageCompression.Raw;//psdToken.RleCompress ? ImageCompression.Rle : ImageCompression.Raw;
          ch.ImageData = new byte[layerSize];
        }

        var channels = psdLayer.ChannelsArray;
        var alphaChannel = psdLayer.AlphaChannel;

        unsafe
        {
          for (int y = 0; y < psdLayer.Rect.Height; y++)
          {
            int rowIndex = y * psdLayer.Rect.Width;
            ColorBgra* srcRow = surface.GetRowAddressUnchecked(y + psdLayer.Rect.Top);

            for (int x = 0; x < psdLayer.Rect.Width; x++)
            {
              int pos = rowIndex + x;
              int srcIndex = x + psdLayer.Rect.Left;

              channels[0].ImageData[pos] = srcRow[srcIndex].R;
              channels[1].ImageData[pos] = srcRow[srcIndex].G;
              channels[2].ImageData[pos] = srcRow[srcIndex].B;
              alphaChannel.ImageData[pos] = srcRow[srcIndex].A;
            }
          }
        }
      }

      psdFile.Save(output);
    }

    private void BlendOpToBlenModeKey(UserBlendOp op, PhotoshopFile.Layer layer)
    {

      switch (op.ToString())
      {
        case "Normal":
          layer.BlendModeKey = "norm";
          break;
        case "Multiply":
          layer.BlendModeKey = "mul ";
          break;
        case "Additive":
          layer.BlendModeKey = "norm";
          break;
        case "ColorBurn":
          layer.BlendModeKey = "div ";
          break;
        case "ColorDodge":
          layer.BlendModeKey = "idiv";
          break;
        case "Reflect":
          layer.BlendModeKey = "norm";
          break;
        case "Glow":
          layer.BlendModeKey = "norm";
          break;
        case "Overlay":
          layer.BlendModeKey = "over";
          break;
        case "Difference":
          layer.BlendModeKey = "diff";
          break;
        case "Negation":
          layer.BlendModeKey = "norm";
          break;
        case "Lighten":
          layer.BlendModeKey = "lite";
          break;
        case "Darken":
          layer.BlendModeKey = "dark";
          break;
        case "Screen":
          layer.BlendModeKey = "scrn";
          break;
        case "Xor":
          layer.BlendModeKey = "norm";
          break;
        default:
          layer.BlendModeKey = "norm";
          break;
      }
    }

    private UserBlendOp BlendModeKeyToBlendOp(PhotoshopFile.Layer layer)
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
        case "lite":
          blendOp = UserBlendOps.CreateBlendOp(typeof(UserBlendOps.LightenBlendOp));
          break;
        case "hue ":
          break;
        case "sat ":
          break;
        case "colr":
          break;
        case "lum ":
          break;
        case "mul ":
          blendOp = UserBlendOps.CreateBlendOp(typeof(UserBlendOps.MultiplyBlendOp));
          break;
        case "scrn":
          blendOp = UserBlendOps.CreateBlendOp(typeof(UserBlendOps.ScreenBlendOp));
          break;
        case "diss":
          break;
        case "over":
          blendOp = UserBlendOps.CreateBlendOp(typeof(UserBlendOps.OverlayBlendOp));
          break;
        case "hLit":
          break;
        case "sLit":
          break;
        case "diff":
          blendOp = UserBlendOps.CreateBlendOp(typeof(UserBlendOps.DifferenceBlendOp));
          break;
        case "smud":
          break;
        case "div ":
          blendOp = UserBlendOps.CreateBlendOp(typeof(UserBlendOps.ColorDodgeBlendOp));
          break;
        case "idiv":
          blendOp = UserBlendOps.CreateBlendOp(typeof(UserBlendOps.ColorBurnBlendOp));
          break;
      }
      return blendOp;
    }

    protected override Document OnLoad(System.IO.Stream input)
    {
      PsdFile psdFile = new PsdFile();

      psdFile.Load(input);

      BitmapLayer layer;
      Document document = new Document(psdFile.Columns, psdFile.Rows);

      if (psdFile.Resolution != null)
      {
        document.DpuUnit = MeasurementUnit.Inch;
        document.DpuX = psdFile.Resolution.HRes;
        document.DpuY = psdFile.Resolution.VRes;
      }

      if (psdFile.Layers.Count == 0)
      {
        layer = ImageDecoderPdn.DecodeImage(psdFile);
        document.Layers.Add(layer);
      }
      else
      {
        foreach (PhotoshopFile.Layer l in psdFile.Layers)
        {
          if (!l.Rect.IsEmpty)
          {
            layer = ImageDecoderPdn.DecodeImage(l);

            layer.Name = l.Name;
            layer.Opacity = l.Opacity;
            layer.Visible = l.Visible;

            layer.SetBlendOp(BlendModeKeyToBlendOp(l));

            document.Layers.Add(layer);
          }
        }
      }
      return document;
    }
  }
}
