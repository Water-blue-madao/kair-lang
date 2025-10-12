namespace Kairc;

/// <summary>
/// LLD (lld-link) リンカヘルパー
/// </summary>
public static class LlvmLinkerHelper
{
    /// <summary>
    /// lld-linkを実行してオブジェクトファイルを実行ファイルにリンク
    /// </summary>
    public static void RunLldLink(string lldLinkPath, string inputFile, string outputFile, string kernel32LibPath)
    {
        // /subsystem:console: コンソールアプリケーション
        // /entry:Start: エントリポイント指定
        // /out: 出力ファイル
        var args = $"\"{inputFile}\" \"{kernel32LibPath}\" /subsystem:console /entry:Start /out:\"{outputFile}\"";
        ProcessHelper.RunTool(lldLinkPath, args, "lld-link", outputFile);
    }
}
