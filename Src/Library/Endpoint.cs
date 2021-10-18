﻿using FastEndpoints.Validation;
using FastEndpoints.Validation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;

namespace FastEndpoints;

[HideFromDocs]
public abstract class BaseEndpoint : IEndpoint
{
#pragma warning disable CS8601
    internal static JsonSerializerOptions? SerializerOptions { get; set; } //set on app startup from .UseFastEndpoints()
    internal static MethodInfo ExecMethodInfo { get; set; } = typeof(BaseEndpoint).GetMethod(nameof(BaseEndpoint.ExecAsync), BindingFlags.NonPublic | BindingFlags.Instance);
    internal static PropertyInfo SettingsPropInfo { get; set; } = typeof(BaseEndpoint).GetProperty(nameof(Settings), BindingFlags.NonPublic | BindingFlags.Instance);
#pragma warning restore CS8601

    internal EndpointSettings Settings { get; set; } = new();

    internal abstract Task ExecAsync(HttpContext ctx, IValidator validator, object preProcessors, object postProcessors, CancellationToken ct);

    internal string GetTestURL()
    {
        Configure();

        if (Settings.Routes is null)
            throw new ArgumentNullException($"GetTestURL()[{nameof(Settings.Routes)}]");

        return Settings.Routes[0];
    }

    /// <summary>
    /// the http context of the current request
    /// </summary>
#pragma warning disable CS8618
    public HttpContext HttpContext { get; set; }
#pragma warning restore CS8618

    /// <summary>
    /// use this method to configure how this endpoint should be listening to incoming requests
    /// </summary>
    public abstract void Configure();

    /// <summary>
    /// the list of validation failures for the current request dto
    /// </summary>
    public List<ValidationFailure> ValidationFailures { get; } = new();
}

/// <summary>
/// use this base class for defining endpoints that doesn't need a request dto. usually used for routes that doesn't have any parameters.
/// </summary>
public abstract class EndpointWithoutRequest : Endpoint<EmptyRequest> { }

/// <summary>
/// use this base class for defining endpoints that doesn't need a request dto but return a response dto.
/// </summary>
/// <typeparam name="TResponse">the type of the response dto</typeparam>
public abstract class EndpointWithoutRequest<TResponse> : Endpoint<EmptyRequest, TResponse> where TResponse : notnull, new() { }

/// <summary>
/// use this base class for defining endpoints that only use a request dto and don't use a response dto.
/// </summary>
/// <typeparam name="TRequest">the type of the request dto</typeparam>
public abstract class Endpoint<TRequest> : Endpoint<TRequest, object> where TRequest : notnull, new() { };

/// <summary>
/// use this base class for defining endpoints that use both request and response dtos.
/// </summary>
/// <typeparam name="TRequest">the type of the request dto</typeparam>
/// <typeparam name="TResponse">the type of the response dto</typeparam>
public abstract class Endpoint<TRequest, TResponse> : BaseEndpoint where TRequest : notnull, new() where TResponse : notnull, new()
{
    /// <summary>
    /// indicates if there are any validation failures for the current request
    /// </summary>
    public bool ValidationFailed => ValidationFailures.Count > 0;
    /// <summary>
    /// the current user principal
    /// </summary>
    protected ClaimsPrincipal User => HttpContext.User;
    /// <summary>
    /// the response that is sent to the client.
    /// </summary>
    protected TResponse Response { get; set; } = new();
    /// <summary>
    /// gives access to the configuration
    /// </summary>
    protected IConfiguration Config => HttpContext.RequestServices.GetRequiredService<IConfiguration>();
    /// <summary>
    /// gives access to the hosting environment
    /// </summary>
    protected IWebHostEnvironment Env => HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
    /// <summary>
    /// the logger for the current endpoint type
    /// </summary>
    protected ILogger Logger => HttpContext.RequestServices.GetRequiredService<ILogger<Endpoint<TRequest, TResponse>>>();
    /// <summary>
    /// the base url of the current request
    /// </summary>
    protected string BaseURL => HttpContext.Request?.Scheme + "://" + HttpContext.Request?.Host + "/";
    /// <summary>
    /// the http method of the current request
    /// </summary>
    protected Http HttpMethod => Enum.Parse<Http>(HttpContext.Request.Method);
    /// <summary>
    /// the form sent with the request. only populated if content-type is 'application/x-www-form-urlencoded' or 'multipart/form-data'
    /// </summary>
    protected IFormCollection Form => HttpContext.Request.Form;
    /// <summary>
    /// the files sent with the request. only populated when content-type is 'multipart/form-data'
    /// </summary>
    protected IFormFileCollection Files => Form.Files;

