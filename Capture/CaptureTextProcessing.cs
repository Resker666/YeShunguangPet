using System.Collections.Generic;
using System.Threading;

namespace YeShunguangPet;

public enum CaptureTextAction { Summary, Translate, Rewrite }

public sealed record CaptureTextRequest(CaptureTextAction Action, string Text, string? Language = null, string? RewriteStyle = null);

public interface ICaptureTextProcessor
{
    IAsyncEnumerable<string> ProcessAsync(CaptureTextRequest request, CancellationToken cancellationToken = default);
}
