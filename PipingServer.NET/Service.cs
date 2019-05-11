﻿using System;
using System.Net;
using System.IO;
using System.Reflection;
using System.ServiceModel;
using System.ServiceModel.Activation;
using System.ServiceModel.Description;
using System.ServiceModel.Web;
using System.Text;
using System.Web;
using System.Security;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HttpMultipartParser;

namespace Piping
{
    [AspNetCompatibilityRequirements(RequirementsMode =AspNetCompatibilityRequirementsMode.Allowed)]
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.Single)]
    public class Service : IService
    {
        readonly bool EnableLog = false;
        readonly string Location;
        readonly string BasePath;
        readonly FileVersionInfo VERSION;
        readonly Encoding Encoding = new UTF8Encoding(false);
        /// <summary>
        /// デフォルト設定の反映
        /// </summary>
        /// <param name="config"></param>
        public static void Configure(ServiceConfiguration config)
        {
            var TransferMode = System.ServiceModel.TransferMode.Streamed;
            var SendTimeout = TimeSpan.FromHours(1);
            var OpenTimeout = TimeSpan.FromHours(1);
            var CloseTimeout = TimeSpan.FromHours(1);
            var MaxBufferSize = int.MaxValue;
            var MaxReceivedMessageSize = int.MaxValue;
            config.AddServiceEndpoint(typeof(IService), new WebHttpBinding
            {
                TransferMode = TransferMode,
                SendTimeout = SendTimeout,
                OpenTimeout = OpenTimeout,
                CloseTimeout = CloseTimeout,
                MaxBufferSize = MaxBufferSize,
                MaxReceivedMessageSize = MaxReceivedMessageSize,
            }, "").EndpointBehaviors.Add(new WebHttpBehavior());
            var sdb = config.Description.Behaviors.Find<ServiceDebugBehavior>();
            if (sdb != null)
                sdb.HttpHelpPageEnabled = false;
        }
        readonly Dictionary<string, bool> pathToEstablished = new Dictionary<string, bool>();
        readonly Dictionary<string, UnestablishedPipe> pathToUnestablishedPipe = new Dictionary<string, UnestablishedPipe>();
        public Service()
        {
            Location = Assembly.GetExecutingAssembly().Location;
            BasePath = Path.GetDirectoryName(Location);
            VERSION = FileVersionInfo.GetVersionInfo(Location);
            NAME_TO_RESERVED_PATH = new Dictionary<string, Func<Task<Stream>>>
            {
                {DefaultPath.Root, GetDefaultPageAsync },
                {DefaultPath.Version, GetVersionAsync},
                {DefaultPath.Help, GetHelpAsync},
                {DefaultPath.Favicon, GetFaviconAsync},
                {DefaultPath.Robots, GetRobotsAsync},
            };
        }
        internal Dictionary<string, Func<Task<Stream>>> NAME_TO_RESERVED_PATH;
        
        internal static Uri GetBaseUri(IEnumerable<Uri> BaseAddresses, Uri RequestUri)
        {
            var RequestUriString = RequestUri.ToString();
            return BaseAddresses.FirstOrDefault(IsFind);
            bool IsFind(Uri a)
            {
                var _a = a.ToString();
                if (_a.Last() != '/')
                    _a += '/';
                return RequestUriString.IndexOf(_a) == 0;
            }
        }
        internal Uri GetBaseUri()
            => GetBaseUri(OperationContext.Current.Host.BaseAddresses, WebOperationContext.Current.IncomingRequest.UriTemplateMatch.RequestUri);
        internal static string GetRelativeUri(IEnumerable<Uri> BaseAddresses, Uri RequestUri)
            => GetRelativeUri(GetBaseUri(BaseAddresses, RequestUri), RequestUri);
        internal static string GetRelativeUri(Uri BaseAddress, Uri RequestUri)
        {
            var b = BaseAddress.ToString();
            if (b.Last() != '/')
                b += '/';
            var r = RequestUri.ToString();
            var result = r.Substring(b.Length -1);
            if (!result.Any())
                result = "/";
            else if (result.FirstOrDefault() != '/')
                result = '/' + result;
            return result;
        }
        internal string GetRelativeUri()
            => GetRelativeUri(OperationContext.Current.Host.BaseAddresses, WebOperationContext.Current.IncomingRequest.UriTemplateMatch.RequestUri);
        /// <summary>
        /// エントリーポイント
        /// </summary>
        /// <param name="inputStream"></param>
        /// <returns></returns>
        [OperationBehavior(ReleaseInstanceMode = ReleaseInstanceMode.None)]
        public Task<Stream> DefaultAsync(Stream inputStream)
        {
            var Current = WebOperationContext.Current;
            var Request = Current.IncomingRequest;
            var Response = Current.OutgoingResponse;
            var Method = Request.Method;
            switch (Method)
            {
                case "POST":
                case "PUT":
                    return UploadAsync(inputStream, GetRelativeUri(), Request, Response);
                case "GET":
                    return DownloadAsync(GetRelativeUri(), Response);
                case "OPTIONS":
                    return Task.FromResult(OptionsResponseGenerator(Response));
                default:
                    return Task.FromResult(NotImplemented(Response));
            }
        }
        protected Stream BadRequest(OutgoingWebResponseContext Response, string AndMessage = null)
        {
            Response.StatusCode = HttpStatusCode.BadRequest;
            Response.StatusDescription = AndMessage;
            Response.ContentLength = 0;
            Response.ContentType = $"text/plain;charset={Encoding.WebName}";
            return new MemoryStream(new byte[0]);
        }

        public async Task<Stream> UploadAsync(Stream InputStream, string RelativeUri, IncomingWebRequestContext Request = null, OutgoingWebResponseContext Response = null)
        {
            var output = new MemoryStream();
            Request ??= WebOperationContext.Current.IncomingRequest;
            Response ??= WebOperationContext.Current.OutgoingResponse;

            if (NAME_TO_RESERVED_PATH.TryGetValue(RelativeUri.ToLower(), out _))
                return BadRequest(Response, $"[ERROR] Cannot send to a reserved path '{RelativeUri}'. (e.g. '/mypath123')\n");
            var Key = new RequestKey(RelativeUri);
            // Get the number of receivers
            var Receivers = Key.Receivers;
            // If the number of receivers is invalid
            if (Receivers <= 0)
                return BadRequest(Response, $"[ERROR] n should > 0, but n = ${Receivers}.\n");
            if (EnableLog)
                Console.WriteLine(pathToUnestablishedPipe.Select(v => $"{v.Key}:{v.Value}"));
            // If the path connection is connecting
            if (pathToEstablished.TryGetValue(Key.LocalPath, out _))
                return BadRequest(Response, $"[ERROR] Connection on '${RelativeUri}' has been established already.\n");

            // If the path connection is connecting
            // Get unestablished pipe
            if (pathToUnestablishedPipe.TryGetValue(Key.LocalPath, out var unestablishedPipe))
            {
                // If a sender have not been registered yet
                if (unestablishedPipe.Sender == null)
                {
                    if (Receivers == unestablishedPipe.ReceiversCount)
                    {
                        unestablishedPipe.Sender = createSender(Request, Response, RelativeUri);
                        Response.Headers.Add("Access-Control-Allow-Origin", "*");
                        using (var writer = new StreamWriter(output, Encoding, 1024, true))
                        {
                            await writer.WriteLineAsync($"[INFO] Waiting for ${Receivers} receiver(s)...");
                            await writer.WriteLineAsync($"[INFO] {unestablishedPipe.Receivers.Count} receiver(s) has/have been connected.");
                        }
                    }
                }
            }
            throw new NotImplementedException();
        }
        ReqAndUnsubscribe createSender(IncomingWebRequestContext Request, OutgoingWebResponseContext Response, string RelativeUri)
        {
            throw new NotImplementedException();
        }
        ResAndUnsubscribe createReceiver(IncomingWebRequestContext Request, OutgoingWebResponseContext Response, string RelativeUri)
        {
            throw new NotImplementedException();
        }
        Task<Stream> IService.PostUploadAsync(Stream InputStream) => UploadAsync(InputStream, GetRelativeUri(), WebOperationContext.Current.IncomingRequest, WebOperationContext.Current.OutgoingResponse);
        Task<Stream> IService.PutUploadAsync(Stream InputStream) => UploadAsync(InputStream, GetRelativeUri(), WebOperationContext.Current.IncomingRequest, WebOperationContext.Current.OutgoingResponse);
        public Task<Stream> DownloadAsync(string RelativeUri, OutgoingWebResponseContext Response = null)
        {
            if (NAME_TO_RESERVED_PATH.TryGetValue(RelativeUri.ToLower(), out var Generator))
                return Generator();
            Response ??= WebOperationContext.Current.OutgoingResponse;
            throw new NotImplementedException();
        }
        Task<Stream> IService.GetDownloadAsync() => DownloadAsync(GetRelativeUri(), WebOperationContext.Current.OutgoingResponse);
        public Task<Stream> GetDefaultPageAsync()
            => Task.FromResult(DefaultPageResponseGenerator(WebOperationContext.Current.OutgoingResponse));
        public Task<Stream> GetVersionAsync()
            => Task.FromResult(VersionResponseGenerator(WebOperationContext.Current.OutgoingResponse));
        public Task<Stream> GetHelpAsync()
            => Task.FromResult(HelpPageResponseGenerator(WebOperationContext.Current.OutgoingResponse));
        public Task<Stream> GetFaviconAsync()
            => Task.FromResult(FileGetGenerator(DefaultPath.Favicon, WebOperationContext.Current.OutgoingResponse));
        public Task<Stream> GetRobotsAsync()
            => Task.FromResult(FileGetGenerator(DefaultPath.Robots, WebOperationContext.Current.OutgoingResponse));
        public Task<Stream> GetOptionsAsync()
            => Task.FromResult(OptionsResponseGenerator(WebOperationContext.Current.OutgoingResponse));
        protected Stream DefaultPageResponseGenerator(OutgoingWebResponseContext Response)
        {
            var Encoding = Response.BindingWriteEncoding;
            var Bytes = Encoding.GetBytes(GetDefaultPage());
            Response.ContentLength = Bytes.Length;
            Response.ContentType = $"text/html;charset={Encoding.WebName}";
            return new MemoryStream(Bytes);
        }
        internal static string GetDefaultPage() => Properties.Resource.DefaultPage;
        protected Stream HelpPageResponseGenerator(OutgoingWebResponseContext Response)
        {
            var url = GetBaseUri();
            var Encoding = Response.BindingWriteEncoding;
            var Bytes = Encoding.GetBytes(GetHelpPageText(url, VERSION));
            Response.ContentLength = Bytes.Length;
            Response.ContentType = $"text/plain;charset={Encoding.WebName}";
            return new MemoryStream(Bytes);
        }
        internal static string GetHelpPageText(Uri url, FileVersionInfo version)
        {
            return $@"Help for piping - server {version}
(Repository: https://github.com/nwtgck/piping-server)

======= Get  =======
curl {url}/mypath

======= Send =======
# Send a file
curl -T myfile {url}/mypath

# Send a text
echo 'hello!' | curl -T - {url}/mypath

# Send a directory (zip)
zip -q -r - ./mydir | curl -T - {url}/mypath

# Send a directory (tar.gz)
tar zfcp - ./mydir | curl -T - {url}/mypath

# Encryption
## Send
cat myfile | openssl aes-256-cbc | curl -T - {url}/mypath
## Get
curl {url}/mypath | openssl aes-256-cbc -d";
        }
        protected Stream VersionResponseGenerator(OutgoingWebResponseContext Response)
        {
            var Encoding = Response.BindingWriteEncoding;
            var Bytes = Encoding.GetBytes($"{VERSION.FileVersion}\n");
            Response.ContentLength = Bytes.Length;
            Response.ContentType = $"text/plain;charset={Encoding.WebName}";
            return new MemoryStream(Bytes);
        }
        protected Stream OptionsResponseGenerator(OutgoingWebResponseContext Response)
        {
            Response.StatusCode = HttpStatusCode.OK;
            Response.Headers.Add("Access-Control-Allow-Origin", "*");
            Response.Headers.Add("Access-Control-Allow-Methods", "GET, HEAD, POST, PUT, OPTIONS");
            Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Content-Disposition");
            Response.Headers.Add("Access-Control-Max-Age", "86400");
            Response.ContentLength = 0;
            return new MemoryStream(new byte[0]);
        }
        protected Stream NotImplemented(OutgoingWebResponseContext Response)
        {
            Response.StatusCode = HttpStatusCode.NotImplemented;
            Response.ContentLength = 0;
            return new MemoryStream(new byte[0]);
        }
        protected bool ExistsFile(string FileName)
        {
            var FilePath = Path.Combine(BasePath, FileName.TrimStart('/'));
            return File.Exists(FilePath);
        }
        protected Stream FileGetGenerator(string FileName, OutgoingWebResponseContext Response)
        {
            var FilePath = Path.Combine(BasePath, FileName.TrimStart('/'));
            try
            {
                var Bytes = File.ReadAllBytes(FilePath);
                Response.StatusCode = HttpStatusCode.OK;
                Response.ContentType = MimeMapping.GetMimeMapping(FilePath);
                Response.ContentLength = Bytes.Length;
                return new MemoryStream(Bytes);
            }
            catch (FileNotFoundException)
            {
                Response.StatusCode = HttpStatusCode.NotFound;
            }
            catch (SecurityException)
            {
                Response.StatusCode = HttpStatusCode.NotFound;
            }
            catch (Exception)
            {
                Response.StatusCode = HttpStatusCode.InternalServerError;
            }
            Response.ContentLength = 0;
            return new MemoryStream(new byte[0]);
        }
        protected Pipe getPipeIfEstablished(UnestablishedPipe p)
        {
            if (p.Sender != null && p.Receivers.Count == p.ReceiversCount)
                return new Pipe(p.Sender.ReqRes, p.Receivers.Select(v =>
                {
                    v.FireUnsubscribeClose();
                    return v.ReqRes;
                }));
            return null;
        }
        protected async Task<Stream> RunPipeAsync(string path, Pipe pipe)
        {
            pathToEstablished[path] = true;
            pathToUnestablishedPipe.Remove(path);
            var (Sender, Receivers) = pipe;
            using (var writer = new StreamWriter(Sender.ResponseStream, Encoding, 1024, true))
                await writer.WriteLineAsync($"[INFO] Start sending with ${pipe.Receivers.Count} receiver(s)");
            var IsMutiForm = (pipe.Sender.Request.Headers[HttpResponseHeader.ContentType] ?? "").IndexOf("multipart/form-data") > 0;
            // TODO: support web multipart
            var (Part, PartLength, PartContentType, PartContentDisposition) = await GetPartStream();
            Task<(Stream stream, long contentLength, string contentType, string contentDisposition)> GetPartStream() {
                var tcs = new TaskCompletionSource<(Stream, long, string, string)>();
                var sm = new StreamingMultipartFormDataParser(Sender.RequestStream);
                sm.FileHandler += (name, fileName, contentType, contentDisposition, buffer, bytes)
                    => tcs.TrySetResult((new MemoryStream(buffer), buffer.LongLength, contentType, contentDisposition));
                sm.Run();
                return tcs.Task;
            }
            // 実装中
            var closeCount = 0;
            foreach (var receiver in Receivers)
            {
                // Close receiver
                void closeReceiver()
                {
                    closeCount++;
                    //senderData.unpipe(passThrough);
                    // If close-count is # of receivers
                    if (closeCount == Receivers.Count)
                    {
                        using (var writer = new StreamWriter(Sender.ResponseStream, Encoding, 1024, true))
                            writer.WriteLineAsync("[INFO] All receiver(s) was/were closed halfway.");
                        pathToEstablished.Remove(path);
                        Sender.ResponseStream.Close();
                    }
                }
                if (Part == null)
                {
                    if (!string.IsNullOrEmpty(Sender.Request.Headers[HttpRequestHeader.ContentLength]))
                        receiver.Response.ContentLength = Sender.Request.ContentLength;
                    if (!string.IsNullOrEmpty(Sender.Request.Headers[HttpRequestHeader.ContentType]))
                        receiver.Response.ContentType = Sender.Request.ContentType;
                    if (!string.IsNullOrEmpty(Sender.Request.Headers["content-disposition"]))
                        receiver.Response.Headers.Add("content-disposition", Sender.Request.Headers["content-disposition"]);
                } else
                {
                    receiver.Response.ContentLength = PartLength;
                    if (!string.IsNullOrEmpty(PartContentType))
                        receiver.Response.ContentType = PartContentType;
                    if (!string.IsNullOrEmpty(PartContentDisposition))
                        receiver.Response.Headers.Add("content-disposition", PartContentDisposition);
                }
                receiver.Response.StatusCode = HttpStatusCode.OK;
                receiver.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                // TODO sender to receiver
            }
            //TODO 仮
            return pipe.Sender.ResponseStream;
        }
    }
}
