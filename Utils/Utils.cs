using K4os.Compression.LZ4;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
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

        private static FileStream? stream = null; //temporarily implemented for debugging graphs
        public static byte[] Compress(byte[] bytes)
        {
            return bytes;
            /*
            using (MemoryStream compressStream = new())
            using (DeflateStream compressor = new(compressStream, CompressionMode.Compress))
            {
                compressor.Read(bytes, 0, bytes.Length);
                compressor.Close();
                var output = compressStream.ToArray();
                //try writing debug file
                try
                {
                    if (OnlineManager.lobby != null && OnlineManager.lobby.isOwner)
                    {
                        if (bytes.Length > 128)
                        {
                            if (stream == null)
                            {
                                stream = File.OpenWrite(AssetManager.ResolveFilePath("compressionChart.csv", true));
                                stream.Seek(stream.Length, SeekOrigin.End); //move to end; we're appending
                            }
                            string str = $"{bytes.Length},{output.Length}\n";
                            var arr = str.ToCharArray().Select(c => (byte)c).ToArray();
                            stream.Write(arr, 0, arr.Length);
                            //just let it run indefinitely
                        }
                    }
                    else if (stream != null) { stream.Close(); stream = null; } //close stream if it exists
                }
                catch { }
                return output;
            }
            */
            
            if (bytes.Length < 8) return bytes;

            byte[] output = K4os.Compression.LZ4.Legacy.LZ4Wrapper.Wrap(bytes, 0, bytes.Length, GetCompressionLevel(bytes.Length));
            if (bytes.Length > CompressionThreshold)
                RainMeadow.Debug($"Compressed bytes {bytes.Length} into {output.Length}");

            //try writing debug file
            try
            {
                if (OnlineManager.lobby != null && OnlineManager.lobby.isOwner)
                {
                    if (bytes.Length > 128)
                    {
                        if (stream == null)
                        {
                            stream = File.OpenWrite(AssetManager.ResolveFilePath("compressionChart.csv", true));
                            stream.Seek(stream.Length, SeekOrigin.End); //move to end; we're appending
                        }
                        string str = $"{bytes.Length},{output.Length}\n";
                        var arr = str.ToCharArray().Select(c => (byte)c).ToArray();
                        stream.Write(arr, 0, arr.Length);
                        //just let it run indefinitely
                    }
                }
                else if (stream != null) { stream.Close(); stream = null; } //close stream if it exists
            }
            catch { }

            return output;

        }

        public static int CompressionThreshold = 1024; //begins high compression; each higher level requires double the previous requirement
        //public static int CompressionThreshold = 400;
        private static LZ4Level GetCompressionLevel(int length)
        {
            //return LZ4Level.L09_HC;
            int idx = length / CompressionThreshold;
            
            if (idx <= 0) return (LZ4Level)0; //below compression threshold = fast compression

            //compressions 3-11
            for (int shiftCounter = 4; shiftCounter <= 12; shiftCounter++)
            {
                idx >>>= 1; //divide by 2
                if (idx <= 0) return (LZ4Level)(shiftCounter - 1); //4 - 1 = 3
            }
            return (LZ4Level)12; //compression 12
            
            //if (idx < 3) return (LZ4Level)0; //compressions 0, 1, and 2 are all the same: fast compression
            //if (idx > 9) return (LZ4Level)9; //cap at 9; above 9 starts getting stupidly slow
            //if (idx > 12) return (LZ4Level)12; //if above max; set to max
            //return (LZ4Level)idx;
        }

        public static byte[]? Decompress(byte[]? bytes)
        {
            return bytes;
            /*
            if (bytes == null) return null;
            using (MemoryStream inputStream = new())
            using (MemoryStream decompressStream = new(bytes))
            using (DeflateStream decompressor = new(decompressStream, CompressionMode.Decompress))
            {
                decompressor.CopyTo(inputStream);
                return inputStream.ToArray();
            }
            */
            if (bytes == null) return null;
            if (bytes.Length < 8) return bytes;

            byte[] output = K4os.Compression.LZ4.Legacy.LZ4Wrapper.Unwrap(bytes);
            if (output.Length > CompressionThreshold)
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

        public static void FillArray<T>(ref T[] arr, T val)
        {
            for (int i = 0; i < arr.Length; i++) arr[i] = val;
        }
    }
}
