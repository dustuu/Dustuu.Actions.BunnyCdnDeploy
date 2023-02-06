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
    private const string LOG_CENSOR = "***";
    private const string ENV_BRANCH_NAME = "GITHUB_REF_NAME";

    private static void Log(string message, params string[] toCensor)
    {
        foreach (string target in toCensor) { message = message.Replace(target, LOG_CENSOR); }
        Console.WriteLine(message);
    }

    private static void LogToString
    (object obj, [CallerArgumentExpression(nameof(obj))] string objName = null!, params string[] toCensor) =>
        Log($"{objName}: {obj}", toCensor);

    private static void LogToJson
    (object obj, [CallerArgumentExpression(nameof(obj))] string objName = null!, params string[] toCensor) =>
        Log($"{objName}: {JsonSerializer.Serialize(obj)}", toCensor);

    private static async Task Main(string[] args)
    {
        // Configure Cancellation
        using CancellationTokenSource tokenSource = new();
        Console.CancelKeyPress += delegate { tokenSource.Cancel(); };

        // Read Environment Variables
        string? branchName = Environment.GetEnvironmentVariable(ENV_BRANCH_NAME);
        if (string.IsNullOrEmpty(branchName)) { throw new Exception("Couldn't find branch name!"); }
        LogToString(branchName);
        Log("Cleaning Branch Name...");
        branchName = branchName.Split('/')[^1].ToLowerInvariant();
        LogToString(branchName);

        // Configure Inputs
        ParserResult<ActionInputs> parser = Parser.Default.ParseArguments<ActionInputs>(args);
        if (parser.Errors.ToArray() is { Length: > 0 } errors)
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
        if (branchName != inputs.BranchMainName)
        { subdomainPieces.Add(SafeName().Replace(branchName, string.Empty)); }
        // If a Dns Subdomain is provided, add it to the subdomain
        subdomainPieces.AddRange(inputs.DnsSubdomain.Split('.'));
        // Finish building the subdomain
        string subdomain =
            string.Join('.', subdomainPieces).ToLowerInvariant();
        LogToString(subdomain);

        // Build the HttpClient
        HttpClient http = new() { BaseAddress = new(BUNNY_CDN_API) };
        http.DefaultRequestHeaders.Add("AccessKey", inputs.ApiKey);

        // Get DNS Zone
        Log("Getting DNS Zone...");
        DnsZone? dnsZone = await http.Get<DnsZone>($"dnszone/{inputs.DnsZoneId}");
        Log("Got DNS Zone!");
        LogToJson(dnsZone);

        // Start building the deployment name
        List<string> deploymentNamePieces = new();
        // Add the subdomain pieces
        deploymentNamePieces.AddRange(subdomainPieces);
        // Add the Dns Zone domain to the deployment name
        deploymentNamePieces.AddRange(dnsZone.Domain.Split('.'));
        // Finish building the deployment name
        string deploymentName =
            string.Join('.', deploymentNamePieces).ToLowerInvariant();
        LogToString(deploymentName);

        // Build a resource name from the deployment name
        string resourceName =
            string.Join('-', deploymentNamePieces.AsEnumerable().Reverse()).ToLowerInvariant();
        LogToString(resourceName);

        // Check for a pre-existing Storage Zone with the same name
        Log($"Checking for pre-existing Storage Zone with name '{resourceName}'...");
        StorageZone? storageZone = (await http.Get<StorageZone[]>("storagezone"))
            .SingleOrDefaultEqualsIgnoreCase(s => s.Name, resourceName);

        // Create Storage Zone if not found
        if (storageZone is null)
        {
            Log("Creating Storage Zone...");
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
            Log("Storage Zone Created!");
        }
        else { Log("Storage Zone found! Skipping creation."); }
        if (storageZone is null) { throw new Exception("Failed to create storage zone"); }
        LogToJson(storageZone, toCensor: storageZone.Password);

        // Ensure Storage Zone has Rewrite404To200 enabled
        Log("Checking if Rewrite404To200 is already enabled on Storage Zone...");
        if (!storageZone.Rewrite404To200)
        {
            Log("Enabling Rewrite404To200 on Storage Zone...");
            await http.PostResponseMessage
            (
                $"storagezone/{storageZone.Id}",
                storageZone with { Rewrite404To200 = true }
            );
            Log("Enabled Rewrite404To200 on Storage Zone!");
        }
        else { Log("Rewrite404To200 was already enabled on Storage Zone!"); }

        // Check for a pre-existing Pull Zone with the same name
        Log($"Checking for pre-existing Pull Zone with name '{resourceName}'...");
        PullZone? pullZone = (await http.Get<PullZone[]>($"pullzone"))
            .SingleOrDefaultEqualsIgnoreCase(p => p.Name, resourceName);

        // Create Pull Zone if not found
        if (pullZone is null )
        {
            Log("Creating Pull Zone...");
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
            Log("Refreshing Storage Zone with created Pull Zone...");
            storageZone = await http.Get<StorageZone>($"storagezone/{storageZone.Id}");
            Log("Refreshed Storage Zone!");
            LogToJson(storageZone, toCensor: storageZone.Password);

            // Get Finalized Pull Zone
            Log("Finalized pull zone from refreshed Storage Zone:");
            pullZone = storageZone.PullZones.Single();
            LogToJson(pullZone);
        }
        else { Log("Pull Zone found! Skipping creation."); }

        // Get System Host Name
        Log("Getting System Host Name...");
        PullZone.HostName systemHostName = pullZone.Hostnames.Single(h => h.IsSystemHostname);
        LogToJson(systemHostName);

        // Build the new record
        Log("Building DNS Record...");
        DnsZone.Record record = new()
        {
            Type = DnsZone.Record.TYPE_CNAME,
            Name = subdomain, //If subdomain is empty, this will be a root record
            Value = systemHostName.Value,
            Ttl = 300 // 300 seconds = 5 Minutes
        };
        LogToJson(record);

        // Check for a previously existing old record
        DnsZone.Record? recordOld = dnsZone.Records
            .Where(r => r.Type == DnsZone.Record.TYPE_CNAME)
            .SingleOrDefaultEqualsIgnoreCase(r => r.Name, subdomain);

        // Create the record if it doesn't exist
        if (recordOld is null)
        {
            Log("Adding DNS Record...");
            await http.PutResponseMessage( $"dnszone/{inputs.DnsZoneId}/records", record );
            Log("Added DNS Record!");
        }
        // Otherwise, update the record if needed
        else
        {
            if (recordOld.Value != record.Value)
            {
                Log("Updating DNS Record...");
                await http.PostResponseMessage($"dnszone/{inputs.DnsZoneId}/records/{recordOld.Id}", record);
                Log("Updated DNS Record!");
            }
            else { Log("DNS Records was already up to date!"); }
        }

        // Add custom hostname to Pull Zone
        Log($"Checking for existing custom host name: '{deploymentName}'...");
        PullZone.HostName? hostName =
            pullZone.Hostnames.SingleOrDefaultEqualsIgnoreCase(h => h.Value, deploymentName);
        bool needsSsl = hostName is null || !hostName.ForceSSL;

        // Create the custom HostName if needed
        if (hostName is null)
        {
            Log("Creating custom host name...");
            await http.PostResponseMessage
            (
                $"pullzone/{pullZone.Id}/addHostname",
                new AddHostnameRequestBody() { Hostname = deploymentName }
            );
            Log("Custom host name created!");
        }
        else
        {
            Log("Custom host name already existed! Skipping creation.");
            LogToJson(hostName);
        }

        if (needsSsl)
        {
            // Load free SSL certificate
            Log("Loading free SSL certificate...");
            await http.GetResponseMessage($"pullzone/loadFreeCertificate?hostname={deploymentName}");
            Log("Loaded free SSL certificate!");

            // Force SSL
            Log("Setting force SSL to 'true'...");
            await http.PostResponseMessage
            (
                $"pullzone/{pullZone.Id}/setForceSSL",
                new SetForceSslRequestBody() { Hostname = deploymentName, ForceSSL = true }
            );
            Log("Set Force SSL to 'true'!");
        }
        else { Log("SSL already configured! skipping configuration."); }

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

        // Find files to upload (local files not on CDN)
        IEnumerable<FilePathWrapper> toUpload = localFilesByCdnMatch[false];
        // Limit the amount of files to upload if a DebugLimit is provided
        if (inputs.DebugLimit > 0) { toUpload = toUpload.Take(inputs.DebugLimit); }
        // Upload files to CDN
        Log($"Files to upload: {toUpload.Count()}");
        foreach (FilePathWrapper upload in toUpload)
        {
            Log($"Uploading: {upload.LocalPath}...");
            await bunnyCDNStorage.UploadAsync(upload.LocalPath, upload.CdnPath);
        }

        // Find files to update (local files already on CDN)
        IEnumerable<FilePathWrapper> toUpdate = localFilesByCdnMatch[true];
        // Limit the amount of files to update if a DebugLimit is provided
        if (inputs.DebugLimit > 0) { toUpdate = toUpdate.Take(inputs.DebugLimit); }
        // Update files on CDN
        Log($"Files to update: {toUpdate.Count()}");
        foreach (FilePathWrapper check in toUpdate)
        {
            // TODO: Check for changes before upload
            Log($"Updating: {check.LocalPath}...");
            await bunnyCDNStorage.UploadAsync(check.LocalPath, check.CdnPath);
        }

        // Find files to delete (files on the CDN which do not have any local equivalent)
        IEnumerable<FilePathWrapper> toDelete = cdnFiles.Where(c => localFiles.All(l => c.FilePath != l.FilePath));
        // Limit the amount of files to delete if a DebugLimit is provided
        if (inputs.DebugLimit > 0) { toDelete = toDelete.Take(inputs.DebugLimit); }
        // Delete files on CDN
        Log($"Files to delete: {toDelete.Count()}");
        foreach (FilePathWrapper delete in toDelete)
        {
            Log($"Deleting: {delete.LocalPath}...");
            await bunnyCDNStorage.DeleteObjectAsync(delete.CdnPath);
        }

        // Purge the Pull Zone Cache
        Log("Purging Pull Zone Cache...");
        await http.PostResponseMessage<object>($"pullzone/{pullZone.Id}/purgeCache", null!);
        Log("Purged Pull Zone Cache!");

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

    [GeneratedRegex("[^a-z0-9-]")]
    private static partial Regex SafeName();
}