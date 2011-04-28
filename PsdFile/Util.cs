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

  }
}
