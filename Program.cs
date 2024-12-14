using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConsoleApp3;

internal class Program
{
    static async Task Main(string[] args)
    {
        bool dryRun = true;
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddUserSecrets<Program>()
            .Build();

        var sourceOptions = config.GetSection("Gitlab:Source").Get<GitlabOptions>() ?? throw new Exception();
        var destinationOptions = config.GetSection("Gitlab:Destination").Get<GitlabOptions>() ?? throw new Exception();

        var sourceClient = new HttpClient();
        sourceClient.DefaultRequestHeaders.Add("PRIVATE-TOKEN", sourceOptions.PrivateToken);

        var destinationClient = new HttpClient();
        destinationClient.DefaultRequestHeaders.Add("PRIVATE-TOKEN", destinationOptions.PrivateToken);

        var jsonOptions = new JsonSerializerOptions()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            Converters = {
                new JsonStringEnumConverter()
            }
        };

        var packateTypesToTransfer = new string[] { "maven", "nuget" };
        foreach (var item in packateTypesToTransfer)
        {
            var sourceBasePackageApiUrl = $"{sourceOptions.BaseAddress}/api/v4/projects/{sourceOptions.ProjectId}/packages";
            var packages = await sourceClient.GetFromJsonAsync<GitlabMavenPackage[]>($"{sourceBasePackageApiUrl}/?package_type=maven", options: jsonOptions) ?? throw new Exception("failed");
            foreach (var pkg in packages)
            {
                Console.WriteLine();
                Console.WriteLine($"{pkg.PackageType} | {pkg.Id} | {pkg.Name} | {pkg.Version} | {pkg.Status}");

                var files = await sourceClient.GetFromJsonAsync<MavenPackageFileItem[]>($"{sourceBasePackageApiUrl}/{pkg.Id}/package_files", options: jsonOptions) ?? throw new Exception("failed");

                foreach (var file in files)
                {
                    Console.WriteLine($"\t{file.FileName} | {file.FileSha1}");

                    var downloadUrl = $"{sourceBasePackageApiUrl}/{pkg.PackageType.ToString().ToLower()}/{pkg.Name}/{pkg.Version}/{file.FileName}";
                    Debug.WriteLine($"\t{file.FileName} | {downloadUrl}");

                    var fileContent = await sourceClient.GetByteArrayAsync(downloadUrl);
                    if (fileContent.Length != file.Size)
                    {
                        Console.WriteLine($"error: downloaded file has wrong size {fileContent.Length} (expected: {file.Size})");
                        Debugger.Break();
                    }

                    var fileUploadUrl = $"{destinationOptions.BaseAddress}/api/v4/projects/{destinationOptions.ProjectId}/packages/{pkg.PackageType.ToString().ToLower()}/{pkg.Name}/{pkg.Version}/{file.FileName}";

                    Debug.WriteLine($"\t{file.FileName} | {fileUploadUrl}");

                    if (!dryRun)
                    {
                        var uploadRes = await destinationClient.PutAsync(fileUploadUrl, new ByteArrayContent(fileContent));
                        uploadRes.EnsureSuccessStatusCode();
                    }
                }
            }
        }

    }
}

internal class GitlabOptions
{
    public required string BaseAddress { get; init; }
    public required string ProjectId { get; init; }
    public required string PrivateToken { get; init; }
}

class GitlabMavenPackage
{
    public required long Id { get; init; } // 60449
    public required string Name { get; init; } // "visapisdk/lib"
    public required string Version { get; init; } // "0.6.0"
    public required PackageType PackageType { get; init; } // "maven"
    public required PackageStatus Status { get; init; } // "default"
}

enum PackageType
{
    Maven,
    Nuget
}

enum PackageStatus
{
    Default
}

class MavenPackageFileItem
{
    public required long Id { get; init; } //12492,
    public required long PackageId { get; init; } //9407,
    public required string CreatedAt { get; init; } //"2022-11-09T11:17:03.736+01:00",
    public required string FileName { get; init; } //"lib-0.0.4.jar",
    public required long Size { get; init; } //4438853,
    public required string FileMd5 { get; init; }
    public required string FileSha1 { get; init; }
    public required string? FileSha256 { get; init; }
}