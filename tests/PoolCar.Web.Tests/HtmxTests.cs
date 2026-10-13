using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using PoolCar.Web.Htmx;
using Shouldly;

namespace PoolCar.Web.Tests;

public class HtmxTests
{
    [Theory]
    [InlineData(null, null, false, false, false)]
    [InlineData("true", null, true, false, true)]
    [InlineData("TRUE", null, true, false, true)]
    [InlineData("true", "true", true, true, false)]
    [InlineData("false", null, false, false, false)]
    public void ReadsHtmxHeaders(string? request, string? boosted, bool isHtmx, bool isBoosted, bool isPartial)
    {
        var httpRequest = CreateContext(request, boosted).Request;

        httpRequest.IsHtmx().ShouldBe(isHtmx);
        httpRequest.IsHtmxBoosted().ShouldBe(isBoosted);
        httpRequest.IsHtmxPartial().ShouldBe(isPartial);
    }

    [Fact]
    public void HistoryRestore_IsNotPartial()
    {
        var httpRequest = CreateContext("true", null).Request;
        httpRequest.Headers[HtmxRequestExtensions.HistoryRestoreHeader] = "true";

        httpRequest.IsHtmxPartial().ShouldBeFalse();
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("true", "true", false)]
    [InlineData("true", null, true)]
    public void PartialOrPage_ReturnsPartialOnlyForHtmxSwap(string? request, string? boosted, bool expectPartial)
    {
        var page = CreatePage(request, boosted);

        var result = page.PartialOrPage("_Cars", "model");

        if (expectPartial)
        {
            var partial = result.ShouldBeOfType<PartialViewResult>();
            partial.ViewName.ShouldBe("_Cars");
            partial.Model.ShouldBe("model");
        }
        else
        {
            result.ShouldBeOfType<PageResult>();
        }
    }

    [Fact]
    public void Partial_WithStatusCode_SetsStatus()
    {
        var result = CreatePage("true", null).Partial("_Form", null, StatusCodes.Status422UnprocessableEntity);

        result.StatusCode.ShouldBe(422);
    }

    [Fact]
    public void ToastResult_RendersOutOfBandToast()
    {
        var toast = Toast.Error("Deze auto is al gereserveerd.");

        var result = CreatePage("true", null).ToastResult(toast, StatusCodes.Status409Conflict);

        result.ViewName.ShouldBe(Toast.OutOfBandPartial);
        result.Model.ShouldBe(toast);
        result.StatusCode.ShouldBe(409);
    }

    [Fact]
    public void HtmxRedirect_ForHtmx_SetsHeader()
    {
        var page = CreatePage("true", null);

        page.HtmxRedirect("/reserveringen").ShouldBeOfType<NoContentResult>();
        page.Response.Headers["HX-Redirect"].ToString().ShouldBe("/reserveringen");
    }

    [Fact]
    public void HtmxRedirect_WithoutHtmx_Redirects()
    {
        var page = CreatePage(null, null);

        page.HtmxRedirect("/reserveringen").ShouldBeOfType<RedirectResult>().Url.ShouldBe("/reserveringen");
        page.Response.Headers.ContainsKey("HX-Redirect").ShouldBeFalse();
    }

    [Theory]
    [InlineData(ToastKind.Success)]
    [InlineData(ToastKind.Error)]
    public void Toast_IsKeptForNextPage(ToastKind kind)
    {
        var tempData = new TempDataDictionary(new DefaultHttpContext(), new InMemoryTempDataProvider());

        new Toast("Gelukt.", kind).Store(tempData);

        Toast.Take(tempData).ShouldBe(new Toast("Gelukt.", kind));
    }

    [Fact]
    public void Take_WithoutToast_ReturnsNull()
    {
        var tempData = new TempDataDictionary(new DefaultHttpContext(), new InMemoryTempDataProvider());

        Toast.Take(tempData).ShouldBeNull();
    }

    private static DefaultHttpContext CreateContext(string? request, string? boosted)
    {
        var context = new DefaultHttpContext();
        if (request is not null)
        {
            context.Request.Headers["HX-Request"] = request;
        }

        if (boosted is not null)
        {
            context.Request.Headers["HX-Boosted"] = boosted;
        }

        return context;
    }

    private static TestPage CreatePage(string? request, string? boosted) =>
        new()
        {
            MetadataProvider = new EmptyModelMetadataProvider(),
            PageContext = new PageContext
            {
                HttpContext = CreateContext(request, boosted),
                ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()),
            },
        };

    private sealed class TestPage : PageModel;

    private sealed class InMemoryTempDataProvider : ITempDataProvider
    {
        private IDictionary<string, object> _values = new Dictionary<string, object>();

        public IDictionary<string, object> LoadTempData(HttpContext context) => _values;

        public void SaveTempData(HttpContext context, IDictionary<string, object> values) => _values = values;
    }
}
