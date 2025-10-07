namespace Kairc;

public static class LinkerHelper
{
    /// <summary>
    /// GoLinkを実行します。
    /// </summary>
    /// <param name="golinkPath">GoLinkのパス</param>
    /// <param name="inputFile">入力ファイル</param>
    /// <param name="outputFile">出力ファイル</param>
    public static void RunGoLink(string golinkPath, string inputFile, string outputFile)
    {
        var args = $"/console kernel32.dll \"{inputFile}\" /fo \"{outputFile}\"";
        ProcessHelper.RunTool(golinkPath, args, "GoLink", outputFile);
    }
}

