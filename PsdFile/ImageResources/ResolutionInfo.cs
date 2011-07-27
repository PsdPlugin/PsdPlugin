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
    /// Fixed-point number: pixels per inch
    /// </summary>
    private short m_hRes;
    public short HRes
    {
      get { return m_hRes; }
      set { m_hRes = value; }
    }

    /// <summary>
    /// Fixed-point number: pixels per inch
    /// </summary>
    private short m_vRes;
    public short VRes
    {
      get { return m_vRes; }
      set { m_vRes = value; }
    }

    /// <summary>
    /// 1=pixels per inch, 2=pixels per centimeter
    /// </summary>
    public enum ResUnit
    {
      PxPerInch = 1,
      PxPerCent = 2
    }

    private ResUnit m_hResUnit;
    public ResUnit HResUnit
    {
      get { return m_hResUnit; }
      set { m_hResUnit = value; }
    }

    private ResUnit m_vResUnit;
    public ResUnit VResUnit
    {
      get { return m_vResUnit; }
      set { m_vResUnit = value; }
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
    private Unit m_widthUnit;

    public Unit WidthUnit
    {
      get { return m_widthUnit; }
      set { m_widthUnit = value; }
    }

    private Unit m_heightUnit;

    public Unit HeightUnit
    {
      get { return m_heightUnit; }
      set { m_heightUnit = value; }
    }

    public ResolutionInfo(): base()
    {
      base.ID = (short)ResourceID.ResolutionInfo;
    }
    public ResolutionInfo(ImageResource imgRes)
      : base(imgRes)
    {
      BinaryReverseReader reader = imgRes.DataReader;

      this.m_hRes = reader.ReadInt16();
      this.m_hResUnit = (ResUnit)reader.ReadInt32();
      this.m_widthUnit = (Unit)reader.ReadInt16();

      this.m_vRes = reader.ReadInt16();
      this.m_vResUnit = (ResUnit)reader.ReadInt32();
      this.m_heightUnit = (Unit)reader.ReadInt16();

      reader.Close();
    }

    protected override void StoreData()
    {
      System.IO.MemoryStream stream = new System.IO.MemoryStream();
      BinaryReverseWriter writer = new BinaryReverseWriter(stream);

      writer.Write((Int16)m_hRes);
      writer.Write((Int32)m_hResUnit);
      writer.Write((Int16)m_widthUnit);

      writer.Write((Int16)m_vRes);
      writer.Write((Int32)m_vResUnit);
      writer.Write((Int16)m_heightUnit);

      writer.Close();
      stream.Close();

      Data = stream.ToArray();
    }

  }
}
