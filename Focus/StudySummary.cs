using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace YeShunguangPet;

public sealed record StudySummaryRequest(DateOnly Date, StudyDay Today, IReadOnlyList<StudyDay> Week);

public interface IStudySummaryService
{
    bool IsAvailable { get; }
    Task<string> SummarizeAsync(StudySummaryRequest request, CancellationToken cancellationToken = default);
}
