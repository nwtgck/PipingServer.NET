﻿using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using PipingServer.Core.Pipes;
using PipingServer.Core.Streams;

namespace PipingServer.Core.Tests
{
    public class MockPipelineStreamResult : IPipelineStreamResult, IDisposable
    {
        public PipeType PipeType { get; set; }
        public PipelineStream Stream { get; set; } = PipelineStream.Empty;
        public int? StatusCode { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public event EventHandler? OnFinally;

        #region IDisposable Support
        private bool disposedValue = false; // 重複する呼び出しを検出するには

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    if (Stream != PipelineStream.Empty)
                        Stream.Dispose();
                    OnFinally?.Invoke(this, new EventArgs());
                    foreach (EventHandler d in (OnFinally?.GetInvocationList() ?? Enumerable.Empty<Delegate>()))
                        OnFinally -= d;
                }
                disposedValue = true;
            }
        }
        public void Dispose()
        {
            Dispose(true);
        }
        #endregion
    }
}
