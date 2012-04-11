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
using System.IO;
using System.Drawing;

namespace PhotoshopFile
{
  /// <summary>
  /// Stores the raw data for unimplemented image resource types.
  /// </summary>
  public class RawImageResource : ImageResource
  {
    public byte[] Data { get; private set; }

    private ResourceID id;
    public override ResourceID ID
    {
      get { return id; }
    }

    public RawImageResource(string name) : base(name)
    {
    }

    public RawImageResource(PsdBinaryReader reader, string name, ResourceID resourceId, int numBytes)
      : base(name)
    {
      this.id = resourceId;
      Data = reader.ReadBytes(numBytes);
    }
    
    protected override void WriteData(PsdBinaryWriter writer)
    {
      writer.Write(Data);
    }

  }
}
