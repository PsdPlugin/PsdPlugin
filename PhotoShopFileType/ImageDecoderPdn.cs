/////////////////////////////////////////////////////////////////////////////////
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

using PaintDotNet;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;

using PhotoshopFile;

namespace PaintDotNet.Data.PhotoshopFileType
{
  public static class ImageDecoderPdn
  {
    private class DecodeContext
    {
      public PhotoshopFile.Layer Layer { get; private set; }
      public int ByteDepth { get; private set; }
      public Channel[] Channels { get; private set; }
      public Channel AlphaChannel { get; private set; }
      public PsdColorMode ColorMode { get; private set; }
      public byte[] ColorModeData { get; private set; }

      public Rectangle Rectangle { get; private set; }
      public MaskDecodeContext LayerMaskContext { get; private set; }
      public MaskDecodeContext UserMaskContext { get; private set; }

      public DecodeContext(PhotoshopFile.Layer layer, Rectangle bounds)
      {
        Layer = layer;
        ByteDepth = Util.BytesFromBitDepth(layer.PsdFile.BitDepth);
        Channels = layer.Channels.ToIdArray();
        AlphaChannel = layer.AlphaChannel;
        ColorMode = layer.PsdFile.ColorMode;
        ColorModeData = layer.PsdFile.ColorModeData;

        // Clip the layer to the specified bounds
        Rectangle = Layer.Rect.IntersectWith(bounds);

        if (layer.Masks != null)
        {
          LayerMaskContext = GetMaskContext(layer.Masks.LayerMask);
          UserMaskContext = GetMaskContext(layer.Masks.UserMask);
        }
      }

      private MaskDecodeContext GetMaskContext(Mask mask)
      {
        if ((mask == null) || (mask.Disabled))
        {
          return null;
        }

        return new MaskDecodeContext(mask, this);
      }
    }

    private class MaskDecodeContext
    {
      public Mask Mask { get; private set; }
      public Rectangle Rectangle { get; private set; }
      public byte[] AlphaBuffer { get; private set; }

      public MaskDecodeContext(Mask mask, DecodeContext layerContext)
      {
        Mask = mask;

        // The PositionVsLayer flag is documented to indicate a position
        // relative to the layer, but Photoshop treats the position as
        // absolute.  So that's what we do, too.
        Rectangle = mask.Rect.IntersectWith(layerContext.Rectangle);
        AlphaBuffer = new byte[layerContext.Rectangle.Width];
      }

      public bool IsRowEmpty(int row)
      {
        return (Mask.ImageData == null)
          || (Mask.ImageData.Length == 0)
          || (Rectangle.Size.IsEmpty)
          || (row < Rectangle.Top)
          || (row >= Rectangle.Bottom);
      }
    }

    /// <summary>
    /// Decode image from Photoshop's channel-separated formats to BGRA.
    /// </summary>
    public static unsafe void DecodeImage(BitmapLayer pdnLayer,
      PhotoshopFile.Layer psdLayer)
    {
      var decodeContext = new DecodeContext(psdLayer, pdnLayer.Bounds);
      DecodeDelegate decoder = null;

      if (decodeContext.ByteDepth == 4)
        decoder = GetDecodeDelegate32(decodeContext.ColorMode);
      else
        decoder = GetDecodeDelegate(decodeContext.ColorMode);

      DecodeImage(pdnLayer, decodeContext, decoder);
    }

    private unsafe delegate void DecodeDelegate(
      ColorBgra* pDestStart, ColorBgra* pDestEnd,
      int idxSrc, DecodeContext context);

    private static unsafe DecodeDelegate GetDecodeDelegate(PsdColorMode psdColorMode)
    {
      switch (psdColorMode)
      {
        case PsdColorMode.Bitmap:
          return SetPDNRowBitmap;
        case PsdColorMode.Grayscale:
        case PsdColorMode.Duotone:
          return SetPDNRowGrayscale;
        case PsdColorMode.Indexed:
          return SetPDNRowIndexed;
        case PsdColorMode.RGB:
          return SetPDNRowRgb;
        case PsdColorMode.CMYK:
          return SetPDNRowCmyk;
        case PsdColorMode.Lab:
          return SetPDNRowLab;
        case PsdColorMode.Multichannel:
          throw new Exception("Cannot decode multichannel.");
        default:
          throw new Exception("Unknown color mode.");
      }
    }

