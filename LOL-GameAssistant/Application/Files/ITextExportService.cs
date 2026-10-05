namespace LOL_GameAssistant.Application.Files;

/// <summary>将用户明确选择的导出内容写入文本文件的端口。</summary>
public interface ITextExportService
{
    /// <summary>使用带 BOM 的 UTF-8 编码保存文本，便于表格软件识别中文。</summary>
    Task SaveUtf8WithBomAsync(string path, string content, CancellationToken cancellationToken = default);
}