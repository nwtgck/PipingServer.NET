﻿using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static DebugUtils;

namespace PipingServer.Core.Streams.Tests
{
    [TestClass()]
    public class PipelineStreamTests
    {
        readonly Encoding Encoding = Encoding.UTF8;
        [TestMethod]
        public void CompleteTest()
        {
            using var Stream = new PipelineStream();
            Assert.AreEqual(0, Stream.Length);
            var WriteMessage1 = "HELLO";
            Stream.Write(Encoding.GetBytes(WriteMessage1));
            Stream.Flush();
            var ReadCount = Encoding.GetByteCount(WriteMessage1);
            Assert.AreEqual(ReadCount, Stream.Length);
            var ReadBytes = new byte[ReadCount];
            ReadCount = Stream.Read(ReadBytes);
            Assert.AreEqual(0, Stream.Length);
            var ReadMessage1 = Encoding.GetString(ReadBytes.AsSpan().Slice(0, ReadCount));
            Assert.AreEqual(WriteMessage1, ReadMessage1);

            Stream.Complete();
            var WriteMessage2 = "HI!";
            Assert.ThrowsException<InvalidOperationException>(() => Stream.Write(Encoding.GetBytes(WriteMessage2)));
        }

        [TestMethod]
        public async Task CompleteAsyncTestAsync()
        {
            using var Stream = new PipelineStream();
            Assert.AreEqual(0, Stream.Length);
            Assert.IsTrue(Stream.CanRead);
            Assert.IsTrue(Stream.CanWrite);
            var WriteMessage1 = "HELLO";
            await Stream.WriteAsync(Encoding.GetBytes(WriteMessage1));
            await Stream.CompleteAsync();
            await Stream.FlushAsync();
            var ReadCount = Encoding.GetByteCount(WriteMessage1);
            Assert.AreEqual(ReadCount, Stream.Length);
            var ReadBytes = new byte[ReadCount];
            ReadCount = await Stream.ReadAsync(ReadBytes);
            var ReadMessage1 = Encoding.GetString(ReadBytes.AsSpan().Slice(0, ReadCount));
            Assert.AreEqual(WriteMessage1, ReadMessage1);
            Assert.IsTrue(Stream.CanRead);
            Assert.IsFalse(Stream.CanWrite);
            var WriteMessage2 = "HI!";
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () => await Stream.WriteAsync(Encoding.GetBytes(WriteMessage2)));
        }

        [TestMethod]
        public void PipelineStreamTest()
        {
            using var Stream = new PipelineStream();
            Assert.AreEqual(0, Stream.Length);
            Assert.IsTrue(Stream.CanRead);
            Assert.IsFalse(Stream.CanSeek);
            Assert.IsTrue(Stream.CanWrite);
            Assert.IsFalse(Stream.CanTimeout);
        }

        [TestMethod]
        public void PipelineStreamTestIsEmpty()
        {
            using var Stream = PipelineStream.Empty;
            Assert.IsFalse(Stream.CanRead);
            Assert.IsFalse(Stream.CanSeek);
            Assert.IsFalse(Stream.CanWrite);
            Assert.IsFalse(Stream.CanTimeout);
        }

        [TestMethod]
        public void FlushTest()
        {
            var Message = "HELLO WORLD.";
            using var Stream = new PipelineStream();
            Assert.AreEqual(0, Stream.Length);
            using (var Writer = new StreamWriter(Stream, Encoding, 1024, true))
                Writer.WriteLine(Message);
            Assert.AreNotEqual(0, Stream.Length);
            using (var Reader = new StreamReader(Stream, Encoding, false, 1024, true))
                Assert.AreEqual(Message, Reader.ReadLine());
            Assert.AreEqual(0, Stream.Length);
        }

        [TestMethod]
        public async Task FlushAsyncTestAsync()
        {
            var Message = "HELLO WORLD.";
            using var Stream = new PipelineStream();
            Assert.AreEqual(0, Stream.Length);
            await using (var Writer = new StreamWriter(Stream, Encoding, 1024, true))
                await Writer.WriteLineAsync(Message);
            Assert.AreNotEqual(0, Stream.Length);
            using (var Reader = new StreamReader(Stream, Encoding, false, 1024, true))
                Assert.AreEqual(Message, await Reader.ReadLineAsync());
            Assert.AreEqual(0, Stream.Length);

        }

        [TestMethod]
        public void SeekTest()
        {
            using var Stream = new PipelineStream();
            Assert.IsFalse(Stream.CanSeek);
            Assert.ThrowsException<NotSupportedException>(() => Stream.Seek(0, SeekOrigin.Begin));
        }