    private static unsafe DecodeDelegate GetDecodeDelegate32(PsdColorMode psdColorMode)
    {
      switch (psdColorMode)
      {
        case PsdColorMode.Grayscale:
          return SetPDNRowGrayscale32;
        case PsdColorMode.RGB:
          return SetPDNRowRgb32;
        default:
          throw new PsdInvalidException(
            "32-bit HDR images must be either RGB or grayscale.");
      }
    }

    /// <summary>
    /// Decode image from Photoshop's channel-separated formats to BGRA,
    /// using the specified decode delegate on each row.
    /// </summary>
    private static unsafe void DecodeImage(BitmapLayer pdnLayer,
      DecodeContext decodeContext, DecodeDelegate decoder)
    {
      var psdLayer = decodeContext.Layer;
      var surface = pdnLayer.Surface;
      var rect = decodeContext.Rectangle;

      // Convert rows from the Photoshop representation, writing the
      // resulting ARGB values to to the Paint.NET Surface.

      for (int y = rect.Top; y < rect.Bottom; y++)
      {
        // Calculate index into ImageData source from row and column.
        int idxSrcPixel = (y - psdLayer.Rect.Top) * psdLayer.Rect.Width
          + (rect.Left - psdLayer.Rect.Left);
        int idxSrcByte = idxSrcPixel * decodeContext.ByteDepth;

        // Calculate pointers to destination Surface.
        var pDestRow = surface.GetRowAddress(y);
        var pDestStart = pDestRow + decodeContext.Rectangle.Left;
        var pDestEnd = pDestRow + decodeContext.Rectangle.Right;

        // For 16-bit images, take the higher-order byte from the image
        // data, which is now in little-endian order.
        if (decodeContext.ByteDepth == 2)
          idxSrcByte++;

        // Decode the color and alpha channels
        decoder(pDestStart, pDestEnd, idxSrcByte, decodeContext);
        SetPDNAlphaRow(pDestStart, pDestEnd, idxSrcByte,
          decodeContext.ByteDepth, decodeContext.AlphaChannel);

        // Apply layer masks(s) to the alpha channel
        var layerMaskAlphaRow = GetMaskAlphaRow(y,
          decodeContext, decodeContext.LayerMaskContext);
        var userMaskAlphaRow = GetMaskAlphaRow(y,
          decodeContext, decodeContext.UserMaskContext);
        ApplyPDNMask(pDestStart, pDestEnd, layerMaskAlphaRow, userMaskAlphaRow);
      }
    }

    /////////////////////////////////////////////////////////////////////////// 

    unsafe private static void SetPDNAlphaRow(
      ColorBgra* pDestStart, ColorBgra* pDestEnd, int idxSrc, int byteDepth,
      Channel alphaChannel)
    {
      // Set alpha to fully-opaque if there is no alpha channel
      if (alphaChannel == null)
      {
        ColorBgra* pDest = pDestStart;
        while (pDest < pDestEnd)
        {
          pDest->A = 255;
          pDest++;
        }
      }
      // Set the alpha channel data
      else
      {
        fixed (byte* pSrcAlphaChannel = &alphaChannel.ImageData[0])
        {
          ColorBgra* pDest = pDestStart;
          byte* pSrcAlpha = pSrcAlphaChannel + idxSrc;
          while (pDest < pDestEnd)
          {
            pDest->A = (byteDepth < 4)
              ? *pSrcAlpha
              : RGBByteFromHDRFloat(pSrcAlpha);

            pDest++;
            pSrcAlpha += byteDepth;
          }
        }
      }
    }

    /////////////////////////////////////////////////////////////////////////// 

