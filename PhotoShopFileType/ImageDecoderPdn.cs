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

    public static byte GetBitmapValue(byte[] bitmap, int pos)
    {
      byte mask = (byte)(0x80 >> (pos % 8));
      byte bwValue = (byte)(bitmap[pos / 8] & mask);
      bwValue = (bwValue == 0) ? (byte)255 : (byte)0;
      return bwValue;
    }

    /////////////////////////////////////////////////////////////////////////// 

    public static BitmapLayer DecodeImage(PhotoshopFile.Layer psdLayer, bool isBackground)
    {
      BitmapLayer pdnLayer = isBackground
        ? PaintDotNet.Layer.CreateBackgroundLayer(psdLayer.PsdFile.Columns, psdLayer.PsdFile.Rows)
        : new BitmapLayer(psdLayer.PsdFile.Columns, psdLayer.PsdFile.Rows);
      int byteDepth = Util.BytesFromBitDepth(psdLayer.PsdFile.Depth);
      bool hasMaskChannel = psdLayer.Channels.ContainsId(-2);
      var channels = psdLayer.Channels.ToIdArray();

      Surface surface = pdnLayer.Surface;
      var clearColor = isBackground ? (ColorBgra)0xffffffff : (ColorBgra)0;
      surface.Clear(clearColor);

      unsafe
      {
        // Map source row to destination row.
        int ySrcStart = Math.Max(0, -psdLayer.Rect.Y);
        int yDestStart = psdLayer.Rect.Y + ySrcStart;
        int yDestEnd = Math.Min(surface.Height,  psdLayer.Rect.Y + psdLayer.Rect.Height);

        // Map source column to destination column.
        int xSrcStart = Math.Max(0, -psdLayer.Rect.X);
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
          var pDestStart = pDestRow + (psdLayer.Rect.X + xSrcStart);
          var pDestEnd = pDestRow + xDestEnd;

          if (psdLayer.PsdFile.Depth != 32)
          {
            // For 16-bit images, take the higher-order byte from the image
            // data, which is now in little-endian order.
            if (byteDepth == 2)
              idxSrcStart++;

            SetPDNColorRow(pDestStart, pDestEnd, idxSrcStart, byteDepth, psdLayer, channels);
            SetPDNAlphaRow(pDestStart, pDestEnd, idxSrcStart, byteDepth, psdLayer.AlphaChannel);
            if (hasMaskChannel)
              SetPDNMaskRow(pDestStart, pDestEnd, ySrc, xSrcStart, byteDepth, psdLayer.MaskData);
          }
          else
          {
            SetPDNColorRow32(pDestStart, pDestEnd, idxSrcStart, psdLayer.PsdFile.ColorMode, psdLayer, channels);
            SetPDNAlphaRow(pDestStart, pDestEnd, idxSrcStart, byteDepth, psdLayer.AlphaChannel);
            if (hasMaskChannel)
              SetPDNMaskRow(pDestStart, pDestEnd, ySrc, xSrcStart, byteDepth, psdLayer.MaskData);
          }

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
      int idxSrc, PsdColorMode colorMode, PhotoshopFile.Layer psdLayer, Channel[] channels)
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
          throw new Exception("32-bit HDR images must be either RGB or grayscale.");
      }
    }

    /////////////////////////////////////////////////////////////////////////// 


    private static unsafe void SetPDNAlphaRow(
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

    unsafe private static void SetPDNMaskRow(ColorBgra* pDestStart, ColorBgra* pDestEnd,
      int ySrc, int xSrcStart, int byteDepth, Mask mask)
    {
      if (mask.ImageData.Length == 0)
        return;

      // Calculate mask coordinates
      int yMask = ySrc - mask.Rect.Y;
      int xMaskStart = xSrcStart - mask.Rect.X;

      // If mask position is not relative to the layer, then add back the
      // layer coordinates to get the position relative to the canvas.
      if (!mask.PositionIsRelative)
      {
        yMask += mask.Layer.Rect.Y;
        xMaskStart += mask.Layer.Rect.X;
      }

      // Restrict the mask to valid coordinates
      if ((yMask < 0) || (yMask >= mask.Rect.Height))
        return;
      xMaskStart = Math.Max(xMaskStart, 0);
      xMaskStart = Math.Min(xMaskStart, mask.Rect.Width);

      // Set the alpha from the mask
      fixed (byte* pMaskData = &mask.ImageData[0])
      {
        byte* pMask = pMaskData + (yMask * mask.Rect.Width + xMaskStart) * byteDepth;
        byte* pMaskEnd = pMaskData + (yMask + 1) * mask.Rect.Width * byteDepth;

        // Take the high-order byte if values are 16-bit little-endian
        if (byteDepth == 2)
          pMask++;

        ColorBgra* pDest = pDestStart;
        while ((pDest < pDestEnd) && (pMask < pMaskEnd))
        {
          var maskAlpha = (byteDepth < 4)
            ? *pMask
            : RGBByteFromHDRFloat(pMask);

          if (maskAlpha < 255)
            pDest->A = (byte)(pDest->A * maskAlpha / 255);

          pMask += byteDepth;
          pDest++;
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
  }
}