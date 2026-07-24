using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Threading;

using ACE.Common;

namespace ACE.Server
{
    partial class Program
    {
        private static void CheckForWorldDatabaseUpdate()
        {
            log.Info($"Automatic World Database Update started...");
            try
            {
                var worldDb = new Database.WorldDatabase();
                var currentVersion = worldDb.GetVersion();
                log.Info($"Current World Database version: Base - {currentVersion.BaseVersion} | Patch - {currentVersion.PatchVersion}");

                var url = "https://api.github.com/repos/ACEmulator/ACE-World-16PY-Patches/releases/latest";

                using var client = new WebClient();
                var html = client.GetStringFromURL(url).Result;
                var json = JsonSerializer.Deserialize<JsonElement>(html);
                string tag = json.GetProperty("tag_name").GetString();
                string dbURL = json.GetProperty("assets")[0].GetProperty("browser_download_url").GetString();
                string dbFileName = json.GetProperty("assets")[0].GetProperty("name").GetString();

                if (currentVersion.PatchVersion != tag)
                {
                    var patchVersionSplit = currentVersion.PatchVersion.Split(".");
                    var tagSplit = tag.Split(".");

                    int.TryParse(patchVersionSplit[0], out var patchMajor);
                    int.TryParse(patchVersionSplit[1], out var patchMinor);
                    int.TryParse(patchVersionSplit[2], out var patchBuild);

                    int.TryParse(tagSplit[0], out var tagMajor);
                    int.TryParse(tagSplit[1], out var tagMinor);
                    int.TryParse(tagSplit[2], out var tagBuild);

                    if (tagMajor > patchMajor || tagMinor > patchMinor || (tagBuild > patchBuild && patchBuild != 0))
                    {
                        log.Info($"Latest patch version is {tag} -- Update Required!");
                        UpdateToLatestWorldDatabase(dbURL, dbFileName);
                        var newVersion = worldDb.GetVersion();
                        log.Info($"Updated World Database version: Base - {newVersion.BaseVersion} | Patch - {newVersion.PatchVersion}");
                    }
                    else
                    {
                        log.Info($"Latest patch version is {tag} -- No Update Required!");
                    }
                }
                else
                {
                    log.Info($"Latest patch version is {tag} -- No Update Required!");
                }
            }
            catch (Exception ex)
            {
                log.Info($"Unable to continue with Automatic World Database Update due to the following error: {ex}");
            }
            log.Info($"Automatic World Database Update complete.");
        }

        private static void UpdateToLatestWorldDatabase(string dbURL, string dbFileName)
        {
            Console.WriteLine();

            if (IsRunningInContainer)
            {
                Console.WriteLine(" ");
                Console.WriteLine("This process will take a while, depending on many factors, and may look stuck while reading and importing the world database, please be patient! ");
                Console.WriteLine(" ");
            }

            Console.Write($"Downloading {dbFileName} .... ");
            using var client = new WebClient();
            try
            {
                var dlTask = client.DownloadFile(dbURL, dbFileName);
                dlTask.Wait();
            }
            catch
            {
                Console.Write($"Download for {dbFileName} failed!");
                return;
            }
            Console.WriteLine("download complete!");

            Console.Write($"Extracting {dbFileName} .... ");
            ZipFile.ExtractToDirectory(dbFileName, ".", true);
            Console.WriteLine("extraction complete!");
            Console.Write($"Deleting {dbFileName} .... ");
            File.Delete(dbFileName);
            Console.WriteLine("Deleted!");

            var sqlFile = dbFileName.Substring(0, dbFileName.Length - 4);
            Console.Write($"Importing {sqlFile} into SQL server at {ConfigManager.Config.MySql.World.Host}:{ConfigManager.Config.MySql.World.Port} (This will take a while, please be patient) .... ");
            using (var sr = File.OpenText(sqlFile))
            {
                var sqlConnect = new MySqlConnector.MySqlConnection($"server={ConfigManager.Config.MySql.World.Host};port={ConfigManager.Config.MySql.World.Port};user={ConfigManager.Config.MySql.World.Username};password={ConfigManager.Config.MySql.World.Password};{ConfigManager.Config.MySql.World.ConnectionOptions}");

                var line = string.Empty;
                var completeSQLline = string.Empty;

                var dbname = ConfigManager.Config.MySql.World.Database;

                while ((line = sr.ReadLine()) != null)
                {
                    line = line.Replace("ace_world", dbname);
                    //do minimal amount of work here
                    if (line.EndsWith(";"))
                    {
                        completeSQLline += line + Environment.NewLine;

                        var script = new MySqlConnector.MySqlCommand(completeSQLline, sqlConnect);
                        try
                        {
                            ExecuteScript(script);
                        }
                        catch (MySqlConnector.MySqlException)
                        {

                        }
                        completeSQLline = string.Empty;
                    }
                    else
                        completeSQLline += line + Environment.NewLine;
                }
                CleanupConnection(sqlConnect);
            }
            Console.WriteLine(" complete!");

            Console.Write($"Deleting {sqlFile} .... ");
            File.Delete(sqlFile);
            Console.WriteLine("Deleted!");
        }