    /// <summary>
    /// Gets one row of alpha values from the mask.
    /// </summary>
    /// <param name="y">The y-coordinate of the row.</param>
    /// <param name="layerContext">The decode context for the layer containing
    ///   the mask.</param>
    /// <param name="maskContext">The decode context for the mask.</param>
    /// <returns>An array of alpha values for the row, corresponding to the
    ///   width of the layer decode context.</returns>
    unsafe private static byte[] GetMaskAlphaRow(
      int y, DecodeContext layerContext, MaskDecodeContext maskContext)
    {
      if (maskContext == null)
      {
        return null;
      }
      var mask = maskContext.Mask;

      // Background color for areas not covered by the mask
      byte backgroundColor = mask.InvertOnBlend
        ? (byte)(255 - mask.BackgroundColor)
        : mask.BackgroundColor;
      fixed (byte* pAlpha = &maskContext.AlphaBuffer[0])
      {
        Util.Fill(pAlpha, pAlpha + maskContext.AlphaBuffer.Length,
          backgroundColor);
      }
      if (maskContext.IsRowEmpty(y))
      {
        return maskContext.AlphaBuffer;
      }
      
      //////////////////////////////////////
      // Transfer mask into the alpha array
      fixed (byte* pAlphaRow = &maskContext.AlphaBuffer[0],
        pMaskData = &mask.ImageData[0])
      {
        // Get pointers to starting positions
        int alphaColumn = maskContext.Rectangle.X - layerContext.Rectangle.X;
        byte* pAlpha = pAlphaRow + alphaColumn;
        byte* pAlphaEnd = pAlpha + maskContext.Rectangle.Width;

        int maskRow = y - maskContext.Mask.Rect.Y;
        int maskColumn = maskContext.Rectangle.X - maskContext.Mask.Rect.X;
        int idxMaskPixel = (maskRow * mask.Rect.Width) + maskColumn;
        byte* pMask = pMaskData + idxMaskPixel * layerContext.ByteDepth;

        // Take the high-order byte if values are 16-bit (little-endian)
        if (layerContext.ByteDepth == 2)
          pMask++;

        // Decode mask into the alpha array.
        if (layerContext.ByteDepth == 4)
        {
          DecodeMaskAlphaRow32(pAlpha, pAlphaEnd, pMask);
        }
        else
        {
          DecodeMaskAlphaRow(pAlpha, pAlphaEnd, pMask, layerContext.ByteDepth);
        }

        // Obsolete since Photoshop CS6, but retained for compatibility with
        // older versions.  Note that the background has already been inverted.
        if (mask.InvertOnBlend)
        {
          Util.Invert(pAlpha, pAlphaEnd);
        }
      }

      return maskContext.AlphaBuffer;
    }

    private static unsafe void DecodeMaskAlphaRow32(
      byte* pAlpha, byte* pAlphaEnd, byte* pMask)
    {
      while (pAlpha < pAlphaEnd)
      {
        *pAlpha = RGBByteFromHDRFloat(pMask);

        pAlpha++;
        pMask += 4;
      }
    }

    private static unsafe void DecodeMaskAlphaRow(
      byte* pAlpha, byte* pAlphaEnd, byte* pMask, int byteDepth)
    {
      while (pAlpha < pAlphaEnd)
      {
        *pAlpha = *pMask;

        pAlpha++;
        pMask += byteDepth;
      }
    }

    /////////////////////////////////////////////////////////////////////////// 

    private static unsafe void ApplyPDNMask(ColorBgra* pDestStart, ColorBgra* pDestEnd,
      byte[] layerMaskAlpha, byte[] userMaskAlpha)
    {
      // Do nothing if there are no masks
      if ((layerMaskAlpha == null) && (userMaskAlpha == null))
        return;

      // Apply one mask
      else if ((layerMaskAlpha == null) || (userMaskAlpha == null))
      {
        var maskAlpha = layerMaskAlpha ?? userMaskAlpha;
        fixed (byte* pMaskAlpha = &maskAlpha[0])
        {
          var pDest = pDestStart;
          var pMask = pMaskAlpha;
          while (pDest < pDestEnd)
          {
            pDest->A = (byte)(pDest->A * *pMask / 255);
            pDest++;
            pMask++;
          }
        }
      }

      // Apply both masks in one pass, to minimize rounding error
      else
      {
        fixed (byte* pLayerMaskAlpha = &layerMaskAlpha[0],
          pUserMaskAlpha = &userMaskAlpha[0])
        {
          var pDest = pDestStart;
          var pMask1 = pLayerMaskAlpha;
          var pMask2 = pUserMaskAlpha;
          while (pDest < pDestEnd)
          {
            var alphaFactor = (*pMask1) * (*pMask2);
            pDest->A = (byte)(pDest->A * alphaFactor / 65025);

            pDest++;
            pMask1++;
            pMask2++;
          }
        }
      }


    }

    /////////////////////////////////////////////////////////////////////////// 

    #region Decode 32-bit HDR channels

    private static unsafe void SetPDNRowRgb32(ColorBgra* pDest, ColorBgra* pDestEnd,
      int idxSrc, DecodeContext context)
    {
      fixed (byte* pSrcRedChannel = &context.Channels[0].ImageData[0],
        pSrcGreenChannel = &context.Channels[1].ImageData[0],
        pSrcBlueChannel = &context.Channels[2].ImageData[0])
      {
        while (pDest < pDestEnd)
        {
          pDest->R = RGBByteFromHDRFloat(pSrcRedChannel + idxSrc);
          pDest->G = RGBByteFromHDRFloat(pSrcGreenChannel + idxSrc);
          pDest->B = RGBByteFromHDRFloat(pSrcBlueChannel + idxSrc);

          pDest++;
          idxSrc += 4;
        }
      }
    }

