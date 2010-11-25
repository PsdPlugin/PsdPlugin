using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PhotoshopFile
{
  public static class Util
  {
    public static int BytesFromBits(int bits)
    {
      return (bits + 7) / 8;
    }

    /////////////////////////////////////////////////////////////////////////// 

    public static int RoundUp(int value, int stride)
    {
      return ((value + stride - 1) / stride) * stride;
    }

  }
}
