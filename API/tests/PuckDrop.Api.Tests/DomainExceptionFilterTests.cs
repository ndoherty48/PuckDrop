using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using PuckDrop.Api.Contracts;
using PuckDrop.Api.Filters;
using Xunit;

namespace PuckDrop.Api.Tests;

public class DomainExceptionFilterTests
{
    private static ExceptionContext CreateContext(Exception exception)
    {
        var actionContext = new ActionContext(
            new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        return new ExceptionContext(actionContext, [])
        {
            Exception = exception
        };
    }

    [Fact]
    public void OnException_KeyNotFoundException_MapsTo404()
    {
        var context = CreateContext(new KeyNotFoundException("Poll 'x' not found."));

        new DomainExceptionFilter().OnException(context);

        Assert.True(context.ExceptionHandled);
        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(404, result.StatusCode);
        var body = Assert.IsType<ErrorResponse>(result.Value);
        Assert.Equal("NOT_FOUND", body.Error);
        Assert.Equal("Poll 'x' not found.", body.Message);
    }

    [Fact]
    public void OnException_InvalidOperationException_MapsTo400WithInvalidOperationCode()
    {
        var context = CreateContext(new InvalidOperationException("Cannot publish."));

        new DomainExceptionFilter().OnException(context);

        Assert.True(context.ExceptionHandled);
        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(400, result.StatusCode);
        var body = Assert.IsType<ErrorResponse>(result.Value);
        Assert.Equal("INVALID_OPERATION", body.Error);
    }

    [Fact]
    public void OnException_ArgumentException_MapsTo400WithValidationCode()
    {
        var context = CreateContext(new ArgumentException("Bad option ID."));

        new DomainExceptionFilter().OnException(context);

        Assert.True(context.ExceptionHandled);
        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(400, result.StatusCode);
        var body = Assert.IsType<ErrorResponse>(result.Value);
        Assert.Equal("VALIDATION_ERROR", body.Error);
    }

    [Fact]
    public void OnException_ArgumentExceptionSubclass_IsStillMappedByTheBaseTypePattern()
    {
        // Type patterns match subclasses, so the ArgumentException arm handles this.
        var context = CreateContext(new ArgumentOutOfRangeException("count"));

        new DomainExceptionFilter().OnException(context);

        Assert.True(context.ExceptionHandled);
        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("VALIDATION_ERROR", Assert.IsType<ErrorResponse>(result.Value).Error);
    }

    [Fact]
    public void OnException_TrulyUnmappedExceptionType_LeavesItUnhandled()
    {
        var context = CreateContext(new NotSupportedException("Not supported."));

        new DomainExceptionFilter().OnException(context);

        Assert.False(context.ExceptionHandled);
        Assert.Null(context.Result);
    }
}
