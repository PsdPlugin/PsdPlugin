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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using PaintDotNet;
using PhotoshopFile;

namespace PaintDotNet.Data.PhotoshopFileType
{
  public static class BlendOpMapping
  {
    /// <summary>
    /// Convert a Paint.NET BlendOp to a Photoshop blend mode.
    /// </summary>
    public static string ToPsdBlendMode(this UserBlendOp op)
    {
      var opType = op.GetType();

      if (opType == typeof(UserBlendOps.NormalBlendOp))
        return PsdBlendMode.Normal;
      
      else if (opType == typeof(UserBlendOps.MultiplyBlendOp))
        return PsdBlendMode.Multiply;
      else if (opType == typeof(UserBlendOps.AdditiveBlendOp))
        return PsdBlendMode.LinearDodge;
      else if (opType == typeof(UserBlendOps.ColorBurnBlendOp))
        return PsdBlendMode.ColorBurn;
      else if (opType == typeof(UserBlendOps.ColorDodgeBlendOp))
        return PsdBlendMode.ColorDodge;
      else if (opType == typeof(UserBlendOps.OverlayBlendOp))
        return PsdBlendMode.Overlay;
      else if (opType == typeof(UserBlendOps.DifferenceBlendOp))
        return PsdBlendMode.Difference;
      else if (opType == typeof(UserBlendOps.LightenBlendOp))
        return PsdBlendMode.Lighten;
      else if (opType == typeof(UserBlendOps.DarkenBlendOp))
        return PsdBlendMode.Darken;
      else if (opType == typeof(UserBlendOps.ScreenBlendOp))
        return PsdBlendMode.Screen;

      // Paint.NET blend modes without a Photoshop equivalent are saved as Normal
      // Namely: Glow, Negation, Reflect, Xor
      else
        return PsdBlendMode.Normal;
    }

    /// <summary>
    /// Convert a Photoshop blend mode to a Paint.NET BlendOp.
    /// </summary>
    public static UserBlendOp FromPsdBlendMode(string blendModeKey)
    {
      switch (blendModeKey)
      {
        case PsdBlendMode.Normal:
          return new UserBlendOps.NormalBlendOp();
        case PsdBlendMode.Multiply:
          return new UserBlendOps.MultiplyBlendOp();
        case PsdBlendMode.LinearDodge:
          return new UserBlendOps.AdditiveBlendOp();
        case PsdBlendMode.ColorBurn:
          return new UserBlendOps.ColorBurnBlendOp();
        case PsdBlendMode.ColorDodge:
          return new UserBlendOps.ColorDodgeBlendOp();
        case PsdBlendMode.Overlay:
          return new UserBlendOps.OverlayBlendOp();
        case PsdBlendMode.Difference:
          return new UserBlendOps.DifferenceBlendOp();
        case PsdBlendMode.Lighten:
          return new UserBlendOps.LightenBlendOp();
        case PsdBlendMode.Darken:
          return new UserBlendOps.DarkenBlendOp();
        case PsdBlendMode.Screen:
          return new UserBlendOps.ScreenBlendOp();

        // Photoshop blend modes without a Paint.NET equivalent are loaded as Normal
        default:
          return new UserBlendOps.NormalBlendOp();
      }
    }

  }
}
