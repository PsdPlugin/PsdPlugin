/////////////////////////////////////////////////////////////////////////////////
//
// Photoshop PSD FileType Plugin for Paint.NET
// http://psdplugin.codeplex.com/
//
// This software is provided under the MIT License:
//   Copyright (c) 2006-2007 Frank Blumenberg
//   Copyright (c) 2010-2013 Tao Yue
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
  static class ImageDecoderPdn
  {

    /// <summary>
    /// Decode image from Photoshop's channel-separated and non-RGB formats to
    /// Paint.NET's ARGB (little-endian BGRA).
    /// </summary>
    public static BitmapLayer DecodeImage(PhotoshopFile.Layer psdLayer, bool isBackground)
    {
      BitmapLayer pdnLayer = isBackground
        ? PaintDotNet.Layer.CreateBackgroundLayer(psdLayer.PsdFile.ColumnCount, psdLayer.PsdFile.RowCount)
        : new BitmapLayer(psdLayer.PsdFile.ColumnCount, psdLayer.PsdFile.RowCount);
      int byteDepth = Util.BytesFromBitDepth(psdLayer.PsdFile.BitDepth);

      var hasLayerMask = (psdLayer.Masks != null)
        && (psdLayer.Masks.LayerMask != null)
        && (psdLayer.Masks.LayerMask.Disabled == false);
      var hasUserMask = (psdLayer.Masks != null)
        && (psdLayer.Masks.UserMask != null)
        && (psdLayer.Masks.UserMask.Disabled == false);

      var channels = psdLayer.Channels.ToIdArray();
      var surface = pdnLayer.Surface;

      unsafe
      {
        // Map source row to destination row.
        int ySrcStart = Math.Max(0, -psdLayer.Rect.Y);
        int yDestStart = psdLayer.Rect.Y + ySrcStart;
        int yDestEnd = Math.Min(surface.Height, psdLayer.Rect.Y + psdLayer.Rect.Height);

        // Map source column to destination column.
        int xSrcStart = Math.Max(0, -psdLayer.Rect.X);
        int xDestStart = psdLayer.Rect.X + xSrcStart;
        int xDestEnd = Math.Min(surface.Width, psdLayer.Rect.X + psdLayer.Rect.Width);

        // Convert rows from the Photoshop representation, writing the
        // resulting ARGB values to to the Paint.NET Surface.
        int ySrc = ySrcStart;
        int yDest = yDestStart;
        while (yDest < yDestEnd)
        {
          // Calculate indexes into ImageData source.
          int idxSrcRow = ySrc * psdLayer.Rect.Width * byteDepth;
          int idxSrcStart = idxSrcRow + xSrcStart * byteDepth;

          // Calculate pointers to destination Surface.
          var pDestRow = surface.GetRowAddress(yDest);
          var pDestStart = pDestRow + xDestStart;
          var pDestEnd = pDestRow + xDestEnd;

          if (psdLayer.PsdFile.BitDepth != 32)
          {
            // For 16-bit images, take the higher-order byte from the image
            // data, which is now in little-endian order.
            if (byteDepth == 2)
              idxSrcStart++;

            SetPDNColorRow(pDestStart, pDestEnd, idxSrcStart, byteDepth, psdLayer, channels);
            SetPDNAlphaRow(pDestStart, pDestEnd, idxSrcStart, byteDepth, psdLayer.AlphaChannel);
          }
          else
          {
            SetPDNColorRow32(pDestStart, pDestEnd, idxSrcStart, psdLayer.PsdFile.ColorMode, channels);
            SetPDNAlphaRow(pDestStart, pDestEnd, idxSrcStart, byteDepth, psdLayer.AlphaChannel);
          }

          // Apply layer masks(s) to the alpha channel
          var numPixels = xDestEnd - xDestStart;
          var layerMaskAlphaRow = hasLayerMask
            ? GetMaskAlphaRow(yDest, xDestStart, numPixels, byteDepth, psdLayer.Masks.LayerMask)
            : null;
          var userMaskAlphaRow = hasUserMask
            ? GetMaskAlphaRow(yDest, xDestStart, numPixels, byteDepth, psdLayer.Masks.UserMask)
            : null;
          ApplyPDNMask(pDestStart, pDestEnd, layerMaskAlphaRow, userMaskAlphaRow);

          // Advance to the next row
          ySrc++;
          yDest++;
        }
      }

      return pdnLayer;
    }

    /////////////////////////////////////////////////////////////////////////// 
    
    unsafe private static void SetPDNColorRow(ColorBgra* pDestStart, ColorBgra* pDestEnd,
      int idxSrc, int byteDepth, PhotoshopFile.Layer psdLayer, Channel[] channels)
    {
      var pDest = pDestStart;
      while (pDest < pDestEnd)
      {
        SetPDNColor(pDest, psdLayer, channels, idxSrc);
        pDest++;
        idxSrc += byteDepth;
      }
    }

    /////////////////////////////////////////////////////////////////////////// 

    unsafe private static void SetPDNColorRow32(ColorBgra* pDestStart, ColorBgra* pDestEnd,
      int idxSrc, PsdColorMode colorMode, Channel[] channels)
    {
      var pDest = pDestStart;
      switch (colorMode)
      {
        case PsdColorMode.Grayscale:
          fixed (byte* channelPtr = &channels[0].ImageData[0])
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
          break;
        case PsdColorMode.RGB:
          fixed (byte* pSrcRedChannel = &channels[0].ImageData[0],
            pSrcGreenChannel = &channels[1].ImageData[0],
            pSrcBlueChannel = &channels[2].ImageData[0])
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
          break;
        default:
          throw new PsdInvalidException("32-bit HDR images must be either RGB or grayscale.");
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
    /// Get alpha values from the layer mask, corresponding to the Surface
    /// position.
    /// </summary>
    /// <param name="ySurface">Row index on the Surface.</param>
    /// <param name="xSurface">Starting column index in the Surface.</param>
    /// <param name="numPixels">Number of columns to apply to the Surface.</param>
    /// <param name="mask">Mask to convert into alpha values.</param>
    /// <returns>Array of alpha values for the row.  Index 0 corresponds to xSurface.</returns>
    unsafe private static byte[] GetMaskAlphaRow(
      int ySurface, int xSurface, int numPixels, int byteDepth, Mask mask)
    {
      // Background color for areas not covered by the mask
      bool isInvertedMask = mask.InvertOnBlend;
      byte backgroundColor = isInvertedMask
        ? (byte)(255 - mask.BackgroundColor)
        : mask.BackgroundColor;

      // If there is no mask image and the background is not masked out, then
      // return null to suppress the alpha-merging.
      bool isEmptyMask = ((mask.ImageData == null) || (mask.ImageData.Length == 0));
      if (isEmptyMask && (backgroundColor == 255))
        return null;
      
      // Fill alpha array with background color
      var alphaRow = new byte[numPixels];
      fixed (byte* pAlphaRow = &alphaRow[0])
      {
        byte* pAlpha = pAlphaRow;
        Util.Fill(pAlpha, pAlphaRow + numPixels, backgroundColor);
      }
      if (isEmptyMask)
        return alphaRow;

      // Calculate the Mask position that corresponds to the Surface position
      int yMask = ySurface - mask.Rect.Y;
      int xMaskStart = xSurface - mask.Rect.X;
      if (mask.PositionVsLayer)
      {
        // Mask is specified relative to the layer.
        yMask -= mask.Layer.Rect.Y;
        xMaskStart -= mask.Layer.Rect.X;
      }
      int xMaskEnd = xMaskStart + numPixels;

      // Row position is outside the mask rectangle.
      if ((yMask < 0) || (yMask >= mask.Rect.Height))
        return alphaRow;

      // Clip the copy parameters to the mask boundaries.
      int xAlphaStart = 0;
      int xAlphaEnd = numPixels;
      if (xMaskStart < 0)
      {
        xAlphaStart -= xMaskStart;
        xMaskStart = 0;
      }
      if (xMaskEnd > mask.Rect.Width)
      {
        xAlphaEnd += (mask.Rect.Width - xMaskEnd);
        xMaskEnd = mask.Rect.Width;
      }

      // Mask lies outside the layer region.
      if (xAlphaStart > xAlphaEnd)
        return alphaRow;

      //////////////////////////////////////
      // Transfer mask into the alpha array
      fixed (byte* pAlphaRow = &alphaRow[0],
        pMaskData = &mask.ImageData[0])
      {
        // Get pointers to positions
        byte* pAlpha = pAlphaRow + xAlphaStart;
        byte* pAlphaEnd = pAlphaRow + xAlphaEnd;
        byte* pMaskRow = pMaskData + yMask * mask.Rect.Width * byteDepth;
        byte* pMask = pMaskRow + xMaskStart * byteDepth;

        // Take the high-order byte if values are 16-bit (little-endian)
        if (byteDepth == 2)
          pMask++;

        // Decode mask into the alpha array.
        while (pAlpha < pAlphaEnd)
        {
          byte maskAlpha = (byteDepth < 4)
            ? *pMask
            : RGBByteFromHDRFloat(pMask);
          if (isInvertedMask)
            maskAlpha = (byte)(255 - maskAlpha);

          *pAlpha = maskAlpha;

          pAlpha++;
          pMask += byteDepth;
        }
      }

      return alphaRow;
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

    unsafe private static void SetPDNColor(ColorBgra* dstPixel, PhotoshopFile.Layer layer,
        Channel[] channels, int pos)
    {
      switch (layer.PsdFile.ColorMode)
      {
        case PsdColorMode.RGB:
          dstPixel->R = channels[0].ImageData[pos];
          dstPixel->G = channels[1].ImageData[pos];
          dstPixel->B = channels[2].ImageData[pos];
          break;
        case PsdColorMode.CMYK:
          SetPDNColorCMYK(dstPixel,
            channels[0].ImageData[pos],
            channels[1].ImageData[pos],
            channels[2].ImageData[pos],
            channels[3].ImageData[pos]);
          break;
        case PsdColorMode.Multichannel:
          SetPDNColorCMYK(dstPixel,
            channels[0].ImageData[pos],
            channels[1].ImageData[pos],
            channels[2].ImageData[pos],
            0);
          break;
        case PsdColorMode.Bitmap:
          byte bwValue = GetBitmapValue(channels[0].ImageData, pos);
          dstPixel->R = bwValue;
          dstPixel->G = bwValue;
          dstPixel->B = bwValue;
          break;
        case PsdColorMode.Grayscale:
        case PsdColorMode.Duotone:
          dstPixel->R = channels[0].ImageData[pos];
          dstPixel->G = channels[0].ImageData[pos];
          dstPixel->B = channels[0].ImageData[pos];
          break;
        case PsdColorMode.Indexed:
          int index = (int)channels[0].ImageData[pos];
          dstPixel->R = (byte)layer.PsdFile.ColorModeData[index];
          dstPixel->G = layer.PsdFile.ColorModeData[index + 256];
          dstPixel->B = layer.PsdFile.ColorModeData[index + 2 * 256];
          break;
        case PsdColorMode.Lab:
          SetPDNColorLab(dstPixel,
            channels[0].ImageData[pos],
            channels[1].ImageData[pos],
            channels[2].ImageData[pos]);
          break;
      }
    }

    /////////////////////////////////////////////////////////////////////////// 

    unsafe private static void SetPDNColorLab(ColorBgra* dstPixel, byte lb, byte ab, byte bb)
    {
      double exL, exA, exB;

      exL = (double)lb;
      exA = (double)ab;
      exB = (double)bb;

      double L_coef, a_coef, b_coef;
      L_coef = 2.55;
      a_coef = 1.00;
      b_coef = 1.00;

      int L = (int)(exL / L_coef);
      int a = (int)(exA / a_coef - 127.5);
      int b = (int)(exB / b_coef - 127.5);

      // For the conversion we first convert values to XYZ and then to RGB
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

      SetPDNColorXYZ(dstPixel, X, Y, Z);
    }

    ////////////////////////////////////////////////////////////////////////////


    unsafe private static void SetPDNColorXYZ(ColorBgra* dstPixel, double X, double Y, double Z)
    {
      // Standards used Observer = 2, Illuminant = D65
      // ref_X = 95.047, ref_Y = 100.000, ref_Z = 108.883

      double var_X = X / 100.0;
      double var_Y = Y / 100.0;
      double var_Z = Z / 100.0;

      double var_R = var_X * 3.2406 + var_Y * (-1.5372) + var_Z * (-0.4986);
      double var_G = var_X * (-0.9689) + var_Y * 1.8758 + var_Z * 0.0415;
      double var_B = var_X * 0.0557 + var_Y * (-0.2040) + var_Z * 1.0570;

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

      dstPixel->R = (byte)nRed;
      dstPixel->G = (byte)nGreen;
      dstPixel->B = (byte)nBlue;
    }

    ///////////////////////////////////////////////////////////////////////////////

    private static double rgbExponent = 1 / 2.19921875;
    unsafe private static byte RGBByteFromHDRFloat(byte* ptr)
    {
      float* floatPtr = (float*)ptr;
      var result = Math.Round(255 * Math.Pow(*floatPtr, rgbExponent));
      return (byte)result;
    }

    ///////////////////////////////////////////////////////////////////////////////
    //
    // The algorithms for these routines were taken from:
    //     http://www.neuro.sfc.keio.ac.jp/~aly/polygon/info/color-space-faq.html
    //
    // RGB --> CMYK                              CMYK --> RGB
    // ---------------------------------------   --------------------------------------------
    // Black   = minimum(1-Red,1-Green,1-Blue)   Red   = 1-minimum(1,Cyan*(1-Black)+Black)
    // Cyan    = (1-Red-Black)/(1-Black)         Green = 1-minimum(1,Magenta*(1-Black)+Black)
    // Magenta = (1-Green-Black)/(1-Black)       Blue  = 1-minimum(1,Yellow*(1-Black)+Black)
    // Yellow  = (1-Blue-Black)/(1-Black)
    //

    unsafe private static void SetPDNColorCMYK(ColorBgra* dstPixel, byte c, byte m, byte y, byte k)
    {
      // CMYK values are stored as complements, presumably to allow for some
      // measure of compatibility with RGB-only applications.
      var C = 255 - c;
      var M = 255 - m;
      var Y = 255 - y;
      var K = 255 - k;

      int nRed = 255 - Math.Min(255, C * (255 - K) / 255 + K);
      int nGreen = 255 - Math.Min(255, M * (255 - K) / 255 + K);
      int nBlue = 255 - Math.Min(255, Y * (255 - K) / 255 + K);

      dstPixel->R = (byte)nRed;
      dstPixel->G = (byte)nGreen;
      dstPixel->B = (byte)nBlue;
    }

    /////////////////////////////////////////////////////////////////////////// 

    public static byte GetBitmapValue(byte[] bitmap, int pos)
    {
      byte mask = (byte)(0x80 >> (pos % 8));
      byte bwValue = (byte)(bitmap[pos / 8] & mask);
      bwValue = (bwValue == 0) ? (byte)255 : (byte)0;
      return bwValue;
    }
  }
}