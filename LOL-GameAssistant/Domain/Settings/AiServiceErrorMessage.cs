using System.Text.Json;
using System.Text.RegularExpressions;

namespace LOL_GameAssistant.Domain.Settings;

/// <summary>
/// 把 AI 服务商返回的错误翻译成可读说明。
/// 只显示状态码（例如“服务返回 400”）时，用户无法区分模型名写错、Key 无效还是余额不足，
/// 只能靠猜；这里保留服务端给出的原因，并对可能回显的密钥做脱敏。
/// </summary>
public static class AiServiceErrorMessage
{
    private const int MaxDetailLength = 160;

    /// <summary>服务端错误里可能回显密钥；展示前先按此脱敏。</summary>
    private static readonly Regex SecretPattern = new(
        @"(sk-[A-Za-z0-9_\-]{4,}|[A-Za-z0-9_\-]{24,})", RegexOptions.Compiled);

    public static string Describe(int statusCode, string? body)
    {
        string hint = statusCode switch
        {
            400 => "请求被拒绝，通常是模型名称不存在或参数不支持",
            401 or 403 => "API Key 无效或没有访问权限",
            402 => "账户余额不足",
            404 => "接口地址不存在，请确认服务地址是 OpenAI 兼容地址",
            429 => "请求过于频繁或额度已用尽",
            >= 500 => "服务端暂时不可用，可稍后重试",
            _ => "请求失败"
        };

        string? detail = TryReadDetail(body);
        return detail == null
            ? $"服务返回 {statusCode}：{hint}。"
            : $"服务返回 {statusCode}：{hint}（{detail}）。";
    }

    /// <summary>从 OpenAI 兼容与 Claude 两种错误结构里取出 message；取不到或不是 JSON 就返回 null。</summary>
    public static string? TryReadDetail(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("error", out JsonElement error))
            {
                if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out JsonElement message))
                    return Sanitize(message.GetString());
                if (error.ValueKind == JsonValueKind.String) return Sanitize(error.GetString());
            }
            if (root.TryGetProperty("message", out JsonElement topLevel)) return Sanitize(topLevel.GetString());
        }
        catch (JsonException)
        {
            // 网关或代理返回的错误页不是 JSON，不展示原文。
        }
        return null;
    }

    public static string? Sanitize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        string masked = SecretPattern.Replace(message.Trim(), "***");
        return masked.Length <= MaxDetailLength ? masked : masked[..MaxDetailLength] + "…";
    }
}