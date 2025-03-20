using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Diagnostics;

// Console logging lock for parallel processing
object ConsoleLock = new();

string? SearchExtension = "";
string? path = null;

if (args.Length > 1) {
    path = args[1];
}

if (args.Length > 2) {
    SearchExtension = args[2];
}

if (string.IsNullOrEmpty(path)) {
    Console.WriteLine("Enter a path to search");
    path = Console.ReadLine();
}

if (string.IsNullOrEmpty(path)) {
    Console.WriteLine("You need to enter a valid path");
    return;
}


if (!Directory.Exists(path)) {
    // Try these paths in order:
    string[] possiblePaths = {
        path, // Original absolute path
        Path.Combine(Directory.GetCurrentDirectory(), path), // Relative to current directory
        Path.Combine(Directory.GetCurrentDirectory(), "NoOpTexturePacker",
            path), // Project directory
        Path.GetFullPath(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..",
                path)) // Relative to executable
    };

    bool foundValidPath = false;
    foreach (string testPath in possiblePaths) {
        if (Directory.Exists(testPath)) {
            path = testPath;
            Console.WriteLine($"Found directory at: {path}");
            foundValidPath = true;
            break;
        }
    }

    if (!foundValidPath) {
        Console.WriteLine(
            $"The directory '{path}' does not exist. Would you like to create it? Y/N");
        string? response = Console.ReadLine();

        if (response?.Trim().Equals("Y", StringComparison.OrdinalIgnoreCase) == true) {
            try {
                Directory.CreateDirectory(path);
                Console.WriteLine($"Directory '{path}' created successfully.");
            } catch (Exception ex) {
                Console.WriteLine($"Failed to create directory: {ex.Message}");
                return;
            }
        } else {
            Console.WriteLine("Please restart the application with a valid directory path.");
            return;
        }
    }
}

if (string.IsNullOrEmpty(SearchExtension)) {
    Console.WriteLine("Enter the file extension to search for (ex. png, jpg, exr)");
    SearchExtension = Console.ReadLine();
}

if (string.IsNullOrEmpty(SearchExtension) ||
    (!SearchExtension.Equals("png", StringComparison.CurrentCultureIgnoreCase) &&
     !SearchExtension.Equals("jpg", StringComparison.CurrentCultureIgnoreCase) &&
     !SearchExtension.Equals("exr", StringComparison.CurrentCultureIgnoreCase))) {
    SearchExtension = "png";
    Console.WriteLine("Moving forward with png");
}

// Options for processing
var ShouldProcessIndividualTextures = true;
var ShouldProcessORMTextures = true;
var ShouldExtractFromORM = false;
var ShouldSaveUnityORM = true;
var ShouldSaveUnrealORM = true;
var ShouldSaveUnitySmoothnessInMetallic = true;
var ShouldDeleteNonORMFiles = false;
var IsUnrealORMFormat =
    true; // If true, ORM = (AO, Roughness, Metallic), if false, ORM = (AO, Smoothness, Metallic)

if (!(args.Length > 3 && bool.TryParse(args[3], out ShouldProcessIndividualTextures))) {
    ShouldProcessIndividualTextures =
        GetYesOrNoAnswerFromConsole(
            "Should process individual textures (AO, Roughness, Metallic)? Y/N");
}

if (!(args.Length > 4 && bool.TryParse(args[4], out ShouldProcessORMTextures))) {
    ShouldProcessORMTextures =
        GetYesOrNoAnswerFromConsole("Should process existing ORM textures? Y/N");
}

if (ShouldProcessORMTextures) {
    if (!(args.Length > 5 && bool.TryParse(args[5], out IsUnrealORMFormat))) {
        IsUnrealORMFormat = GetYesOrNoAnswerFromConsole(
            "Are existing ORM textures in Unreal format (R=AO, G=Roughness, B=Metallic)? Y/N\nAnswer No if ORM is in Unity format (R=AO, G=Smoothness, B=Metallic)");
    }

    if (!(args.Length > 6 && bool.TryParse(args[6], out ShouldExtractFromORM))) {
        ShouldExtractFromORM =
            GetYesOrNoAnswerFromConsole(
                "Extract individual AO, Roughness/Smoothness, and Metallic textures from ORM? Y/N");
    }
}

