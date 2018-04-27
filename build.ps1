$env:VersionDateSuffix = [System.DateTime]::Now.ToString("yyyyMMddHHmmss");

$VisualStudioVersion = "15.0";
$DotnetSDKVersion = "2.1.4";

# Get dotnet paths
$MSBuildExtensionsPath = "C:\Program Files\dotnet\sdk\" + $DotnetSDKVersion;
$MSBuildSDKsPath = $MSBuildExtensionsPath + "\SDKs";

# Get Visual Studio install path
$VSINSTALLDIR =  $(Get-ItemProperty "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\VisualStudio\SxS\VS7").$VisualStudioVersion;

# Add Visual Studio environment variables
$env:VisualStudioVersion = $VisualStudioVersion;
$env:VSINSTALLDIR = $VSINSTALLDIR;

# Add dotnet environment variables
$env:MSBuildExtensionsPath = $MSBuildExtensionsPath;
$env:MSBuildSDKsPath = $MSBuildSDKsPath;

dotnet build -bl:Build.binlog;
if ($LASTEXITCODE -eq 0) {
   dotnet pack -bl:Pack.binlog;
}