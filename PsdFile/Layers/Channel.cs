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

    /// <summary>
    /// Raw image data for this color channel, in compressed on-disk format.
    /// </summary>
    /// <remarks>
    /// If null, the ImageData will be automatically compressed during save.
    /// </remarks>
    public byte[] ImageDataRaw { get; set; }

    /// <summary>
    /// Decompressed image data for this color channel.
    /// </summary>
    /// <remarks>
    /// When making changes to the ImageData, set ImageDataRaw to null so that
    /// the correct data will be compressed during save.
    /// </remarks>
    public byte[] ImageData { get; set; }

    /// <summary>
    /// Image compression method used.
    /// </summary>
    public ImageCompression ImageCompression { get; set; }

    /// <summary>
    /// RLE-compressed length of each row.
    /// </summary>
    public RleRowLengths RleRowLengths { get; set; }

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
      var dataLength = this.Length - 2;

      switch (ImageCompression)
      {
        case ImageCompression.Raw:
          ImageDataRaw = reader.ReadBytes(dataLength);
          break;
        case ImageCompression.Rle:
          // RLE row lengths
          RleRowLengths = new RleRowLengths(reader, Rect.Height);
          var rleDataLength = (int)(endPosition - reader.BaseStream.Position);
          Debug.Assert(rleDataLength == RleRowLengths.Total,
            "RLE row lengths do not sum to length of channel image data.");

          // The PSD specification states that rows are padded to even sizes.
          // However, Photoshop doesn't actually do this.  RLE rows can have
          // odd lengths in the header, and there is no padding between rows.
          ImageDataRaw = reader.ReadBytes(rleDataLength);
          break;
        case ImageCompression.Zip:
        case ImageCompression.ZipPrediction:
          ImageDataRaw = reader.ReadBytes(dataLength);
          break;
      }

      Debug.Assert(reader.BaseStream.Position == endPosition,
        "Pixel data was not fully read in.");
    }

    /// <summary>
    /// Decodes the raw image data from the compressed on-disk format into
    /// an uncompressed bitmap, in native byte order.
    /// </summary>
    public void DecodeImageData()
    {
      if (this.ImageCompression == ImageCompression.Raw)
        ImageData = ImageDataRaw;
      else
        DecompressImageData();

      // Rearrange the decompressed bytes into words, with native byte order.
      if (ImageCompression == ImageCompression.ZipPrediction)
        UnpredictImageData(Rect);
      else
        ReverseEndianness(ImageData, Rect);
    }

    private void DecompressImageData()
    {
      using (var stream = new MemoryStream(ImageDataRaw))
      {
        var bytesPerRow = Util.BytesPerRow(Rect, Layer.PsdFile.BitDepth);
        var bytesTotal = Rect.Height * bytesPerRow;
        ImageData = new byte[bytesTotal];

        switch (this.ImageCompression)
        {
          case ImageCompression.Rle:
            var rleReader = new RleReader(stream);
            for (int i = 0; i < Rect.Height; i++)
            {
              int rowIndex = i * bytesPerRow;
              rleReader.Read(ImageData, rowIndex, bytesPerRow);
            }
            break;

          case ImageCompression.Zip:
          case ImageCompression.ZipPrediction:

            // .NET implements Deflate (RFC 1951) but not zlib (RFC 1950),
            // so we have to skip the first two bytes.
            stream.ReadByte();
            stream.ReadByte();

            var deflateStream = new DeflateStream(stream, CompressionMode.Decompress);
            var bytesDecompressed = deflateStream.Read(ImageData, 0, bytesTotal);
            Debug.Assert(bytesDecompressed == bytesTotal,
              "ZIP deflation output is different length than expected.");
            break;

          default:
            throw new PsdInvalidException("Unknown image compression method.");
        }
      }
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
    /// Unpredicts the raw decompressed image data into a little-endian
    /// scanline bitmap.
    /// </summary>
    unsafe private void UnpredictImageData(Rectangle rect)
    {
      if (Layer.PsdFile.BitDepth == 16)
      {
        // 16-bitdepth images are delta-encoded word-by-word.  The deltas
        // are thus big-endian and must be reversed for further processing.
        ReverseEndianness(ImageData, rect);

        fixed (byte* ptrData = &ImageData[0])
        {
          // Delta-decode each row
          for (int iRow = 0; iRow < rect.Height; iRow++)
          {
            UInt16* ptr = (UInt16*)(ptrData + iRow * rect.Width * 2);
            UInt16* ptrEnd = (UInt16*)(ptrData + (iRow + 1) * rect.Width * 2);

            // Start with column index 1 on each row
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
        var reorderedData = new byte[ImageData.Length];
        fixed (byte* ptrData = &ImageData[0]) 
        {
          // Delta-decode each row
          for (int iRow = 0; iRow < rect.Height; iRow++)
          {
            byte* ptr = ptrData + iRow * rect.Width * 4;
            byte* ptrEnd = ptrData + (iRow + 1) * rect.Width * 4;

            // Start with column index 1 on each row
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

        ImageData = reorderedData;
      }
      else
      {
        throw new PsdInvalidException("ZIP with prediction is only available for 16 and 32 bit depths.");
      }
    }

    /// <summary>
    /// Compresses the image data.
    /// </summary>
    public void CompressImageData()
    {
      // Do not recompress if compressed data is already present.
      if (ImageDataRaw != null)
        return;

      if (ImageData == null)
        return;

      if (ImageCompression == ImageCompression.Raw)
      {
        ImageDataRaw = ImageData;
        this.Length = 2 + ImageDataRaw.Length;
      }
      else if (ImageCompression == ImageCompression.Rle)
      {
        RleRowLengths = new RleRowLengths(Layer.Rect.Height);

        using (var dataStream = new MemoryStream())
        {
          var rleWriter = new RleWriter(dataStream);
          var bytesPerRow = Util.BytesPerRow(Layer.Rect, Layer.PsdFile.BitDepth);
          for (int row = 0; row < Layer.Rect.Height; row++)
          {
            int rowIndex = row * Layer.Rect.Width;
            RleRowLengths[row] = rleWriter.Write(
              ImageData, rowIndex, bytesPerRow);
          }

          // Save compressed data
          dataStream.Flush();
          ImageDataRaw = dataStream.ToArray();
          Debug.Assert(RleRowLengths.Total == ImageDataRaw.Length,
            "RLE row lengths do not sum to the compressed data length.");
        }
        Length = 2 + 2 * Layer.Rect.Height + ImageDataRaw.Length;
      }
      else
      {
        throw new NotImplementedException("Only raw and RLE compression have been implemented.");
      }
    }

    internal void SavePixelData(PsdBinaryWriter writer)
    {
      Debug.WriteLine("Channel SavePixelData started at " + writer.BaseStream.Position.ToString(CultureInfo.InvariantCulture));

      writer.Write((short)ImageCompression);
      if (ImageDataRaw == null)
        return;
      
      if (ImageCompression == PhotoshopFile.ImageCompression.Rle)
        RleRowLengths.Write(writer);
      writer.Write(ImageDataRaw);
    }

  }
}