    /// <summary>
    /// specify one or more route patterns this endpoint should be listening for
    /// </summary>
    /// <param name="patterns"></param>
    protected void Routes(params string[] patterns)
    {
        Settings.Routes = patterns;
        Settings.InternalConfigAction = b =>
        {
            if (typeof(TRequest) != typeof(EmptyRequest)) b.Accepts<TRequest>("application/json");
            b.Produces<TResponse>();
        };
    }
    /// <summary>
    /// specify one or more http method verbs this endpoint should be accepting requests for
    /// </summary>
    /// <param name="methods"></param>
    protected void Verbs(params Http[] methods) => Settings.Verbs = methods.Select(m => m.ToString()).ToArray();
    /// <summary>
    /// disable auto validation failure responses (400 bad request with error details) for this endpoint
    /// </summary>
    protected void DontThrowIfValidationFails() => Settings.ThrowIfValidationFails = false;
    /// <summary>
    /// allow unauthenticated requests to this endpoint. optionally specify a set of verbs to allow unauthenticated access with.
    /// i.e. if the endpoint is listening to POST, PUT &amp; PATCH and you specify AllowAnonymous(Http.POST), then only PUT &amp; PATCH will require authentication.
    /// </summary>
    protected void AllowAnonymous(params Http[] verbs)
    {
        Settings.AnonymousVerbs =
            verbs.Length > 0
            ? verbs.Select(v => v.ToString()).ToArray()
            : Enum.GetNames(typeof(Http));
    }
    /// <summary>
    /// enable file uploads with multipart/form-data content type
    /// </summary>
    protected void AllowFileUploads() => Settings.AllowFileUploads = true;
    /// <summary>
    /// specify one or more authorization policy names you have added to the middleware pipeline during app startup/ service configuration that should be applied to this endpoint.
    /// </summary>
    /// <param name="policyNames">one or more policy names (must have been added to the pipeline on startup)</param>
    protected void Policies(params string[] policyNames) => Settings.PreBuiltUserPolicies = policyNames;
    /// <summary>
    /// specify that the current claim principal/ user should posses at least one of the roles (claim type) mentioned here. access will be forbidden if the user doesn't have any of the specified roles.
    /// </summary>
    /// <param name="rolesNames">one or more roles that has access</param>
    protected void Roles(params string[] rolesNames) => Settings.Roles = rolesNames;
    /// <summary>
    /// specify the permissions a user principal should posses in order to access this endpoint. they must posses ALL of the permissions mentioned here. if not, a 403 forbidden response will be sent.
    /// </summary>
    /// <param name="permissions">the permissions needed to access this endpoint</param>
    protected void Permissions(params string[] permissions) => Permissions(false, permissions);
    /// <summary>
    /// specify the permissions a user principal should posses in order to access this endpoint.
    /// </summary>
    /// <param name="allowAny">if set to true, having any 1 of the specified permissions will enable access</param>
    /// <param name="permissions">the permissions</param>
    protected void Permissions(bool allowAny, params string[] permissions)
    {
        Settings.AllowAnyPermission = allowAny;
        Settings.Permissions = permissions;
    }
    /// <summary>
    /// specify to allow access if the user has any of the given permissions
    /// </summary>
    /// <param name="permissions">the permissions</param>
    protected void AnyPermission(params string[] permissions) => Permissions(true, permissions);
    /// <summary>
    /// specify the claim types a user principal should posses in order to access this endpoint. they must posses ALL of the claim types mentioned here. if not, a 403 forbidden response will be sent.
    /// </summary>
    /// <param name="claims">the claims needed to access this endpoint</param>
    protected void Claims(params string[] claims) => Claims(false, claims);
    /// <summary>
    /// specify the claim types a user principal should posses in order to access this endpoint.
    /// </summary>
    /// <param name="allowAny">if set to true, having any 1 of the specified permissions will enable access</param>
    /// <param name="claims">the claims</param>
    protected void Claims(bool allowAny, params string[] claims)
    {
        Settings.AllowAnyClaim = allowAny;
        Settings.Claims = claims;
    }
    /// <summary>
    /// specify to allow access if the user has any of the given claims
    /// </summary>
    /// <param name="claims">the claims</param>
    protected void AnyClaim(params string[] claims) => Claims(true, claims);
    /// <summary>
    /// configure a collection of pre-processors to be executed before the main handler function is called. processors are executed in the order they are defined here.
    /// </summary>
    /// <param name="preProcessors">the pre processors to be executed</param>
    protected void PreProcessors(params IPreProcessor<TRequest>[] preProcessors) => Settings.PreProcessors = preProcessors;
    /// <summary>
    /// configure a collection of post-processors to be executed after the main handler function is done. processors are executed in the order they are defined here.
    /// </summary>
    /// <param name="postProcessors">the post processors to be executed</param>
    protected void PostProcessors(params IPostProcessor<TRequest, TResponse>[] postProcessors) => Settings.PostProcessors = postProcessors;
    /// <summary>
    /// specify response caching settings for this endpoint
    /// </summary>
    /// <param name="durationSeconds">the duration in seconds for which the response is cached</param>
    /// <param name="location">the location where the data from a particular URL must be cached</param>
    /// <param name="noStore">specify whether the data should be stored or not</param>
    /// <param name="varyByHeader">the value for the Vary response header</param>
    /// <param name="varyByQueryKeys">the query keys to vary by</param>
    protected void ResponseCache(int durationSeconds, ResponseCacheLocation location = ResponseCacheLocation.Any, bool noStore = false, string? varyByHeader = null, string[]? varyByQueryKeys = null)
    {
        Settings.ResponseCacheSettings = new()
        {
            Duration = durationSeconds,
            Location = location,
            NoStore = noStore,
            VaryByHeader = varyByHeader,
            VaryByQueryKeys = varyByQueryKeys
        };
    }
    /// <summary>
    /// set endpoint configurations options using an endpoint builder action
    /// </summary>
    /// <param name="builder">the builder for this endpoint</param>
    protected void Options(Action<RouteHandlerBuilder> builder) => Settings.UserConfigAction = builder;

