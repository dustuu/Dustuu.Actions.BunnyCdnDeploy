using BunnyCDN.Net.Storage;
using BunnyCDN.Net.Storage.Models;
using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dustuu.Actions.BunnyCdnDeploy;

internal class Program
{
    private static async Task Main(string[] args)
    {
        using IHost host = Host.CreateDefaultBuilder(args).Build();

        ParserResult<ActionInputs> parser = Parser.Default.ParseArguments<ActionInputs>(() => new(), args);
        parser.WithNotParsed
        (
            errors =>
            {
                host.Services.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("DotNet.GitHubAction.Program")
                    .LogError
                    (string.Join(Environment.NewLine, errors.Select(error => error.ToString())));

                Environment.Exit(2);
            }
        );

        await parser.WithParsedAsync(DeployToBunnyCdn);
        await host.RunAsync();
    }

    private static async Task DeployToBunnyCdn(ActionInputs inputs)
    {
        // Configure Cancellation
        using CancellationTokenSource tokenSource = new();
        Console.CancelKeyPress += delegate { tokenSource.Cancel(); };

        // Verify inputs
        DirectoryInfo workspace = new(inputs.Workspace);
        Console.WriteLine($"Workspace Dir: {workspace.FullName}");

        DirectoryInfo directory = workspace.CreateSubdirectory(inputs.Directory);
        Console.WriteLine($"Directory Dir: {directory.FullName}");

        BunnyCDNStorage bunnyCDNStorage = new(inputs.StorageZoneUsername, inputs.StorageZonePassword, inputs.StorageZoneRegion);

        FilePathWrapper[] localFiles =
            directory
            .GetFiles("*", SearchOption.AllDirectories)
            .Select(fi => new FilePathWrapper(fi, directory, bunnyCDNStorage))
            .ToArray();
        Console.WriteLine($"Local Files Found: {localFiles.Length}");

        async Task<IEnumerable<FilePathWrapper>> RecurseStorageObjects(IEnumerable<StorageObject> storageObjects)
        {
            List<FilePathWrapper> filePathWrappers = new();

            foreach (StorageObject storageObject in storageObjects)
            {
                // TODO: Delete unused empty directories
                if (storageObject.IsDirectory)
                {
                    List<StorageObject> children = await bunnyCDNStorage.GetStorageObjectsAsync(storageObject.FullPath);
                    filePathWrappers.AddRange(await RecurseStorageObjects(children));
                }
                else
                {
                    filePathWrappers.Add(new FilePathWrapper(storageObject, directory, bunnyCDNStorage));
                }
            }

            return filePathWrappers;
        }

        Console.WriteLine("Getting Storage Objects...");
        List<StorageObject> topObjects =
            await bunnyCDNStorage.GetStorageObjectsAsync($"/{bunnyCDNStorage.StorageZoneName}/");

        FilePathWrapper[] cdnFiles = (await RecurseStorageObjects(topObjects)).ToArray();
        Console.WriteLine($"CDN Files Found: {cdnFiles.Length}");

        // Split local files by whether or not they are already on the CDN
        ILookup<bool, FilePathWrapper> localFilesByCdnMatch =
            localFiles.ToLookup(l => cdnFiles.Any(c => l.FilePath == c.FilePath));
        IEnumerable<FilePathWrapper> toUpload = localFilesByCdnMatch[false];
        IEnumerable<FilePathWrapper> toCheckUpdate = localFilesByCdnMatch[true];

        Console.WriteLine($"files to upload: {toUpload.Count()}");
        foreach (FilePathWrapper upload in toUpload)
        {
            Console.WriteLine($"uploading: {upload.LocalPath}");
            await bunnyCDNStorage.UploadAsync(upload.LocalPath, upload.CdnPath);
        }

        Console.WriteLine($"files to check: {toCheckUpdate.Count()}");
        foreach (FilePathWrapper check in toCheckUpdate)
        {
            // TODO: Check for changes before upload
            Console.WriteLine($"checking: {check.LocalPath}");
            await bunnyCDNStorage.UploadAsync(check.LocalPath, check.CdnPath);
        }

        // Delete files on the CDN which do not have any local equivalent
        IEnumerable<FilePathWrapper> toDelete =
            cdnFiles.Where(c => localFiles.All(l => c.FilePath != l.FilePath));

        Console.WriteLine($"files to delete: {toDelete.Count()}");
        foreach (FilePathWrapper delete in toDelete)
        {
            Console.WriteLine($"deleting: {delete.LocalPath}");
            await bunnyCDNStorage.DeleteObjectAsync(delete.CdnPath);
        }

        Console.WriteLine("Done!");

        Environment.Exit(0);
    }
}