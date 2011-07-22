using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;

namespace PhotoshopFile
{
  public static class Util
  {
    public struct RectanglePosition
    {
      public int Top { get; set; }
      public int Bottom { get; set; }
      public int Left { get; set; }
      public int Right { get; set; }
    }

    /////////////////////////////////////////////////////////////////////////// 

    unsafe static public void SwapBytes2(byte* ptr)
    {
      byte byte0 = *ptr;
      *ptr = *(ptr + 1);
      *(ptr + 1) = byte0;
    }

    /////////////////////////////////////////////////////////////////////////// 

    unsafe static public void SwapBytes4(byte* ptr)
    {
      byte byte0 = *ptr;
      byte byte1 = *(ptr + 1);

      *ptr = *(ptr + 3);
      *(ptr + 1) = *(ptr + 2);
      *(ptr + 2) = byte1;
      *(ptr + 3) = byte0;
    }

    /////////////////////////////////////////////////////////////////////////// 

    unsafe static public void SwapBytes(byte* ptr, int nLength)
    {
      for (long i = 0; i < nLength / 2; ++i)
      {
        byte t = *(ptr + i);
        *(ptr + i) = *(ptr + nLength - i - 1);
        *(ptr + nLength - i - 1) = t;
      }
    }

    /////////////////////////////////////////////////////////////////////////// 

    public static int BytesPerRow(Rectangle rect, int depth)
    {
      switch (depth)
      {
        case 1:
          return (rect.Width + 7) / 8;
        default:
          return rect.Width * BytesFromBitDepth(depth);
      }
    }

    /////////////////////////////////////////////////////////////////////////// 

    public static int RoundUp(int value, int stride)
    {
      return ((value + stride - 1) / stride) * stride;
    }

    /////////////////////////////////////////////////////////////////////////// 

    public static int BytesFromBitDepth(int depth)
    {
      switch (depth)
      {
        case 1:
        case 8:
          return 1;
        case 16:
          return 2;
        case 32:
          return 4;
        default:
          throw new ArgumentException("Invalid bit depth.");
      }
    }

    /////////////////////////////////////////////////////////////////////////// 

    public static short ChannelCount(PsdColorMode colorMode)
    {
      switch (colorMode)
      {
        case PsdColorMode.Bitmap:
        case PsdColorMode.Duotone:
        case PsdColorMode.Grayscale:
        case PsdColorMode.Indexed:
          return 1;
        case PsdColorMode.Lab:
        case PsdColorMode.Multichannel:
        case PsdColorMode.RGB:
          return 3;
        case PsdColorMode.CMYK:
          return 4;
      }

      throw new ArgumentException("Unknown color mode.");
    }

  }
}
