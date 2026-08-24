using Gridlet.AspNetCore.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using static Gridlet.AspNetCore.GridletEndpointHelpers;

namespace Gridlet.AspNetCore;

internal static partial class GridletApiEndpoints
{
    private static void MapResultExports(RouteGroupBuilder api)
        => api.MapPost("/exports/{format}", ExportResults);

    private static Task<IResult> ExportResults(
        string format,
        ResultExportRequest body,
        IOptionsMonitor<GridletOptions> options,
        CancellationToken cancellationToken)
        => Execute(async () =>
        {
            var normalizedFormat = format.ToLowerInvariant();
            if (normalizedFormat is not ("xlsx" or "parquet"))
            {
                throw new GridletValidationException(
                    "The result export format must be 'xlsx' or 'parquet'.");
            }
            GridletResultExporter.Validate(body, options.CurrentValue.Limits.MaxQueryResultRows);
            var content = normalizedFormat == "xlsx"
                ? GridletResultExporter.WriteExcel(body)
                : await GridletResultExporter.WriteParquetAsync(body, cancellationToken);
            var contentType = normalizedFormat == "xlsx"
                ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                : "application/vnd.apache.parquet";
            return Results.File(content, contentType);
        });
}
