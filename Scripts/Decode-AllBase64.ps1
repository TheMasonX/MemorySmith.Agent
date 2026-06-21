# Define the target directory and file extension filter
$TargetFolder = "D:\@Repos\MemorySmith.Agent"
$ExtensionFilter = "*.md" # Change to "*.txt" or "*.cs" if needed

# Regex to validate if the string is valid Base64 format
$Base64Regex = '^[A-Za-z0-9+/]*={0,2}$'

# Get all matching files in the folder (including subfolders)
$Files = Get-ChildItem -Path $TargetFolder -Filter $ExtensionFilter -File -Recurse

foreach ($File in $Files) {
    try {
        # Read file contents and strip out whitespace/newlines
        $Content = (Get-Content -Path $File.FullName -Raw).Trim() -replace "`r|`n|\s", ""
        
        $Valid = $false
        # Validate that content length is a multiple of 4 and matches Base64 structure
        if (($Content.Length % 4 -eq 0) -and ($Content.Length -gt 0) -and ($Content -match $Base64Regex)) {
            try {
                $DecodedBytes = [Convert]::FromBase64String($Content)
                $reencoded = [Convert]::ToBase64String($DecodedBytes)
                $Valid = ($reencoded -eq $Content)
            }
            catch {
                $Valid = $false
            }
        }
        if ($Valid -and $DecodedBytes.Length -gt 0) {
            Write-Host "Decoding file: $($File.FullName)" -ForegroundColor Cyan
            
            # Convert Base64 string back into raw bytes
            $NewPath = $File.FullName
            
            # Make a backup
            Copy-Item $File.FullName "$($File.FullName).bak"
            
            # Write the bytes back to the file
            [System.IO.File]::WriteAllBytes($NewPath, $DecodedBytes)
            
            # If the filename changed (extension removed), delete the old .b64 file
            if ($NewPath -ne $File.FullName) {
                Remove-Item -Path $File.FullName -Force
            }
            
            Write-Host "Successfully decoded: $NewPath" -ForegroundColor Green
        } else {
            Write-Host "Skipped (Not valid Base64): $($File.FullName)" -ForegroundColor Yellow
        }
    }
    catch {
        Write-Error "Failed to process file $($File.FullName): $_"
    }
}
