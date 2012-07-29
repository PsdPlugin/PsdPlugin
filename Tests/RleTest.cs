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
using System.Linq;

using NUnit.Framework;

using PhotoshopFile;

namespace Tests
{
  [TestFixture]
  public class RleTest
  {
    private const int rowCount = 200;
    private const int bytesPerRow = 1000;

    [Test]
    public void RleReaderTest()
    {
      var testData = new TestData(rowCount, bytesPerRow);
      byte[] decodedData = null;
      try
      {
        decodedData = DecodeRleData(testData.RleData, testData.DataLengths);
      }
      catch (Exception e)
      {
        Assert.Fail("Failed with seed = " + testData.Seed + "\n" + e.ToString());
      }

      Assert.AreEqual(testData.Data, decodedData,
        "Decoded RLE stream differs from original data, seed = " + testData.Seed);
    }

    [Test]
    public void RleWriterTest()
    {
      var testData = new TestData(rowCount, bytesPerRow);
      byte[] encodedData = null;
      byte[] decodedData = null;
      try
      {
        // We cannot verify the encoded data directly, as there are multiple
        // RLE representations depending on the algorithm used.  So we will
        // encode then decode, and compare the decoded data.  The RleReaderTest
        // verifies that decoding is working properly, so all we're testing
        // is the encoding.
        var rleStream = new MemoryStream();
        var rleWriter = new RleWriter(rleStream);
        var offset = 0;
        for (int i = 0; i < testData.DataLengths.Length; i++)
        {
          var dataLength = testData.DataLengths[i];
          rleWriter.Write(testData.Data, offset, dataLength);
          offset += dataLength;
        }

        rleStream.Flush();
        encodedData = rleStream.ToArray();
        decodedData = DecodeRleData(encodedData, testData.DataLengths);
      }
      catch (Exception e)
      {
        Assert.Fail("Failed with seed = " + testData.Seed + "\n" + e.ToString());
      }

      Assert.AreEqual(testData.Data, decodedData,
        "Decoded RLE stream differs from original data, seed = " + testData.Seed);
    }

    private byte[] DecodeRleData(byte[] rleData, int[] dataLengths)
    {
      var totalDataLength = dataLengths.Sum();
      var data = new byte[totalDataLength];

      var rleStream = new MemoryStream(rleData);
      var rleReader = new RleReader(rleStream);

      int offset = 0;
      for (int i = 0; i < dataLengths.Length; i++)
      {
        var count = dataLengths[i];
        rleReader.Read(data, offset, count);
        offset += count;
      }

      return data;
    }

    /// <summary>
    /// Generate RLE data to test with.
    /// </summary>
    unsafe private class TestData
    {
      public byte[] Data { get; set; }
      public int[] DataLengths { get; set; }
      public byte[] RleData { get; set; }
      public int Seed { get; set; }

      Random random;

