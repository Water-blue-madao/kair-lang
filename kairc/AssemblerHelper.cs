namespace Kairc;

public static class AssemblerHelper
{
    // 重要: 古い .obj ファイルによる問題を避けるため、必ず一時ディレクトリを使用して .obj を生成する
    // 古い .obj を再利用すると、原因不明の実行時エラーが発生することがある
    public static void RunNasm(string nasmPath, string inputFile, string outputFile)
    {
        var args = $"-f win64 \"{inputFile}\" -o \"{outputFile}\"";
        ProcessHelper.RunTool(nasmPath, args, "NASM", outputFile);
    }
}

