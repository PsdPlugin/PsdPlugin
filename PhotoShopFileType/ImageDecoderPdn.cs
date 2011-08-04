/////////////////////////////////////////////////////////////////////////////////
//
// Photoshop PSD FileType Plugin for Paint.NET
// http://psdplugin.codeplex.com/
//
// This software is provided under the MIT License:
//   Copyright (c) 2006-2007 Frank Blumenberg
//   Copyright (c) 2010-2011 Tao Yue
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

      Surface surface = pdnLayer.Surface;
      var clearColor = isBackground ? (ColorBgra)0xffffffff : (ColorBgra)0;
      surface.Clear(clearColor);

      bool hasMaskChannel = psdLayer.SortedChannels.ContainsKey(-2);
      var channels = psdLayer.ChannelsArray;
      var alphaChannel = psdLayer.AlphaChannel;

      int yPsdLayerStart = Math.Max(0, -psdLayer.Rect.Y);
      int yPsdLayerEnd = Math.Min(psdLayer.Rect.Height, surface.Height - psdLayer.Rect.Y);
      int byteDepth = Util.BytesFromBitDepth(psdLayer.PsdFile.Depth);

      for (int yPsdLayer = yPsdLayerStart; yPsdLayer < yPsdLayerEnd; yPsdLayer++)
      {
        unsafe
        {
          ColorBgra* dstRow = surface.GetRowAddress(yPsdLayer + psdLayer.Rect.Y);

          int xPsdLayerStart = Math.Max(0, -psdLayer.Rect.X);
          int xPsdLayerEnd = Math.Min(psdLayer.Rect.Width, psdLayer.PsdFile.Columns - psdLayer.Rect.Left);
          int xPsdLayerEndCopy = Math.Min(xPsdLayerEnd, surface.Width - psdLayer.Rect.X);

          int srcRowIndex = yPsdLayer * psdLayer.Rect.Width * byteDepth;
          int dstIndex = psdLayer.Rect.Left + xPsdLayerStart;
          ColorBgra* dstPixel = dstRow + dstIndex;

          if (psdLayer.PsdFile.Depth != 32)
          {
            // Take the higher-order byte from the little-endian image data
            if (byteDepth == 2)
              srcRowIndex++;

            var dstPixelCopy = dstPixel;
            for (int xPsdLayer = xPsdLayerStart; xPsdLayer < xPsdLayerEndCopy; xPsdLayer++)
            {
              int srcIndex = srcRowIndex + xPsdLayer * byteDepth;
              SetPDNColor(dstPixelCopy, psdLayer, channels, srcIndex);
              dstPixelCopy++;
            }

            SetPDNAlphaRow(dstPixel, hasMaskChannel, psdLayer.MaskData, alphaChannel,
              byteDepth, xPsdLayerStart, xPsdLayerEndCopy, yPsdLayer, srcRowIndex);
          }
          else
          {
            SetPDNColorRow32(dstPixel, psdLayer.PsdFile.ColorMode, channels, xPsdLayerStart, xPsdLayerEndCopy, yPsdLayer, srcRowIndex);
            SetPDNAlphaRow(dstPixel, hasMaskChannel, psdLayer.MaskData, alphaChannel,
              byteDepth, xPsdLayerStart, xPsdLayerEndCopy, yPsdLayer, srcRowIndex);
          }
        }
      }

      return pdnLayer;
    }

    /////////////////////////////////////////////////////////////////////////// 

    unsafe private static void SetPDNColorRow32(ColorBgra* dstPixel, PsdColorMode colorMode,
      PhotoshopFile.Layer.Channel[] channels, int xPsdLayerStart, int xPsdLayerEndCopy, int yPsdLayer, int srcRowIndex)
    {
      switch (colorMode)
      {
        case PsdColorMode.Grayscale:
          fixed (byte* channelPtr = &channels[0].ImageData[0])
          {
            for (int xPsdLayer = xPsdLayerStart; xPsdLayer < xPsdLayerEndCopy; xPsdLayer++)
            {
              int srcIndex = srcRowIndex + xPsdLayer * 4;
              byte* ptr = channelPtr + srcIndex;

              byte rgbValue = RGBByteFromHDRFloat(ptr);
              dstPixel->R = rgbValue;
              dstPixel->G = rgbValue;
              dstPixel->B = rgbValue;
              dstPixel++;
            }
          }
          break;
        case PsdColorMode.RGB:
          fixed (byte* rChannelPtr = &channels[0].ImageData[0])
          {
            fixed (byte* gChannelPtr = &channels[1].ImageData[0])
            {
              fixed (byte* bChannelPtr = &channels[2].ImageData[0])
              {
                for (int xPsdLayer = xPsdLayerStart; xPsdLayer < xPsdLayerEndCopy; xPsdLayer++)
                {
                  int srcIndex = srcRowIndex + xPsdLayer * 4;

                  dstPixel->R = RGBByteFromHDRFloat(rChannelPtr + srcIndex);
                  dstPixel->G = RGBByteFromHDRFloat(gChannelPtr + srcIndex);
                  dstPixel->B = RGBByteFromHDRFloat(bChannelPtr + srcIndex);
                  dstPixel++;
                }
              }
            }
          }
          break;
        default:
          throw new Exception("32-bit HDR images must be either RGB or grayscale.");
      }
    }

    /////////////////////////////////////////////////////////////////////////// 

    unsafe private static void SetPDNAlphaRow(ColorBgra* dstPixel,
      bool hasMaskChannel, PhotoshopFile.Layer.Mask mask, PhotoshopFile.Layer.Channel alphaChannel,
      int byteDepth, int xPsdLayerStart, int xPsdLayerEndCopy, int yPsdLayer, int srcRowIndex)
    {
      var dstPixelCopy = dstPixel;

      // Set alpha to fully-opaque if there is no alpha channel
      if (alphaChannel == null)
      {
        for (int xPsdLayer = xPsdLayerStart; xPsdLayer < xPsdLayerEndCopy; xPsdLayer++)
        {
          dstPixelCopy->A = 255;
          dstPixelCopy++;
        }
      }
      // Set the alpha channel data
      else
      {
        fixed (byte* alphaChannelPtr = &alphaChannel.ImageData[0])
        {
          for (int xPsdLayer = xPsdLayerStart; xPsdLayer < xPsdLayerEndCopy; xPsdLayer++)
          {
            int srcIndex = srcRowIndex + xPsdLayer * byteDepth;
            byte* alphaPtr = alphaChannelPtr + srcIndex;

            // Get alpha value
            if (byteDepth < 4)
              dstPixelCopy->A = *alphaPtr;
            else
              dstPixelCopy->A = RGBByteFromHDRFloat(alphaPtr);

            dstPixelCopy++;
          }
        }
      }

      // Merge in the layer mask
      if (hasMaskChannel)
      {
        // Set parameters for the mask channel
        int xMaskStart = xPsdLayerStart - mask.Rect.X;
        int xMaskEnd = xPsdLayerEndCopy - mask.Rect.X;
        int yMask = yPsdLayer - mask.Rect.Y;
        if (!mask.PositionIsRelative)
        {
          xMaskStart += mask.Layer.Rect.X;
          xMaskEnd += mask.Layer.Rect.X;
          yMask += mask.Layer.Rect.Y;
        }
        xMaskStart = Math.Max(xMaskStart, 0);
        xMaskEnd = Math.Max(xMaskEnd, 0);
        yMask = Math.Max(yMask, 0);
        xMaskStart = Math.Min(xMaskStart, mask.Rect.Width);
        xMaskEnd = Math.Min(xMaskEnd, mask.Rect.Width);
        yMask = Math.Min(yMask, mask.Rect.Height);

        // Pointer addressing will fail for an empty mask
        if (mask.ImageData.Length > 0)
        {
          // Set the alpha from the mask
          dstPixelCopy = dstPixel;
          fixed (byte* maskDataPtr = &mask.ImageData[0])
          {
            byte* maskDataEndPtr = maskDataPtr + mask.ImageData.Length;
            byte* maskPtr = maskDataPtr + (yMask * mask.Rect.Width + xMaskStart) * byteDepth;
            if (byteDepth == 2)
              maskPtr++;  // High-order byte
            byte* maskEndPtr = maskDataPtr + (yMask * mask.Rect.Width + xMaskEnd) * byteDepth;
            if (maskEndPtr > maskDataEndPtr)
              maskEndPtr = maskDataEndPtr;

            while (maskPtr < maskEndPtr)
            {
              var maskAlpha = (byteDepth < 4)
                ? *maskPtr
                : RGBByteFromHDRFloat(maskPtr);

              if (maskAlpha < 255)
                dstPixelCopy->A = (byte)(dstPixelCopy->A * maskAlpha / 255);

              maskPtr += byteDepth;
              dstPixelCopy++;
            }
          }
        }
      }
    }

    /////////////////////////////////////////////////////////////////////////// 

    unsafe private static void SetPDNColor(ColorBgra* dstPixel, PhotoshopFile.Layer layer,
        PhotoshopFile.Layer.Channel[] channels, int pos)
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
      double C, M, Y, K;

      C = (double)(255 - c) / 255;
      M = (double)(255 - m) / 255;
      Y = (double)(255 - y) / 255;
      K = (double)(255 - k) / 255;

      int nRed = (int)((1.0 - (C * (1 - K) + K)) * 255);
      int nGreen = (int)((1.0 - (M * (1 - K) + K)) * 255);
      int nBlue = (int)((1.0 - (Y * (1 - K) + K)) * 255);

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
  }
}