    private static unsafe void SetPDNRowGrayscale32(ColorBgra* pDest, ColorBgra* pDestEnd,
      int idxSrc, DecodeContext context)
    {
      fixed (byte* channelPtr = &context.Channels[0].ImageData[0])
      {
        while (pDest < pDestEnd)
        {
          byte* pSource = channelPtr + idxSrc;
          byte rgbValue = RGBByteFromHDRFloat(pSource);
          pDest->R = rgbValue;
          pDest->G = rgbValue;
          pDest->B = rgbValue;

          pDest++;
          idxSrc += 4;
        }
      }
    }

    #endregion

    /////////////////////////////////////////////////////////////////////////// 

    #region Decode 8-bit and 16-bit channels

    private static unsafe void SetPDNRowRgb(ColorBgra* pDest, ColorBgra* pDestEnd,
      int idxSrc, DecodeContext context)
    {
      while (pDest < pDestEnd)
      {
        pDest->R = context.Channels[0].ImageData[idxSrc];
        pDest->G = context.Channels[1].ImageData[idxSrc];
        pDest->B = context.Channels[2].ImageData[idxSrc];

        pDest++;
        idxSrc += context.ByteDepth;
      }
    }

    ///////////////////////////////////////////////////////////////////////////////
    //
    // The color-conversion formulas come from the Colour Space Conversions FAQ:
    //     http://www.poynton.com/PDFs/coloureq.pdf
    //
    // RGB --> CMYK                              CMYK --> RGB
    // ---------------------------------------   --------------------------------------------
    // Black   = minimum(1-Red,1-Green,1-Blue)   Red   = 1-minimum(1,Cyan*(1-Black)+Black)
    // Cyan    = (1-Red-Black)/(1-Black)         Green = 1-minimum(1,Magenta*(1-Black)+Black)
    // Magenta = (1-Green-Black)/(1-Black)       Blue  = 1-minimum(1,Yellow*(1-Black)+Black)
    // Yellow  = (1-Blue-Black)/(1-Black)
    //
    ///////////////////////////////////////////////////////////////////////////////

    private static unsafe void SetPDNRowCmyk(ColorBgra* pDest, ColorBgra* pDestEnd,
      int idxSrc, DecodeContext context)
    {
      while (pDest < pDestEnd)
      {
        // CMYK values are stored as complements, presumably to allow for some
        // measure of compatibility with RGB-only applications.
        var C = 255 - context.Channels[0].ImageData[idxSrc];
        var M = 255 - context.Channels[1].ImageData[idxSrc];
        var Y = 255 - context.Channels[2].ImageData[idxSrc];
        var K = 255 - context.Channels[3].ImageData[idxSrc];

        int nRed = 255 - Math.Min(255, C * (255 - K) / 255 + K);
        int nGreen = 255 - Math.Min(255, M * (255 - K) / 255 + K);
        int nBlue = 255 - Math.Min(255, Y * (255 - K) / 255 + K);

        pDest->R = (byte)nRed;
        pDest->G = (byte)nGreen;
        pDest->B = (byte)nBlue;

        pDest++;
        idxSrc += context.ByteDepth;
      }
    }

    private static unsafe void SetPDNRowBitmap(ColorBgra* pDest, ColorBgra* pDestEnd,
      int idxSrc, DecodeContext context)
    {
      var bitmap = context.Channels[0].ImageData;
      while (pDest < pDestEnd)
      {
        byte mask = (byte)(0x80 >> (idxSrc % 8));
        byte bwValue = (byte)(bitmap[idxSrc / 8] & mask);
        bwValue = (bwValue == 0) ? (byte)255 : (byte)0;

        pDest->R = bwValue;
        pDest->G = bwValue;
        pDest->B = bwValue;

        pDest++;
        idxSrc += context.ByteDepth;
      }
    }

    private static unsafe void SetPDNRowGrayscale(ColorBgra* pDest, ColorBgra* pDestEnd,
      int idxSrc, DecodeContext context)
    {
      while (pDest < pDestEnd)
      {
        var level = context.Channels[0].ImageData[idxSrc];
        pDest->R = level;
        pDest->G = level;
        pDest->B = level;

        pDest++;
        idxSrc += context.ByteDepth;
      }
    }

