using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ImageLoader
{
    

    /// <summary>
    /// Loads simple binary image files such as Intel HEX, Motorola S-Record.
    /// </summary>
    public static class SimpleImageLoader
    {
        /// <summary>
        /// Load a image from the stream.
        /// </summary>
        /// <param name="stream">A stream to read a image.</param>
        /// <param name="cancellationToken">CancellationToken to cancel loading operation.</param>
        /// <returns>An instance of SparseImage which contains data of the loaded image.</returns>
        public static async Task<SparseImage<byte>> LoadAsync(Stream stream, CancellationToken cancellationToken)
        {
            var signature = new byte[1];
            var bytesRead = await stream.ReadAsync(signature, 0, 1, cancellationToken);
            stream.Seek(-bytesRead, SeekOrigin.Current);
            if (signature[0] == 58)
                return await SimpleImageLoader.LoadHexAsync(stream, cancellationToken);
            else
                throw new NotSupportedException("Unknown format.");
        }

        /// <summary>
        /// Load a Intel HEX image from the stream.
        /// </summary>
        /// <param name="stream">A stream to read a image.</param>
        /// <param name="cancellationToken">CancellationToken to cancel loading operation.</param>
        /// <returns>An instance of SparseImage which contains data of the loaded image.</returns>
        public static async Task<SparseImage<byte>> LoadHexAsync(Stream stream, CancellationToken cancellationToken)
        {
            var streamReader = new StreamReader(stream);
            var memory = new SparseImage<byte>();

            var segment = 0;
            var upperLinearAddress = 0u;
            var useLinearAddress = false;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await streamReader.ReadLineAsync();
                if (line[0] != ':')
                    throw new InvalidDataException("Invalid record.");

                // Sum of all bytes in a record including checksum becomes zero
                // because checksum of Intel HEX record is two's complement.
                if (SimpleImageLoader.CalculateChecksum(line.Substring(1)) != 0)
                    throw new InvalidDataException("CalculateChecksum error");

                // Intel-HEX Format
                // : | (LEN) | (OFFSET) | (TYPE) | (DATA) | (CHECKSUM) |
                // 1 |   2   |    4     |    2   |    ?   |     2      |
                var length = int.Parse(line.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
                var offset = int.Parse(line.Substring(3, 4), System.Globalization.NumberStyles.HexNumber);
                var type = int.Parse(line.Substring(7, 2), System.Globalization.NumberStyles.HexNumber);
                var data = line.Substring(9, length * 2);

                switch (type)
                {
                    case 1: // End-Record
                        return memory;
                    case 0: // Data-Record
                        for (int i = 0; i < data.Length; i += 2)
                        {
                            ulong address;
                            if (useLinearAddress)
                            {
                                address = upperLinearAddress | (uint) ((i >> 1) + offset);
                            }
                            else
                            {
                                address = (uint) ((i >> 1) + offset + segment);
                            }
                            memory[address] = byte.Parse(data.Substring(i, 2), System.Globalization.NumberStyles.HexNumber);
                        }
                        break;
                    case 2: // Segment Address Record
                        segment = int.Parse(data, System.Globalization.NumberStyles.HexNumber) << 4;
                        useLinearAddress = false;
                        break;
                    case 3: // Start Address Record
                        // nothing to do for this record
                        break;
                    case 4: // Extended Linear Address Record
                        upperLinearAddress = uint.Parse(data, System.Globalization.NumberStyles.HexNumber) << 16;
                        useLinearAddress = true;
                        break;
                    case 5:
                        // nothing to do for this record
                        break;
                    default:
                        throw new InvalidDataException("Invalid record.");
                }
            }
        }

        /// <summary>
        /// Calculate checksum of a row.
        /// </summary>
        /// <param name="data">String of a row</param>
        /// <returns></returns>
        private static int CalculateChecksum(string data)
        {
            var sum = 0;

            for (int i = 0; i < data.Length; i += 2)
            {
                var dtb = data.Substring(i, 2);
                sum += int.Parse(dtb, System.Globalization.NumberStyles.HexNumber);
                sum &= 0xff;
            }

            return sum;
        }
    }
}
