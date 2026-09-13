using System;
using System.Threading;
using System.Threading.Tasks;
using Aveline.Api.Modules.VisualIntelligence.DTOs;

namespace Aveline.Api.Modules.VisualIntelligence.Services;

public interface IVisionService
{
    Task<ImageAnalysisResultDto> AnalyzeAsync(
        string imageUrl,
        Guid organizationId,
        CancellationToken cancellationToken = default);
}
