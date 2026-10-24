using System.ComponentModel;
using System.Text.RegularExpressions;

using KnowledgeBase.Agent.Files.Errors;

namespace KnowledgeBase.Agent.Files;

public record PatchFileRequest(
    [Description("The path of the file to be patched.")]
    string FilePath,
    [Description("The original content of the file. Whitespace sensitive.")]
    string OriginalContent,
    [Description("The new content to replace the original content. Whitespace sensitive.")]
    string PatchedContent);

internal sealed class PatchFileHandler()
{
    public async Task<Result<string, Error>> Handle(PatchFileRequest request, CancellationToken cancellationToken)
    {
        if (!File.Exists(request.FilePath))
        {
            return new FileNotFoundError(request.FilePath);
        }

        try
        {
            string currentContent = await File.ReadAllTextAsync(request.FilePath, cancellationToken);
            string newContent = Regex.Replace(currentContent, Regex.Escape(request.OriginalContent), request.PatchedContent);
            await File.WriteAllTextAsync(request.FilePath, newContent, cancellationToken);
            return request.FilePath;
        }
        catch (Exception ex)
        {
            return new FilePatchError(request.FilePath, ex.Message);
        }
    }
}