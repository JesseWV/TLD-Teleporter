using MelonLoader;

[assembly: AssemblyTitle(Teleporter.BuildInfo.Name)]
[assembly: AssemblyDescription(Teleporter.BuildInfo.Description)]
[assembly: AssemblyCompany(Teleporter.BuildInfo.Author)]
[assembly: AssemblyProduct(Teleporter.BuildInfo.Name)]
[assembly: AssemblyVersion(Teleporter.BuildInfo.Version)]
[assembly: AssemblyFileVersion(Teleporter.BuildInfo.Version)]

[assembly: MelonInfo(typeof(Teleporter.Main), Teleporter.BuildInfo.Name, Teleporter.BuildInfo.Version, Teleporter.BuildInfo.Author)]
[assembly: MelonGame("Hinterland", "TheLongDark")]