      public TestData(int rowCount, int bytesPerRow)
      {
        Data = new byte[rowCount * bytesPerRow];
        DataLengths = new int[rowCount];

        var rleStream = new MemoryStream();
        fixed (byte* ptrDataStart = &Data[0])
        {
          byte* ptrData = ptrDataStart;

          // Makes it possible to investigate failures with the randomized data
          Seed = (int)(DateTime.Now.Ticks % Int32.MaxValue);
          random = new Random(Seed);

          // Start first row with a pattern to test all code paths.
          WriteRepeatedBytes(ref ptrData, rleStream, 1, 128);
          WriteRepeatedBytes(ref ptrData, rleStream, 1, 72);
          WriteRawBytes(ref ptrData, rleStream, 2, 100);
          var b = WriteRawBytes(ref ptrData, rleStream, 2, 50);
          WriteRepeatedBytes(ref ptrData, rleStream, b, 50);  // Repeat last byte
          WriteRawBytes(ref ptrData, rleStream, (byte)(b + 1), 1);
          WriteRepeatedBytes(ref ptrData, rleStream, (byte)(b + 2), 1);
          b = WriteRawBytes(ref ptrData, rleStream, (byte)(b + 3), 128);
          b = WriteRawBytes(ref ptrData, rleStream, (byte)(b + 1), 128);
          b = WriteRawBytes(ref ptrData, rleStream, b, 128);  // Repeat last byte
          WriteRepeatedBytes(ref ptrData, rleStream, b, 128);
          WriteRepeatedBytes(ref ptrData, rleStream, b, 86);
          DataLengths[0] = (int)(ptrData - ptrDataStart);

          // Prevent buffer overrun in case we change the bytesPerRow without
          // adjusting the test.
          Assert.That(DataLengths[0] == bytesPerRow,
            "First row was generated with an incorrect data length.");

          // Write remaining rows at random
          for (int idxRow = 1; idxRow < rowCount; idxRow++)
          {
            byte* ptrRow = ptrData;
            var bytesRemaining = bytesPerRow;
            var startPosition = rleStream.Position;
            int count;

            while (bytesRemaining > 0)
            {
              b = (byte)random.Next(255);

              var rPacket = random.Next(4);
              switch (rPacket)
              {
                // Single-byte repeated
                case 0:
                  WriteRepeatedBytes(ref ptrData, rleStream, b, 1);
                  bytesRemaining--;
                  break;

                // Single-byte raw
                case 1:
                  WriteRawBytes(ref ptrData, rleStream, b, 1);
                  bytesRemaining--;
                  break;

                // Multi-byte repeated
                case 2:
                  count = random.Next(199) + 1;
                  if (count > 128)
                    count = 128;
                  count = Math.Min(count, bytesRemaining);
                  WriteRepeatedBytes(ref ptrData, rleStream, b, count);
                  bytesRemaining -= count;
                  break;

                // Multi-byte raw
                case 3:
                  count = random.Next(199) + 1;
                  if (count > 128)
                    count = 128;
                  count = Math.Min(count, bytesRemaining);
                  WriteRawBytes(ref ptrData, rleStream, b, count);
                  bytesRemaining -= count;
                  break;
              }
            }

            Assert.That((ptrData - ptrRow) == bytesPerRow,
              "Randomized row was generated with an incorrect data length.");
            DataLengths[idxRow] = bytesPerRow;
          }   // Loop over rows
        }   // Fixed pointer

        RleData = rleStream.ToArray();
      }


      /// <summary>
      /// Write a repeated byte.
      /// </summary>
      /// <param name="byte">Byte to write repeatedly to RLE and raw data buffers.</param>
      /// <param name="count">Number of times the byte repeats.</param>
      unsafe private void WriteRepeatedBytes (ref byte* ptrData,
        MemoryStream rleStream, byte b, int count)
      {
        if (count > 128)
          throw new ArgumentOutOfRangeException("count");

        // Write RLE packet
        rleStream.WriteByte(unchecked((byte)(1 - count)));
        rleStream.WriteByte(b);

        // Write repeated bytes to data
        byte* ptrDataEnd = ptrData + count;
        while (ptrData < ptrDataEnd)
        {
          *ptrData = b;
          ptrData++;
        }
      }

      /// <summary>
      /// Write non-repeated byte data.
      /// </summary>
      /// <param name="byte">Byte to start with.</param>
      /// <param name="count">Number of unique bytes to write.</param>
      /// <returns>Last byte written.</returns>
      unsafe private byte WriteRawBytes(ref byte* ptrData,
        MemoryStream rleStream, byte b, int count)
      {
        if (count > 128)
          throw new ArgumentOutOfRangeException("count");

        // Write raw header for packet
        var header = unchecked((byte)(count - 1));
        rleStream.WriteByte(header);

        // Write data
        byte* ptrDataEnd = ptrData + count;
        while (ptrData < ptrDataEnd)
        {
          *ptrData = b;
          rleStream.WriteByte(b);

          // Get a random byte, but make sure it is not repeated.
          var r = (byte)random.Next(256);
          if (r == b)
            r++;
          b = r;

          ptrData++;
        }

        var lastByte = *(ptrData - 1);
        return lastByte;
      }
    }
  }
}
