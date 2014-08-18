/////////////////////////////////////////////////////////////////////////////////
//
// Photoshop PSD FileType Plugin for Paint.NET
// http://psdplugin.codeplex.com/
//
// This software is provided under the MIT License:
//   Copyright (c) 2006-2007 Frank Blumenberg
//   Copyright (c) 2010-2014 Tao Yue
//
// See LICENSE.txt for complete licensing and attribution information.
//
/////////////////////////////////////////////////////////////////////////////////

using PaintDotNet;
using PaintDotNet.Threading;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Threading;

using PhotoshopFile;

namespace PaintDotNet.Data.PhotoshopFileType
{
  public class PhotoshopFileTypes
      : IFileTypeFactory
  {
    public static readonly FileType Psd = new PhotoshopFileType();

    private static FileType[] fileTypes = new FileType[] { Psd };

    public FileType[] GetFileTypeInstances()
    {
      return (FileType[])fileTypes.Clone();
    }
  }


  public class PhotoshopFileType : FileType
  {
    public PhotoshopFileType()
      : base("Photoshop",
             FileTypeFlags.SupportsLoading |
               FileTypeFlags.SupportsSaving |
               FileTypeFlags.SavesWithProgress |
               FileTypeFlags.SupportsLayers,
             new string[] { ".psd", ".psb" })
    {
    }

    public override SaveConfigWidget CreateSaveConfigWidget()
    {
      return new PsdSaveConfigWidget();
    }

    protected override SaveConfigToken OnCreateDefaultSaveConfigToken()
    {
      return new PsdSaveConfigToken(true);
    }

    protected override void OnSave(Document input, System.IO.Stream output, SaveConfigToken token,
      Surface scratchSurface, ProgressEventHandler callback)
    {
      // Because the function signature takes in a Stream, we cannot force the
      // extension to .PSB for large documents.  However, Photoshop is happy
      // to load a PSB file even if it has a .PSD extension.

      var psdToken = (PsdSaveConfigToken)token;
      PsdSave.Save(input, output, psdToken, scratchSurface, callback);
    }

    protected override Document OnLoad(System.IO.Stream input)
    {
      return PsdLoad.Load(input);
    }
  }

}