        [TestMethod]
        public void SetLengthTest()
        {
            using var Stream = new PipelineStream();
            Assert.ThrowsException<NotSupportedException>(() => Stream.SetLength(0));
        }
        [TestMethod]
        public void WriteTest()
        {
            var data = Enumerable.Range(0, 200).Select(v => (byte)v).ToArray();
            var buffer = new byte[100];
            using var stream = new PipelineStream();
            stream.Write(data.AsSpan());
        }
        [TestMethod]
        public void WriteAndCompleteAddingTest()
        {
            var data = Enumerable.Range(0, 200).Select(v => (byte)v).ToArray();
            var buffer = new byte[100];
            using var stream = new PipelineStream();
            Assert.AreEqual(true, stream.CanWrite);
            stream.Write(data.AsSpan());
            stream.Complete();
            Assert.AreEqual(false, stream.CanWrite);
            Assert.ThrowsException<InvalidOperationException>(() => stream.Write(data.AsSpan()));
        }
        [TestMethod]
        public async Task WriteAsyncTestAsync()
        {
            var Time = TimeSpan.FromMilliseconds(100);
            var data = Enumerable.Range(0, 200).Select(v => (byte)v).ToArray();
            var buffer = new byte[100];
            using var stream = new PipelineStream();
            using var TokenSource = new CancellationTokenSource(Time);
            await stream.WriteAsync(data, TokenSource.Token);
        }
        [TestMethod]
        public async Task WriteAsyncAndCompleteAddingTestAsync()
        {
            var Time = TimeSpan.FromMilliseconds(100);
            var data = Enumerable.Range(0, 200).Select(v => (byte)v).ToArray();
            var buffer = new byte[100];
            using var stream = new PipelineStream();
            Assert.AreEqual(true, stream.CanWrite);
            using (var TokenSource = new CancellationTokenSource(Time))
                await stream.WriteAsync(data, TokenSource.Token);
            await stream.CompleteAsync();
            Assert.AreEqual(false, stream.CanWrite);
            using (var TokenSource = new CancellationTokenSource(Time))
                await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                    async () => await stream.WriteAsync(data, TokenSource.Token));
        }
        [TestMethod, TestCategory("ShortTime")]
        public async Task StoppedReadAsyncTest1Async()
        {
            var buffer = new byte[100];
            var Time = TimeSpan.FromMilliseconds(10);
            using var stream = new PipelineStream();
            using var TokenSource = CreateTokenSource(Time);
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                async () => await stream.ReadAsync(buffer.AsMemory(), TokenSource.Token));
        }
        [TestMethod, TestCategory("ShortTime")]
        public async Task StartReadAsyncTest1Async()
        {
            var data = Enumerable.Range(0, 200).Select(v => (byte)v).ToArray();
            var buffer = new byte[100];
            var Time = TimeSpan.FromMilliseconds(100);
            using var stream = new PipelineStream();
            await stream.WriteAsync(data);
            int ReadBytes = 0;
            using (var TokenSource = new CancellationTokenSource(Time))
                ReadBytes = await stream.ReadAsync(buffer, TokenSource.Token);
            Assert.AreEqual(buffer.Length, ReadBytes);
            ReadBytes = await stream.ReadAsync(buffer);
            Assert.AreEqual(buffer.Length, ReadBytes);
            using (var TokenSource = new CancellationTokenSource(Time))
                await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                    async () => await stream.ReadAsync(buffer, TokenSource.Token));
            await stream.WriteAsync(data);
            Assert.AreEqual(buffer.Length, ReadBytes);
            ReadBytes = await stream.ReadAsync(buffer);
            Assert.AreEqual(buffer.Length, ReadBytes);
        }
        [TestMethod, TestCategory("ShortTime")]
        public async Task StoppedReadAsyncTest2Async()
        {
            var buffer = new byte[100];
            var Time = TimeSpan.FromMilliseconds(10);
            using var stream = new PipelineStream();
            using var TokenSource = CreateTokenSource(Time);
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                () => stream.ReadAsync(buffer, 0, buffer.Length, TokenSource.Token));
        }
        [TestMethod, TestCategory("ShortTime")]
        public async Task StartReadAsyncTest2Async()
        {
            var data = Enumerable.Range(0, 200).Select(v => (byte)v).ToArray();
            var buffer = new byte[100];
            var Time = TimeSpan.FromMilliseconds(100);
            using var stream = new PipelineStream();
            await stream.WriteAsync(data, 0, data.Length);
            int ReadBytes = 0;
            using (var TokenSource = new CancellationTokenSource(Time))
                ReadBytes = await stream.ReadAsync(buffer, 0, buffer.Length, TokenSource.Token);
            Assert.AreEqual(buffer.Length, ReadBytes);
            ReadBytes = await stream.ReadAsync(buffer, 0, buffer.Length);
            Assert.AreEqual(buffer.Length, ReadBytes);
            using (var TokenSource = new CancellationTokenSource(Time))
                await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                    () => stream.ReadAsync(buffer, 0, buffer.Length, TokenSource.Token));
            await stream.WriteAsync(data, 0, data.Length);
            Assert.AreEqual(buffer.Length, ReadBytes);
            ReadBytes = await stream.ReadAsync(buffer, 0, buffer.Length);
            Assert.AreEqual(buffer.Length, ReadBytes);
        }
    }
}
