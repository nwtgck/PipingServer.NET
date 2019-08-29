﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Piping.Streams;

namespace Piping
{
    public class CompletableStreamResult : IActionResult
    {
        public string Identity = string.Empty;
        readonly ILogger<CompletableStreamResult> logger;
        public CompletableQueueStream Stream { get; set; } = CompletableQueueStream.Empty;
        public event EventHandler? OnFinally;
        public void FireFinally(ActionContext? context = null)
        {
            try
            {
                OnFinally?.Invoke(context, new EventArgs());
            }
            catch (Exception)
            {

            }
            foreach (var d in (OnFinally?.GetInvocationList() ?? Enumerable.Empty<Delegate>()).Cast<EventHandler>())
                OnFinally -= d;
        }
        public int? StatusCode { get; set; }
        public long? ContentLength { get; set; } = null;
        public string? ContentType { get; set; } = null;
        public string? ContentDisposition { get; set; } = null;
        public string? AccessControlAllowOrigin { get; set; } = null;
        public string? AccessControlExposeHeaders { get; set; } = null;
        public int BufferSize { get; set; } = 1024;
        public CompletableStreamResult(ILogger<CompletableStreamResult> logger)
            => this.logger = logger;
        public CompletableStreamResult(ILogger<CompletableStreamResult> logger, CompletableQueueStream? Stream = null, long? ContentLength = null, string? ContentType = null, string? ContentDisposition = null)
            => (this.logger, this.Stream, this.ContentLength, this.ContentType, this.ContentDisposition) = (logger, Stream ?? CompletableQueueStream.Empty, ContentLength, ContentType, ContentDisposition);
        public Task ExecuteResultAsync(ActionContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var executor = context.HttpContext.RequestServices.GetRequiredService<IActionResultExecutor<CompletableStreamResult>>();
            return executor.ExecuteAsync(context, this);
        }
    }
}
