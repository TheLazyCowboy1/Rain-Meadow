using K4os.Compression.LZ4;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace RainMeadow
{
    internal static class Utils
    {
        public static void Restart(string args = "")
        {
            Process currentProcess = Process.GetCurrentProcess();
            string text = "\"" + currentProcess.MainModule.FileName + "\"";
            IDictionary environmentVariables = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Process);
            List<string> list = new List<string>();
            foreach (object obj in environmentVariables)
            {
                DictionaryEntry dictionaryEntry = (DictionaryEntry)obj;
                if (dictionaryEntry.Key.ToString().StartsWith("DOORSTOP"))
                {
                    list.Add(dictionaryEntry.Key.ToString());
                }
            }
            foreach (string text2 in list)
            {
                environmentVariables.Remove(text2);
            }
            ProcessStartInfo processStartInfo = new ProcessStartInfo();
            processStartInfo.EnvironmentVariables.Clear();
            foreach (object obj2 in environmentVariables)
            {
                DictionaryEntry dictionaryEntry2 = (DictionaryEntry)obj2;
                processStartInfo.EnvironmentVariables.Add((string)dictionaryEntry2.Key, (string)dictionaryEntry2.Value);
            }
            processStartInfo.UseShellExecute = false;
            processStartInfo.FileName = text;
            processStartInfo.Arguments = args;
            Process.Start(processStartInfo);
            Application.Quit();
        }

        /// <summary>
        /// Adds a range of items to a list, excluding items which are already in the list.
        /// </summary>
        /// <param name="self">The list to add to.</param>
        /// <param name="items">The range of items to add.</param>
        public static void AddDistinctRange<T>(this IList<T> self, IEnumerable<T> items)
        {
            foreach(var item in items)
            {
                if (self.Contains(item))
                {
                    continue;
                }

                self.Add(item);
            }
        }

        public static byte[] Compress(byte[] bytes)
        {
            if (bytes.Length < 8) return bytes;

            byte[] output = K4os.Compression.LZ4.Legacy.LZ4Wrapper.Wrap(bytes, 0, bytes.Length, GetCompressionLevel(bytes.Length));
            if (bytes.Length > CompressionThreshold * 3)
                RainMeadow.Debug($"Compressed bytes {bytes.Length} into {output.Length}");
            return output;
        }

        public static int CompressionThreshold = 400; //this means that high compression begins at 1200; maximal compression begins at 4800
        private static LZ4Level GetCompressionLevel(int length)
        {
            int idx = length / CompressionThreshold;
            if (idx < 3) return 0; //compressions 0, 1, and 2 are all the same
            if (idx > 12) return (LZ4Level)12; //if above max; set to max
            return (LZ4Level)idx;
        }

        public static byte[]? Decompress(byte[]? bytes)
        {
            if (bytes == null) return null;
            if (bytes.Length < 8) return bytes;

            byte[] output = K4os.Compression.LZ4.Legacy.LZ4Wrapper.Unwrap(bytes);
            if (bytes.Length > CompressionThreshold * 3)
                RainMeadow.Debug($"Decompressed bytes {bytes.Length} into {output.Length}");
            return output;
            /*
            int formerLength = bytes.Length;
            using (var outStream = new MemoryStream())
            using (var compressStream = new MemoryStream(bytes))
            using (var decompressor = K4os.Compression.LZ4.Streams.LZ4Stream.Decode(compressStream))
            {
                decompressor.CopyTo(outStream);
                var output = outStream.ToArray();
                RainMeadow.Trace($"Decompressed {formerLength} bytes to {output.Length} bytes.");
                return output;
            }
            */
        }
    }
}
