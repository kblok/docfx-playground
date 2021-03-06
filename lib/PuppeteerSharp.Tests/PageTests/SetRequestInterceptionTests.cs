﻿using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace PuppeteerSharp.Tests.PageTests
{
    [Collection("PuppeteerLoaderFixture collection")]
    public class SetRequestInterceptionTests : PuppeteerPageBaseTest
    {
        public SetRequestInterceptionTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public async Task ShouldIntercept()
        {
            await Page.SetRequestInterceptionAsync(true);
            Page.Request += async (sender, e) =>
            {
                Assert.Contains("empty.html", e.Request.Url);
                Assert.NotNull(e.Request.Headers);
                Assert.Equal(HttpMethod.Get, e.Request.Method);
                Assert.Null(e.Request.PostData);
                Assert.Equal(ResourceType.Document, e.Request.ResourceType);
                Assert.Equal(Page.MainFrame, e.Request.Frame);
                Assert.Equal("about:blank", e.Request.Frame.Url);
                await e.Request.ContinueAsync();
            };
            var response = await Page.GoToAsync(TestConstants.EmptyPage);
            Assert.True(response.Ok);
        }

        [Fact]
        public async Task ShouldStopIntercepting()
        {
            await Page.SetRequestInterceptionAsync(true);
            async void EventHandler(object sender, RequestEventArgs e)
            {
                await e.Request.ContinueAsync();
                Page.Request -= EventHandler;
            }
            Page.Request += EventHandler;
            await Page.GoToAsync(TestConstants.EmptyPage);
            await Page.SetRequestInterceptionAsync(false);
            await Page.GoToAsync(TestConstants.EmptyPage);
        }

        [Fact]
        public async Task ShouldShowCustomHTTPHeaders()
        {
            await Page.SetExtraHttpHeadersAsync(new Dictionary<string, string>
            {
                ["foo"] = "bar"
            });
            await Page.SetRequestInterceptionAsync(true);
            Page.Request += async (sender, e) =>
            {
                Assert.Equal("bar", e.Request.Headers["foo"]);
                await e.Request.ContinueAsync();
            };
            var response = await Page.GoToAsync(TestConstants.EmptyPage);
            Assert.True(response.Ok);
        }

        [Fact]
        public async Task ShouldWorksWithCustomizingRefererHeaders()
        {
            await Page.SetExtraHttpHeadersAsync(new Dictionary<string, string>
            {
                ["referer"] = TestConstants.EmptyPage
            });
            await Page.SetRequestInterceptionAsync(true);
            Page.Request += async (sender, e) =>
            {
                Assert.Equal(TestConstants.EmptyPage, e.Request.Headers["referer"]);
                await e.Request.ContinueAsync();
            };
            var response = await Page.GoToAsync(TestConstants.EmptyPage);
            Assert.True(response.Ok);
        }

        [Fact]
        public async Task ShouldBeAbortable()
        {
            await Page.SetRequestInterceptionAsync(true);
            Page.Request += async (sender, e) =>
            {
                if (e.Request.Url.EndsWith(".css"))
                {
                    await e.Request.AbortAsync();
                }
                else
                {
                    await e.Request.ContinueAsync();
                }
            };
            var failedRequests = 0;
            Page.RequestFailed += (sender, e) => failedRequests++;
            var response = await Page.GoToAsync(TestConstants.ServerUrl + "/one-style.html");
            Assert.True(response.Ok);
            Assert.Null(response.Request.Failure);
            Assert.Equal(1, failedRequests);
        }

        [Fact]
        public async Task ShouldBeAbortableWithCustomErrorCodes()
        {
            await Page.SetRequestInterceptionAsync(true);
            Page.Request += async (sender, e) =>
            {
                await e.Request.AbortAsync(RequestAbortErrorCode.InternetDisconnected);
            };
            Request failedRequest = null;
            Page.RequestFailed += (sender, e) => failedRequest = e.Request;
            await Page.GoToAsync(TestConstants.EmptyPage).ContinueWith(e => { });
            Assert.NotNull(failedRequest);
            Assert.Equal("net::ERR_INTERNET_DISCONNECTED", failedRequest.Failure);
        }

        [Fact]
        public async Task ShouldAmendHTTPHeaders()
        {
            await Page.SetRequestInterceptionAsync(true);
            Page.Request += async (sender, e) =>
            {
                var headers = new Dictionary<string, object>(e.Request.Headers);
                headers["FOO"] = "bar";
                await e.Request.ContinueAsync(new Payload { Headers = headers });
            };
            await Page.GoToAsync(TestConstants.EmptyPage);
            var requestTask = Server.WaitForRequest("/sleep.zzz", request => request.Headers["foo"].ToString());
            await Task.WhenAll(
                requestTask,
                Page.EvaluateExpressionAsync("fetch('/sleep.zzz')")
            );
            Assert.Equal("bar", requestTask.Result);
        }

        [Fact]
        public async Task ShouldFailNavigationWhenAbortingMainResource()
        {
            await Page.SetRequestInterceptionAsync(true);
            Page.Request += async (sender, e) => await e.Request.AbortAsync();
            var exception = await Assert.ThrowsAsync<NavigationException>(
                () => Page.GoToAsync(TestConstants.EmptyPage));
            Assert.Contains("net::ERR_FAILED", exception.Message);
        }

        [Fact]
        public async Task ShouldWorkWithRedirects()
        {
            await Page.SetRequestInterceptionAsync(true);
            var requests = new List<Request>();
            Page.Request += async (sender, e) =>
            {
                await e.Request.ContinueAsync();
                requests.Add(e.Request);
            };

            Server.SetRedirect("/non-existing-page.html", "/non-existing-page-2.html");
            Server.SetRedirect("/non-existing-page-2.html", "/non-existing-page-3.html");
            Server.SetRedirect("/non-existing-page-3.html", "/non-existing-page-4.html");
            Server.SetRedirect("/non-existing-page-4.html", "/empty.html");
            var response = await Page.GoToAsync(TestConstants.ServerUrl + "/non-existing-page.html");
            Assert.Equal(HttpStatusCode.OK, response.Status);
            Assert.Contains("empty.html", response.Url);
            Assert.Equal(5, requests.Count);
            Assert.Equal(ResourceType.Document, requests[2].ResourceType);
        }

        [Fact]
        public async Task ShouldBeAbleToAbortRedirects()
        {
            await Page.SetRequestInterceptionAsync(true);
            Server.SetRedirect("/non-existing.json", "/non-existing-2.json");
            Server.SetRedirect("/non-existing-2.json", "/simple.html");
            Page.Request += async (sender, e) =>
            {
                if (e.Request.Url.Contains("non-existing-2"))
                {
                    await e.Request.AbortAsync();
                }
                else
                {
                    await e.Request.ContinueAsync();
                }
            };
            await Page.GoToAsync(TestConstants.EmptyPage);
            var result = await Page.EvaluateFunctionAsync<string>(@"async () => {
                try
                {
                    await fetch('/non-existing.json');
                }
                catch (e)
                {
                    return e.message;
                }
            }");
            Assert.Contains("Failed to fetch", result);
        }

        [Fact]
        public async Task ShouldWorkWithEqualRequests()
        {
            await Page.GoToAsync(TestConstants.EmptyPage);
            var responseCount = 1;
            Server.SetRoute("/zzz", context => context.Response.WriteAsync((responseCount++) * 11 + string.Empty));
            await Page.SetRequestInterceptionAsync(true);

            var spinner = false;
            // Cancel 2nd request.
            Page.Request += async (sender, e) =>
            {
                if (spinner)
                {
                    spinner = !spinner;
                    await e.Request.AbortAsync();
                }
                else
                {
                    spinner = !spinner;
                    await e.Request.ContinueAsync();
                }
            };
            var results = await Page.EvaluateExpressionAsync<string[]>(@"Promise.all([
              fetch('/zzz').then(response => response.text()).catch(e => 'FAILED'),
              fetch('/zzz').then(response => response.text()).catch(e => 'FAILED'),
              fetch('/zzz').then(response => response.text()).catch(e => 'FAILED'),
            ])");
            Assert.Equal(new[] { "11", "FAILED", "22" }, results);
        }

        [Fact]
        public async Task ShouldNavigateToDataURLAndFireDataURLRequests()
        {
            await Page.SetRequestInterceptionAsync(true);
            var requests = new List<Request>();
            Page.Request += async (sender, e) =>
            {
                requests.Add(e.Request);
                await e.Request.ContinueAsync();
            };
            var dataURL = "data:text/html,<div>yo</div>";
            var response = await Page.GoToAsync(dataURL);
            Assert.Equal(HttpStatusCode.OK, response.Status);
            Assert.Single(requests);
            Assert.Equal(dataURL, requests[0].Url);
        }

        [Fact]
        public async Task ShouldAbortDataServer()
        {
            await Page.SetRequestInterceptionAsync(true);
            Page.Request += async (sender, e) =>
            {
                await e.Request.AbortAsync();
            };
            var exception = await Assert.ThrowsAsync<NavigationException>(
                          () => Page.GoToAsync("data:text/html,No way!"));
            Assert.Contains("net::ERR_FAILED", exception.Message);
        }

        [Fact]
        public async Task ShouldNavigateToURLWithHashAndAndFireRequestsWithoutHash()
        {
            await Page.SetRequestInterceptionAsync(true);
            var requests = new List<Request>();
            Page.Request += async (sender, e) =>
            {
                requests.Add(e.Request);
                await e.Request.ContinueAsync();
            };
            var response = await Page.GoToAsync(TestConstants.EmptyPage + "#hash");
            Assert.Equal(HttpStatusCode.OK, response.Status);
            Assert.Equal(TestConstants.EmptyPage, response.Url);
            Assert.Single(requests);
            Assert.Equal(TestConstants.EmptyPage, requests[0].Url);
        }

        [Fact]
        public async Task ShouldWorkWithEncodedServer()
        {
            // The requestWillBeSent will report encoded URL, whereas interception will
            // report URL as-is. @see crbug.com/759388
            await Page.SetRequestInterceptionAsync(true);
            Page.Request += async (sender, e) => await e.Request.ContinueAsync();
            var response = await Page.GoToAsync(TestConstants.ServerUrl + "/some nonexisting page");
            Assert.Equal(HttpStatusCode.NotFound, response.Status);
        }

        [Fact]
        public async Task ShouldWorkWithBadlyEncodedServer()
        {
            await Page.SetRequestInterceptionAsync(true);
            Server.SetRoute("/malformed?rnd=%911", context => Task.CompletedTask);
            Page.Request += async (sender, e) => await e.Request.ContinueAsync();
            var response = await Page.GoToAsync(TestConstants.ServerUrl + "/malformed?rnd=%911");
            Assert.Equal(HttpStatusCode.OK, response.Status);
        }

        [Fact]
        public async Task ShouldWorkWithEncodedServerNegative2()
        {
            // The requestWillBeSent will report URL as-is, whereas interception will
            // report encoded URL for stylesheet. @see crbug.com/759388
            await Page.SetRequestInterceptionAsync(true);
            var requests = new List<Request>();
            Page.Request += async (sender, e) =>
            {
                requests.Add(e.Request);
                await e.Request.ContinueAsync();
            };
            var response = await Page.GoToAsync($"data:text/html,<link rel=\"stylesheet\" href=\"{TestConstants.ServerUrl}/fonts?helvetica|arial\"/>");
            Assert.Equal(HttpStatusCode.OK, response.Status);
            Assert.Equal(2, requests.Count);
            Assert.Equal(HttpStatusCode.NotFound, requests[1].Response.Status);
        }

        [Fact]
        public async Task ShouldNotThrowInvalidInterceptionIdIfTheRequestWasCancelled()
        {
            await Page.SetContentAsync("<iframe></iframe>");
            await Page.SetRequestInterceptionAsync(true);
            Request request = null;
            var requestIntercepted = new TaskCompletionSource<bool>();
            Page.Request += (sender, e) => {
                request = e.Request;
                requestIntercepted.SetResult(true);
            };

            var _ = Page.QuerySelectorAsync("iframe").EvaluateFunctionAsync<object>("(frame, url) => frame.src = url", TestConstants.ServerUrl);
            // Wait for request interception.
            await requestIntercepted.Task;
            // Delete frame to cause request to be canceled.
            _ = Page.QuerySelectorAsync("iframe").EvaluateFunctionAsync<object>("frame => frame.remove()");
            await request.ContinueAsync();
        }

        [Fact]
        public async Task ShouldThrowIfInterceptionIsNotEnabled()
        {
            Exception exception = null;
            Page.Request += async (sender, e) =>
            {
                try
                {
                    await e.Request.ContinueAsync();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            };
            await Page.GoToAsync(TestConstants.EmptyPage);
            Assert.Contains("Request Interception is not enabled", exception.Message);
        }
    }
}
