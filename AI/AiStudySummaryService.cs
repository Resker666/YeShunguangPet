using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace YeShunguangPet;

public sealed class AiStudySummaryService : IStudySummaryService
{
    private readonly Func<AiOptions> _options;
    private readonly Func<IAiProvider?> _providerFactory;

    public AiStudySummaryService(Func<AiOptions> options, Func<IAiProvider?>? providerFactory = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _providerFactory = providerFactory ?? (() => AiProviderFactory.Create(_options(), new AiSecretStore()));
    }

    public bool IsAvailable
    {
        get
        {
            var options = _options();
            return options.Enabled && options.AllowStudySummaries;
        }
    }

    public async Task<string> SummarizeAsync(StudySummaryRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable) throw new InvalidOperationException("请先在 AI 设置中启用专注总结。");
        var provider = _providerFactory() ?? throw new InvalidOperationException("AI Provider 未启用。");
        var prompt = new AiPrompt(
            "你是离线专注助手。只返回 JSON：{\"text\":\"一段中文总结\",\"mood\":\"encourage\"}。不要给出医疗或职业诊断，不要输出 JSON 之外的内容。总结最多 600 个汉字。",
            $"日期：{request.Date:yyyy-MM-dd}\n今日专注：{request.Today.Minutes} 分钟，完成 {request.Today.Sessions} 次\n近七天：{request.Week.Sum(x => x.Minutes)} 分钟，完成 {request.Week.Sum(x => x.Sessions)} 次\n请给出简短、具体、鼓励性的总结和一个明天可执行的小建议。");
        var suggestion = await new AiSuggestionService(provider).SuggestAsync(prompt, cancellationToken, 600);
        return suggestion.Text;
    }
}
