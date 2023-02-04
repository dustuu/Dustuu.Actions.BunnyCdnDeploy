using BunnyCDN.Net.Storage;
using BunnyCDN.Net.Storage.Models;
using CommandLine;
using Dustuu.Actions.BunnyCdnDeploy.Extensions;
using Dustuu.Actions.BunnyCdnDeploy.Models;
using Dustuu.Actions.BunnyCdnDeploy.Models.RequestBodies;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dustuu.Actions.BunnyCdnDeploy;

internal partial class Program
{
    private const string BUNNY_CDN_API = "https://api.bunny.net/";

    private static void Log(string message) => Console.WriteLine(message);

    private static void LogToString
    (object obj, [CallerArgumentExpression(nameof(obj))] string objName = null!) =>
        Log($"{objName}: {obj}");

    private static void LogToJson
    (object obj, [CallerArgumentExpression(nameof(obj))] string objName = null!) =>
        Log($"{objName}: {JsonSerializer.Serialize(obj)}");

    private static async Task Main(string[] args)
    {
        // TODO: FORCE ALL INPUTS TO LOWER CASE!!!
        // TODO: FORCE ALL INPUTS TO LOWER CASE!!!
        // TODO: FORCE ALL INPUTS TO LOWER CASE!!!
        // TODO: FORCE ALL INPUTS TO LOWER CASE!!!
        // TODO: FORCE ALL INPUTS TO LOWER CASE!!!

        // Configure Cancellation
        using CancellationTokenSource tokenSource = new();
        Console.CancelKeyPress += delegate { tokenSource.Cancel(); };

        // Configure Inputs
        ParserResult<ActionInputs> parser = Parser.Default.ParseArguments<ActionInputs>(args);
        if ( parser.Errors.ToArray() is { Length: > 0 } errors )
        {
            foreach (Error error in errors) { LogToString(error); }
            Environment.Exit(2);
            return;
        }
        ActionInputs inputs = parser.Value;

        // Start building the subdomain
        List<string> subdomainPieces = new();
        // If the current branch is not the main branch, add the branch name to the subdomain
        // NOTE: If two branches are sanitized by Regex to be equal, there will be a conflict
        // EXAMPLE: 'my_branch!' and 'my..branch???' will both resolve to 'mybranch' and conflict
        if (inputs.BranchCurrentName != inputs.BranchMainName)
        { subdomainPieces.Add(SafeName().Replace(inputs.BranchCurrentName, string.Empty)); }
        // If a Dns Subdomain is provided, add it to the subdomain
        subdomainPieces.AddRange(inputs.DnsSubdomain.Split('.'));
        // Finish building the subdomain
        string subdomain = string.Join('.', subdomainPieces);

        // Build the HttpClient
        HttpClient http = new() { BaseAddress = new(BUNNY_CDN_API) };
        http.DefaultRequestHeaders.Add("AccessKey", inputs.ApiKey);

        // Get DNS Zone
        DnsZone? dnsZone = await http.Get<DnsZone>($"dnszone/{inputs.DnsZoneId}");
        LogToJson(dnsZone);

        // Start building the deployment name
        List<string> deploymentNamePieces = new();
        // Add the subdomain pieces
        deploymentNamePieces.AddRange(subdomainPieces);
        // Add the Dns Zone domain to the deployment name
        deploymentNamePieces.AddRange(dnsZone.Domain.Split('.'));
        // Finish building the deployment name
        string deploymentName = string.Join('.', deploymentNamePieces);
        LogToString(deploymentName);

        // Build a resource name from the deployment name
        string resourceName = string.Join('-', deploymentNamePieces.AsEnumerable().Reverse());
        LogToString(resourceName);

        StorageZone[] test = await http.Get<StorageZone[]>("storagezone");
        LogToJson(test);

        // Check for Storage Zone
        StorageZone? storageZone =
            (await http.Get<StorageZone[]>("storagezone"))
            .SingleOrDefault(p => p.Name == resourceName);

        // Create Storage Zone if not found
        if (storageZone is null)
        {
            Log("No Existing StorageZone found!");

            storageZone = await http.Post<AddStorageZoneRequestBody, StorageZone>
            (
                "storagezone",
                new AddStorageZoneRequestBody()
                {
                    Name = resourceName,
                    Region = StorageZone.REGION_GERMANY,
                    ZoneTier = StorageZone.ZONE_TIER_SSD
                }
            );
            LogToJson(storageZone);
        }
        if (storageZone is null) { throw new Exception("Failed to create storage zone"); }

        // Check for Pull Zone
        PullZone? pullZone =
            (await http.Get<PullZone[]>($"pullzone"))
            .SingleOrDefault(p => p.Name == resourceName);

        // Create Pull Zone if not found
        if (pullZone is null )
        {
            // Create Pull Zone
            await http.PostResponseMessage
            (
                "pullzone",
                new AddPullZoneRequestBody()
                {
                    Name = resourceName,
                    EnableGeoZoneUS = true,
                    EnableGeoZoneEU = true,
                    EnableGeoZoneASIA = true,
                    EnableGeoZoneSA = true,
                    EnableGeoZoneAF = true,
                    OriginType = PullZone.ORIGIN_TYPE_STORAGE_ZONE,
                    StorageZoneId = storageZone.Id,
                    Type = PullZone.TYPE_SMALL_FILES
                }
            );
            Log("Pull Zone created!");

            // Refresh Storage Zone to reflect pull zone
            storageZone = await http.Get<StorageZone>($"storagezone/{storageZone.Id}");
            LogToJson(storageZone);

            // Get Finalized Pull Zone
            pullZone = storageZone.PullZones.Single();
            LogToJson(pullZone);
        }

        // Create Storage Connection
        BunnyCDNStorage bunnyCDNStorage =
            new(storageZone.Name, storageZone.Password, storageZone.Region);

        // Find Local Files
        DirectoryInfo workspace = new(inputs.Workspace);
        DirectoryInfo directory = workspace.CreateSubdirectory(inputs.Directory);
        FilePathWrapper[] localFiles =
            directory
            .GetFiles("*", SearchOption.AllDirectories)
            .Select(fi => new FilePathWrapper(fi, directory, bunnyCDNStorage))
            .ToArray();
        Log($"Local Files Found: {localFiles.Length}");

        Log("Getting Storage Objects...");
        List<StorageObject> topObjects =
            await bunnyCDNStorage.GetStorageObjectsAsync($"/{bunnyCDNStorage.StorageZoneName}/");

        FilePathWrapper[] cdnFiles =
            (await RecurseStorageObjects(topObjects, bunnyCDNStorage, directory)).ToArray();
        Log($"CDN Files Found: {cdnFiles.Length}");

        // Split local files by whether or not they are already on the CDN
        ILookup<bool, FilePathWrapper> localFilesByCdnMatch =
            localFiles.ToLookup(l => cdnFiles.Any(c => l.FilePath == c.FilePath));
        IEnumerable<FilePathWrapper> toUpload = localFilesByCdnMatch[false]
            // TODO: Remove Take when not debugging
            .Take(3);
        IEnumerable<FilePathWrapper> toUpdate = localFilesByCdnMatch[true]
            // TODO: Remove Take when not debugging
            .Take(3);

        Log($"files to upload: {toUpload.Count()}");
        foreach (FilePathWrapper upload in toUpload)
        {
            Log($"uploading: {upload.LocalPath}");
            await bunnyCDNStorage.UploadAsync(upload.LocalPath, upload.CdnPath);
        }

        Log($"files to update: {toUpdate.Count()}");
        foreach (FilePathWrapper check in toUpdate)
        {
            // TODO: Check for changes before upload
            Log($"updating: {check.LocalPath}");
            await bunnyCDNStorage.UploadAsync(check.LocalPath, check.CdnPath);
        }

        // Delete files on the CDN which do not have any local equivalent
        IEnumerable<FilePathWrapper> toDelete =
            cdnFiles.Where(c => localFiles.All(l => c.FilePath != l.FilePath))
            // TODO: Remove Take when not debugging
            .Take(3);

        Log($"files to delete: {toDelete.Count()}");
        foreach (FilePathWrapper delete in toDelete)
        {
            Log($"deleting: {delete.LocalPath}");
            await bunnyCDNStorage.DeleteObjectAsync(delete.CdnPath);
        }

        // Get System Host Name
        PullZone.HostName systemHostName = pullZone.Hostnames.Single(h => h.IsSystemHostname);
        LogToJson(systemHostName);

        // Build the new record
        DnsZone.Record record = new()
        {
            Type = DnsZone.Record.TYPE_CNAME,
            Name = subdomain, //If subdomain is empty, this will be a root record
            Value = systemHostName.Value,
            Ttl = 300 // 300 seconds = 5 Minutes
        };

        // Check for a previously existing old record
        DnsZone.Record? recordOld = dnsZone.Records
            .SingleOrDefault(r => r.Type == DnsZone.Record.TYPE_CNAME && r.Name == subdomain);

        // Create the record if it doesn't exist
        if (recordOld is null)
        {
            await http.PutResponseMessage( $"dnszone/{inputs.DnsZoneId}/records", record );
            Log("Added dns record!");
        }
        // Otherwise, update the record
        else
        {
            await http.PostResponseMessage( $"dnszone/{inputs.DnsZoneId}/records/{recordOld.Id}", record );
            Log("Updated dns record!");
        }

        // Add custom hostname to Pull Zone
        Log($"Checking for existing custom host name: '{deploymentName}'");
        if ( pullZone.Hostnames.Any(h => h.Value == deploymentName) )
        {
            Log("Custom host name already existed! Skipping creation.");
        }
        else
        {
            await http.PostResponseMessage
            (
                $"pullzone/{pullZone.Id}/addHostname",
                new AddHostnameRequestBody() { Hostname = deploymentName }
            );
            Log("Custom host name created!");
        }

        // TODO: Check if already done?
        // Load free SSL certificate (allowed to re-run even if already loaded)
        await http.GetResponseMessage($"pullzone/loadFreeCertificate?hostname={deploymentName}");
        Log("Loaded free SSL certificate!");

        // TODO: Check if already done?
        // Force SSL (allowed to re-run even if already set)
        await http.PostResponseMessage
        (
            $"pullzone/{pullZone.Id}/setForceSSL",
            new SetForceSslRequestBody() { Hostname = deploymentName, ForceSSL = true }
        );
        Log("Set force SSL!");

        Log("Done!");

        Environment.Exit(0);
    }

    // Recursive local function to iterate over all CDN storage objects
    private static async Task<IEnumerable<FilePathWrapper>> RecurseStorageObjects
    (IEnumerable<StorageObject> storageObjects, BunnyCDNStorage bunnyCDNStorage, DirectoryInfo directory)
    {
        List<FilePathWrapper> filePathWrappers = new();

        foreach (StorageObject storageObject in storageObjects)
        {
            // TODO: Delete unused empty directories
            if (storageObject.IsDirectory)
            {
                List<StorageObject> children = await bunnyCDNStorage.GetStorageObjectsAsync(storageObject.FullPath);
                filePathWrappers.AddRange(await RecurseStorageObjects(children, bunnyCDNStorage, directory));
            }
            else
            {
                filePathWrappers.Add(new FilePathWrapper(storageObject, directory, bunnyCDNStorage));
            }
        }

        return filePathWrappers;
    }

    [GeneratedRegex("[^A-Za-z0-9-]")]
    private static partial Regex SafeName();
}