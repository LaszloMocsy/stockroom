namespace Stockroom.Api.Errors;

internal static class ErrorHandlingExtensions
{
    /// <summary>Registers the error envelope writer, problem details, and minimal API validation.</summary>
    public static IServiceCollection AddStockroomErrorHandling(this IServiceCollection services)
    {
        // Registered before AddProblemDetails, so it is chosen ahead of the default problem-details writer.
        services.AddSingleton<IProblemDetailsWriter, ErrorEnvelopeWriter>();
        services.AddProblemDetails();

        // DataAnnotations on endpoint parameters and request bodies. The source generator only discovers
        // public types used by endpoints in this assembly: an internal request type is silently not validated.
        services.AddValidation();
        return services;
    }

    /// <summary>
    /// Turns unhandled exceptions and empty error responses (404, 405, …) into the error envelope.
    /// Call it after the request logging middleware, so the logged status is the one the client receives.
    /// </summary>
    public static IApplicationBuilder UseStockroomErrorHandling(this IApplicationBuilder app) =>
        app
            .UseExceptionHandler(new ExceptionHandlerOptions
            {
                // Kestrel signals client errors (e.g. an oversized body) with BadHttpRequestException.
                StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
                    ? badRequest.StatusCode
                    : StatusCodes.Status500InternalServerError,
            })
            .UseStatusCodePages();
}
