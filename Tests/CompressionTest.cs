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
using System.Runtime.InteropServices;

using NUnit.Framework;

namespace PhotoshopFile.Tests
{
  [TestFixture]
  public class CompressionTest
  {
    /// <summary>
    /// Verifies that image data will compress and decompress, producing an
    /// identical array.
    /// </summary>
    [TestCase(1, ImageCompression.Raw)]
    [TestCase(1, ImageCompression.Rle)]
    [TestCase(1, ImageCompression.Zip)]
    [TestCase(8, ImageCompression.Raw)]
    [TestCase(8, ImageCompression.Rle)]
    [TestCase(8, ImageCompression.Zip)]
    [TestCase(16, ImageCompression.Raw)]
    [TestCase(16, ImageCompression.Rle)]
    [TestCase(16, ImageCompression.Zip)]
    [TestCase(16, ImageCompression.ZipPrediction)]
    [TestCase(32, ImageCompression.Raw)]
    [TestCase(32, ImageCompression.Rle)]
    [TestCase(32, ImageCompression.Zip)]
    [TestCase(32, ImageCompression.ZipPrediction)]
    public void CompressDecompress(int bitDepth, ImageCompression compression)
    {
      var size = new Size(900, 200);
      var data = GenerateData(size, bitDepth);
      VerifyCompressDecompress(compression, data, size, bitDepth);
    }

    /// <summary>
    /// Verifies that an exception is thrown for an invalid combination of bit
    /// depth and compression method.
    /// </summary>
    [TestCase(1, ImageCompression.ZipPrediction)]
    [TestCase(8, ImageCompression.ZipPrediction)]
    public void CompressInvalid(int bitDepth, ImageCompression compression)
    {
      var size = new Size(1, 1);
      var data = GenerateData(size, bitDepth);
      Assert.Throws<PsdInvalidException>(() =>
        VerifyCompressDecompress(compression, data, size, bitDepth)
      );
    }

    /// <summary>
    /// Verifies that a zero-byte image can be compressed.
    /// </summary>
    [TestCase(8, ImageCompression.Raw)]
    [TestCase(8, ImageCompression.Rle)]
    [TestCase(8, ImageCompression.Zip)]
    [TestCase(16, ImageCompression.Raw)]
    [TestCase(16, ImageCompression.ZipPrediction)]
    [TestCase(32, ImageCompression.ZipPrediction)]
    public void CompressZeroLength(int bitDepth, ImageCompression compression)
    {
      var size = new Size(0, 0);
      var data = new byte[0];
      VerifyCompressDecompress(compression, data, size, bitDepth);
    }

    internal static void VerifyCompressDecompress(
      ImageCompression compression, byte[] imageData, Size size, int bitDepth)
    {
      var channel = CreateChannel(compression, size, bitDepth);

      channel.ImageData = imageData;
      channel.CompressImageData();

      channel.ImageData = null;
      channel.DecodeImageData();

      Assert.AreEqual(imageData, channel.ImageData,
        $"Image data changed after {compression} a compress/decompress cycle.");
    }

    private static Channel CreateChannel(ImageCompression compression,
      Size size, int bitDepth)
    {
      var psd = new PsdFile(PsdFileVersion.Psd)
      {
        BitDepth = bitDepth,
        ColorMode = (bitDepth == 1) ? PsdColorMode.Bitmap : PsdColorMode.RGB
      };

      var layer = new Layer(psd)
      {
        Rect = new Rectangle(Point.Empty, size)
      };
      psd.Layers.Add(layer);

      var channel = new Channel((short)0, layer)
      {
        ImageCompression = compression
      };
      layer.Channels.Add(channel);

      return channel;
    }

    private byte[] GenerateData(Size size, int bitDepth)
    {
      switch (bitDepth)
      {
        case 1:
          return GenerateData1(size);
        case 8:
          return GenerateData<byte>(size, GeneratePixel8);
        case 16:
          return GenerateData<Int16>(size, GeneratePixel16);
        case 32:
          return GenerateData<float>(size, GeneratePixel32);
        default:
          throw new Exception(
            $"Cannot generate test data for bit depth {bitDepth}.");
      }
    }

    private byte[] GenerateData1(Size size)
    {
      // Generate 8-bit data
      Func<Size, int, int, byte> generator8 = GeneratePixel8;
      var data8 = GenerateData(size, generator8);

      // Convert it to 1-bit data
      var bytesPerRow1 = Util.BytesPerRow(size, 1);
      var length1 = bytesPerRow1 * size.Height;
      var data1 = new byte[length1];

      for (int y = 0; y < size.Height; y++)
      {
        var rowIndex8 = y * size.Width;
        var rowIndex1 = y * bytesPerRow1;

        for (int x = 0; x < size.Width; x++)
        {
          // Threshold 8-bit values at 128
          var value8 = data8[rowIndex8 + x];
          var value1 = value8 >> 7;

          // Set the bit in the 1-bit image data array
          var index1 = rowIndex1 + x / 8;
          var shift = 7 - (x % 8);
          var mask1 = value1 << shift;
          data1[index1] |= (byte)mask1;
        }
      }

      return data1;
    }

    private byte[] GenerateData<T>(Size size,
      Func<Size, int, int, T> generatePixel)
    {
      var length = size.Width * size.Height;
      var data = new T[length];

      for (int y = 0; y < size.Height; y++)
      {
        var rowIndex = y * size.Width;
        for (int x = 0; x < size.Width; x++)
        {
          data[rowIndex + x] = generatePixel(size, x, y);
        }
      }

      // Byte array can be returned directly
      if (typeof(T) == typeof(byte))
      {
        return data as byte[];
      }

      // Copy generated data into a byte array
      var wordSize = Marshal.SizeOf(default(T));
      var result = new byte[data.Length * wordSize];

      var dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
      var dataPtr = dataHandle.AddrOfPinnedObject();
      Marshal.Copy(dataPtr, result, 0, result.Length);
      dataHandle.Free();

      return result;
    }

    private static byte GeneratePixel8(Size size, int x, int y)
    {
      var index = y * size.Width + x;
      var rowPattern = y % 4;
      switch (rowPattern)
      {
        case 0:
          return (byte)(y % 256);
        case 1:
          return (byte)(x % 256);
        case 2:
          return (byte)(x / 5);
        case 3:
          return (byte)(x % 5);
        default:
          throw new Exception("No matching 8-bit pattern.");
      }
    }

    private static Int16 GeneratePixel16(Size size, int x, int y)
    {
      var index = y * size.Width + x;
      var rowPattern = y % 3;
      switch (rowPattern)
      {
        case 0:
          return (Int16)(y % 256 * 0x101);
        case 1:
          return (Int16)(x % 5 * 0x101);
        case 2:
          // Gradient from 0 to 0xffff
          return (Int16)(x * 0xffff / size.Width);
        default:
          throw new Exception("No matching 16-bit pattern.");
      }
    }

    private static float GeneratePixel32(Size size, int x, int y)
    {
      var index = y * size.Width + x;
      var rowPattern = y % 2;
      switch (rowPattern)
      {
        case 0:
          return (float)(y % 256 / 255.0);
        case 1:
          // Gradient from 0 to 1.0
          return (float)(x) / size.Width;
        default:
          throw new Exception("No matching 32-bit pattern.");
      }
    }
  }
}
