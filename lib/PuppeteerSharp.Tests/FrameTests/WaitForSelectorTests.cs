﻿using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace PuppeteerSharp.Tests.FrameTests
{
    [Collection("PuppeteerLoaderFixture collection")]
    public class WaitForSelectorTests : PuppeteerPageBaseTest
    {
        const string AddElement = "tag => document.body.appendChild(document.createElement(tag))";

        public WaitForSelectorTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public async Task ShouldImmediatelyResolveTaskIfNodeExists()
        {
            await Page.GoToAsync(TestConstants.EmptyPage);
            var frame = Page.MainFrame;
            var added = false;
            await frame.WaitForSelectorAsync("*").ContinueWith(_ => added = true);
            Assert.True(added);

            added = false;
            await frame.EvaluateFunctionAsync(AddElement, "div");
            await frame.WaitForSelectorAsync("div").ContinueWith(_ => added = true);
            Assert.True(added);
        }

        [Fact]
        public async Task ShouldResolveTaskWhenNodeIsAdded()
        {
            await Page.GoToAsync(TestConstants.EmptyPage);
            var frame = Page.MainFrame;
            var added = false;
            var watchdog = frame.WaitForSelectorAsync("div").ContinueWith(_ => added = true);
            // run nop function..
            await frame.EvaluateExpressionAsync("42");
            // .. to be sure that waitForSelector promise is not resolved yet.
            Assert.False(added);

            await frame.EvaluateFunctionAsync(AddElement, "br");
            Assert.False(added);

            await frame.EvaluateFunctionAsync(AddElement, "div");
            await watchdog;
            Assert.True(added);
        }

        [Fact]
        public async Task ShouldWorkWhenNodeIsAddedThroughInnerHTML()
        {
            await Page.GoToAsync(TestConstants.EmptyPage);
            var watchdog = Page.WaitForSelectorAsync("h3 div");
            await Page.EvaluateFunctionAsync(AddElement, "span");
            await Page.EvaluateExpressionAsync("document.querySelector('span').innerHTML = '<h3><div></div></h3>'");
            await watchdog;
        }

        [Fact]
        public async Task PageWaitForSelectorAsyncIsShortcutForMainFrame()
        {
            await Page.GoToAsync(TestConstants.EmptyPage);
            await FrameUtils.AttachFrameAsync(Page, "frame1", TestConstants.EmptyPage);
            var otherFrame = Page.Frames.ElementAt(1);
            var added = false;
            var waitForSelectorTask = Page.WaitForSelectorAsync("div").ContinueWith(_ => added = true);
            await otherFrame.EvaluateFunctionAsync(AddElement, "div");
            Assert.False(added);

            await Page.EvaluateFunctionAsync(AddElement, "div");
            Assert.True(await waitForSelectorTask);
            Assert.True(added);
        }

        [Fact]
        public async Task ShouldRunInSpecifiedFrame()
        {
            await FrameUtils.AttachFrameAsync(Page, "frame1", TestConstants.EmptyPage);
            await FrameUtils.AttachFrameAsync(Page, "frame2", TestConstants.EmptyPage);
            var frame1 = Page.Frames.ElementAt(1);
            var frame2 = Page.Frames.ElementAt(2);
            var added = false;
            var selectorTask = frame2.WaitForSelectorAsync("div").ContinueWith(_ => added = true);
            Assert.False(added);

            await frame1.EvaluateFunctionAsync(AddElement, "div");
            Assert.False(added);

            await frame2.EvaluateFunctionAsync(AddElement, "div");
            Assert.True(added);
        }

        [Fact]
        public async Task ShouldThrowIfEvaluationFailed()
        {
            await Page.EvaluateOnNewDocumentAsync(@"function() {
                document.querySelector = null;
            }");
            await Page.GoToAsync(TestConstants.EmptyPage);
            var exception = await Assert.ThrowsAnyAsync<PuppeteerException>(() => Page.WaitForSelectorAsync("*"));
            Assert.Contains("document.querySelector is not a function", exception.Message);
        }

        [Fact]
        public async Task ShouldThrowWhenFrameIsDetached()
        {
            await FrameUtils.AttachFrameAsync(Page, "frame1", TestConstants.EmptyPage);
            var frame = Page.Frames.ElementAt(1);
            var waitTask = frame.WaitForSelectorAsync(".box").ContinueWith(task => task?.Exception?.InnerException);
            await FrameUtils.DetachFrameAsync(Page, "frame1");
            var waitException = await waitTask;
            Assert.NotNull(waitException);
            Assert.Contains("waitForSelector failed: frame got detached", waitException.Message);
        }

        [Fact]
        public async Task ShouldSurviveCrossProcessNavigation()
        {
            var boxFound = false;
            var waitForSelector = Page.WaitForSelectorAsync(".box").ContinueWith(_ => boxFound = true);
            await Page.GoToAsync(TestConstants.EmptyPage);
            Assert.False(boxFound);
            await Page.ReloadAsync();
            Assert.False(boxFound);
            await Page.GoToAsync(TestConstants.CrossProcessHttpPrefix + "/grid.html");
            await waitForSelector;
            Assert.True(boxFound);
        }

        [Fact]
        public async Task ShouldWaitForVisible()
        {
            var divFound = false;
            var waitForSelector = Page.WaitForSelectorAsync("div", new WaitForSelectorOptions { Visible = true })
                .ContinueWith(_ => divFound = true);
            await Page.SetContentAsync("<div style='display: none; visibility: hidden;'>1</div>");
            Assert.False(divFound);
            await Page.EvaluateExpressionAsync("document.querySelector('div').style.removeProperty('display')");
            Assert.False(divFound);
            await Page.EvaluateExpressionAsync("document.querySelector('div').style.removeProperty('visibility')");
            Assert.True(await waitForSelector);
            Assert.True(divFound);
        }

        [Fact]
        public async Task ShouldWaitForVisibleRecursively()
        {
            var divVisible = false;
            var waitForSelector = Page.WaitForSelectorAsync("div#inner", new WaitForSelectorOptions { Visible = true })
                .ContinueWith(_ => divVisible = true);
            await Page.SetContentAsync("<div style='display: none; visibility: hidden;'><div id='inner'>hi</div></div>");
            Assert.False(divVisible);
            await Page.EvaluateExpressionAsync("document.querySelector('div').style.removeProperty('display')");
            Assert.False(divVisible);
            await Page.EvaluateExpressionAsync("document.querySelector('div').style.removeProperty('visibility')");
            Assert.True(await waitForSelector);
            Assert.True(divVisible);
        }

        [Theory]
        [InlineData("visibility", "hidden")]
        [InlineData("display", "none")]
        public async Task HiddenShouldWaitForVisibility(string propertyName, string propertyValue)
        {
            var divHidden = false;
            await Page.SetContentAsync("<div style='display: block;'></div>");
            var waitForSelector = Page.WaitForSelectorAsync("div", new WaitForSelectorOptions { Hidden = true })
                .ContinueWith(_ => divHidden = true);
            await Page.WaitForSelectorAsync("div"); // do a round trip
            Assert.False(divHidden);
            await Page.EvaluateExpressionAsync($"document.querySelector('div').style.setProperty('{propertyName}', '{propertyValue}')");
            Assert.True(await waitForSelector);
            Assert.True(divHidden);
        }

        [Fact]
        public async Task HiddenShouldWaitForRemoval()
        {
            await Page.SetContentAsync("<div></div>");
            var divRemoved = false;
            var waitForSelector = Page.WaitForSelectorAsync("div", new WaitForSelectorOptions { Hidden = true })
                .ContinueWith(_ => divRemoved = true);
            await Page.WaitForSelectorAsync("div"); // do a round trip
            Assert.False(divRemoved);
            await Page.EvaluateExpressionAsync("document.querySelector('div').remove()");
            Assert.True(await waitForSelector);
            Assert.True(divRemoved);
        }

        [Fact]
        public async Task ShouldRespectTimeout()
        {
            var exception = await Assert.ThrowsAnyAsync<PuppeteerException>(async ()
                => await Page.WaitForSelectorAsync("div", new WaitForSelectorOptions { Timeout = 10 }));

            Assert.NotNull(exception);
            Assert.Contains("waiting failed: timeout", exception.Message);
        }

        [Fact]
        public async Task ShouldRespondToNodeAttributeMutation()
        {
            var divFound = false;
            var waitForSelector = Page.WaitForSelectorAsync(".zombo").ContinueWith(_ => divFound = true);
            await Page.SetContentAsync("<div class='notZombo'></div>");
            Assert.False(divFound);
            await Page.EvaluateExpressionAsync("document.querySelector('div').className = 'zombo'");
            Assert.True(await waitForSelector);
        }

        [Fact]
        public async Task ShouldReturnTheElementHandle()
        {
            var waitForSelector = Page.WaitForSelectorAsync(".zombo");
            await Page.SetContentAsync("<div class='zombo'>anything</div>");
            Assert.Equal("anything", await Page.EvaluateFunctionAsync<string>("x => x.textContent", await waitForSelector));
        }
    }
}