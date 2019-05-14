﻿using System;
using System.Collections.Concurrent;
using System.IO;

namespace Piping
{
    public class BufferStream : Stream
    {
        readonly BlockingCollection<byte[]> data;
        byte[] _currentBlock = null;
        public int BoundedCapacity => data.BoundedCapacity;
        int _currentBlockIndex = 0;
        public int BufferedWrites => data.Count;
        public bool IsAddingCompleted => data.IsAddingCompleted;
        public bool IsCompleted => data.IsCompleted;
        public void CompleteAdding() => data.CompleteAdding();
        public BufferStream() =>data = new BlockingCollection<byte[]>();
        public BufferStream(int boundedCapacity) => data = new BlockingCollection<byte[]>(boundedCapacity);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotImplementedException();

        public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_currentBlock == null || _currentBlockIndex == _currentBlock.Length)
            {
                if (!GetNextBlock())
                    return 0;
            }
            int minCount = Math.Min(count, _currentBlock.Length - _currentBlockIndex);
            Array.Copy(_currentBlock, _currentBlockIndex, buffer, offset, minCount);
            _currentBlockIndex += minCount;
            return minCount;
        }

        /// <summary>
        /// Loads the next block in to <see cref="_currentBlock"/>
        /// </summary>
        /// <returns>True if the next block was retrieved.</returns>
        private bool GetNextBlock()
        {
            if (!data.TryTake(out _currentBlock))
            {
                if (data.IsCompleted)
                    return false;
                try
                {
                    _currentBlock = data.Take();
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }
            _currentBlockIndex = 0;
            return true;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotImplementedException();

        public override void SetLength(long value) => throw new NotImplementedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            var localArray = new byte[count];
            Array.Copy(buffer, offset, localArray, 0, count);
            data.Add(localArray);
        }
    }
}