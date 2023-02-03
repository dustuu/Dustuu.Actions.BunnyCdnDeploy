using BunnyCDN.Net.Storage;
using BunnyCDN.Net.Storage.Models;
using CommandLine;
using Dustuu.Actions.BunnyCdnDeploy.Models;
using Dustuu.Actions.BunnyCdnDeploy.Models.RequestBodies;
using Dustuu.Actions.BunnyCdnDeploy.Models.ResponseBodies;
using System.Net.Http.Json;
using static System.Text.Json.JsonSerializer;

namespace Dustuu.Actions.BunnyCdnDeploy;

internal class Program
{
    private const string BUNNY_CDN_API = "https://api.bunny.net/";

    private static void Log(string message) => Console.WriteLine(message);

    private static async Task Main(string[] args)
    {
        // Configure Cancellation
        using CancellationTokenSource tokenSource = new();
        Console.CancelKeyPress += delegate { tokenSource.Cancel(); };

        // Configure Inputs
        ParserResult<ActionInputs> parser = Parser.Default.ParseArguments<ActionInputs>(args);
        if ( parser.Errors.ToArray() is { Length: > 0 } errors )
        {
            foreach (Error error in errors) { Log($"Input Error: '{error}'"); }
            Environment.Exit(2);
            return;
        }
        ActionInputs inputs = parser.Value;

        // TODO: Use IHttpClientFactory
        // TODO: Throw exceptions when any call is not sucess
        // Build the HttpClient
        HttpClient http = new();
        http.DefaultRequestHeaders.Add("AccessKey", inputs.BunnyCdnApiKey);
        http.BaseAddress = new(BUNNY_CDN_API);

        // Get DNS Zone
        HttpResponseMessage getDnsZoneResponseMessage =
            await http.GetAsync($"dnszone/{inputs.DnsZoneId}");
        DnsZone? dnsZone = await getDnsZoneResponseMessage.Content.ReadFromJsonAsync<DnsZone>();
        if (dnsZone is null) { return; }
        Log(Serialize(dnsZone));

        // Build the custom host name
        // TODO: Allow configuration here, no branch or no root subdomain
        string subdomain = $"{inputs.Branch}.branch.{inputs.DnsRootSubdomain}";
        string customHostName = $"{subdomain}.{dnsZone.Domain}";
        string fileTimeUtc = DateTime.Now.ToFileTimeUtc().ToString();
        string generatedName = string.Join('-', customHostName.Split('.').Reverse());
        Log($"generatedName: {generatedName}, {generatedName.Length}");

        // Check for Storage Zone
        // HttpResponseMessage listStorageZonesResponseMessage = await http.GetAsync($"storagezone");

        // Log(await listStorageZonesResponseMessage.Content.ReadAsStringAsync());
        // return;
        StorageZone? storageZone =
            (await http.GetFromJsonAsync<StorageZone[]>($"storagezone"))?
            .SingleOrDefault(p => p.Name == generatedName);

        // Create Storage Zone if not found
        if (storageZone is null)
        {
            HttpResponseMessage addStorageZoneResponseMessage =
                await http.PostAsJsonAsync
                (
                    "storagezone",
                    new AddStorageZoneRequestBody()
                    {
                        Name = generatedName,
                        Region = StorageZone.REGION_GERMANY,
                        ZoneTier = StorageZone.ZONE_TIER_SSD
                    }
                );
            if (!addStorageZoneResponseMessage.IsSuccessStatusCode)
            {
                Log($"Problem!: {await addStorageZoneResponseMessage.Content.ReadAsStringAsync()}");
            }

            storageZone = await addStorageZoneResponseMessage.Content.ReadFromJsonAsync<StorageZone>();
            Log(Serialize(storageZone));
        }
        if (storageZone is null) { throw new Exception("Failed to create storage zone"); }

        // Check for Pull Zone
        PullZone? pullZone =
            (await http.GetFromJsonAsync<PullZone[]>($"pullzone"))?
            .SingleOrDefault(p => p.Name == generatedName);
        /*ListPullZonesResponseBody? listPullZonesResponseBody =
            await http.GetFromJsonAsync<ListPullZonesResponseBody>($"pullzone");
        PullZone? pullZone = listPullZonesResponseBody?.Items
            .SingleOrDefault(p => p.Name == generatedName);*/

        // Create Pull Zone if not found
        if (pullZone is null )
        {
            // Create Pull Zone
            HttpResponseMessage addPullZoneResponseMessage =
                await http.PostAsJsonAsync
                (
                    "pullzone",
                    new AddPullZoneRequestBody()
                    {
                        Name = generatedName,
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
            HttpResponseMessage getStorageZoneResponseMessage =
                await http.GetAsync($"storagezone/{storageZone.Id}");
            storageZone = await getStorageZoneResponseMessage.Content.ReadFromJsonAsync<StorageZone>();
            if (storageZone is null) { throw new Exception("Failed to refresh storage zone"); }

            Log(Serialize(storageZone));

            // Get Finalized Pull Zone
            pullZone = storageZone.PullZones.Single();
            Log(Serialize(pullZone));
        }
        if (pullZone is null) { throw new Exception("Failed to create pull zone"); }

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
        Console.WriteLine($"Local Files Found: {localFiles.Length}");

        Console.WriteLine("Getting Storage Objects...");
        List<StorageObject> topObjects =
            await bunnyCDNStorage.GetStorageObjectsAsync($"/{bunnyCDNStorage.StorageZoneName}/");

        FilePathWrapper[] cdnFiles =
            (await RecurseStorageObjects(topObjects, bunnyCDNStorage, directory))
            .ToArray();
        Console.WriteLine($"CDN Files Found: {cdnFiles.Length}");

        // Split local files by whether or not they are already on the CDN
        ILookup<bool, FilePathWrapper> localFilesByCdnMatch =
            localFiles.ToLookup(l => cdnFiles.Any(c => l.FilePath == c.FilePath));
        IEnumerable<FilePathWrapper> toUpload = localFilesByCdnMatch[false];
        IEnumerable<FilePathWrapper> toUpdate = localFilesByCdnMatch[true];

        Console.WriteLine($"files to upload: {toUpload.Count()}");
        foreach (FilePathWrapper upload in toUpload)
        {
            Console.WriteLine($"uploading: {upload.LocalPath}");
            await bunnyCDNStorage.UploadAsync(upload.LocalPath, upload.CdnPath);
        }

        Console.WriteLine($"files to update: {toUpdate.Count()}");
        foreach (FilePathWrapper check in toUpdate)
        {
            // TODO: Check for changes before upload
            Console.WriteLine($"updating: {check.LocalPath}");
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

        // Get System Host Name
        PullZone.HostName systemHostName = pullZone.Hostnames.Single(h => h.IsSystemHostname);
        Log(Serialize(systemHostName));

        // Build the new record
        DnsZone.Record record =
            new()
            {
                Type = DnsZone.Record.TYPE_CNAME,
                Name = subdomain,
                Value = systemHostName.Value,
                Ttl = 300 // 300 seconds = 5 Minutes
            };

        // Check for a previously existing old record
        DnsZone.Record? recordOld =
            dnsZone.Records.SingleOrDefault(r => r.Type == DnsZone.Record.TYPE_CNAME && r.Name == subdomain);

        // Create the record if it doesn't exist
        if (recordOld is null)
        {
            HttpResponseMessage addDnsRecordResponseMessage =
                await http.PutAsJsonAsync
                (
                    $"dnszone/{inputs.DnsZoneId}/records",
                    record
                );

            if (!addDnsRecordResponseMessage.IsSuccessStatusCode) { return; }
            Log("Added dns record!");
        }
        // Otherwise, update the record
        else
        {
            HttpResponseMessage updateDnsRecordResponseMessage =
                await http.PostAsJsonAsync
                (
                    $"dnszone/{inputs.DnsZoneId}/records/{recordOld.Id}",
                    record
                );

            if (!updateDnsRecordResponseMessage.IsSuccessStatusCode) { return; }
            Log("Updated dns record!");
        }

        // Add custom hostname to Pull Zone using DNS record
        // TODO: Check first?
        Log($"Adding custom host name: '{customHostName}'");
        HttpResponseMessage addCustomHostNameResponseMessage =
            await http.PostAsJsonAsync
            (
                $"pullzone/{pullZone.Id}/addHostname",
                new AddHostnameRequestBody() { Hostname = customHostName }
            );
        if (!addCustomHostNameResponseMessage.IsSuccessStatusCode) { return; }
        Log("Added custom host name!");

        // Load free SSL certificate
        HttpResponseMessage loadFreeCertificateResponseMessage =
            await http.GetAsync($"pullzone/loadFreeCertificate?hostname={customHostName}");
        if (!loadFreeCertificateResponseMessage.IsSuccessStatusCode) { return; }
        Log("Loaded free SSL certificate!");

        // Force SSL
        HttpResponseMessage setForceSslResponseBody =
            await http.PostAsJsonAsync
            (
                $"pullzone/{pullZone.Id}/setForceSSL",
                new SetForceSslRequestBody() { Hostname = customHostName, ForceSSL = true }
            );
        if (!setForceSslResponseBody.IsSuccessStatusCode)
        { return; }
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
}