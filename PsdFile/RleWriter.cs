/////////////////////////////////////////////////////////////////////////////////
//
// Photoshop PSD FileType Plugin for Paint.NET
// http://psdplugin.codeplex.com/
//
// This software is provided under the MIT License:
//   Copyright (c) 2006-2007 Frank Blumenberg
//   Copyright (c) 2010-2012 Tao Yue
//
// Portions of this file are provided under the BSD 3-clause License:
//   Copyright (c) 2006, Jonas Beckeman
//
// See LICENSE.txt for complete licensing and attribution information.
//
/////////////////////////////////////////////////////////////////////////////////

using System;
using System.Diagnostics;
using System.IO;


namespace PhotoshopFile
{
  public class RleWriter
  {
    private int maxPacketLength = 128;

    // Current task
    private object rleLock;
    private Stream stream;
    private byte[] data;
    private int offset;

    // Current packet
    private bool rlePacket;
    private int packetLength;
    private int idxDataRawPacket;
    private byte lastValue;

    public RleWriter(Stream stream)
    {
      rleLock = new object();
      this.stream = stream;
    }

    /// <summary>
    /// Encode byte data as an RLE stream.
    /// </summary>
    /// <param name="data">Raw data to be encoded.</param>
    /// <param name="offset">Offset at which to begin transferring data.</param>
    /// <param name="count">Number of bytes of data to transfer.</param>
    /// <returns>Number of RLE-encoded bytes written to the stream.</returns>
    unsafe public int Write(byte[] data, int offset, int count)
    {
      if (!Util.CheckBufferBounds(data, offset, count))
        throw new ArgumentOutOfRangeException();

      // We cannot encode a count of 0, because the RLE packet header uses 0 to
      // mean a length of 1.
      if (count == 0)
        throw new ArgumentOutOfRangeException("count");

      lock (rleLock)
      {
        var startPosition = stream.Position;

        this.data = data;
        this.offset = offset;
        fixed (byte* ptrData = &data[0])
        {
          byte* ptr = ptrData + offset;
          byte* ptrEnd = ptr + count;
          var bytesEncoded = EncodeToStream(ptr, ptrEnd);
          Debug.Assert(bytesEncoded == count, "Encoded byte count should match the argument.");
        }

        return (int)(stream.Position - startPosition);
      }
    }

    private void ClearPacket()
    {
      this.rlePacket = false;
      this.packetLength = 0;
    }

    private void WriteRlePacket()
    {
      var header = unchecked((byte)(1 - packetLength));
      stream.WriteByte(header);
      stream.WriteByte(lastValue);
    }

    private void WriteRawPacket()
    {
      var header = unchecked((byte)(packetLength - 1));
      stream.WriteByte(header);
      stream.Write(data, idxDataRawPacket, packetLength);
    }

    private void WritePacket()
    {
      if (rlePacket)
        WriteRlePacket();
      else
        WriteRawPacket();
    }

    unsafe private int EncodeToStream(byte* ptr, byte* ptrEnd)
    {
      idxDataRawPacket = offset;

      // Begin the first packet.
      rlePacket = false;
      lastValue = *ptr;
      packetLength = 1;

      ptr++;
      int totalLength = 1;

      // Loop invariant: Packet is never empty.
      while (ptr < ptrEnd)
      {
        byte color = *ptr;

        if (packetLength == 1)
        {
          // Second byte; decide whether packet will be RLE or raw.
          rlePacket = (color == lastValue);
          lastValue = color;
          packetLength = 2;
        }
        else if (packetLength == maxPacketLength)
        {
          // Packet is full.  Emit it and start a new one.
          WritePacket();

          rlePacket = false;
          lastValue = color;
          idxDataRawPacket = offset + totalLength;
          packetLength = 1;
        }
        else if (rlePacket)
        {
          // Decide whether to continue the RLE packet.
          if (color == lastValue)
          {
            // Same color is found, so lengthen the run.
            packetLength++;
          }
          else
          {
            // Different color, so terminate the run and start a new packet.
            WriteRlePacket();

            rlePacket = false;
            lastValue = color;
            idxDataRawPacket = offset + totalLength;
            packetLength = 1;
          }
        }    // RLE packet
        else
        {
          // Decide whether to continue the raw packet.
          if (color == lastValue)
          {
            // Last color repeats, so emit the current packet without the last
            // color, and start a new RLE packet with initial run-length of 2.
            packetLength--;
            WriteRawPacket();

            rlePacket = true;
            packetLength = 2;
            // idxDataRawPacket need not be adjusted, as this is an RLE packet
          }
          else
          {
            // Last color does not repeat, so incorporate it into the current
            // raw packet.
            lastValue = color;
            packetLength++;
          }
        }    // Raw packet

        ptr++;
        totalLength++;
      }

      // Loop terminates with a non-empty packet waiting to be written out.
      WritePacket();
      ClearPacket();

      return totalLength;
    }
  }
}
