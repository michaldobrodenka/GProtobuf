//using System;
//using System.Buffers;
//using System.Buffers.Binary;
//using System.Collections.Generic;
//using System.Linq;
//using System.Runtime.CompilerServices;
//using System.Runtime.InteropServices;
//using System.Text;
//using System.Threading.Tasks;
//using static System.Runtime.InteropServices.JavaScript.JSType;

//namespace PooledBufferBenchmark
//{
//    class BytePoolWriter : IDisposable
//    {
//        private byte[] _buffer;
//        private int _written;

//        public BytePoolWriter(int initialCapacity = 256)
//        {
//            _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
//            _written = 0;
//        }

//        public int WrittenBytes => _written;

//        public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);

//        public Span<byte> GetSpan(int sizeHint)
//        {
//            if (sizeHint < 0) throw new ArgumentOutOfRangeException(nameof(sizeHint));
//            Ensure(sizeHint);
//            return _buffer.AsSpan(_written);
//        }

//        public void Advance(int count)
//        {
//            if ((uint)count > (uint)(_buffer.Length - _written))
//                throw new ArgumentOutOfRangeException(nameof(count));
//            _written += count;
//        }

//        private void Ensure(int sizeHint)
//        {
//            if (sizeHint == 0) sizeHint = 1;
//            int needed = _written + sizeHint;
//            if (needed <= _buffer.Length) return;

//            int newCapacity = Math.Max(_buffer.Length * 2, needed);
//            var newArr = ArrayPool<byte>.Shared.Rent(newCapacity);
//            _buffer.AsSpan(0, _written).CopyTo(newArr);
//            ArrayPool<byte>.Shared.Return(_buffer, clearArray: false); // vyčistiť iba ak nesie citlivé dáta
//            _buffer = newArr;
//        }

//        public void Dispose()
//        {
//            ArrayPool<byte>.Shared.Return(_buffer, clearArray: false);
//            _buffer = Array.Empty<byte>();
//            _written = 0;
//        }
//    }

//    /// Zbiera int32 do vnútorného BytePoolWriter (LE poradie bajtov)
//    file sealed class IntCollectorBytes : IDisposable
//    {
//        private readonly BytePoolWriter _writer;
//        private int _count;

//        public IntCollectorBytes(int initialInts = 64)
//        {
//            if (initialInts < 0) initialInts = 0;
//            _writer = new BytePoolWriter(Math.Max(initialInts * sizeof(int), 16));
//            _count = 0;
//        }

//        public int Count => _count;

//        public void Add(int value)
//        {
//            var span = _writer.GetSpan(sizeof(int));
//            BinaryPrimitives.WriteInt32LittleEndian(span, value);
//            _writer.Advance(sizeof(int));
//            _count++;
//        }

//        public List<int> ToList()
//        {
//            var bytes = _writer.WrittenSpan;
//            if (bytes.Length != _count * sizeof(int))
//                throw new InvalidOperationException("Počet bajtov nie je násobok sizeof(int).");

//            var list = new List<int>(_count);

//            // Rýchla cesta: naplníme List bez Add() slučky
//            CollectionsMarshal.SetCount(list, _count);
//            var dst = CollectionsMarshal.AsSpan(list);

//            if (BitConverter.IsLittleEndian)
//            {
//                MemoryMarshal.Cast<byte, int>(bytes).CopyTo(dst);
//            }
//            else
//            {
//                // big-endian fallback – prečítať LE každý prvok
//                for (int i = 0; i < _count; i++)
//                    dst[i] = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(i * 4, 4));
//            }
//            return list;
//        }

//        [MethodImpl(MethodImplOptions.AggressiveInlining)]
//        public T[] ToArray()
//        {
//            int n = _count;
//            if (n == 0) return Array.Empty<T>();
//            var dst = GC.AllocateUninitializedArray<T>(n);
//            _array.AsSpan(0, n).CopyTo(dst);
//            return dst;
//        }

//        public void Dispose() => _writer.Dispose();
//    }
//}
