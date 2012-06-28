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
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;


namespace PhotoshopFile
{
  public class RleHelper
  {
    ////////////////////////////////////////////////////////////////////////

    private class RlePacketStateMachine
    {
      private bool rlePacket = false;
      private byte lastValue;
      private int idxPacketData;
      private int packetLength;
      private int maxPacketLength = 128;
      private Stream stream;
      private byte[] data;

      internal void Flush()
      {
        byte header;
        if (rlePacket)
        {
          header = (byte)(-(packetLength - 1));
          stream.WriteByte(header);
          stream.WriteByte(lastValue);
        }
        else
        {
          header = (byte)(packetLength - 1);
          stream.WriteByte(header);
          stream.Write(data, idxPacketData, packetLength);
        }

        packetLength = 0;
      }

      internal void PushRow(byte[] imgData, int startIdx, int endIdx)
      {
        data = imgData;
        for (int i = startIdx; i < endIdx; i++)
        {
          byte color = imgData[i];
          if (packetLength == 0)
          {
            // Starting a fresh packet.
            rlePacket = false;
            lastValue = color;
            idxPacketData = i;
            packetLength = 1;
          }
          else if (packetLength == 1)
          {
            // 2nd byte of this packet... decide RLE or non-RLE.
            rlePacket = (color == lastValue);
            lastValue = color;
            packetLength = 2;
          }
          else if (packetLength == maxPacketLength)
          {
            // Packet is full. Start a new one.
            Flush();
            rlePacket = false;
            lastValue = color;
            idxPacketData = i;
            packetLength = 1;
          }
          else if (packetLength >= 2 && rlePacket && color != lastValue)
          {
            // We were filling in an RLE packet, and we got a non-repeated color.
            // Emit the current packet and start a new one.
            Flush();
            rlePacket = false;
            lastValue = color;
            idxPacketData = i;
            packetLength = 1;
          }
          else if (packetLength >= 2 && rlePacket && color == lastValue)
          {
            // We are filling in an RLE packet, and we got another repeated color.
            // Add the new color to the current packet.
            ++packetLength;
          }
          else if (packetLength >= 2 && !rlePacket && color != lastValue)
          {
            // We are filling in a raw packet, and we got another random color.
            // Add the new color to the current packet.
            lastValue = color;
            ++packetLength;
          }
          else if (packetLength >= 2 && !rlePacket && color == lastValue)
          {
            // We were filling in a raw packet, but we got a repeated color.
            // Emit the current packet without its last color, and start a
            // new RLE packet that starts with a length of 2.
            --packetLength;
            Flush();
            rlePacket = true;
            packetLength = 2;
            lastValue = color;
          }
        }

        Flush();
      }

      internal RlePacketStateMachine(Stream stream)
      {
        this.stream = stream;
      }
    }

    ////////////////////////////////////////////////////////////////////////

    public static int EncodeRow(Stream stream, byte[] imgData, int startIdx, int columns)
    {
      var startPosition = stream.Position;

      var machine = new RlePacketStateMachine(stream);
      machine.PushRow(imgData, startIdx, startIdx + columns);

      return (int)(stream.Position - startPosition);
    }

    ////////////////////////////////////////////////////////////////////////

    public static void DecodeRow(Stream stream, byte[] imgData, int startIdx, int columns)
    {
      int count = 0;
      while (count < columns)
      {
        byte byteValue = (byte)stream.ReadByte();

        int len = (int)byteValue;
        if (len < 128)
        {
          len++;
          while (len != 0 && (startIdx + count) < imgData.Length)
          {
            byteValue = (byte)stream.ReadByte();

            imgData[startIdx + count] = byteValue;
            count++;
            len--;
          }
        }
        else if (len > 128)
        {
          // Next -len+1 bytes in the dest are replicated from next source byte.
          // (Interpret len as a negative 8-bit int.)
          len ^= 0x0FF;
          len += 2;
          byteValue = (byte)stream.ReadByte();

          while (len != 0 && (startIdx + count) < imgData.Length)
          {
            imgData[startIdx + count] = byteValue;
            count++;
            len--;
          }
        }
        else if (128 == len)
        {
          // Do nothing
        }
      }

    }
  }

}