    private static unsafe void SetPDNRowIndexed(ColorBgra* pDest, ColorBgra* pDestEnd,
      int idxSrc, DecodeContext context)
    {
      while (pDest < pDestEnd)
      {
        int index = (int)context.Channels[0].ImageData[idxSrc];
        pDest->R = (byte)context.Layer.PsdFile.ColorModeData[index];
        pDest->G = context.Layer.PsdFile.ColorModeData[index + 256];
        pDest->B = context.Layer.PsdFile.ColorModeData[index + 2 * 256];

        pDest++;
        idxSrc += context.ByteDepth;
      }
    }

    private static unsafe void SetPDNRowLab(ColorBgra* pDest, ColorBgra* pDestEnd,
      int idxSrc, DecodeContext context)
    {
      while (pDest < pDestEnd)
      {
        double exL, exA, exB;
        exL = (double)context.Channels[0].ImageData[idxSrc];
        exA = (double)context.Channels[1].ImageData[idxSrc];
        exB = (double)context.Channels[2].ImageData[idxSrc];

        int L = (int)(exL / 2.55);
        int a = (int)(exA - 127.5);
        int b = (int)(exB - 127.5);

        // First, convert from Lab to XYZ.
        // Standards used Observer = 2, Illuminant = D65

        const double ref_X = 95.047;
        const double ref_Y = 100.000;
        const double ref_Z = 108.883;

        double var_Y = ((double)L + 16.0) / 116.0;
        double var_X = (double)a / 500.0 + var_Y;
        double var_Z = var_Y - (double)b / 200.0;

        double var_X3 = var_X * var_X * var_X;
        double var_Y3 = var_Y * var_Y * var_Y;
        double var_Z3 = var_Z * var_Z * var_Z;

        if (var_Y3 > 0.008856)
          var_Y = var_Y3;
        else
          var_Y = (var_Y - 16 / 116) / 7.787;

        if (var_X3 > 0.008856)
          var_X = var_X3;
        else
          var_X = (var_X - 16 / 116) / 7.787;

        if (var_Z3 > 0.008856)
          var_Z = var_Z3;
        else
          var_Z = (var_Z - 16 / 116) / 7.787;

        double X = ref_X * var_X;
        double Y = ref_Y * var_Y;
        double Z = ref_Z * var_Z;

        // Then, convert from XYZ to RGB.
        // Standards used Observer = 2, Illuminant = D65
        // ref_X = 95.047, ref_Y = 100.000, ref_Z = 108.883

        double var_R = X * 0.032406 + Y * (-0.015372) + Z * (-0.004986);
        double var_G = X * (-0.009689) + Y * 0.018758 + Z * 0.000415;
        double var_B = X * 0.000557 + Y * (-0.002040) + Z * 0.010570;

        if (var_R > 0.0031308)
          var_R = 1.055 * (Math.Pow(var_R, 1 / 2.4)) - 0.055;
        else
          var_R = 12.92 * var_R;

        if (var_G > 0.0031308)
          var_G = 1.055 * (Math.Pow(var_G, 1 / 2.4)) - 0.055;
        else
          var_G = 12.92 * var_G;

        if (var_B > 0.0031308)
          var_B = 1.055 * (Math.Pow(var_B, 1 / 2.4)) - 0.055;
        else
          var_B = 12.92 * var_B;

        int nRed = (int)(var_R * 256.0);
        int nGreen = (int)(var_G * 256.0);
        int nBlue = (int)(var_B * 256.0);

        if (nRed < 0) nRed = 0;
        else if (nRed > 255) nRed = 255;
        if (nGreen < 0) nGreen = 0;
        else if (nGreen > 255) nGreen = 255;
        if (nBlue < 0) nBlue = 0;
        else if (nBlue > 255) nBlue = 255;

        pDest->R = (byte)nRed;
        pDest->G = (byte)nGreen;
        pDest->B = (byte)nBlue;

        pDest++;
        idxSrc += context.ByteDepth;
      }
    }

    #endregion

    ///////////////////////////////////////////////////////////////////////////////

    private static double rgbExponent = 1 / 2.19921875;
    unsafe private static byte RGBByteFromHDRFloat(byte* ptr)
    {
      float* floatPtr = (float*)ptr;
      var result = (byte)(255 * Math.Pow(*floatPtr, rgbExponent));
      return result;
    }
  }
}