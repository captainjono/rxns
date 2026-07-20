# Parameters
param(
    [string]$ApiKey,
    [string]$Source = "https://api.nuget.org/v3/index.json",
    [string]$RootPath = ".",
    [string]$Clean = "false",
    [string]$Force="false"
)

# Validate required parameters
if (-not $ApiKey) {
    throw "ApiKey parameter is required"
}

# Find all .nupkg files in bin directories
Write-Host "Searching for NuGet packages..."
$nupkgFiles = Get-ChildItem -Path $RootPath -Filter "Rxns*.nupkg" -Recurse | 
    Where-Object { $_.DirectoryName -like "*bin*" }

if (-not $nupkgFiles) {
    Write-Warning "No NuGet packages found in bin directories"
    exit
}


if($Clean -ne 'false') {
    foreach ($package in $nupkgFiles) {
        Write-Host "Deleting $($package.FullName)..."
        Remove-Item $package.FullName -force
    }

    Write-Host "Cleaned packages";
    return
}


foreach ($package in $nupkgFiles) {
    Write-Host "Found $($package.FullName)..."

}

if$($Force -ne "true") { 
    $response = Read-Host "Are you sure you want to release all thee packages? Press 'y' to continue"
    if ($response -eq 'y') {
        # Your code here
        Write-Host "Continuing..."
    } else {
        Write-Host "Operation cancelled"
        return;
    }
}


return;
foreach ($package in $nupkgFiles) {
    Write-Host "Uploading $($package.FullName)..."
    
    try {
        # Construct the dotnet nuget push command
        $commandArgs = @(
            "nuget", "push",
            $package.FullName,
            "--api-key", $ApiKey,
            "--source", $Source
        )
        
        # Execute the command
        $process = Start-Process "dotnet" -ArgumentList $commandArgs -Wait -PassThru -NoNewWindow
        
        # Check the exit code
        if ($process.ExitCode -eq 0) {
            Write-Host "Successfully uploaded $($package.Name)" -ForegroundColor Green
        }
        else {
            Write-Warning "Failed to upload $($package.Name): Exit code $($process.ExitCode)"
        }
    }
    catch {
        Write-Warning "Error uploading $($package.Name): $_"
    }
}