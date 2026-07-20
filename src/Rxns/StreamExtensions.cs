using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace System.IO
{
    /// <summary>
    /// Extensions for the stream class
    /// </summary>
    public static class StreamExtensions
    {
        public static Stream ToStream(this byte[] bytes)
        {
            var asStream = new MemoryStream(bytes.Length);
            asStream.Write(bytes, 0, bytes.Length);
            asStream.Position = 0;

            return asStream;
        }

        /// <summary>
        /// Reads the bytes of 2 streams, computing a hash along the way, in order to determin if they are equal
        /// at a byte level.
        /// </summary>
        /// <param name="source">A stream to compare</param>
        /// <param name="destination">A stream to compare</param>
        /// <returns>If the streams bytes are equivalent</returns>
        public static bool IsEquaTo(this Stream source, Stream destination)
        {
            return source.ComputeHash() == destination.ComputeHash();
        }

        /// <summary>
        /// Computes an MD5 has on the source stream
        /// </summary>
        /// <param name="source">The stream to hash</param>
        /// <returns>The hash</returns>
        public static string ComputeHash(this Stream source)
        {
            var hash = source.ComputeMd5AsBytes();
            return hash.ToHash();
        }

        public static string ToHash(this byte[] bytesToHash)
        {
            return BitConverter.ToString(bytesToHash); //.Replace("-", string.Empty).ToLower();
        }

        public static byte[] FromHash(this string hashedBytes)
        {

            //if(hashedBytes.IsNullOrWhitespace())
            //  hashedBytes = hashedBytes.Replace("-", string.Empty).ToLower();

            var length = (hashedBytes.Length + 1) / 3;
            var asBytes = new byte[length];
            for (var i = 0; i < length; i++)
                asBytes[i] = Convert.ToByte(hashedBytes.Substring(3 * i, 2), 16);

            return asBytes;
        }

        public static byte[] ComputeMd5AsBytes(this Stream source)
        {
            HashAlgorithm ha = new MD5CryptoServiceProvider();

            using (var buffered = new BufferedStream(source, 1200000))
            {
                var hash = ha.ComputeHash(buffered);
                return hash;
            }
        }

        public static byte[] ComputeMd5AsBytes(this byte[] source)
        {
            HashAlgorithm ha = new MD5CryptoServiceProvider();
            var hash = ha.ComputeHash(source);
            return hash;
        }

        public static string ComputeHash(this byte[] toHash)
        {
            var hash = toHash.ComputeMd5AsBytes();
            return hash.ToHash();
        }

        public static string ComputeMd5(this byte[] toHash)
        {
            return toHash.ComputeHash();
        }

        public static string AsBase64(this string source)
        {
            if (source == null) return null;
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(source));
        }
        public static string AsBase64(this byte[] source)
        {
            if (source == null) return null;
            return Convert.ToBase64String(source);
        }

        public static byte[] FromBase64(this string source)
        {
            return Convert.FromBase64String(source);
        }

        public static string FromBase64AsString(this string source)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(source));
        }

        public static string ComputeMd5(this Stream source)
        {
            return source.ComputeHash();
        }


        public static string ComputeHash(this string source)
        {
            return ComputeHash(Encoding.UTF8.GetBytes(source));
        }

        /// <summary>
        /// SHA-256 of a stream as a lowercase hex string (64 chars).
        /// Used by content-addressed update storage (FileSystemAppUpdateRepo)
        /// to dedup byte-identical uploads across versions.
        /// </summary>
        public static string Sha256(this Stream source)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                using (var buffered = new BufferedStream(source, 1200000))
                {
                    return ToHexLower(sha.ComputeHash(buffered));
                }
            }
        }

        /// <summary>SHA-256 of a byte array as lowercase hex.</summary>
        public static string Sha256(this byte[] source)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                return ToHexLower(sha.ComputeHash(source));
            }
        }

        /// <summary>SHA-256 of a UTF-8 string as lowercase hex.</summary>
        public static string Sha256(this string source)
        {
            return Sha256(Encoding.UTF8.GetBytes(source));
        }

        private static string ToHexLower(byte[] bytes)
        {
            var c = new char[bytes.Length * 2];
            for (int i = 0, j = 0; i < bytes.Length; i++)
            {
                var b = bytes[i];
                c[j++] = (char)((b >> 4) > 9 ? (b >> 4) + 0x57 : (b >> 4) + 0x30);
                c[j++] = (char)((b & 0xF) > 9 ? (b & 0xF) + 0x57 : (b & 0xF) + 0x30);
            }
            return new string(c);
        }

    }
}