    /// <summary>
    /// the handler method for the endpoint. this method is called for each request received.
    /// </summary>
    /// <param name="req">the request dto</param>
    /// <param name="ct">a cancellation token</param>
    public abstract Task HandleAsync(TRequest req, CancellationToken ct);

    internal override async Task ExecAsync(HttpContext ctx, IValidator? validator, object? preProcessors, object? postProcessors, CancellationToken cancellation)
    {
        HttpContext = ctx;
        var req = await BindIncomingDataAsync(ctx, cancellation).ConfigureAwait(false);
        try
        {
            BindFromUserClaims(req, ctx, ValidationFailures);
            await ValidateRequestAsync(req, (IValidator<TRequest>?)validator, preProcessors, cancellation).ConfigureAwait(false);
            await RunPreprocessors(preProcessors, req, cancellation).ConfigureAwait(false);
            await HandleAsync(req, cancellation).ConfigureAwait(false);
            await RunPostProcessors(postProcessors, req, cancellation).ConfigureAwait(false);
        }
        catch (ValidationFailureException)
        {
            await SendErrorsAsync(cancellation).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// adds a "GeneralError" to the current list of validation failures
    /// </summary>
    /// <param name="message">the error message</param>
    protected void AddError(string message)
        => ValidationFailures.Add(new ValidationFailure("GeneralErrors", message));

    /// <summary>
    /// adds an error message for the specified property of the request dto
    /// </summary>
    /// <param name="property">the property to add teh error message for</param>
    /// <param name="errorMessage">the error message</param>
    protected void AddError(Expression<Func<TRequest, object>> property, string errorMessage)
    {
        ValidationFailures.Add(
            new ValidationFailure(property.PropertyName(), errorMessage));
    }

    /// <summary>
    /// interrupt the flow of handler execution and send a 400 bad request with error details if there are any validation failures in the current request. if there are no validation failures, execution will continue past this call.
    /// </summary>
    protected void ThrowIfAnyErrors()
    {
        if (ValidationFailed) throw new ValidationFailureException();
    }

    /// <summary>
    /// add a "GeneralError" to the validation failure list and send back a 400 bad request with error details immediately interrupting handler execution flow. if there are any vallidation failures, no execution will continue past this call.
    /// </summary>
    /// <param name="message">the error message</param>
    protected void ThrowError(string message)
    {
        AddError(message);
        ThrowIfAnyErrors();
    }

    /// <summary>
    /// adds an error message for the specified property of the request dto and sends back a 400 bad request with error details immediately interrupting handler execution flow. no execution will continue past this call.
    /// </summary>
    /// <param name="property"></param>
    /// <param name="errorMessage"></param>
    protected void ThrowError(Expression<Func<TRequest, object>> property, string errorMessage)
    {
        AddError(property, errorMessage);
        ThrowIfAnyErrors();
    }

    /// <summary>
    /// send the supplied response dto serialized as json to the client.
    /// </summary>
    /// <param name="response">the object to serialize to json</param>
    /// <param name="statusCode">optional custom http status code</param>
    /// <param name="cancellation">optional cancellation token</param>
    protected Task SendAsync(TResponse response, int statusCode = 200, CancellationToken cancellation = default)
    {
        Response = response;
        HttpContext.Response.StatusCode = statusCode;
        return HttpContext.Response.WriteAsJsonAsync(response, SerializerOptions, cancellation);
    }

    /// <summary>
    /// send the supplied string content to the client.
    /// </summary>
    /// <param name="content">the string to write to the response body</param>
    /// <param name="statusCode">optional custom http status code</param>
    /// <param name="cancellation">optional cancellation token</param>
    protected Task SendStringAsync(string content, int statusCode = 200, CancellationToken cancellation = default)
    {
        HttpContext.Response.StatusCode = statusCode;
        HttpContext.Response.ContentType = "text/plain";
        return HttpContext.Response.WriteAsync(content, cancellation);
    }

    /// <summary>
    /// send an http 200 ok response without any body
    /// </summary>
    protected Task SendOkAsync()
    {
        HttpContext.Response.StatusCode = 200;
        return Task.CompletedTask;
    }

    /// <summary>
    /// send a 400 bad request with error details of the current validation failures
    /// </summary>
    /// <param name="cancellation"></param>
    protected Task SendErrorsAsync(CancellationToken cancellation = default)
    {
        HttpContext.Response.StatusCode = 400;
        return HttpContext.Response.WriteAsJsonAsync(new ErrorResponse(ValidationFailures), SerializerOptions, cancellation);
    }

    /// <summary>
    /// send a 204 no content response
    /// </summary>
    protected Task SendNoContentAsync()
    {
        HttpContext.Response.StatusCode = 204;
        return Task.CompletedTask;
    }

    /// <summary>
    /// send a 404 not found response
    /// </summary>
    protected Task SendNotFoundAsync()
    {
        HttpContext.Response.StatusCode = 404;
        return Task.CompletedTask;
    }

    /// <summary>
    /// send a 401 unauthorized response
    /// </summary>
    protected Task SendUnauthorizedAsync()
    {
        HttpContext.Response.StatusCode = 401;
        return Task.CompletedTask;
    }

    /// <summary>
    /// send a 403 unauthorized response
    /// </summary>
    protected Task SendForbiddenAsync()
    {
        HttpContext.Response.StatusCode = 403;
        return Task.CompletedTask;
    }

    /// <summary>
    /// send a byte array to the client
    /// </summary>
    /// <param name="bytes">the bytes to send</param>
    /// <param name="contentType">optional content type to set on the http response</param>
    /// <param name="cancellation">optional cancellation token</param>
    protected async Task SendBytesAsync(byte[] bytes, string? fileName = null, string contentType = "application/octet-stream", CancellationToken cancellation = default)
    {
        using var memoryStream = new MemoryStream(bytes);
        await SendStreamAsync(memoryStream, fileName, bytes.Length, contentType, cancellation).ConfigureAwait(false);
    }

    /// <summary>
    /// send a file to the client
    /// </summary>
    /// <param name="fileInfo"></param>
    /// <param name="contentType">optional content type to set on the http response</param>
    /// <param name="cancellation">optional cancellation token</param>
    protected Task SendFileAsync(FileInfo fileInfo, string contentType = "application/octet-stream", CancellationToken cancellation = default)
    {
        return SendStreamAsync(fileInfo.OpenRead(), fileInfo.Name, fileInfo.Length, contentType, cancellation);
    }

    /// <summary>
    /// send the contents of a stream to the client
    /// </summary>
    /// <param name="stream">the stream to read the data from</param>
    /// <param name="fileName">and optional file name to set in the content-disposition header</param>
    /// <param name="fileLengthBytes">optional total size of the file/stream</param>
    /// <param name="contentType">optional content type to set on the http response</param>
    /// <param name="cancellation">optional cancellation token</param>
    protected Task SendStreamAsync(Stream stream, string? fileName = null, long? fileLengthBytes = null, string contentType = "application/octet-stream", CancellationToken cancellation = default)
    {
        HttpContext.Response.StatusCode = 200;
        HttpContext.Response.ContentType = contentType;
        HttpContext.Response.ContentLength = fileLengthBytes;

        if (fileName is not null)
            HttpContext.Response.Headers.Add("Content-Disposition", $"attachment; filename={fileName}");

        return HttpContext.WriteToResponseAsync(stream, cancellation == default ? HttpContext.RequestAborted : cancellation);
    }

    /// <summary>
    /// publish the given model/dto to all the subscribers of the event notification
    /// </summary>
    /// <param name="eventModel">the notification event model/dto to publish</param>
    /// <param name="waitMode">specify whether to wait for none, any or all of the subscribers to complete their work</param>
    ///<param name="cancellation">an optional cancellation token</param>
    /// <returns>a Task that matches the wait mode specified.
    /// Mode.WaitForNone returns an already completed Task (fire and forget).
    /// Mode.WaitForAny returns a Task that will complete when any of the subscribers complete their work.
    /// Mode.WaitForAll return a Task that will complete only when all of the subscribers complete their work.</returns>
    protected Task PublishAsync<TEvent>(TEvent eventModel, Mode waitMode = Mode.WaitForAll, CancellationToken cancellation = default) where TEvent : class
        => Event<TEvent>.PublishAsync(eventModel, waitMode, cancellation);

    /// <summary>
    /// try to resolve an instance for the given type from the dependency injection container. will return null if unresolvable.
    /// </summary>
    /// <typeparam name="TService">the type of the service to resolve</typeparam>
    protected TService? TryResolve<TService>() => HttpContext.RequestServices.GetService<TService>();

    /// <summary>
    /// try to resolve an instance for the given type from the dependency injection container. will return null if unresolvable.
    /// </summary>
    /// <param name="typeOfService">the type of the service to resolve</param>
    protected object? TryResolve(Type typeOfService) => HttpContext.RequestServices.GetService(typeOfService);

    /// <summary>
    /// resolve an instance for the given type from the dependency injection container. will throw if unresolvable.
    /// </summary>
    /// <typeparam name="TService">the type of the service to resolve</typeparam>
    /// <exception cref="InvalidOperationException">Thrown if requested service cannot be resolved</exception>
    protected TService Resolve<TService>() where TService : notnull => HttpContext.RequestServices.GetRequiredService<TService>();

    /// <summary>
    /// resolve an instance for the given type from the dependency injection container. will throw if unresolvable.
    /// </summary>
    /// <param name="typeOfService">the type of the service to resolve</param>
    /// <exception cref="InvalidOperationException">Thrown if requested service cannot be resolved</exception>
    protected object Resolve(Type typeOfService) => HttpContext.RequestServices.GetRequiredService(typeOfService);

    private static async Task<TRequest> BindIncomingDataAsync(HttpContext ctx, CancellationToken cancellation)
    {
        TRequest? req = default;

        if (ctx.Request.HasJsonContentType())
            req = await ctx.Request.ReadFromJsonAsync<TRequest>(SerializerOptions, cancellation).ConfigureAwait(false);

        if (req is null) req = new();

        BindFromFormValues(req, ctx.Request);

        BindFromRouteValues(req, ctx.Request.RouteValues);

        BindFromQueryParams(req, ctx.Request.Query);

        return req;
    }

    private async Task ValidateRequestAsync(TRequest req, IValidator<TRequest>? validator, object? preProcessors, CancellationToken cancellation)
    {
        if (validator is null) return;

        var valResult = await validator.ValidateAsync(req, cancellation).ConfigureAwait(false);

        if (!valResult.IsValid)
            ValidationFailures.AddRange(valResult.Errors);

        if (ValidationFailed && ((IValidatorWithState)validator).ThrowIfValidationFails)
        {
            await RunPreprocessors(preProcessors, req, cancellation).ConfigureAwait(false);
            throw new ValidationFailureException();
        }
    }

    private async Task RunPostProcessors(object? postProcessors, TRequest req, CancellationToken cancellation)
    {
        if (postProcessors is not null)
        {
            foreach (var pp in (IPostProcessor<TRequest, TResponse>[])postProcessors)
                await pp.PostProcessAsync(req, Response, HttpContext, ValidationFailures, cancellation).ConfigureAwait(false);
        }
    }

    private async Task RunPreprocessors(object? preProcessors, TRequest req, CancellationToken cancellation)
    {
        if (preProcessors is not null)
        {
            foreach (var p in (IPreProcessor<TRequest>[])preProcessors)
                await p.PreProcessAsync(req, HttpContext, ValidationFailures, cancellation).ConfigureAwait(false);
        }
    }

    private static void BindFromFormValues(TRequest req, HttpRequest httpRequest)
    {
        if (!httpRequest.HasFormContentType) return;

        var formFields = httpRequest.Form.Select(kv => new KeyValuePair<string, object?>(kv.Key, kv.Value[0])).ToArray();

        for (int i = 0; i < formFields.Length; i++)
            Bind(req, formFields[i]);
    }

    private static void BindFromUserClaims(TRequest req, HttpContext ctx, List<ValidationFailure> failures)
    {
        for (int i = 0; i < ReqTypeCache<TRequest>.CachedFromClaimProps.Count; i++)
        {
            var (claimType, forbidIfMissing, propInfo) = ReqTypeCache<TRequest>.CachedFromClaimProps[i];
            var claimVal = ctx.User.FindFirst(c => c.Type.Equals(claimType, StringComparison.OrdinalIgnoreCase))?.Value;

            if (claimVal is null && forbidIfMissing)
                failures.Add(new(claimType, "User doesn't have this claim type!"));

            if (claimVal is not null)
                propInfo.SetValue(req, claimVal);
        }
        if (failures.Count > 0) throw new ValidationFailureException();
    }

    private static void BindFromRouteValues(TRequest req, RouteValueDictionary routeValues)
    {
        var routeKVPs = routeValues.Where(rv => ((string?)rv.Value)?.StartsWith("{") == false).ToArray();

        for (int i = 0; i < routeKVPs.Length; i++)
            Bind(req, routeKVPs[i]);
    }

    private static void BindFromQueryParams(TRequest req, IQueryCollection query)
    {
        var queryParams = query.Select(kv => new KeyValuePair<string, object?>(kv.Key, kv.Value[0])).ToArray();

        for (int i = 0; i < queryParams.Length; i++)
            Bind(req, queryParams[i]);
    }

    private static void Bind(TRequest req, KeyValuePair<string, object?> rv)
    {
        if (ReqTypeCache<TRequest>.CachedProps.TryGetValue(rv.Key.ToLower(), out var prop))
        {
            bool success = false;

            switch (prop.TypeCode)
            {
                case TypeCode.String:
                    success = true;
                    prop.PropInfo.SetValue(req, rv.Value);
                    break;

                case TypeCode.Boolean:
                    success = bool.TryParse((string?)rv.Value, out var resBool);
                    prop.PropInfo.SetValue(req, resBool);
                    break;

                case TypeCode.Int32:
                    success = int.TryParse((string?)rv.Value, out var resInt);
                    prop.PropInfo.SetValue(req, resInt);
                    break;

                case TypeCode.Int64:
                    success = long.TryParse((string?)rv.Value, out var resLong);
                    prop.PropInfo.SetValue(req, resLong);
                    break;

                case TypeCode.Double:
                    success = double.TryParse((string?)rv.Value, out var resDbl);
                    prop.PropInfo.SetValue(req, resDbl);
                    break;

                case TypeCode.Decimal:
                    success = decimal.TryParse((string?)rv.Value, out var resDec);
                    prop.PropInfo.SetValue(req, resDec);
                    break;
            }

            if (!success)
            {
                throw new NotSupportedException(
                "Binding route value failed! " +
                $"{typeof(TRequest).FullName}.{prop.PropInfo.Name}[{prop.TypeCode}] Tried: \"{rv.Value}\"");
            }
        }
    }
}
