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
using System.Threading;

using PhotoshopFile;

namespace PaintDotNet.Data.PhotoshopFileType
{
  public static class PsdLoad
  {
    public static Document Load(System.IO.Stream input)
    {
      // Load and decompress Photoshop file structures
      var psdFile = new PsdFile();
      psdFile.Load(input);
      CheckSufficientMemory(psdFile);

      // Convert into Paint.NET internal representation
      var document = new Document(psdFile.ColumnCount, psdFile.RowCount);

      if (psdFile.Resolution != null)
      {
        // PSD files always specify the resolution in DPI.  When loading and
        // saving cm, we will have to round-trip the conversion, but doubles
        // have plenty of precision to spare vs. PSD's 16/16 fixed-point.

        if ((psdFile.Resolution.HResDisplayUnit == ResolutionInfo.ResUnit.PxPerCm)
          && (psdFile.Resolution.VResDisplayUnit == ResolutionInfo.ResUnit.PxPerCm))
        {
          document.DpuUnit = MeasurementUnit.Centimeter;

          // HACK: Paint.NET truncates DpuX and DpuY to three decimal places,
          // so add 0.0005 to get a rounded value instead.
          document.DpuX = psdFile.Resolution.HDpi / 2.54 + 0.0005;
          document.DpuY = psdFile.Resolution.VDpi / 2.54 + 0.0005;
        }
        else
        {
          document.DpuUnit = MeasurementUnit.Inch;
          document.DpuX = psdFile.Resolution.HDpi;
          document.DpuY = psdFile.Resolution.VDpi;
        }
      }

      if (psdFile.Layers.Count == 0)
      {
        psdFile.BaseLayer.CreateMissingChannels();
        var layer = ImageDecoderPdn.DecodeImage(psdFile.BaseLayer, true);
        document.Layers.Add(layer);
      }
      else
      {
        var threadPool = new PaintDotNet.Threading.PrivateThreadPool();
        var pdnLayers = new Layer[psdFile.Layers.Count];

        for (int i = 0; i < psdFile.Layers.Count; i++)
        {
          var psdLayer = psdFile.Layers[i];
          psdLayer.CreateMissingChannels();

          var context = new LoadLayerContext(psdLayer, pdnLayers, i);
          var waitCallback = new WaitCallback(context.LoadLayer);
          threadPool.QueueUserWorkItem(waitCallback);
        }
        threadPool.Drain();

        document.Layers.AddRange(pdnLayers);
      }
      return document;
    }

    /// <summary>
    /// Verify that the PSD file will fit into physical memory once loaded
    /// and converted to Paint.NET format.
    /// 
    /// <remarks>
    /// This check is necessary because layers in Paint.NET have the same
    /// dimensions as the canvas.  Thus, PSD files that contain lots of
    /// tiny adjustment layers may blow up in size by several
    /// orders of magnitude.</remarks>
    /// </summary>
    private static void CheckSufficientMemory(PsdFile psdFile)
    {
      // Memory for layers, plus scratch, composite, and background
      var numLayers = psdFile.Layers.Count + 2;
      if (psdFile.Layers.Count == 0)
        numLayers++;
      long numPixels = psdFile.ColumnCount * psdFile.RowCount;
      ulong bytesRequired = (ulong)(4 * numPixels * numLayers);

      // Check that the file will fit entirely into physical memory.
      // Otherwise, we will thrash and make the Paint.NET UI nonresponsive.
      var computerInfo = new Microsoft.VisualBasic.Devices.ComputerInfo();
      if (bytesRequired > computerInfo.TotalPhysicalMemory)
        throw new OutOfMemoryException();
    }

    private class LoadLayerContext
    {
      PhotoshopFile.Layer psdLayer;

      Layer[] pdnLayers;
      int idxPdnLayer;

      public LoadLayerContext(PhotoshopFile.Layer psdLayer,
        Layer[] pdnLayers, int idxPdnLayer)
      {
        this.psdLayer = psdLayer;
        this.pdnLayers = pdnLayers;
        this.idxPdnLayer = idxPdnLayer;
      }

      public void LoadLayer(object context)
      {
        var pdnLayer = ImageDecoderPdn.DecodeImage(psdLayer, isBackground: false);
        pdnLayer.Name = psdLayer.Name;
        pdnLayer.Opacity = psdLayer.Opacity;
        pdnLayer.Visible = psdLayer.Visible;
        pdnLayer.SetBlendOp(BlendOpMapping.FromPsdBlendMode(psdLayer.BlendModeKey));

        pdnLayers[idxPdnLayer] = pdnLayer;
      }
    }
  }

}
