// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Testing;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Tests
{
    public class Http3StreamTests : Http3TestBase
    {
        [Fact]
        public async Task HelloWorldTest()
        {
            var headers = new[]
            {
                new KeyValuePair<string, string>(HeaderNames.Method, "Custom"),
                new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>(HeaderNames.Authority, "localhost:80"),
            };

            var requestStream = await InitializeConnectionAndStreamsAsync(_echoApplication);

            var doneWithHeaders = await requestStream.SendHeadersAsync(headers);
            await requestStream.SendDataAsync(Encoding.ASCII.GetBytes("Hello world"), endStream: true);

            var responseHeaders = await requestStream.ExpectHeadersAsync();
            var responseData = await requestStream.ExpectDataAsync();
            Assert.Equal("Hello world", Encoding.ASCII.GetString(responseData.ToArray()));
        }

        [Fact]
        public async Task EmptyMethod_Reset()
        {
            var headers = new[]
            {
                new KeyValuePair<string, string>(HeaderNames.Method, ""),
                new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>(HeaderNames.Authority, "localhost:80"),
            };

            var requestStream = await InitializeConnectionAndStreamsAsync(_echoApplication);
            var doneWithHeaders = await requestStream.SendHeadersAsync(headers);
            await requestStream.WaitForStreamErrorAsync(Http3ErrorCode.ProtocolError, CoreStrings.FormatHttp3ErrorMethodInvalid(""));
        }

        [Fact]
        public async Task InvalidCustomMethod_Reset()
        {
            var headers = new[]
            {
                new KeyValuePair<string, string>(HeaderNames.Method, "Hello,World"),
                new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>(HeaderNames.Authority, "localhost:80"),
            };

            var requestStream = await InitializeConnectionAndStreamsAsync(_echoApplication);
            var doneWithHeaders = await requestStream.SendHeadersAsync(headers);
            await requestStream.WaitForStreamErrorAsync(Http3ErrorCode.ProtocolError, CoreStrings.FormatHttp3ErrorMethodInvalid("Hello,World"));
        }

        [Fact]
        public async Task CustomMethod_Accepted()
        {
            var headers = new[]
            {
                new KeyValuePair<string, string>(HeaderNames.Method, "Custom"),
                new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>(HeaderNames.Authority, "localhost:80"),
            };

            var requestStream = await InitializeConnectionAndStreamsAsync(_echoMethod);
            var doneWithHeaders = await requestStream.SendHeadersAsync(headers, endStream: true);

            var responseHeaders = await requestStream.ExpectHeadersAsync();

            Assert.Equal(4, responseHeaders.Count);
            Assert.Contains("date", responseHeaders.Keys, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("200", responseHeaders[HeaderNames.Status]);
            Assert.Equal("Custom", responseHeaders["Method"]);
            Assert.Equal("0", responseHeaders["content-length"]);
        }

        [Fact]
        public async Task RequestHeadersMaxRequestHeaderFieldSize_EndsStream()
        {
            var headers = new[]
            {
                new KeyValuePair<string, string>(HeaderNames.Method, "Custom"),
                new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>(HeaderNames.Authority, "localhost:80"),
                new KeyValuePair<string, string>("test", new string('a', 10000))
            };

            var requestStream = await InitializeConnectionAndStreamsAsync(_echoApplication);

            var doneWithHeaders = await requestStream.SendHeadersAsync(headers);
            await requestStream.SendDataAsync(Encoding.ASCII.GetBytes("Hello world"));

            // TODO figure out how to test errors for request streams that would be set on the Quic Stream.
            await requestStream.ExpectReceiveEndOfStream();
        }

        [Fact]
        public async Task ConnectMethod_Accepted()
        {
            var requestStream = await InitializeConnectionAndStreamsAsync(_echoMethod);

            // :path and :scheme are not allowed, :authority is optional
            var headers = new[] { new KeyValuePair<string, string>(HeaderNames.Method, "CONNECT") };

            var doneWithHeaders = await requestStream.SendHeadersAsync(headers, endStream: true);

            var responseHeaders = await requestStream.ExpectHeadersAsync();

            Assert.Equal(4, responseHeaders.Count);
            Assert.Contains("date", responseHeaders.Keys, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("200", responseHeaders[HeaderNames.Status]);
            Assert.Equal("CONNECT", responseHeaders["Method"]);
            Assert.Equal("0", responseHeaders["content-length"]);
        }

        [Fact]
        public async Task OptionsStar_LeftOutOfPath()
        {
            var requestStream = await InitializeConnectionAndStreamsAsync(_echoPath);
            var headers = new[] { new KeyValuePair<string, string>(HeaderNames.Method, "OPTIONS"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>(HeaderNames.Path, "*")};

            var doneWithHeaders = await requestStream.SendHeadersAsync(headers, endStream: true);

            var responseHeaders = await requestStream.ExpectHeadersAsync();

            Assert.Equal(5, responseHeaders.Count);
            Assert.Contains("date", responseHeaders.Keys, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("200", responseHeaders[HeaderNames.Status]);
            Assert.Equal("", responseHeaders["path"]);
            Assert.Equal("*", responseHeaders["rawtarget"]);
            Assert.Equal("0", responseHeaders["content-length"]);
        }

        [Fact]
        public async Task OptionsSlash_Accepted()
        {
            var requestStream = await InitializeConnectionAndStreamsAsync(_echoPath);

            var headers = new[] { new KeyValuePair<string, string>(HeaderNames.Method, "OPTIONS"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>(HeaderNames.Path, "/")};

            var doneWithHeaders = await requestStream.SendHeadersAsync(headers, endStream: true);

            var responseHeaders = await requestStream.ExpectHeadersAsync();

            Assert.Equal(5, responseHeaders.Count);
            Assert.Contains("date", responseHeaders.Keys, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("200", responseHeaders[HeaderNames.Status]);
            Assert.Equal("/", responseHeaders["path"]);
            Assert.Equal("/", responseHeaders["rawtarget"]);
            Assert.Equal("0", responseHeaders["content-length"]);
        }

        [Fact]
        public async Task PathAndQuery_Separated()
        {
            var requestStream = await InitializeConnectionAndStreamsAsync(context =>
            {
                context.Response.Headers["path"] = context.Request.Path.Value;
                context.Response.Headers["query"] = context.Request.QueryString.Value;
                context.Response.Headers["rawtarget"] = context.Features.Get<IHttpRequestFeature>().RawTarget;
                return Task.CompletedTask;
            });

            // :path and :scheme are not allowed, :authority is optional
            var headers = new[] { new KeyValuePair<string, string>(HeaderNames.Method, "GET"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>(HeaderNames.Path, "/a/path?a&que%35ry")};

            var doneWithHeaders = await requestStream.SendHeadersAsync(headers, endStream: true);

            var responseHeaders = await requestStream.ExpectHeadersAsync();

            Assert.Equal(6, responseHeaders.Count);
            Assert.Contains("date", responseHeaders.Keys, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("200", responseHeaders[HeaderNames.Status]);
            Assert.Equal("/a/path", responseHeaders["path"]);
            Assert.Equal("?a&que%35ry", responseHeaders["query"]);
            Assert.Equal("/a/path?a&que%35ry", responseHeaders["rawtarget"]);
            Assert.Equal("0", responseHeaders["content-length"]);
        }

        [Theory]
        [InlineData("/", "/")]
        [InlineData("/a%5E", "/a^")]
        [InlineData("/a%E2%82%AC", "/a€")]
        [InlineData("/a%2Fb", "/a%2Fb")] // Forward slash, not decoded
        [InlineData("/a%b", "/a%b")] // Incomplete encoding, not decoded
        [InlineData("/a/b/c/../d", "/a/b/d")] // Navigation processed
        [InlineData("/a/b/c/../../../../d", "/d")] // Navigation escape prevented
        [InlineData("/a/b/c/.%2E/d", "/a/b/d")] // Decode before navigation processing
        public async Task Path_DecodedAndNormalized(string input, string expected)
        {
            var requestStream = await InitializeConnectionAndStreamsAsync(context =>
            {
                Assert.Equal(expected, context.Request.Path.Value);
                Assert.Equal(input, context.Features.Get<IHttpRequestFeature>().RawTarget);
                return Task.CompletedTask;
            });

            // :path and :scheme are not allowed, :authority is optional
            var headers = new[] { new KeyValuePair<string, string>(HeaderNames.Method, "GET"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>(HeaderNames.Path, input)};

            var doneWithHeaders = await requestStream.SendHeadersAsync(headers, endStream: true);

            var responseHeaders = await requestStream.ExpectHeadersAsync();

            Assert.Equal(3, responseHeaders.Count);
            Assert.Contains("date", responseHeaders.Keys, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("200", responseHeaders[HeaderNames.Status]);
            Assert.Equal("0", responseHeaders["content-length"]);
        }

        [Theory]
        [InlineData(":path", "/")]
        [InlineData(":scheme", "http")]
        public async Task ConnectMethod_WithSchemeOrPath_Reset(string headerName, string value)
        {
            var requestStream = await InitializeConnectionAndStreamsAsync(_noopApplication);

            // :path and :scheme are not allowed, :authority is optional
            var headers = new[] { new KeyValuePair<string, string>(HeaderNames.Method, "CONNECT"),
                new KeyValuePair<string, string>(headerName, value) };

            var doneWithHeaders = await requestStream.SendHeadersAsync(headers, endStream: true);

            await requestStream.WaitForStreamErrorAsync(Http3ErrorCode.ProtocolError, CoreStrings.Http3ErrorConnectMustNotSendSchemeOrPath);
        }

        [Fact]
        public async Task SchemeMismatch_Reset()
        {
            var requestStream = await InitializeConnectionAndStreamsAsync(_noopApplication);

            // :path and :scheme are not allowed, :authority is optional
            var headers = new[] { new KeyValuePair<string, string>(HeaderNames.Method, "GET"),
                new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "https") }; // Not the expected "http"

            var doneWithHeaders = await requestStream.SendHeadersAsync(headers, endStream: true);

            await requestStream.WaitForStreamErrorAsync(Http3ErrorCode.ProtocolError, CoreStrings.FormatHttp3StreamErrorSchemeMismatch("https", "http"));
        }

        [Fact]
        [QuarantinedTest("https://github.com/dotnet/aspnetcore/issues/21520")]
        public async Task MissingAuthority_200Status()
        {
            var headers = new[]
            {
                new KeyValuePair<string, string>(HeaderNames.Method, "GET"),
                new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
            };
            await InitializeConnectionAsync(_noopApplication);

            var requestStream = await InitializeConnectionAndStreamsAsync(_noopApplication);

            var doneWithHeaders = await requestStream.SendHeadersAsync(headers, endStream: true);

            var responseHeaders = await requestStream.ExpectHeadersAsync();

            Assert.Equal(3, responseHeaders.Count);
            Assert.Contains("date", responseHeaders.Keys, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("200", responseHeaders[HeaderNames.Status]);
            Assert.Equal("0", responseHeaders["content-length"]);
        }

        [Fact]
        public async Task EmptyAuthority_200Status()
        {
            var headers = new[]
            {
                new KeyValuePair<string, string>(HeaderNames.Method, "GET"),
                new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>(HeaderNames.Authority, ""),
            };
            var requestStream = await InitializeConnectionAndStreamsAsync(_noopApplication);

            var doneWithHeaders = await requestStream.SendHeadersAsync(headers, endStream: true);

            var responseHeaders = await requestStream.ExpectHeadersAsync();

            Assert.Equal(3, responseHeaders.Count);
            Assert.Contains("date", responseHeaders.Keys, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("200", responseHeaders[HeaderNames.Status]);
            Assert.Equal("0", responseHeaders["content-length"]);
        }

        [Fact]
        public async Task MissingAuthorityFallsBackToHost_200Status()
        {
            var headers = new[]
            {
                new KeyValuePair<string, string>(HeaderNames.Method, "GET"),
                new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>("Host", "abc"),
            };

            var requestStream = await InitializeConnectionAndStreamsAsync(_echoHost);
            var doneWithHeaders = await requestStream.SendHeadersAsync(headers, endStream: true);

            var responseHeaders = await requestStream.ExpectHeadersAsync();

            Assert.Equal(4, responseHeaders.Count);
            Assert.Contains("date", responseHeaders.Keys, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("200", responseHeaders[HeaderNames.Status]);
            Assert.Equal("0", responseHeaders[HeaderNames.ContentLength]);
            Assert.Equal("abc", responseHeaders[HeaderNames.Host]);
        }

        [Fact]
        public async Task EmptyAuthorityIgnoredOverHost_200Status()
        {
            var headers = new[]
            {
                new KeyValuePair<string, string>(HeaderNames.Method, "GET"),
                new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>(HeaderNames.Authority, ""),
                new KeyValuePair<string, string>("Host", "abc"),
            };

            var requestStream = await InitializeConnectionAndStreamsAsync(_echoHost);
            var doneWithHeaders = await requestStream.SendHeadersAsync(headers, endStream: true);

            var responseHeaders = await requestStream.ExpectHeadersAsync();

            Assert.Equal(4, responseHeaders.Count);
            Assert.Contains("date", responseHeaders.Keys, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("200", responseHeaders[HeaderNames.Status]);
            Assert.Equal("0", responseHeaders[HeaderNames.ContentLength]);
            Assert.Equal("abc", responseHeaders[HeaderNames.Host]);
        }

        [Fact]
        public async Task AuthorityOverridesHost_200Status()
        {
            var headers = new[]
            {
                new KeyValuePair<string, string>(HeaderNames.Method, "GET"),
                new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>(HeaderNames.Authority, "def"),
                new KeyValuePair<string, string>("Host", "abc"),
            };

            var requestStream = await InitializeConnectionAndStreamsAsync(_echoHost);
            var doneWithHeaders = await requestStream.SendHeadersAsync(headers, endStream: true);

            var responseHeaders = await requestStream.ExpectHeadersAsync();

            Assert.Equal(4, responseHeaders.Count);
            Assert.Contains("date", responseHeaders.Keys, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("200", responseHeaders[HeaderNames.Status]);
            Assert.Equal("0", responseHeaders[HeaderNames.ContentLength]);
            Assert.Equal("def", responseHeaders[HeaderNames.Host]);
        }

        [Fact]
        public async Task AuthorityOverridesInvalidHost_200Status()
        {
            var headers = new[]
            {
                new KeyValuePair<string, string>(HeaderNames.Method, "GET"),
                new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>(HeaderNames.Authority, "def"),
                new KeyValuePair<string, string>("Host", "a=bc"),
            };

            var requestStream = await InitializeConnectionAndStreamsAsync(_echoHost);
            var doneWithHeaders = await requestStream.SendHeadersAsync(headers, endStream: true);

            var responseHeaders = await requestStream.ExpectHeadersAsync();

            Assert.Equal(4, responseHeaders.Count);
            Assert.Contains("date", responseHeaders.Keys, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("200", responseHeaders[HeaderNames.Status]);
            Assert.Equal("0", responseHeaders[HeaderNames.ContentLength]);
            Assert.Equal("def", responseHeaders[HeaderNames.Host]);
        }

        [Fact]
        public async Task InvalidAuthority_Reset()
        {
            var headers = new[]
            {
                new KeyValuePair<string, string>(HeaderNames.Method, "GET"),
                new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>(HeaderNames.Authority, "local=host:80"),
            };

            var requestStream = await InitializeConnectionAndStreamsAsync(_noopApplication);
            var doneWithHeaders = await requestStream.SendHeadersAsync(headers, endStream: true);

            await requestStream.WaitForStreamErrorAsync(Http3ErrorCode.ProtocolError,
                CoreStrings.FormatBadRequest_InvalidHostHeader_Detail("local=host:80"));
        }

        [Fact]
        public async Task InvalidAuthorityWithValidHost_Reset()
        {
            var headers = new[]
            {
                new KeyValuePair<string, string>(HeaderNames.Method, "GET"),
                new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>(HeaderNames.Authority, "d=ef"),
                new KeyValuePair<string, string>("Host", "abc"),
            };

            var requestStream = await InitializeConnectionAndStreamsAsync(_noopApplication);
            var doneWithHeaders = await requestStream.SendHeadersAsync(headers, endStream: true);

            await requestStream.WaitForStreamErrorAsync(Http3ErrorCode.ProtocolError,
                CoreStrings.FormatBadRequest_InvalidHostHeader_Detail("d=ef"));
        }

        [Fact]
        public async Task TwoHosts_StreamReset()
        {
            var headers = new[]
            {
                new KeyValuePair<string, string>(HeaderNames.Method, "GET"),
                new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>("Host", "host1"),
                new KeyValuePair<string, string>("Host", "host2"),
            };

            var requestStream = await InitializeConnectionAndStreamsAsync(_noopApplication);
            var doneWithHeaders = await requestStream.SendHeadersAsync(headers, endStream: true);

            await requestStream.WaitForStreamErrorAsync(Http3ErrorCode.ProtocolError,
                CoreStrings.FormatBadRequest_InvalidHostHeader_Detail("host1,host2"));
        }

        [Fact]
        public async Task MaxRequestLineSize_Reset()
        {
            // Default 8kb limit
            // This test has to work around the HPack parser limit for incoming field sizes over 4kb. That's going to be a problem for people with long urls.
            // https://github.com/aspnet/KestrelHttpServer/issues/2872
            var headers = new[]
            {
                new KeyValuePair<string, string>(HeaderNames.Method, "GET" + new string('a', 1024 * 3)),
                new KeyValuePair<string, string>(HeaderNames.Path, "/Hello/How/Are/You/" + new string('a', 1024 * 3)),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>(HeaderNames.Authority, "localhost" + new string('a', 1024 * 3) + ":80"),
            };
            var requestStream = await InitializeConnectionAndStreamsAsync(_noopApplication);
            var doneWithHeaders = await requestStream.SendHeadersAsync(headers, endStream: true);

            await requestStream.WaitForStreamErrorAsync(Http3ErrorCode.ProtocolError,
                CoreStrings.BadRequest_RequestLineTooLong);
        }

        [Fact]
        public async Task ContentLength_Received_SingleDataFrame_Verified()
        {
            var headers = new[]
            {
                new KeyValuePair<string, string>(HeaderNames.Method, "POST"),
                new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>(HeaderNames.ContentLength, "12"),
            };

            var requestStream = await InitializeConnectionAndStreamsAsync(async context =>
            {
                var buffer = new byte[100];
                var read = await context.Request.Body.ReadAsync(buffer, 0, buffer.Length);
                Assert.Equal(12, read);
                read = await context.Request.Body.ReadAsync(buffer, 0, buffer.Length);
                Assert.Equal(0, read);
            });

            var doneWithHeaders = await requestStream.SendHeadersAsync(headers, endStream: false);
            await requestStream.SendDataAsync(new byte[12], endStream: true);

            var responseHeaders = await requestStream.ExpectHeadersAsync();

            Assert.Equal(3, responseHeaders.Count);
            Assert.Contains("date", responseHeaders.Keys, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("200", responseHeaders[HeaderNames.Status]);
            Assert.Equal("0", responseHeaders[HeaderNames.ContentLength]);
        }

        [Fact]
        public async Task ContentLength_Received_MultipleDataFrame_Verified()
        {
            var headers = new[]
            {
                new KeyValuePair<string, string>(HeaderNames.Method, "POST"),
                new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>(HeaderNames.ContentLength, "12"),
            };

            var requestStream = await InitializeConnectionAndStreamsAsync(async context =>
            {
                var buffer = new byte[100];
                var read = await context.Request.Body.ReadAsync(buffer, 0, buffer.Length);
                var total = read;
                while (read > 0)
                {
                    read = await context.Request.Body.ReadAsync(buffer, total, buffer.Length - total);
                    total += read;
                }
                Assert.Equal(12, total);
            });

            var doneWithHeaders = await requestStream.SendHeadersAsync(headers, endStream: false);

            await requestStream.SendDataAsync(new byte[1], endStream: false);
            await requestStream.SendDataAsync(new byte[3], endStream: false);
            await requestStream.SendDataAsync(new byte[8], endStream: true);

            var responseHeaders = await requestStream.ExpectHeadersAsync();

            Assert.Equal(3, responseHeaders.Count);
            Assert.Contains("date", responseHeaders.Keys, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("200", responseHeaders[HeaderNames.Status]);
            Assert.Equal("0", responseHeaders[HeaderNames.ContentLength]);
        }

        [Fact]
        public async Task ContentLength_Received_MultipleDataFrame_ReadViaPipe_Verified()
        {
            var headers = new[]
            {
                new KeyValuePair<string, string>(HeaderNames.Method, "POST"),
                new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>(HeaderNames.ContentLength, "12"),
            };
            var requestStream = await InitializeConnectionAndStreamsAsync(async context =>
            {
                var readResult = await context.Request.BodyReader.ReadAsync();
                while (!readResult.IsCompleted)
                {
                    context.Request.BodyReader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                    readResult = await context.Request.BodyReader.ReadAsync();
                }

                Assert.Equal(12, readResult.Buffer.Length);
                context.Request.BodyReader.AdvanceTo(readResult.Buffer.End);
            });

            var doneWithHeaders = await requestStream.SendHeadersAsync(headers, endStream: false);

            await requestStream.SendDataAsync(new byte[1], endStream: false);
            await requestStream.SendDataAsync(new byte[3], endStream: false);
            await requestStream.SendDataAsync(new byte[8], endStream: true);

            var responseHeaders = await requestStream.ExpectHeadersAsync();

            Assert.Equal(3, responseHeaders.Count);
            Assert.Contains("date", responseHeaders.Keys, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("200", responseHeaders[HeaderNames.Status]);
            Assert.Equal("0", responseHeaders[HeaderNames.ContentLength]);
        }

        [Fact]
        public async Task RemoveConnectionSpecificHeaders()
        {
            var headers = new[]
            {
                new KeyValuePair<string, string>(HeaderNames.Method, "Custom"),
                new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>(HeaderNames.Authority, "localhost:80"),
            };

            var requestStream = await InitializeConnectionAndStreamsAsync(async context =>
            {
                var response = context.Response;

                response.Headers.Add(HeaderNames.TransferEncoding, "chunked");
                response.Headers.Add(HeaderNames.Upgrade, "websocket");
                response.Headers.Add(HeaderNames.Connection, "Keep-Alive");
                response.Headers.Add(HeaderNames.KeepAlive, "timeout=5, max=1000");
                response.Headers.Add(HeaderNames.ProxyConnection, "keep-alive");

                await response.WriteAsync("Hello world");
            });

            var doneWithHeaders = await requestStream.SendHeadersAsync(headers, endStream: true);

            var responseHeaders = await requestStream.ExpectHeadersAsync();
            Assert.Equal(2, responseHeaders.Count);

            var responseData = await requestStream.ExpectDataAsync();
            Assert.Equal("Hello world", Encoding.ASCII.GetString(responseData.ToArray()));

            Assert.Contains(LogMessages, m => m.Message.Equals("One or more of the following response headers have been removed because they are invalid for HTTP/2 and HTTP/3 responses: 'Connection', 'Transfer-Encoding', 'Keep-Alive', 'Upgrade' and 'Proxy-Connection'."));
        }

        [Fact(Skip = "Http3OutputProducer.Complete is called before input recognizes there is an error. Why is this different than HTTP/2?")]
        public async Task ContentLength_Received_NoDataFrames_Reset()
        {
            var headers = new[]
            {
                new KeyValuePair<string, string>(HeaderNames.Method, "POST"),
                new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>(HeaderNames.ContentLength, "12"),
            };

            var requestStream = await InitializeConnectionAndStreamsAsync(_noopApplication);

            await requestStream.SendHeadersAsync(headers, endStream: true);

            await requestStream.WaitForStreamErrorAsync(Http3ErrorCode.ProtocolError, CoreStrings.Http3StreamErrorLessDataThanLength);
        }

        [Fact]
        public async Task ResponseTrailers_WithoutData_Sent()
        {
            var headers = new[]
            {
                new KeyValuePair<string, string>(HeaderNames.Method, "Custom"),
                new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>(HeaderNames.Authority, "localhost:80"),
            };

            var requestStream = await InitializeConnectionAndStreamsAsync(context =>
            {
                var trailersFeature = context.Features.Get<IHttpResponseTrailersFeature>();

                trailersFeature.Trailers.Add("Trailer1", "Value1");
                trailersFeature.Trailers.Add("Trailer2", "Value2");

                return Task.CompletedTask;
            });

            var doneWithHeaders = await requestStream.SendHeadersAsync(headers, endStream: true);

            var responseHeaders = await requestStream.ExpectHeadersAsync();

            var responseTrailers = await requestStream.ExpectHeadersAsync();

            Assert.Equal(2, responseTrailers.Count);
            Assert.Equal("Value1", responseTrailers["Trailer1"]);
            Assert.Equal("Value2", responseTrailers["Trailer2"]);
        }

        [Fact]
        public async Task ResponseTrailers_WithData_Sent()
        {
            var headers = new[]
            {
                new KeyValuePair<string, string>(HeaderNames.Method, "Custom"),
                new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>(HeaderNames.Authority, "localhost:80"),
            };

            var requestStream = await InitializeConnectionAndStreamsAsync(async context =>
            {
                var trailersFeature = context.Features.Get<IHttpResponseTrailersFeature>();

                trailersFeature.Trailers.Add("Trailer1", "Value1");
                trailersFeature.Trailers.Add("Trailer2", "Value2");

                await context.Response.WriteAsync("Hello world");
            });

            var doneWithHeaders = await requestStream.SendHeadersAsync(headers, endStream: true);

            var responseHeaders = await requestStream.ExpectHeadersAsync();
            var responseData = await requestStream.ExpectDataAsync();
            Assert.Equal("Hello world", Encoding.ASCII.GetString(responseData.ToArray()));

            var responseTrailers = await requestStream.ExpectHeadersAsync();

            Assert.Equal(2, responseTrailers.Count);
            Assert.Equal("Value1", responseTrailers["Trailer1"]);
            Assert.Equal("Value2", responseTrailers["Trailer2"]);
        }

        [Fact]
        public async Task ResponseTrailers_WithExeption500_Cleared()
        {
            var headers = new[]
            {
                new KeyValuePair<string, string>(HeaderNames.Method, "Custom"),
                new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>(HeaderNames.Authority, "localhost:80"),
            };

            var requestStream = await InitializeConnectionAndStreamsAsync(context =>
            {
                var trailersFeature = context.Features.Get<IHttpResponseTrailersFeature>();

                trailersFeature.Trailers.Add("Trailer1", "Value1");
                trailersFeature.Trailers.Add("Trailer2", "Value2");

                throw new NotImplementedException("Test Exception");
            });

            var doneWithHeaders = await requestStream.SendHeadersAsync(headers, endStream: true);

            var responseHeaders = await requestStream.ExpectHeadersAsync();

            await requestStream.ExpectReceiveEndOfStream();
        }

        [Fact]
        public async Task ResetStream_ReturnStreamError()
        {
            var headers = new[]
            {
                new KeyValuePair<string, string>(HeaderNames.Method, "Custom"),
                new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>(HeaderNames.Authority, "localhost:80"),
            };

            var requestStream = await InitializeConnectionAndStreamsAsync(context =>
            {
                var resetFeature = context.Features.Get<IHttpResetFeature>();

                resetFeature.Reset((int)Http3ErrorCode.RequestCancelled);

                return Task.CompletedTask;
            });

            var doneWithHeaders = await requestStream.SendHeadersAsync(headers, endStream: true);

            await requestStream.WaitForStreamErrorAsync(Http3ErrorCode.RequestCancelled, CoreStrings.FormatHttp3StreamResetByApplication(Http3ErrorCode.RequestCancelled));
        }
    }
}
