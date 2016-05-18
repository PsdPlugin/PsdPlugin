/////////////////////////////////////////////////////////////////////////////////
//
// Photoshop PSD FileType Plugin for Paint.NET
// http://psdplugin.codeplex.com/
//
// This software is provided under the MIT License:
//   Copyright (c) 2006-2007 Frank Blumenberg
//   Copyright (c) 2010-2016 Tao Yue
//
// See LICENSE.txt for complete licensing and attribution information.
//
/////////////////////////////////////////////////////////////////////////////////

using System;
using System.Drawing;

namespace PhotoshopFile.Compression
{
  public static class ImageDataFactory
  {
    public static ImageData Create(Channel channel, byte[] data)
    {
      var bitDepth = channel.Layer.PsdFile.BitDepth;
      ImageData imageData;
      switch (channel.ImageCompression)
      {
        case ImageCompression.Raw:
          imageData = new RawImage(data, channel.Rect.Size, bitDepth);
          break;
        case ImageCompression.Rle:
          imageData = new RleImage(data, channel.RleRowLengths,
            channel.Rect.Size, bitDepth);
          break;
        case ImageCompression.Zip:
          imageData = new ZipImage(data, channel.Rect.Size, bitDepth);
          break;
        case ImageCompression.ZipPrediction:
          imageData = CreateZipPredict(data, channel.Rect.Size, bitDepth);
          break;
        default:
          throw new PsdInvalidException("Unknown image compression method.");
      }

      // Reverse endianness of multi-byte image data.
      if (bitDepth > 8)
      {
        // ZIP with prediction will reorder the bytes during decompression.
        if (channel.ImageCompression != ImageCompression.ZipPrediction)
        {
          imageData = new EndianReverser(imageData);
        }
      }

      return imageData;
    }

    private static ImageData CreateZipPredict(byte[] data, Size size,
      int bitDepth)
    {
      switch (bitDepth)
      {
        case 16:
          return new ZipPredict16Image(data, size);
        case 32:
          return new ZipPredict32Image(data, size);
        default:
          throw new PsdInvalidException(
            "ZIP with prediction is only available for 16 and 32 bit depths."); 
      }
    }
  }
}
