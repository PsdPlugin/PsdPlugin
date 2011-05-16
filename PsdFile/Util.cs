using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;

namespace PhotoshopFile
{
  public static class Util
  {
    public static int BytesPerRow(Rectangle rect, int depth)
    {
      switch (depth)
      {
        case 1:
          return (rect.Width + 7) / 8;
        default:
          return rect.Width * BytesFromDepth(depth);
      }
    }

    /////////////////////////////////////////////////////////////////////////// 

    public static int RoundUp(int value, int stride)
    {
      return ((value + stride - 1) / stride) * stride;
    }

    /////////////////////////////////////////////////////////////////////////// 

    public static int BytesFromDepth(int depth)
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
