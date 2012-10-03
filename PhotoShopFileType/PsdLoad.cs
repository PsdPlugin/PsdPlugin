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
        psdFile.VerifyLayerSections();
        ApplyLayerSections(psdFile.Layers);
        var pdnLayers = new Layer[psdFile.Layers.Count];

        var threadPool = new PaintDotNet.Threading.PrivateThreadPool();
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
    /// Transform Photoshop's layer tree to Paint.NET's flat layer list.
    /// Indicate where layer sections begin and end, and hide all layers within
    /// hidden layer sections.
    /// </summary>
    private static void ApplyLayerSections(List<PhotoshopFile.Layer> layers)
    {
      // BUG: PsdPluginResources.GetString will always return English resource,
      // because Paint.NET does not set the CurrentUICulture when OnLoad is
      // called.  This situation should be resolved with Paint.NET 4.0, which
      // will provide an alternative mechanism to retrieve the UI language.

      // Cache layer section strings
      var beginSectionWrapper = PsdPluginResources.GetString("LayersPalette_LayerGroupBegin");
      var endSectionWrapper = PsdPluginResources.GetString("LayersPalette_LayerGroupEnd");
      
      // Track the depth of the topmost hidden section.  Any nested sections
      // will be hidden, whether or not they themselves have the flag set.
      int topHiddenSectionDepth = Int32.MaxValue;
      var layerSectionNames = new Stack<string>();

      // Layers are stored bottom-to-top, but layer sections are specified
      // top-to-bottom.
      foreach (var layer in Enumerable.Reverse(layers))
      {
        // Apply to all layers within the layer section, as well as the
        // closing layer.
        if (layerSectionNames.Count > topHiddenSectionDepth)
          layer.Visible = false;

        var sectionInfos = layer.AdditionalInfo.Where(x => x.Key == "lsct");
        if (sectionInfos.Count() > 1)
          throw new PsdInvalidException();
        if (sectionInfos.Count() == 0)
          continue;
        var sectionInfo = (LayerSectionInfo)sectionInfos.Single();

        switch (sectionInfo.SectionType)
        {
          case LayerSectionType.OpenFolder:
          case LayerSectionType.ClosedFolder:
            // Start a new layer section
            if ((!layer.Visible) && (topHiddenSectionDepth == Int32.MaxValue))
              topHiddenSectionDepth = layerSectionNames.Count;
            layerSectionNames.Push(layer.Name);
            layer.Name = String.Format(beginSectionWrapper, layer.Name);
            break;

          case LayerSectionType.SectionDivider:
            // End the current layer section
            var layerSectionName = layerSectionNames.Pop();
            if (layerSectionNames.Count == topHiddenSectionDepth)
              topHiddenSectionDepth = Int32.MaxValue;
            layer.Name = String.Format(endSectionWrapper, layerSectionName);
            break;
        }
      }
    }

    /// <summary>
    /// Verify that the PSD file will fit into physical memory once loaded
    /// and converted to Paint.NET format.
    /// </summary>
    /// <remarks>
    /// This check is necessary because layers in Paint.NET have the same
    /// dimensions as the canvas.  Thus, PSD files that contain lots of
    /// tiny adjustment layers may blow up in size by several
    /// orders of magnitude.
    /// </remarks>
    private static void CheckSufficientMemory(PsdFile psdFile)
    {
      // Memory for PSD layers (or composite image), plus Paint.NET scratch
      // and composite layers.
      var numLayers = psdFile.Layers.Count + 2;
      if (psdFile.Layers.Count == 0)
        numLayers++;
      long numPixels = psdFile.ColumnCount * psdFile.RowCount;
      ulong bytesRequired = (ulong)(4 * numPixels * numLayers);

      // Check that the file will fit entirely into physical memory, so that we
      // do not thrash and make the Paint.NET UI nonresponsive.  We also have
      // to check against virtual memory address space because 32-bit processes
      // cannot access all 4 GB.
      var computerInfo = new Microsoft.VisualBasic.Devices.ComputerInfo();
      var accessibleMemory = Math.Min(computerInfo.TotalPhysicalMemory,
        computerInfo.TotalVirtualMemory);
      if (bytesRequired > accessibleMemory)
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
