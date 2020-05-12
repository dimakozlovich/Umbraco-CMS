﻿using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Serilog.Context;
using Umbraco.Core;
using Umbraco.Core.Logging.Serilog.Enrichers;

namespace Umbraco.Web.Common.Middleware
{
    public class UmbracoRequestLoggingMiddleware : IMiddleware
    {
        private readonly HttpSessionIdEnricher _sessionIdEnricher;
        private readonly HttpRequestNumberEnricher _requestNumberEnricher;
        private readonly HttpRequestIdEnricher _requestIdEnricher;        

        public UmbracoRequestLoggingMiddleware(
            HttpSessionIdEnricher sessionIdEnricher,
            HttpRequestNumberEnricher requestNumberEnricher,
            HttpRequestIdEnricher requestIdEnricher)
        {
            _sessionIdEnricher = sessionIdEnricher;
            _requestNumberEnricher = requestNumberEnricher;
            _requestIdEnricher = requestIdEnricher;
        }

        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            // do not process if client-side request
            if (new Uri(context.Request.GetEncodedUrl(), UriKind.RelativeOrAbsolute).IsClientSideRequest())
            {
                await next(context);
                return;
            }

            // TODO: Need to decide if we want this stuff still, there's new request logging in serilog:
            // https://github.com/serilog/serilog-aspnetcore#request-logging which i think would suffice and replace all of this?

            using (LogContext.Push(_sessionIdEnricher))
            using (LogContext.Push(_requestNumberEnricher))
            using (LogContext.Push(_requestIdEnricher))
            {
                await next(context);
            }
        }
    }
}