if (ShouldProcessIndividualTextures) {
    if (!(args.Length > 7 && bool.TryParse(args[7], out ShouldSaveUnityORM))) {
        ShouldSaveUnityORM =
            GetYesOrNoAnswerFromConsole("Should save Unity ORM from individual textures? Y/N");
    }

    if (!(args.Length > 8 && bool.TryParse(args[8], out ShouldSaveUnrealORM))) {
        ShouldSaveUnrealORM =
            GetYesOrNoAnswerFromConsole("Should save Unreal ORM from individual textures? Y/N");
    }

    if (!(args.Length > 9 && bool.TryParse(args[9], out ShouldSaveUnitySmoothnessInMetallic))) {
        ShouldSaveUnitySmoothnessInMetallic = GetYesOrNoAnswerFromConsole(
            "Should save Unity Smoothness from inverse of roughness to alpha of metallic texture? Y/N");
    }

    if (!(args.Length > 10 && bool.TryParse(args[10], out ShouldDeleteNonORMFiles))) {
        ShouldDeleteNonORMFiles = GetYesOrNoAnswerFromConsole(
            "Should DELETE roughness, metallic and AO textures after the operations are done? Y/N");
    }
}

string[] paths = Directory.GetFiles(path, $"*.{SearchExtension}", SearchOption.AllDirectories);
Console.WriteLine($"Files found: {paths.Length}");
Dictionary<string, List<string>> FilesGroupedByDirectory = [];

// Put files of a single directory in a single dictionary key for easy processing
foreach (string p in paths) {
    string? dir = Path.GetDirectoryName(p);
    if (string.IsNullOrEmpty(dir))
        throw new Exception("The file directory should not be null");

    string Name = Path.GetFileName(p);
    if (!FilesGroupedByDirectory.TryGetValue(dir, out List<string>? InDirectoryFileList)) {
        InDirectoryFileList = [];
        FilesGroupedByDirectory.Add(dir, InDirectoryFileList);
    }

    InDirectoryFileList.Add(p);
}

Stopwatch sw = new Stopwatch();
sw.Start();

// Process all images
Parallel.ForEach(FilesGroupedByDirectory.Keys, Dir => {
    var FilesList = FilesGroupedByDirectory[Dir];

    // Process individual textures (original functionality)
    if (ShouldProcessIndividualTextures) {
        ProcessIndividualTextures(Dir, FilesList);
    }

    // Process ORM textures (new functionality)
    if (ShouldProcessORMTextures) {
        ProcessORMTextures(Dir, FilesList);
    }
});

Console.Write($"Took {sw.ElapsedMilliseconds} ms");
// Finished processing

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("Done!");
Console.ForegroundColor = ConsoleColor.White;

