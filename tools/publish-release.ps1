$ErrorActionPreference='Stop'
$repo='qq517899249-a11y/omg-ability-draft-overlay'
$tag='v0.5.0'
$asset=(Resolve-Path 'artifacts/OMG-AD-Friend-Package-20260919.zip').Path
$credentialText="protocol=https`nhost=github.com`n`n" | git credential fill
$credential=@{}
foreach($line in $credentialText){$parts=$line -split '=',2;if($parts.Count -eq 2){$credential[$parts[0]]=$parts[1]}}
if(-not $credential.password){throw 'GitHub credential is unavailable.'}
$headers=@{Authorization="Bearer $($credential.password)";Accept='application/vnd.github+json';'X-GitHub-Api-Version'='2022-11-28';'User-Agent'='OMG-AD-Release'}
try{$release=Invoke-RestMethod -Uri "https://api.github.com/repos/${repo}/releases/tags/$tag" -Headers $headers -Method Get}
catch{
  if($_.Exception.Response.StatusCode.value__ -ne 404){throw}
  $body=@{tag_name=$tag;target_commitish='main';name='OMG AD v0.5.0';body='Portable Windows build. Extract the complete ZIP and run OMG AD.exe. No separate .NET installation is required.';draft=$false;prerelease=$false}|ConvertTo-Json
  $release=Invoke-RestMethod -Uri "https://api.github.com/repos/${repo}/releases" -Headers $headers -Method Post -Body $body -ContentType 'application/json'
}
$name=[Uri]::EscapeDataString((Split-Path $asset -Leaf))
$existing=$release.assets|Where-Object name -eq (Split-Path $asset -Leaf)
if($existing){Invoke-RestMethod -Uri "https://api.github.com/repos/${repo}/releases/assets/$($existing.id)" -Headers $headers -Method Delete|Out-Null}
$uploadHeaders=$headers.Clone();$uploadHeaders.Accept='application/vnd.github+json'
$uploaded=Invoke-RestMethod -Uri "https://uploads.github.com/repos/${repo}/releases/$($release.id)/assets?name=$name" -Headers $uploadHeaders -Method Post -InFile $asset -ContentType 'application/zip'
[pscustomobject]@{Release=$release.html_url;Asset=$uploaded.browser_download_url;Size=$uploaded.size;State=$uploaded.state}
