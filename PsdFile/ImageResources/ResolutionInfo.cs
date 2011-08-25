/////////////////////////////////////////////////////////////////////////////////
//
// Photoshop PSD FileType Plugin for Paint.NET
// http://psdplugin.codeplex.com/
//
// This software is provided under the MIT License:
//   Copyright (c) 2006-2007 Frank Blumenberg
//   Copyright (c) 2010-2011 Tao Yue
//
// Portions of this file are provided under the BSD 3-clause License:
//   Copyright (c) 2006, Jonas Beckeman
//
// See LICENSE.txt for complete licensing and attribution information.
//
/////////////////////////////////////////////////////////////////////////////////

using System;

namespace PhotoshopFile
{
  /// <summary>
  /// Summary description for ResolutionInfo.
  /// </summary>
  public class ResolutionInfo : ImageResource
  {
    /// <summary>
    /// Horizontal DPI.
    /// </summary>
    public UFixed16_16 HDpi { get; set; }

    /// <summary>
    /// Vertical DPI.
    /// </summary>
    public UFixed16_16 VDpi { get; set; }

    /// <summary>
    /// 1 = pixels per inch, 2 = pixels per centimeter
    /// </summary>
    public enum ResUnit
    {
      PxPerInch = 1,
      PxPerCm = 2
    }

    /// <summary>
    /// Display units for horizontal resolution.  They are still stored as
    /// pixels/inch.
    /// </summary>
    private ResUnit m_hResDisplayUnit;
    public ResUnit HResDisplayUnit
    {
      get { return m_hResDisplayUnit; }
      set { m_hResDisplayUnit = value; }
    }

    /// <summary>
    /// Display units for vertical resolution.  They are still stored as
    /// pixels/inch.
    /// </summary>
    private ResUnit m_vResDisplayUnit;
    public ResUnit VResDisplayUnit
    {
      get { return m_vResDisplayUnit; }
      set { m_vResDisplayUnit = value; }
    }

    /// <summary>
    /// 1=in, 2=cm, 3=pt, 4=picas, 5=columns
    /// </summary>
    public enum Unit
    {
      In = 1,
      Cm = 2,
      Pt = 3,
      Picas = 4,
      Columns = 5
    }

    private Unit m_widthDisplayUnit;
    public Unit WidthDisplayUnit
    {
      get { return m_widthDisplayUnit; }
      set { m_widthDisplayUnit = value; }
    }

    private Unit m_heightDisplayUnit;
    public Unit HeightDisplayUnit
    {
      get { return m_heightDisplayUnit; }
      set { m_heightDisplayUnit = value; }
    }

    public ResolutionInfo(): base()
    {
      base.ID = (short)ResourceID.ResolutionInfo;
    }

    public ResolutionInfo(ImageResource imgRes)
      : base(imgRes)
    {
      BinaryReverseReader reader = imgRes.DataReader;

      this.HDpi = new UFixed16_16(reader.ReadUInt32());
      this.m_hResDisplayUnit = (ResUnit)reader.ReadInt16();
      this.m_widthDisplayUnit = (Unit)reader.ReadInt16();

      this.VDpi = new UFixed16_16(reader.ReadUInt32());
      this.m_vResDisplayUnit = (ResUnit)reader.ReadInt16();
      this.m_heightDisplayUnit = (Unit)reader.ReadInt16();

      reader.Close();
    }

    protected override void StoreData()
    {
      System.IO.MemoryStream stream = new System.IO.MemoryStream();
      BinaryReverseWriter writer = new BinaryReverseWriter(stream);

      writer.Write(HDpi.Integer);
      writer.Write(HDpi.Fraction);
      writer.Write((Int16)m_hResDisplayUnit);
      writer.Write((Int16)m_widthDisplayUnit);

      writer.Write(VDpi.Integer);
      writer.Write(VDpi.Fraction);
      writer.Write((Int16)m_vResDisplayUnit);
      writer.Write((Int16)m_heightDisplayUnit);

      writer.Close();
      stream.Close();

      Data = stream.ToArray();
    }

  }
}