// Process individual AO, Roughness, and Metallic textures
void ProcessIndividualTextures(string Dir, List<string> PBRFilesList) {
    string? RoughnessFile =
        PBRFilesList.FirstOrDefault(x =>
            x.Contains("roughness", StringComparison.CurrentCultureIgnoreCase));
    string? MetallicFile =
        PBRFilesList.FirstOrDefault(x =>
            x.Contains("metallic", StringComparison.CurrentCultureIgnoreCase));
    string? AOFile =
        PBRFilesList.FirstOrDefault(
            x => x.Contains("ao", StringComparison.CurrentCultureIgnoreCase));

    if (string.IsNullOrEmpty(MetallicFile) || string.IsNullOrEmpty(RoughnessFile) ||
        string.IsNullOrEmpty(AOFile)) {
        lock (ConsoleLock) {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(
                $"The folder {Dir} does not contain all required individual maps (AO, Roughness, Metallic). Skipping individual texture processing.");
            Console.ForegroundColor = ConsoleColor.White;
        }

        return;
    }

    Image<Argb32>? AO = null;
    Image<Argb32>? Roughness = null;
    Image<Argb32>? Metallic = null;
    Image<Rgb24>? ORMUE = null;
    Image<Rgb24>? ORMUnity = null;
    Image<Argb32>? NewMetallic = null;

    try {
        AO = Image.Load<Argb32>(AOFile);
        Roughness = Image.Load<Argb32>(RoughnessFile);
        Metallic = Image.Load<Argb32>(MetallicFile);

        if (ShouldSaveUnrealORM) {
            ORMUE = new Image<Rgb24>(AO.Width, AO.Height);
        }

        if (ShouldSaveUnityORM) {
            ORMUnity = new Image<Rgb24>(AO.Width, AO.Height);
        }

        if (ShouldSaveUnitySmoothnessInMetallic) {
            NewMetallic = new Image<Argb32>(AO.Width, AO.Height);
        }

        for (int j = 0; j < Roughness.Height; j++) {
            for (int i = 0; i < Roughness.Width; i++) {
                if (ShouldSaveUnityORM || ShouldSaveUnitySmoothnessInMetallic) {
                    byte InverseRoughness = (byte)(255 - Roughness[i, j].R);

                    if (ShouldSaveUnitySmoothnessInMetallic && NewMetallic != null) {
                        // Fix: Get the pixel, modify it, then set it back
                        Argb32 CurrentMetallicPixel = Metallic[i, j];
                        CurrentMetallicPixel.A = InverseRoughness;
                        NewMetallic[i, j] = CurrentMetallicPixel;
                    }

                    if (ShouldSaveUnityORM && ORMUnity != null) {
                        ORMUnity[i, j] = new Rgb24(AO[i, j].R, InverseRoughness, Metallic[i, j].R);
                    }
                }

                if (ShouldSaveUnrealORM && ORMUE != null) {
                    ORMUE[i, j] = new Rgb24(AO[i, j].R, Roughness[i, j].R, Metallic[i, j].R);
                }
            }
        }

        // Save files
        if (ShouldSaveUnitySmoothnessInMetallic && NewMetallic != null) {
            NewMetallic.Save(MetallicFile);
        }

        if (ShouldSaveUnrealORM && ORMUE != null) {
            ORMUE.Save(Path.Combine(Dir,
                $"{Path.GetFileNameWithoutExtension(MetallicFile).Replace("Metallic", "ormue")}.{SearchExtension}"));
        }

        if (ShouldSaveUnityORM && ORMUnity != null) {
            ORMUnity.Save(Path.Combine(Dir,
                $"{Path.GetFileNameWithoutExtension(MetallicFile).Replace("Metallic", "ormunity")}.{SearchExtension}"));
        }

        if (ShouldDeleteNonORMFiles) {
            if (File.Exists(RoughnessFile)) {
                File.Delete(RoughnessFile);
            }

            if (File.Exists(AOFile)) {
                File.Delete(AOFile);
            }

            if (File.Exists(MetallicFile)) {
                File.Delete(MetallicFile);
            }
        }

        lock (ConsoleLock) {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Processed individual textures in {Dir} successfully!");
            Console.ForegroundColor = ConsoleColor.White;
        }
    } catch (Exception ex) {
        lock (ConsoleLock) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error processing individual textures in {Dir}: {ex.Message}");
            Console.ForegroundColor = ConsoleColor.White;
        }
    } finally {
        AO?.Dispose();
        Roughness?.Dispose();
        Metallic?.Dispose();
        ORMUE?.Dispose();
        ORMUnity?.Dispose();
        NewMetallic?.Dispose();
    }
}

