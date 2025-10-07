namespace Kairc;

public static class AssemblerHelper
{
    // IMPORTANT: Always use temp directory for .obj files to avoid stale object file issues
    // Reusing old .obj files can cause mysterious runtime failures
    public static void RunNasm(string nasmPath, string inputFile, string outputFile)
    {
        var args = $"-f win64 \"{inputFile}\" -o \"{outputFile}\"";
        ProcessHelper.RunTool(nasmPath, args, "NASM", outputFile);
    }
}