        private static string GetContentFolder()
        {
            var sqlConnect = new MySqlConnector.MySqlConnection($"server={ConfigManager.Config.MySql.Shard.Host};port={ConfigManager.Config.MySql.Shard.Port};user={ConfigManager.Config.MySql.Shard.Username};password={ConfigManager.Config.MySql.Shard.Password};database={ConfigManager.Config.MySql.Shard.Database};{ConfigManager.Config.MySql.Shard.ConnectionOptions}");
            var sqlQuery = "SELECT `value` FROM config_properties_string WHERE `key` = 'content_folder';";
            var sqlCommand = new MySqlConnector.MySqlCommand(sqlQuery, sqlConnect);

            sqlConnect.Open();
            var sqlReader = sqlCommand.ExecuteReader();

            var content_folder = "";

            if (sqlReader.HasRows)
            {
                while (sqlReader.Read())
                {
                    content_folder = sqlReader.GetString(0);
                    break;
                }
            }
            else
                content_folder = @".\Content";

            sqlReader.Close();
            sqlCommand.Connection.Close();

            // handle relative path
            if (content_folder.StartsWith("."))
            {
                var cwd = Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar;
                content_folder = cwd + content_folder;
            }

            return content_folder;
        }

        private static void AutoApplyWorldCustomizations()
        {
            var content_folders_search_option = ConfigManager.Config.Offline.RecurseWorldCustomizationPaths ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var content_folders = new List<string> { GetContentFolder() };
            content_folders.AddRange(ConfigManager.Config.Offline.WorldCustomizationAddedPaths ?? Array.Empty<string>());
            content_folders = content_folders
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => Path.GetFullPath(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Console.WriteLine("Searching for World Customization SQL scripts .... ");

            var worldPatchVersion = GetCurrentWorldPatchVersion();
            var manifest = LoadWorldCustomizationManifest();
            var forceReapply = !string.Equals(manifest.WorldPatchVersion, worldPatchVersion, StringComparison.OrdinalIgnoreCase);
            if (forceReapply)
                Console.WriteLine($"World database patch changed from '{manifest.WorldPatchVersion ?? "none"}' to '{worldPatchVersion ?? "unknown"}'. Reapplying all customizations once.");

            var sqlFiles = new List<FileInfo>();
            foreach (var path in content_folders)
            {
                var contentDI = new DirectoryInfo(path);
                if (!contentDI.Exists)
                    continue;

                Console.WriteLine($"Scanning SQL files within {path} .... ");
                sqlFiles.AddRange(contentDI.GetFiles("*.sql", content_folders_search_option));
            }

            sqlFiles = sqlFiles
                .OrderBy(f => f.FullName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (sqlFiles.Count == 0)
            {
                Console.WriteLine("No World Customization SQL scripts found.");
                manifest.WorldPatchVersion = worldPatchVersion;
                SaveWorldCustomizationManifest(manifest);
                return;
            }

            var imported = 0;
            var skipped = 0;
            var skippedFailed = 0;
            var failed = 0;
            using var sqlConnect = new MySqlConnector.MySqlConnection($"server={ConfigManager.Config.MySql.World.Host};port={ConfigManager.Config.MySql.World.Port};user={ConfigManager.Config.MySql.World.Username};password={ConfigManager.Config.MySql.World.Password};database={ConfigManager.Config.MySql.World.Database};{ConfigManager.Config.MySql.World.ConnectionOptions}");

            foreach (var file in sqlFiles)
            {
                var signature = BuildWorldCustomizationSignature(file);
                if (!forceReapply && manifest.Files.TryGetValue(signature.Path, out var previous) && previous.Matches(signature))
                {
                    if (previous.Status == WorldCustomizationImportStatus.Failed)
                    {
                        skippedFailed++;
                        Console.WriteLine($"Skipping previously failed unchanged customization {file.FullName}. Fix or touch the file to retry it.");
                    }
                    else
                        skipped++;

                    continue;
                }

                Console.Write($"Applying {file.FullName} .... ");
                var sqlDBFile = File.ReadAllText(file.FullName);
                sqlDBFile = sqlDBFile.Replace("ace_world", ConfigManager.Config.MySql.World.Database);
                Console.Write($"Importing into World database on SQL server at {ConfigManager.Config.MySql.World.Host}:{ConfigManager.Config.MySql.World.Port} .... ");
                try
                {
                    ExecuteWorldCustomizationScript(sqlConnect, sqlDBFile);
                    signature.Status = WorldCustomizationImportStatus.Applied;
                    signature.LastError = null;
                    manifest.Files[signature.Path] = signature;
                    imported++;
                    Console.WriteLine(" complete!");
                }
                catch (MySqlConnector.MySqlException ex) when (IsAlreadyAppliedCustomizationException(ex))
                {
                    signature.Status = WorldCustomizationImportStatus.Applied;
                    signature.LastError = $"Already applied: {ex.Message}";
                    manifest.Files[signature.Path] = signature;
                    skipped++;
                    Console.WriteLine(" already applied.");
                    Console.WriteLine($" Skipping duplicate customization row: {ex.Message}");
                }
                catch (MySqlConnector.MySqlException ex)
                {
                    signature.Status = WorldCustomizationImportStatus.Failed;
                    signature.LastError = ex.Message;
                    manifest.Files[signature.Path] = signature;
                    failed++;
                    Console.WriteLine(" error!");
                    Console.WriteLine($" Unable to apply customization due to following exception: {ex}");
                }
            }

            CleanupConnection(sqlConnect);
            manifest.WorldPatchVersion = worldPatchVersion;
            SaveWorldCustomizationManifest(manifest);
            Console.WriteLine($"World Customization SQL scripts import complete! Imported {imported:N0}, skipped unchanged {skipped:N0}, skipped unchanged failed {skippedFailed:N0}, failed {failed:N0}.");
        }


        private const int MySqlDuplicateEntryErrorNumber = 1062;
        private static readonly Regex BossMechanicProfileInsertRegex = new Regex(@"INSERT\s+INTO\s+`?boss_mechanic_profile`?\s*\([^;]*?;", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static bool IsAlreadyAppliedCustomizationException(MySqlConnector.MySqlException ex)
        {
            return ex?.Number == MySqlDuplicateEntryErrorNumber;
        }

        private static void ExecuteWorldCustomizationScript(MySqlConnector.MySqlConnection worldConnection, string sql)
        {
            var bossSql = ExtractBossMechanicProfileSql(ref sql);

            if (!string.IsNullOrWhiteSpace(sql))
                ExecuteScript(new MySqlConnector.MySqlCommand(sql, worldConnection));

            if (!string.IsNullOrWhiteSpace(bossSql))
                ExecuteShardCustomizationScript(bossSql);
        }

        private static string ExtractBossMechanicProfileSql(ref string sql)
        {
            if (string.IsNullOrWhiteSpace(sql) || sql.IndexOf("boss_mechanic_profile", StringComparison.OrdinalIgnoreCase) < 0)
                return null;

            var matches = BossMechanicProfileInsertRegex.Matches(sql);
            if (matches.Count == 0)
                return null;

            var bossSql = string.Join(Environment.NewLine, matches.Cast<Match>().Select(match => match.Value));
            sql = BossMechanicProfileInsertRegex.Replace(sql, string.Empty);
            return bossSql;
        }

        private static void ExecuteShardCustomizationScript(string sql)
        {
            using var shardConnect = new MySqlConnector.MySqlConnection($"server={ConfigManager.Config.MySql.Shard.Host};port={ConfigManager.Config.MySql.Shard.Port};user={ConfigManager.Config.MySql.Shard.Username};password={ConfigManager.Config.MySql.Shard.Password};database={ConfigManager.Config.MySql.Shard.Database};{ConfigManager.Config.MySql.Shard.ConnectionOptions}");
            EnsureBossMechanicProfileTable(shardConnect);
            ExecuteScript(new MySqlConnector.MySqlCommand(sql, shardConnect));
            CleanupConnection(shardConnect);
        }

        private static void EnsureBossMechanicProfileTable(MySqlConnector.MySqlConnection shardConnect)
        {
            var sql = @"
CREATE TABLE IF NOT EXISTS `boss_mechanic_profile` (
  `profile_Name` varchar(64) NOT NULL,
  `weenie_Class_Id` int unsigned NOT NULL,
  `draft_Revision` int NOT NULL DEFAULT 0,
  `draft_Json` longtext NULL,
  `published_Revision` int NOT NULL DEFAULT 0,
  `published_Json` longtext NULL,
  `previous_Revision` int NOT NULL DEFAULT 0,
  `previous_Json` longtext NULL,
  `enabled` tinyint(1) NOT NULL DEFAULT 0,
  `modified_By` varchar(64) NULL,
  `modified_At` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`profile_Name`),
  UNIQUE KEY `boss_mechanic_profile_weenie_Class_Id_uidx` (`weenie_Class_Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";

            ExecuteScript(new MySqlConnector.MySqlCommand(sql, shardConnect));
        }

        private static string WorldCustomizationManifestPath => Path.Combine(AppContext.BaseDirectory, "Data", "DerpACE", "world-customization-manifest.json");

        private static string GetCurrentWorldPatchVersion()
        {
            try
            {
                var worldDb = new Database.WorldDatabase();
                return worldDb.GetVersion()?.PatchVersion;
            }
            catch
            {
                return null;
            }
        }

        private static WorldCustomizationManifest LoadWorldCustomizationManifest()
        {
            try
            {
                if (File.Exists(WorldCustomizationManifestPath))
                {
                    var manifest = JsonSerializer.Deserialize<WorldCustomizationManifest>(File.ReadAllText(WorldCustomizationManifestPath));
                    if (manifest != null)
                    {
                        manifest.Files ??= new Dictionary<string, WorldCustomizationManifestEntry>(StringComparer.OrdinalIgnoreCase);
                        return manifest;
                    }
                }
            }
            catch
            {
            }

            return new WorldCustomizationManifest { Files = new Dictionary<string, WorldCustomizationManifestEntry>(StringComparer.OrdinalIgnoreCase) };
        }

        private static void SaveWorldCustomizationManifest(WorldCustomizationManifest manifest)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(WorldCustomizationManifestPath));
            File.WriteAllText(WorldCustomizationManifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static WorldCustomizationManifestEntry BuildWorldCustomizationSignature(FileInfo file)
        {
            using var stream = File.OpenRead(file.FullName);
            return new WorldCustomizationManifestEntry
            {
                Path = file.FullName.ToUpperInvariant(),
                Length = file.Length,
                LastWriteUtcTicks = file.LastWriteTimeUtc.Ticks,
                Sha256 = Convert.ToHexString(SHA256.HashData(stream)),
                ImportedUtc = DateTime.UtcNow,
                Status = WorldCustomizationImportStatus.Pending
            };
        }

        private sealed class WorldCustomizationManifest
        {
            public string WorldPatchVersion { get; set; }
            public Dictionary<string, WorldCustomizationManifestEntry> Files { get; set; } = new Dictionary<string, WorldCustomizationManifestEntry>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class WorldCustomizationManifestEntry
        {
            public string Path { get; set; }
            public long Length { get; set; }
            public long LastWriteUtcTicks { get; set; }
            public string Sha256 { get; set; }
            public DateTime ImportedUtc { get; set; }
            public WorldCustomizationImportStatus Status { get; set; } = WorldCustomizationImportStatus.Applied;
            public string LastError { get; set; }

            public bool Matches(WorldCustomizationManifestEntry other)
            {
                return other != null
                    && Length == other.Length
                    && LastWriteUtcTicks == other.LastWriteUtcTicks
                    && string.Equals(Sha256, other.Sha256, StringComparison.OrdinalIgnoreCase);
            }
        }

        private enum WorldCustomizationImportStatus
        {
            Pending,
            Applied,
            Failed
        }
        private static void AutoApplyDatabaseUpdates()
        {
            log.Info($"Automatic Database Patching started...");
            Thread.Sleep(1000);

            PatchDatabase("Authentication", ConfigManager.Config.MySql.Authentication.Host, ConfigManager.Config.MySql.Authentication.Port, ConfigManager.Config.MySql.Authentication.Username, ConfigManager.Config.MySql.Authentication.Password, ConfigManager.Config.MySql.Authentication.Database, ConfigManager.Config.MySql.Shard.Database, ConfigManager.Config.MySql.World.Database);
            PatchDatabase("Shard", ConfigManager.Config.MySql.Shard.Host, ConfigManager.Config.MySql.Shard.Port, ConfigManager.Config.MySql.Shard.Username, ConfigManager.Config.MySql.Shard.Password, ConfigManager.Config.MySql.Authentication.Database, ConfigManager.Config.MySql.Shard.Database, ConfigManager.Config.MySql.World.Database);
            PatchDatabase("World", ConfigManager.Config.MySql.World.Host, ConfigManager.Config.MySql.World.Port, ConfigManager.Config.MySql.World.Username, ConfigManager.Config.MySql.World.Password, ConfigManager.Config.MySql.Authentication.Database, ConfigManager.Config.MySql.Shard.Database, ConfigManager.Config.MySql.World.Database);

            Thread.Sleep(1000);
            log.Info($"Automatic Database Patching complete.");
        }

        private static void PatchDatabase(string dbType, string host, uint port, string username, string password, string authDB, string shardDB, string worldDB)
        {
            var updatesPath = $"DatabaseSetupScripts{Path.DirectorySeparatorChar}Updates{Path.DirectorySeparatorChar}{dbType}";
            var updatesFile = $"{updatesPath}{Path.DirectorySeparatorChar}applied_updates.txt";

            if (!Directory.Exists(updatesPath))
            {
                // File not found in Environment.CurrentDirectory
                // Lets try the ExecutingAssembly Location
                var executingAssemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;

                var directoryName = Path.GetFullPath(Path.GetDirectoryName(executingAssemblyLocation));

                updatesPath = Path.Combine(directoryName, $"DatabaseSetupScripts{Path.DirectorySeparatorChar}Updates{Path.DirectorySeparatorChar}{dbType}");

                if (!Directory.Exists(updatesPath))
                {
                    Console.WriteLine($" error!");
                    Console.WriteLine($" Unable to locate updates directory");
                }
                else
                {
                    updatesFile = $"{updatesPath}{Path.DirectorySeparatorChar}applied_updates.txt";
                }

            }

            var appliedUpdates = Array.Empty<string>();

            var containerUpdatesFile = $"/ace/Config/{dbType}_applied_updates.txt";
            if (IsRunningInContainer && File.Exists(containerUpdatesFile))
                File.Copy(containerUpdatesFile, updatesFile, true);

            if (File.Exists(updatesFile))
                appliedUpdates = File.ReadAllLines(updatesFile);

            Console.WriteLine($"Searching for {dbType} update SQL scripts .... ");
            foreach (var file in new DirectoryInfo(updatesPath).GetFiles("*.sql").OrderBy(f => f.Name))
            {
                if (appliedUpdates.Contains(file.Name))
                    continue;

                Console.Write($"Found {file.Name} .... ");
                var sqlDBFile = File.ReadAllText(file.FullName);
                var database = "";
                switch (dbType)
                {
                    case "Authentication":
                        database = authDB;
                        break;
                    case "Shard":
                        database = shardDB;
                        break;
                    case "World":
                        database = worldDB;
                        break;
                }
                var sqlConnect = new MySqlConnector.MySqlConnection($"server={host};port={port};user={username};password={password};database={database};DefaultCommandTimeout=120;SslMode=None;AllowPublicKeyRetrieval=true");
                sqlDBFile = sqlDBFile.Replace("ace_auth", authDB);
                sqlDBFile = sqlDBFile.Replace("ace_shard", shardDB);
                sqlDBFile = sqlDBFile.Replace("ace_world", worldDB);
                var script = new MySqlConnector.MySqlCommand(sqlDBFile, sqlConnect);

                Console.Write($"Importing into {database} database on SQL server at {host}:{port} .... ");
                try
                {
                    ExecuteScript(script);
                    //Console.Write($" {count} database records affected ....");
                    Console.WriteLine(" complete!");
                }
                catch (MySqlConnector.MySqlException ex)
                {
                    Console.WriteLine($" error!");
                    Console.WriteLine($" Unable to apply patch due to following exception: {ex}");
                }
                File.AppendAllText(updatesFile, file.Name + Environment.NewLine);
                CleanupConnection(sqlConnect);
            }

            if (IsRunningInContainer && File.Exists(updatesFile))
                File.Copy(updatesFile, containerUpdatesFile, true);

            Console.WriteLine($"{dbType} update SQL scripts import complete!");
        }
    }
}



