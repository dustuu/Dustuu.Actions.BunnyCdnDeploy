using BunnyCDN.Net.Storage;
using BunnyCDN.Net.Storage.Models;
using System.IO;

namespace Dustuu.Actions.BunnyCdnDeploy;

public class FilePathWrapper
{
    public string FilePath { get; private init; }
    private string RootPath { get; init; }
    private string StorageZone { get; init; }

    private FilePathWrapper
    (string filePath, DirectoryInfo root, BunnyCDNStorage storage) =>
        (FilePath, RootPath, StorageZone) =
        (Clean(filePath), Clean(root.FullName), storage.StorageZoneName);

    public FilePathWrapper
    (FileInfo fileInfo, DirectoryInfo root, BunnyCDNStorage storage)
        : this(Path.GetRelativePath(root.FullName, fileInfo.FullName), root, storage) { }

    // Removes the storage zone name from path (Length + 2 because it has '/' on both sides)
    public FilePathWrapper
    (StorageObject storageObject, DirectoryInfo local, BunnyCDNStorage cdn)
        : this(storageObject.FullPath[(storageObject.StorageZoneName.Length + 2)..], local, cdn) { }

    public string LocalPath => $"{RootPath}/{FilePath}";

    public string CdnPath => $"/{StorageZone}/{FilePath}";

    private static string Clean(string path) => path.Replace("\\", "/");
}
