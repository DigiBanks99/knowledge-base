using System.ComponentModel;

using Microsoft.AspNetCore.Mvc;

namespace KnowledgeBase.Agent.Wiki;

public enum WikiNodeType
{
    Directory,
    Page
}

public sealed record WikiNode(string Name, string Path, WikiNodeType Type, DateTimeOffset UpdatedAt, IReadOnlyList<WikiNode> Children);

public sealed record WikiPage(string Path, string Content, DateTimeOffset UpdatedAt);

internal static class WikiEndpoints
{
    extension(IEndpointRouteBuilder endpoints)
    {
        public IEndpointRouteBuilder MapWikiEndpoints()
        {
            RouteGroupBuilder wiki = endpoints
                .MapGroup("wiki")
                .WithTags("wiki");

            wiki.MapGet("tree", GetTree)
                .WithName("GetWikiTree")
                .WithSummary("Get the wiki tree")
                .WithDescription("Returns every folder and page in the wiki, folders first, each sorted by name.")
                .Produces<WikiNode>();

            wiki.MapGet("page", GetPage)
                .WithName("GetWikiPage")
                .WithSummary("Get a wiki page")
                .Produces<WikiPage>()
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status404NotFound);

            return endpoints;
        }
    }

    private static WikiNode GetTree(WikiPaths wikiPaths) => BuildNode(wikiPaths, new DirectoryInfo(wikiPaths.Root));

    private static async Task<Microsoft.AspNetCore.Http.IResult> GetPage(
        [FromQuery, Description("The page path relative to the wiki root, e.g. people/index.md.")] string path,
        WikiPaths wikiPaths,
        CancellationToken cancellationToken)
    {
        if (!wikiPaths.TryResolve(path, out string fullPath) || !fullPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Problem("The path must be a markdown page inside the wiki.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!File.Exists(fullPath))
        {
            return Results.NotFound();
        }

        string content = await File.ReadAllTextAsync(fullPath, cancellationToken);
        return Results.Ok(new WikiPage(wikiPaths.ToRelative(fullPath), content, File.GetLastWriteTimeUtc(fullPath)));
    }

    private static WikiNode BuildNode(WikiPaths wikiPaths, DirectoryInfo directory)
    {
        IEnumerable<WikiNode> directories = directory
            .EnumerateDirectories()
            .OrderBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
            .Select(child => BuildNode(wikiPaths, child));
        IEnumerable<WikiNode> pages = directory
            .EnumerateFiles("*.md")
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Select(file => new WikiNode(file.Name, wikiPaths.ToRelative(file.FullName), WikiNodeType.Page, file.LastWriteTimeUtc, []));

        string path = directory.FullName.Equals(wikiPaths.Root, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : wikiPaths.ToRelative(directory.FullName);
        return new WikiNode(directory.Name, path, WikiNodeType.Directory, directory.LastWriteTimeUtc, [.. directories, .. pages]);
    }
}
