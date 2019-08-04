#addin "Cake.FileHelpers&version=3.2.0"
#addin "Cake.Powershell&version=0.4.8"
#addin "Cake.IIS&version=0.4.2"
#addin "Microsoft.Win32.Registry&version=4.0.0.0"
#addin "System.Reflection.TypeExtensions&version=4.1.0.0"
#addin "Cake"


void ReconfigureEnvironment(string root)
{
    StartPowershellScript(@"tools\reconfigure.ps1", args => 
    {
        args.Append("SourcePath", root);
        args.Append("EnvironmentCode", Argument("EnvironmentCode", "dev1"));
        args.Append("EnvironmentVersion", Argument("EnvironmentVersion", "development"));
    });
}

void CreateIISWebsites(string root, string glob, bool aspNetCore = false, bool inProcess = true)
{
    var env = Argument("Environment", "development");
    var websitePath = $"C:\\iis\\{env}";
    var websiteName = "DefaultIISWebsite";
    var appPoolName = Argument("ApplicationPoolName", "Default");
    
    if(!aspNetCore)
    {
        DefaultIISSetup(websiteName, appPoolName, websitePath, env, root, glob);
    } 

    if(aspNetCore && inProcess)
    {
        InProcessSetup(websitePath, env, root, glob);
    }

    if(aspNetCore && !inProcess)
    {
        OutOfProcessSetup(websiteName, appPoolName, websitePath, env, root, glob);
    }
}

void DefaultIISSetup(string websiteName, string appPoolName, string websitePath, string env, string root, string glob)
{
    var paths = GetFiles(root + glob, new GlobberSettings{ Predicate = x => !FileExists($"{x.Path.FullPath}/appsettings.json") && FileExists($"{x.Path.FullPath}/Web.config") });
    if (paths.Any())
    {
        CreateDirectory(websitePath);
        CreatePoolAndWebsite(websiteName, $"{appPoolName}Pool", websitePath);
        CreateVirtualDirectory(websiteName, env, websitePath);

        foreach(var path in paths)
        {
            CreateApplicationSite(websiteName, $"{appPoolName}Pool", env, path);
        }
    }
}

void InProcessSetup(string websitePath, string env, string root, string glob)
{
    var paths = GetFiles(root + glob, new GlobberSettings{ Predicate = x => FileExists($"{x.Path.FullPath}/appsettings.json") });
    if (paths.Any())
    {
        var port = 81;
        var httpsPort = 44300;
        foreach(var path in paths)
        {
            var projectName = path.GetDirectory().GetDirectoryName().Replace(".", string.Empty);
            CreateDirectory(websitePath);
            CreatePoolAndWebsite($"{projectName}", $"{projectName}Pool", websitePath, "", port++, httpsPort++);
            CreateVirtualDirectory($"{projectName}", env, websitePath);
            CreateApplicationSite($"{projectName}", $"{projectName}Pool", env, path);
        }
    }
}

void OutOfProcessSetup(string websiteName, string appPoolName, string websitePath, string env, string root, string glob)
{
    var paths = GetFiles(root + glob, new GlobberSettings{ Predicate = x => FileExists($"{x.Path.FullPath}/appsettings.json") });
    if (paths.Any())
    {
        CreateDirectory(websitePath);
        CreatePoolAndWebsite($"{websiteName}Core", $"{appPoolName}CorePool", websitePath, "", 81, 44300);
        CreateVirtualDirectory($"{websiteName}Core", env, websitePath);

        foreach(var path in paths)
        {
            CreateApplicationSite($"{websiteName}Core", $"{appPoolName}CorePool", env, path);
        }
    }
}

void CreateDirectory(string websitePath)
{
    StartPowershellScript("New-Item", args =>
    {
        args.Append("ItemType", "Directory");
        args.Append("Path", websitePath);
        args.Append("-Force");
    });
}

