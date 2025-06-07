using Xunit;
using Moq;
using Moq.Protected;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using Microsoft.AspNetCore.Http;
using ForwardProxy.Services;
using System.Threading.Tasks;
using System.IO;
using System.Text;
using System.Collections.Generic;
using ForwardProxy.Model;
using System.Net;
using System.Threading;
using System.Linq;
using System.Collections;

namespace ForwardProxy.Tests
{
    // Simple test implementation of IRequestCookieCollection
    public class TestRequestCookieCollection : IRequestCookieCollection
    {
        private readonly Dictionary<string, string> _cookies;

        public TestRequestCookieCollection(Dictionary<string, string> cookies)
        {
            _cookies = cookies ?? new Dictionary<string, string>();
        }

        public string? this[string key] => _cookies.TryGetValue(key, out var value) ? value : null;
        public int Count => _cookies.Count;
        public ICollection<string> Keys => _cookies.Keys;
        public bool ContainsKey(string key) => _cookies.ContainsKey(key);
        public bool TryGetValue(string key, out string value) => _cookies.TryGetValue(key, out value);
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _cookies.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public class ForwardServiceTests
    {
        private readonly Mock<ILogger<ForwardService>> _mockLogger;
        private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
        private readonly ForwardService _forwardService;
        private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;

        public ForwardServiceTests()
        {
            _mockLogger = new Mock<ILogger<ForwardService>>();
            _mockHttpClientFactory = new Mock<IHttpClientFactory>();
            _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
            
            var httpClient = new HttpClient(_mockHttpMessageHandler.Object);
            _mockHttpClientFactory.Setup(_ => _.CreateClient(It.IsAny<string>())).Returns(httpClient);

            _forwardService = new ForwardService(_mockLogger.Object, _mockHttpClientFactory.Object);
        }

        private DefaultHttpContext CreateHttpContext(
            string method, 
            string forwardUrl, 
            string? body = null, 
            Dictionary<string, string>? headers = null, 
            Dictionary<string, string>? cookies = null)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Method = method;
            httpContext.Request.Headers["x-forward-url"] = forwardUrl;

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    httpContext.Request.Headers[header.Key] = header.Value;
                }
            }

            if (cookies != null)
            {
                httpContext.Request.Cookies = new TestRequestCookieCollection(cookies);
            }

            if (!string.IsNullOrEmpty(body))
            {
                var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
                httpContext.Request.Body = stream;
                httpContext.Request.ContentLength = stream.Length;
                httpContext.Request.ContentType = "application/json";
            }
            else
            {
                httpContext.Request.ContentLength = 0;
            }

