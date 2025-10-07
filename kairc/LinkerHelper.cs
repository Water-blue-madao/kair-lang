namespace Kairc;

public static class LinkerHelper
{
    public static void RunGoLink(string golinkPath, string inputFile, string outputFile)
    {
        var args = $"/console kernel32.dll \"{inputFile}\" /fo \"{outputFile}\"";
        ProcessHelper.RunTool(golinkPath, args, "GoLink", outputFile);
    }
}

