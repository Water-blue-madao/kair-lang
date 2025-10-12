namespace Kairc;

/// <summary>
/// LLVM MC (llvm-mc) アセンブラヘルパー
/// </summary>
public static class LlvmAssemblerHelper
{
    /// <summary>
    /// llvm-mcを実行してアセンブリをオブジェクトファイルに変換
    /// </summary>
    public static void RunLlvmMc(string llvmMcPath, string inputFile, string outputFile)
    {
        // --triple: ターゲットアーキテクチャ指定
        // --output-asm-variant=1: Intel構文を使用（0=AT&T, 1=Intel）
        // --filetype=obj: オブジェクトファイル出力
        var args = $"--triple=x86_64-pc-windows-msvc --output-asm-variant=1 --filetype=obj \"{inputFile}\" -o \"{outputFile}\"";
        ProcessHelper.RunTool(llvmMcPath, args, "llvm-mc", outputFile);
    }
}