void CreatePoolAndWebsite(string websiteName, string appPoolName, string websitePath, string managedRuntimeVersion = "v4.0", int port = 80, int httpsPort = 443)
{
    var appPoolSettings = new ApplicationPoolSettings
    {
        Name = appPoolName,
        ManagedRuntimeVersion = managedRuntimeVersion
    };
    CreatePool(appPoolSettings);
    CreateWebsite(new WebsiteSettings
    {
        ApplicationPool = appPoolSettings,
        Binding = IISBindings.Http
            .SetHostName("localhost")
            .SetIpAddress("*")
            .SetPort(port),
        Name = websiteName,
        PhysicalDirectory = websitePath
    });
    var bindingSettings = new BindingSettings(BindingProtocol.Https)
    {
        HostName = "localhost",
        IpAddress = "*",
        Port = httpsPort
    };
    RemoveBinding(websiteName, bindingSettings);
    AddBinding(websiteName, bindingSettings);
}

void CreateApplicationSite(string websiteName, string appPool, string env, FilePath path)
{
    var siteApplication = new ApplicationSettings
    {
        SiteName = websiteName,
        ApplicationPath = $"/{env}/{path.GetDirectory().GetDirectoryName()}",
        ApplicationPool = appPool,
		VirtualDirectory = "/",
        PhysicalDirectory = path.GetDirectory().FullPath
    };

    if(SiteApplicationExists(siteApplication))
    {
        RemoveSiteApplication(siteApplication);
        AddSiteApplication(siteApplication);
    } else 
    {
        AddSiteApplication(siteApplication);
    }
}

void CreateVirtualDirectory(string websiteName, string env, string physicalDirectory)
{
    var virtualDirectory = new VirtualDirectorySettings 
    {
        SiteName = websiteName,
        PhysicalDirectory = physicalDirectory,
        ApplicationPath = "/",
        Path = $"/{env}"
    };
    if(SiteVirtualDirectoryExists("", virtualDirectory))
    {
        RemoveSiteVirtualDirectory(virtualDirectory);
        AddSiteVirtualDirectory(virtualDirectory);
    } else 
    {
        AddSiteVirtualDirectory(virtualDirectory);
    }
}

void RestoreAll(string root, string glob)
{
    var csprojFiles = GetFiles(root + glob);
    if (csprojFiles.Any())
    {
        var tempSln = new FilePath(root + "BuildSolution.sln");
        FileWriteText(tempSln, CreateTempSolution(csprojFiles));
        NuGetRestore(tempSln, GetRestoreSettings());
    }
}

void Restore(string root) =>
    NuGetRestore(new FilePath($"{root}{Argument<string>("Solution")}/{Argument<string>("Solution")}.sln"), GetRestoreSettings());

void Build(string root)
{
    var solution = Argument<string>("Solution", "") != "" ?
        new FilePath($"{root}{Argument<string>("Solution")}/{Argument<string>("Solution")}.sln") :
        new FilePath(root + "BuildSolution.sln");

    MSBuild(solution, settings => settings
        .SetConfiguration(Argument<string>("Configuration", "Debug"))
        .UseToolVersion(MSBuildToolVersion.VS2019)
        .SetNoLogo(true)
        .SetVerbosity(Verbosity.Quiet)
        .WithConsoleLoggerParameter("ErrorsOnly")
        .SetMaxCpuCount(0)
        .SetNodeReuse(true)
        .WithProperty("BuildInParallel", "true")
        .WithProperty("CreateHardLinksForCopyFilesToOutputDirectoryIfPossible", "true")
        .WithProperty("CreateHardLinksForCopyAdditionalFilesIfPossible", "true")
        .WithProperty("CreateHardLinksForCopyLocalIfPossible", "true")
        .WithProperty("CreateHardLinksForPublishFilesIfPossible", "true"));
}

NuGetRestoreSettings GetRestoreSettings()
{
    return new NuGetRestoreSettings
    {
        NonInteractive = true,
        Verbosity = NuGetVerbosity.Quiet,
        ConfigFile = new FilePath(@"tools\nuget.config")
    };
}

