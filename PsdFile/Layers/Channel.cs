/////////////////////////////////////////////////////////////////////////////////
//
// Photoshop PSD FileType Plugin for Paint.NET
// http://psdplugin.codeplex.com/
//
// This software is provided under the MIT License:
//   Copyright (c) 2006-2007 Frank Blumenberg
//   Copyright (c) 2010-2013 Tao Yue
//
// Portions of this file are provided under the BSD 3-clause License:
//   Copyright (c) 2006, Jonas Beckeman
//
// See LICENSE.txt for complete licensing and attribution information.
//
/////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace PhotoshopFile
{
  public class ChannelList : List<Channel>
  {
    /// <summary>
    /// Returns channels with nonnegative IDs as an array, so that accessing
    /// a channel by Id can be optimized into pointer arithmetic rather than
    /// being implemented as a List scan.
    /// </summary>
    /// <remarks>
    /// This optimization is crucial for blitting lots of pixels back and
    /// forth between Photoshop's per-channel representation, and Paint.NET's
    /// per-pixel BGRA representation.
    /// </remarks>
    public Channel[] ToIdArray()
    {
      var maxId = this.Max(x => x.ID);
      var idArray = new Channel[maxId + 1];
      foreach (var channel in this)
      {
        if (channel.ID >= 0)
          idArray[channel.ID] = channel;
      }
      return idArray;
    }

    public ChannelList()
      : base()
    {
    }

    public Channel GetId(int id)
    {
      return this.Single(x => x.ID == id);
    }

    public bool ContainsId(int id)
    {
      return this.Exists(x => x.ID == id);
    }
  }

  ///////////////////////////////////////////////////////////////////////////

  [DebuggerDisplay("ID = {ID}")]
  public class Channel
  {
    /// <summary>
    /// The layer to which this channel belongs
    /// </summary>
    public Layer Layer { get; private set; }

    /// <summary>
    /// Channel ID.
    /// <list type="bullet">
    /// <item>-1 = transparency mask</item>
    /// <item>-2 = user-supplied layer mask, or vector mask</item>
    /// <item>-3 = user-supplied layer mask, if channel -2 contains a vector mask</item>
    /// <item>
    /// Nonnegative channel IDs give the actual image channels, in the
    /// order defined by the colormode.  For example, 0, 1, 2 = R, G, B.
    /// </item>
    /// </list>
    /// </summary>
    public short ID { get; set; }

    public Rectangle Rect
    {
      get
      {
        switch (ID)
        {
          case -2:
            return Layer.Masks.LayerMask.Rect;
          case -3:
            return Layer.Masks.UserMask.Rect;
          default:
            return Layer.Rect;
        }
      }
    }

    /// <summary>
    /// Total length of the channel data, including compression headers.
    /// </summary>
    public int Length { get; set; }

    private byte[] data;
    private bool dataDecompressed;
    /// <summary>
    /// Compressed raw channel data, excluding compression headers.
    /// </summary>
    public byte[] Data
    {
      get { return data; }
      set
      {
        data = value;
        dataDecompressed = false;

        imageData = null;
        imageDataCompressed = true;
      }
    }

    private byte[] imageData;
    private bool imageDataCompressed;
    /// <summary>
    /// Decompressed image data from the channel.
    /// </summary>
    public byte[] ImageData
    {
      get { return imageData; } 
      set
      {
        imageData = value;
        imageDataCompressed = false;

        data = null;
        dataDecompressed = true;
      }
    }

    /// <summary>
    /// Image compression method used.
    /// </summary>
    public ImageCompression ImageCompression { get; set; }

    public byte[] RleHeader { get; set; }

    //////////////////////////////////////////////////////////////////

    internal Channel(short id, Layer layer)
    {
      ID = id;
      Layer = layer;
    }

    internal Channel(PsdBinaryReader reader, Layer layer)
    {
      Debug.WriteLine("Channel started at " + reader.BaseStream.Position.ToString(CultureInfo.InvariantCulture));
        
      ID = reader.ReadInt16();
      Length = reader.ReadInt32();
      Layer = layer;
    }

    internal void Save(PsdBinaryWriter writer)
    {
      Debug.WriteLine("Channel Save started at " + writer.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      writer.Write(ID);
      writer.Write(Length);
    }

    //////////////////////////////////////////////////////////////////

    internal void LoadPixelData(PsdBinaryReader reader)
    {
      Debug.WriteLine("Channel.LoadPixelData started at " + reader.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      var endPosition = reader.BaseStream.Position + this.Length;
      ImageCompression = (ImageCompression)reader.ReadInt16();
      imageDataCompressed = true;
      var dataLength = this.Length - 2;

      switch (ImageCompression)
      {
        case ImageCompression.Raw:
          ImageData = reader.ReadBytes(dataLength);
          break;
        case ImageCompression.Rle:
          // RLE row lengths
          RleHeader = reader.ReadBytes(2 * Rect.Height);
          var rleDataLength = dataLength - 2 * Rect.Height;

          // The PSD specification states that rows are padded to even sizes.
          // However, PSD files generated by Photoshop CS4 do not actually
          // follow this stipulation.
          Data = reader.ReadBytes(rleDataLength);
          break;
        case ImageCompression.Zip:
        case ImageCompression.ZipPrediction:
          Data = reader.ReadBytes(dataLength);
          break;
      }

      Debug.Assert(reader.BaseStream.Position == endPosition, "Pixel data successfully read in.");
    }

    public void DecompressImageData()
    {
      if (dataDecompressed)
        return;

      var rect = Rect;
      var bytesPerRow = Util.BytesPerRow(rect, Layer.PsdFile.BitDepth);
      var bytesTotal = rect.Height * bytesPerRow;

      if (this.ImageCompression != PhotoshopFile.ImageCompression.Raw)
      {
        imageData = new byte[bytesTotal];

        var stream = new MemoryStream(Data);
        switch (this.ImageCompression)
        {
          case ImageCompression.Rle:
            var rleReader = new RleReader(stream);
            for (int i = 0; i < rect.Height; i++)
            {
              int rowIndex = i * bytesPerRow;
              rleReader.Read(imageData, rowIndex, bytesPerRow);
            }
            break;

          case ImageCompression.Zip:
          case ImageCompression.ZipPrediction:
            // .NET implements Deflate (RFC 1951) but not zlib (RFC 1950),
            // so we have to skip the first two bytes.
            stream.ReadByte();
            stream.ReadByte();

            var deflateStream = new DeflateStream(stream, CompressionMode.Decompress);
            var bytesDecompressed = deflateStream.Read(imageData, 0, bytesTotal);
            Debug.Assert(bytesDecompressed == bytesTotal, "ZIP deflation output is different length than expected.");
            break;
        }
      }

      // Reverse multi-byte pixels to little-endian.  This cannot be done
      // on 32-bit depth images with ZipPrediction because the bytes have
      // been packed together.
      bool fReverseEndianness = (Layer.PsdFile.BitDepth == 16)
        || (Layer.PsdFile.BitDepth == 32) && (ImageCompression != PhotoshopFile.ImageCompression.ZipPrediction);
      if (fReverseEndianness)
        ReverseEndianness(imageData, rect);

      if (this.ImageCompression == PhotoshopFile.ImageCompression.ZipPrediction)
      {
        UnpredictImageData(rect);
      }

      dataDecompressed = true;
    }

    private void ReverseEndianness(byte[] buffer, Rectangle rect)
    {
      var byteDepth = Util.BytesFromBitDepth(Layer.PsdFile.BitDepth);
      var pixelsTotal = rect.Width * rect.Height;
      if (pixelsTotal == 0)
        return;

      if (byteDepth == 2)
      {
        Util.SwapByteArray2(buffer, 0, pixelsTotal);
      }
      else if (byteDepth == 4)
      {
        Util.SwapByteArray4(buffer, 0, pixelsTotal);
      }
      else if (byteDepth > 1)
      {
        throw new NotImplementedException("Byte-swapping implemented only for 16-bit and 32-bit depths.");
      }
    }

    /// <summary>
    /// Undo the prediction on the decompressed image data.
    /// </summary>
    unsafe private void UnpredictImageData(Rectangle rect)
    {
      if (Layer.PsdFile.BitDepth == 16)
      {
        var reorderedData = new byte[imageData.Length];
        fixed (byte* ptrData = &imageData[0])
        {
          for (int iRow = 0; iRow < rect.Height; iRow++)
          {
            UInt16* ptr = (UInt16*)(ptrData + iRow * rect.Width * 2);
            UInt16* ptrEnd = (UInt16*)(ptrData + (iRow + 1) * rect.Width * 2);

            // Start with column 1 of each row
            ptr++;
            while (ptr < ptrEnd)
            {
              *ptr = (UInt16)(*ptr + *(ptr - 1));
              ptr++;
            }
          }
        }
      }
      else if (Layer.PsdFile.BitDepth == 32)
      {
        var reorderedData = new byte[imageData.Length];
        fixed (byte* ptrData = &imageData[0]) 
        {
          // Undo the prediction on the byte stream
          for (int iRow = 0; iRow < rect.Height; iRow++)
          {
            // The rows are predicted individually.
            byte* ptr = ptrData + iRow * rect.Width * 4;
            byte* ptrEnd = ptrData + (iRow + 1) * rect.Width * 4;

            // Start with column 1 of each row
            ptr++;
            while (ptr < ptrEnd)
            {
              *ptr = (byte)(*ptr + *(ptr - 1));
              ptr++;
            }
          }

          // Within each row, the individual bytes of the 32-bit words are
          // packed together, high-order bytes before low-order bytes.
          // We now unpack them into words and reverse to little-endian.
          int offset1 = rect.Width;
          int offset2 = 2 * offset1;
          int offset3 = 3 * offset1;
          fixed (byte* dstPtrData = &reorderedData[0])
          {
            for (int iRow = 0; iRow < rect.Height; iRow++)
            {
              byte* dstPtr = dstPtrData + iRow * rect.Width * 4;
              byte* dstPtrEnd = dstPtrData + (iRow + 1) * rect.Width * 4;

              byte* srcPtr = ptrData + iRow * rect.Width * 4;

              // Reverse to little-endian as we do the unpacking.
              while (dstPtr < dstPtrEnd)
              {
                *(dstPtr++) = *(srcPtr + offset3);
                *(dstPtr++) = *(srcPtr + offset2);
                *(dstPtr++) = *(srcPtr + offset1);
                *(dstPtr++) = *srcPtr;

                srcPtr++;
              }
            }
          }
        }

        imageData = reorderedData;
      }
      else
      {
        throw new PsdInvalidException("ZIP with prediction is only available for 16 and 32 bit depths.");
      }
    }

    public void CompressImageData()
    {
      // Can be called implicitly by Layer.PrepareSave or explicitly by the
      // consumer of this library.  Since image data compression can take
      // some time, explicit calling makes more accurate progress available.
      if (imageDataCompressed)
        return;

      if (ImageCompression == ImageCompression.Rle)
      {
        var dataStream = new MemoryStream();
        var headerStream = new MemoryStream();

        var rleWriter = new RleWriter(dataStream);
        var headerWriter = new PsdBinaryWriter(headerStream, Encoding.ASCII);

        //---------------------------------------------------------------

        var rleRowLengths = new UInt16[Layer.Rect.Height];
        var bytesPerRow = Util.BytesPerRow(Layer.Rect, Layer.PsdFile.BitDepth);

        for (int row = 0; row < Layer.Rect.Height; row++)
        {
          int rowIndex = row * Layer.Rect.Width;
          rleRowLengths[row] = (UInt16)rleWriter.Write(ImageData, rowIndex, bytesPerRow);
        }

        // Write RLE row lengths and save
        for (int i = 0; i < rleRowLengths.Length; i++)
        {
          headerWriter.Write(rleRowLengths[i]);
        }
        headerStream.Flush();
        this.RleHeader = headerStream.ToArray();
        headerStream.Close();

        // Save compressed data
        dataStream.Flush();
        data = dataStream.ToArray();
        dataStream.Close();

        Length = 2 + RleHeader.Length + Data.Length;
      }
      else
      {
        data = ImageData;
        this.Length = 2 + Data.Length;
      }

      imageDataCompressed = true;
    }

    internal void SavePixelData(PsdBinaryWriter writer)
    {
      Debug.WriteLine("Channel SavePixelData started at " + writer.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      writer.Write((short)ImageCompression);
      if (Data == null)
        return;
      
      if (ImageCompression == PhotoshopFile.ImageCompression.Rle)
        writer.Write(this.RleHeader);
      writer.Write(Data);
    }

  }
}