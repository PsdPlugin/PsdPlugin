/////////////////////////////////////////////////////////////////////////////////
//
// Photoshop PSD FileType Plugin for Paint.NET
// http://psdplugin.codeplex.com/
//
// This software is provided under the MIT License:
//   Copyright (c) 2006-2007 Frank Blumenberg
//   Copyright (c) 2010-2013 Tao Yue
//
// See LICENSE.txt for complete licensing and attribution information.
//
/////////////////////////////////////////////////////////////////////////////////

using System;
using System.Diagnostics;

namespace PhotoshopFile
{
  [DebuggerDisplay("Layer Info: { key }")]
  public class RawLayerInfo : LayerInfo
  {
    private string key;
    public override string Key
    {
      get { return key; }
    }

    public byte[] Data { get; private set; }

    public RawLayerInfo(string key)
    {
      this.key = key;
    }

    public RawLayerInfo(PsdBinaryReader reader, string key, int dataLength)
    {
      this.key = key;
      Data = reader.ReadBytes((int)dataLength);
    }

    protected override void WriteData(PsdBinaryWriter writer)
    {
      writer.Write(Data);
    }
  }
}