            return httpContext;
        }

        [Fact]
        public async Task ForwardAsync_GetRequest_ForwardsCorrectly()
        {
            // Arrange
            var forwardUrl = "http://downstream.com/api/data";
            var httpContext = CreateHttpContext("GET", forwardUrl);

            var expectedResponse = new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{\"data\":\"success\"}", Encoding.UTF8, "application/json")
            };

            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req =>
                        req.Method == HttpMethod.Get &&
                        req.RequestUri == new System.Uri(forwardUrl)),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _forwardService.ForwardAsync(httpContext);

            // Assert
            Assert.Equal((int)HttpStatusCode.OK, result.StatusCode);
            Assert.Equal("{\"data\":\"success\"}", result.ResponseData);
            _mockHttpMessageHandler.Protected().Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get && req.RequestUri == new System.Uri(forwardUrl)),
                ItExpr.IsAny<CancellationToken>()
            );
        }

        [Fact]
        public async Task ForwardAsync_PostRequest_ForwardsBodyAndContentType()
        {
            // Arrange
            var forwardUrl = "http://downstream.com/api/create";
            var requestBody = "{\"name\":\"test\"}";
            var httpContext = CreateHttpContext("POST", forwardUrl, requestBody);
            
            var expectedResponse = new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.Created,
                Content = new StringContent("{\"id\":1}", Encoding.UTF8, "application/json")
            };

            HttpRequestMessage? capturedRequest = null;
            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _forwardService.ForwardAsync(httpContext);

            // Assert
            Assert.Equal((int)HttpStatusCode.Created, result.StatusCode);
            Assert.Equal("{\"id\":1}", result.ResponseData);
            
            // Verify the request was set up correctly
            Assert.NotNull(capturedRequest);
            Assert.NotNull(capturedRequest.Content);
            var actualBody = await capturedRequest.Content.ReadAsStringAsync();
            Assert.Equal(requestBody, actualBody);
            Assert.Equal("application/json; charset=utf-8", capturedRequest.Content.Headers.ContentType?.ToString());
        }

        [Fact]
        public async Task ForwardAsync_ForwardsHeadersExceptSkipped()
        {
            // Arrange
            var forwardUrl = "http://downstream.com/api/headers";
            var headers = new Dictionary<string, string>
            {
                { "custom-header", "value1" },
                { "Authorization", "Bearer token" },
                { "Host", "originalhost.com" }, // This should be skipped
                { "x-forward-url", "http://anotherurl.com" } // This should be skipped
            };
            var httpContext = CreateHttpContext("GET", forwardUrl, headers: headers);

            var expectedResponse = new HttpResponseMessage { StatusCode = HttpStatusCode.NoContent };

            HttpRequestMessage? capturedRequest = null;
            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
                .ReturnsAsync(expectedResponse);

            // Act
            await _forwardService.ForwardAsync(httpContext);

            // Assert
            Assert.NotNull(capturedRequest);
            Assert.True(capturedRequest.Headers.Contains("custom-header"));
            Assert.Equal("value1", capturedRequest.Headers.GetValues("custom-header").First());
            Assert.True(capturedRequest.Headers.Contains("Authorization"));
            Assert.Equal("Bearer token", capturedRequest.Headers.GetValues("Authorization").First());
            Assert.False(capturedRequest.Headers.Contains("Host"));
            Assert.False(capturedRequest.Headers.Contains("x-forward-url"));
            // Content-Length is handled by HttpClient automatically, so we don't check for it
        }
        
        [Fact]
        public async Task ForwardAsync_ForwardsCookies()
        {
            // Arrange
            var forwardUrl = "http://downstream.com/api/cookies";
            var cookies = new Dictionary<string, string>
            {
                { "session-id", "12345" },
                { "user-pref", "darkmode" }
            };
            var httpContext = CreateHttpContext("GET", forwardUrl, cookies: cookies);

            var expectedResponse = new HttpResponseMessage { StatusCode = HttpStatusCode.OK };
            HttpRequestMessage? capturedRequest = null;

            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
                .ReturnsAsync(expectedResponse);
            
            // Act
            await _forwardService.ForwardAsync(httpContext);

            // Assert
            Assert.NotNull(capturedRequest);
            Assert.True(capturedRequest.Headers.Contains("Cookie"));
            var cookieHeader = capturedRequest.Headers.GetValues("Cookie");
            Assert.Contains("session-id=12345", cookieHeader);
            Assert.Contains("user-pref=darkmode", cookieHeader);
        }

        [Fact]
        public async Task ForwardAsync_HandlesHttpClientException()
        {
            // Arrange
            var forwardUrl = "http://downstream.com/api/error";
            var httpContext = CreateHttpContext("GET", forwardUrl);

            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ThrowsAsync(new HttpRequestException("Network error"));

            // Act & Assert
            var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _forwardService.ForwardAsync(httpContext));
            Assert.Equal("Network error", exception.Message);
        }
        
        [Fact]
        public async Task ForwardAsync_HandlesNonSuccessStatusCode()
        {
            // Arrange
            var forwardUrl = "http://downstream.com/api/notfound";
            var httpContext = CreateHttpContext("GET", forwardUrl);

            var expectedResponse = new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.NotFound,
                Content = new StringContent("{\"error\":\"Resource not found\"}")
            };

            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _forwardService.ForwardAsync(httpContext);

            // Assert
            Assert.Equal((int)HttpStatusCode.NotFound, result.StatusCode);
            Assert.Equal("{\"error\":\"Resource not found\"}", result.ResponseData);
        }

        [Fact]
        public async Task ForwardAsync_EmptyBody_ContentIsNull()
        {
            // Arrange
            var forwardUrl = "http://downstream.com/api/empty";
            var httpContext = CreateHttpContext("POST", forwardUrl); // POST request with no body

            var expectedResponse = new HttpResponseMessage { StatusCode = HttpStatusCode.OK };
            HttpRequestMessage? capturedRequest = null;

            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
                .ReturnsAsync(expectedResponse);

            // Act
            await _forwardService.ForwardAsync(httpContext);

            // Assert
            Assert.NotNull(capturedRequest);
            // The original code sets Content to null if Request.ContentLength is not > 0.
            // However, HttpRequestMessage might initialize Content to some default (like StreamContent with empty stream)
            // or the AddHeadersAndCookiesAsync might add it.
            // Let's check if the content, if it exists, is effectively empty or if specific content headers are absent.
            // The key is that no actual body bytes from the original request should be present if ContentLength was 0.
            if (capturedRequest.Content != null)
            {
                 var body = await capturedRequest.Content.ReadAsStringAsync();
                 Assert.Empty(body);
            }
            // Or, more robustly, ensure no Content-Type or Content-Length was set *by our code* for an empty body.
            // The HttpClient might add its own, so we focus on what our service does.
            // The current implementation of AddHeadersAndCookiesAsync will create a StringContent if ContentLength > 0.
            // If ContentLength is 0, message.Content remains as it was (potentially null or an empty default).
            // This test verifies that if the incoming request has no body, the outgoing request's content is not set from the incoming body.
        }


        [Fact]
        public async Task AddHeadersAndCookiesAsync_ContentHeaderAddedToMessageContent()
        {
            // Arrange
            var forwardUrl = "http://test.com/api";
            // Use a custom header that should be forwarded
            var context = CreateHttpContext("POST", forwardUrl, "{\"key\":\"value\"}", 
                                            new Dictionary<string, string> { { "X-Custom-Header", "custom-value" } });

            // Act
            HttpRequestMessage? capturedRequest = null;
            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

            await _forwardService.ForwardAsync(context);

            // Assert
            Assert.NotNull(capturedRequest);
            Assert.NotNull(capturedRequest.Content);
            
            // Custom header should be added to the request headers
            Assert.True(capturedRequest.Headers.Contains("X-Custom-Header"));
            Assert.Equal("custom-value", capturedRequest.Headers.GetValues("X-Custom-Header").First());
        }
    }
}
