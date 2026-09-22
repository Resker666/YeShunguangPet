using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace YeShunguangPet;

public sealed class AiCaptureTextProcessor(Func<IAiProvider?> providerFactory) : ICaptureTextProcessor
{
    public async IAsyncEnumerable<string> ProcessAsync(CaptureTextRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var provider = providerFactory() ?? throw new InvalidOperationException("请先在 AI 设置中启用“允许 AI 处理手动确认的 OCR 文字”。");
        var system = request.Action switch
        {
            CaptureTextAction.Translate => $"把用户提供的文字完整翻译为 {request.Language ?? "English"}。保留段落和事实，只返回译文。",
            CaptureTextAction.Rewrite => $"把用户提供的文字改写为 {request.RewriteStyle ?? "清晰简洁"} 风格。保留事实，只返回改写结果。",
            _ => "用简体中文总结用户提供的文字，保留关键事实和行动项，只返回总结。"
        };
        var prompt = new AiPrompt(system + "你只能处理本次明确提供的文字，不要声称读取图片、屏幕或文件。", request.Text) { JsonResponse = false };
        if (provider is IStreamingAiProvider streaming)
        {
            await foreach (var delta in streaming.StreamAsync(prompt, cancellationToken)) yield return delta;
        }
        else yield return await provider.CompleteAsync(prompt, cancellationToken);
    }
}