// Process ORM textures
void ProcessORMTextures(string Dir, List<string> FilesList) {
    var ORMFiles = FilesList.Where(x =>
        x.Contains("orm", StringComparison.CurrentCultureIgnoreCase) &&
        !x.Contains("ormunity", StringComparison.CurrentCultureIgnoreCase)).ToList();

    if (ORMFiles.Count == 0) {
        lock (ConsoleLock) {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(
                $"The folder {Dir} does not contain any ORM textures. Skipping ORM processing.");
            Console.ForegroundColor = ConsoleColor.White;
        }

        return;
    }

    foreach (var ormFile in ORMFiles) {
        Image<Rgb24>? ORM = null;
        Image<Rgb24>? UnityORM = null;
        Image<Argb32>? AO = null;
        Image<Argb32>? Roughness = null;
        Image<Argb32>? Smoothness = null;
        Image<Argb32>? Metallic = null;

        try {
            ORM = Image.Load<Rgb24>(ormFile);
            string baseFileName = Path.GetFileNameWithoutExtension(ormFile);

            // Convert to Unity ORM format if needed
            if (IsUnrealORMFormat) {
                // ORM = (AO, Roughness, Metallic)
                UnityORM = new Image<Rgb24>(ORM.Width, ORM.Height);

                for (int j = 0; j < ORM.Height; j++) {
                    for (int i = 0; i < ORM.Width; i++) {
                        // Convert from Unreal format (R=AO, G=Roughness, B=Metallic)
                        // to Unity format (R=AO, G=Smoothness, B=Metallic)
                        byte ao = ORM[i, j].R;
                        byte roughness = ORM[i, j].G;
                        byte metallic = ORM[i, j].B;
                        byte smoothness =
                            (byte)(255 - roughness); // Invert roughness to get smoothness

                        UnityORM[i, j] = new Rgb24(ao, smoothness, metallic);
                    }
                }

                // Save Unity ORM
                string unityORMPath = Path.Combine(Dir, $"{baseFileName}_unity.{SearchExtension}");
                UnityORM.Save(unityORMPath);

                lock (ConsoleLock) {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Converted {ormFile} to Unity ORM format: {unityORMPath}");
                    Console.ForegroundColor = ConsoleColor.White;
                }
            }

            // Extract individual textures if requested
            if (ShouldExtractFromORM) {
                AO = new Image<Argb32>(ORM.Width, ORM.Height);
                Metallic = new Image<Argb32>(ORM.Width, ORM.Height);

                if (IsUnrealORMFormat) {
                    Roughness = new Image<Argb32>(ORM.Width, ORM.Height);
                    Smoothness = new Image<Argb32>(ORM.Width, ORM.Height);
                } else {
                    Smoothness = new Image<Argb32>(ORM.Width, ORM.Height);
                    Roughness = new Image<Argb32>(ORM.Width, ORM.Height);
                }

                for (int j = 0; j < ORM.Height; j++) {
                    for (int i = 0; i < ORM.Width; i++) {
                        byte ao = ORM[i, j].R;
                        byte gChannel =
                            ORM[i, j].G; // Roughness in Unreal format, Smoothness in Unity format
                        byte metallic = ORM[i, j].B;

                        // AO and Metallic are the same for both formats
                        AO[i, j] = new Argb32(ao, ao, ao, 255);

                        if (IsUnrealORMFormat) {
                            // G channel is roughness in Unreal format
                            Roughness[i, j] = new Argb32(gChannel, gChannel, gChannel, 255);

                            // Calculate smoothness as inverse of roughness
                            byte smoothness = (byte)(255 - gChannel);
                            Smoothness[i, j] = new Argb32(smoothness, smoothness, smoothness, 255);

                            // Fix: Get the pixel, modify it, then set it back
                            Argb32 metallicPixel =
                                new Argb32(metallic, metallic, metallic, smoothness);
                            Metallic[i, j] = metallicPixel;
                        } else {
                            // G channel is smoothness in Unity format
                            Smoothness[i, j] = new Argb32(gChannel, gChannel, gChannel, 255);

                            // Calculate roughness as inverse of smoothness
                            byte roughness = (byte)(255 - gChannel);
                            Roughness[i, j] = new Argb32(roughness, roughness, roughness, 255);

                            // Fix: Get the pixel, modify it, then set it back
                            Argb32 metallicPixel =
                                new Argb32(metallic, metallic, metallic, gChannel);
                            Metallic[i, j] = metallicPixel;
                        }
                    }
                }

                // Save extracted textures
                string aoPath = Path.Combine(Dir, $"{baseFileName}_AO.{SearchExtension}");
                string roughnessPath =
                    Path.Combine(Dir, $"{baseFileName}_Roughness.{SearchExtension}");
                string smoothnessPath =
                    Path.Combine(Dir, $"{baseFileName}_Smoothness.{SearchExtension}");
                string metallicPath =
                    Path.Combine(Dir, $"{baseFileName}_Metallic.{SearchExtension}");

                AO.Save(aoPath);
                Roughness.Save(roughnessPath);
                Smoothness.Save(smoothnessPath);
                Metallic.Save(metallicPath);

                lock (ConsoleLock) {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Extracted textures from {ormFile}:");
                    Console.WriteLine($"  AO: {aoPath}");
                    Console.WriteLine($"  Roughness: {roughnessPath}");
                    Console.WriteLine($"  Smoothness: {smoothnessPath}");
                    Console.WriteLine(
                        $"  Metallic: {metallicPath} (with smoothness in alpha channel)");
                    Console.ForegroundColor = ConsoleColor.White;
                }
            }
        } catch (Exception ex) {
            lock (ConsoleLock) {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error processing ORM texture {ormFile}: {ex.Message}");
                Console.ForegroundColor = ConsoleColor.White;
            }
        } finally {
            ORM?.Dispose();
            UnityORM?.Dispose();
            AO?.Dispose();
            Roughness?.Dispose();
            Smoothness?.Dispose();
            Metallic?.Dispose();
        }
    }
}

// Gets a yes or no answer from the console
static bool GetYesOrNoAnswerFromConsole(string message) {
    string? YesNoAnswer;
    Console.WriteLine(message);
    YesNoAnswer = Console.ReadLine();
    YesNoAnswer ??= "";

    if (YesNoAnswer.Equals("y", StringComparison.CurrentCultureIgnoreCase) ||
        YesNoAnswer.Equals("yes", StringComparison.CurrentCultureIgnoreCase)) {
        return true;
    } else if (YesNoAnswer.Equals("n", StringComparison.CurrentCultureIgnoreCase) ||
               YesNoAnswer.Equals("no", StringComparison.CurrentCultureIgnoreCase)) {
        return false;
    }

    return false;
}