string CreateTempSolution(IEnumerable<FilePath> projects)
{
    var slnContents = new StringBuilder();
    slnContents.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
    slnContents.AppendLine("# Visual Studio Version 16");
    slnContents.AppendLine("VisualStudioVersion = 16.0.28803.202");
    slnContents.AppendLine("MinimumVisualStudioVersion = 10.0.40219.1");
    var projGuids = new List<string>();
    foreach(var project in projects)
    {
        string projGuid;
        var projContents = FileReadText(project);
        var from = projContents.IndexOf("<ProjectGuid>{");

        if (from == -1) projGuid = Guid.NewGuid().ToString().ToUpperInvariant();
        else projGuid = projContents.Substring(from + 14, 36);
        projGuids.Add(projGuid);
        slnContents.AppendFormat("Project(\"{{{0}}}\") = \"{1}\", \"{2}\", \"{{{3}}}\" \r\n", Guid.NewGuid().ToString().ToUpperInvariant(), project.GetFilenameWithoutExtension(), project.FullPath.ToString(), projGuid);
        slnContents.Append("EndProject \r\n");
    }

    slnContents.Append("Global \r\n");
    slnContents.Append("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution \r\n");
    slnContents.Append("\t\tDebug|Any CPU = Debug|Any CPU \r\n");
    slnContents.Append("\t\tRelease|Any CPU = Release|Any CPU \r\n");
    slnContents.Append("\tEndGlobalSection \r\n");
    slnContents.Append("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution \r\n");
    foreach(var guid in projGuids)
    {
        slnContents.AppendFormat("\t\t{{{0}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU \r\n", guid);
        slnContents.AppendFormat("\t\t{{{0}}}.Debug|Any CPU.Build.0 = Debug|Any CPU \r\n", guid);
        slnContents.AppendFormat("\t\t{{{0}}}.Release|Any CPU.ActiveCfg = Release|Any CPU \r\n", guid);
        slnContents.AppendFormat("\t\t{{{0}}}.Release|Any CPU.Build.0 = Release|Any CPU \r\n", guid);
    }
    slnContents.Append("\tEndGlobalSection \r\n");
    slnContents.Append("\tGlobalSection(SolutionProperties) = preSolution \r\n");
    slnContents.Append("\t\tHideSolutionNode = FALSE \r\n");
    slnContents.Append("\tEndGlobalSection \r\n");
    slnContents.Append("\tGlobalSection(ExtensibilityGlobals) = postSolution \r\n");
    slnContents.AppendFormat("\t\tSolutionGuid = {{{0}}} \r\n", Guid.NewGuid().ToString().ToUpperInvariant());
    slnContents.Append("\tEndGlobalSection \r\n");
    slnContents.Append("EndGlobal \r\n");

    return slnContents.ToString();
}

Task("RestoreAll").Does(() => RestoreAll("", "**/*.csproj"));
Task("Restore").Does(() => Restore(""));
Task("Build").Does(() => Build(""));

Task("CreateIISWebsites").Does(() =>
{
    CreateIISWebsites("", "**/*.csproj");
    CreateIISWebsites("", "**/*.csproj", true);
    StartPowershellScript(@"tools\generate-cert.ps1");
});

Task("ReconfigureEnvironment").Does(() =>
{
    ReconfigureEnvironment("");
});

Task("Publish")
    .IsDependentOn("Restore")
    .IsDependentOn("Build");

Task("PublishAll")
    .IsDependentOn("RestoreAll")
    .IsDependentOn("Build");

Task("PrepareEnvironment")
    // .IsDependentOn("ReconfigureEnvironment")
    .IsDependentOn("CreateIISWebsites")
    .IsDependentOn("RestoreAll")
    .IsDependentOn("Build");

RunTarget(Argument<string>("Target", "PublishAll"));

