using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Hypocycloid.Utils
{
    public static class BitHelper
    {
        // Cache for enum sizes to avoid repeated reflection calls
        static readonly Dictionary<Type, int> EnumSizeCache = new();

        //https://stackoverflow.com/a/58496974
        //https://stackoverflow.com/a/10439333
        //https://github.com/SunsetQuest/Fast-Integer-Log2/blob/master/BenchmarkLeading0Count/Program.cs#L583
        public static int Log2(ulong x)
        {
            x |= x >> 1;
            x |= x >> 2;
            x |= x >> 4;
            x |= x >> 8;
            x |= x >> 16;
            x -= x >> 1 & 0x55555555;
            x = (x >> 2 & 0x33333333) + (x & 0x33333333);
            x = (x >> 4) + x & 0x0f0f0f0f;
            x += x >> 8;
            x += x >> 16;
            return (int)((x & 0x0000003f) - 1);
        }

        public static ulong RotateLeft(ulong value, int offset) =>
            (value << offset) | (value >> (64 - offset));

        // ===========================
        // Enum Flag Manipulation
        // ===========================
        // IL2CPP/AOT-safe
        // No unsafe or external packages
        // Handles signed and unsigned enum values
        // Supports special values like -1 (All)
        // Zero allocations, fast memory reinterpretation

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Set<T>(T flag, ref T src)
            where T : struct, Enum
        {
            long s = ReadAsLong(src);
            long f = ReadAsLong(flag);
            long result = s | f;
            src = WriteFromLong<T>(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Unset<T>(T flag, ref T src)
            where T : struct, Enum
        {
            long s = ReadAsLong(src);
            long f = ReadAsLong(flag);
            long result = s & ~f;
            src = WriteFromLong<T>(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Toggle<T>(T flag, ref T src)
            where T : struct, Enum
        {
            long s = ReadAsLong(src);
            long f = ReadAsLong(flag);
            long result = s ^ f;
            src = WriteFromLong<T>(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Has<T>(T flag, T src)
            where T : struct, Enum
        {
            long s = ReadAsLong(src);
            long f = ReadAsLong(flag);
            return (s & f) != 0;
        }

        public static long GenerateRandomSeed()
        {
            Span<byte> buf = stackalloc byte[8];
            RandomNumberGenerator.Fill(buf);
            ulong u = BinaryPrimitives.ReadUInt64LittleEndian(buf);
            return (long)(u & 0x7FFFFFFFFFFFFFFFUL);
        }

        // ---------------- Private ----------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static long ReadAsLong<T>(T value)
            where T : struct
        {
            int size = GetEnumSize<T>();

            // Create a properly sized span to avoid over-allocation
            Span<byte> valueBytes = stackalloc byte[size];
            Span<byte> longBytes = stackalloc byte[8];

            // Clear the long buffer to ensure clean conversion
            longBytes.Clear();

            // Copy enum bytes to temp buffer
            MemoryMarshal
                .AsBytes(MemoryMarshal.CreateReadOnlySpan(ref value, 1))[..size]
                .CopyTo(valueBytes);

            // Copy to long buffer (little-endian safe)
            valueBytes.CopyTo(longBytes);

            return MemoryMarshal.Read<long>(longBytes);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static T WriteFromLong<T>(long value)
            where T : struct
        {
            int size = GetEnumSize<T>();

            Span<byte> longBytes = stackalloc byte[8];
            Span<byte> enumBytes = stackalloc byte[size];

            MemoryMarshal.Write(longBytes, ref value);
            longBytes[..size].CopyTo(enumBytes);

            return MemoryMarshal.Read<T>(enumBytes);
        }

        // Cached version to avoid repeated reflection calls
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int GetEnumSize<T>()
            where T : struct
        {
            Type enumType = typeof(T);

            if (!EnumSizeCache.TryGetValue(enumType, out int size))
            {
                Type underlying = Enum.GetUnderlyingType(enumType);

                if (underlying == typeof(byte) || underlying == typeof(sbyte))
                    size = 1;
                else if (underlying == typeof(short) || underlying == typeof(ushort))
                    size = 2;
                else if (underlying == typeof(int) || underlying == typeof(uint))
                    size = 4;
                else if (underlying == typeof(long) || underlying == typeof(ulong))
                    size = 8;
                else
                    throw new NotSupportedException($"Unsupported enum backing type: {underlying}");

                EnumSizeCache[enumType] = size;
            }

            return size;
        }

        public static bool IsEverything<T>(T value)
            where T : struct, Enum
        {
            return ReadAsLong(value) == -1L;
        }
    }
}
