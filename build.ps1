param([Parameter(Mandatory)][string]$GameDir, [string]$CoreDir = "$GameDir/BepInEx/core")
$ErrorActionPreference = 'Stop'
$managed = Join-Path $GameDir 'REPO_Data/Managed'
$refs = @('mscorlib','System','System.Core','netstandard','Assembly-CSharp','PhotonUnityNetworking','PhotonRealtime','UnityEngine','UnityEngine.CoreModule','UnityEngine.IMGUIModule','UnityEngine.InputLegacyModule','UnityEngine.PhysicsModule','UnityEngine.UI','UnityEngine.TextRenderingModule') | ForEach-Object { "/reference:$managed\$_.dll" }
$refs += @('BepInEx','0Harmony') | ForEach-Object { "/reference:$CoreDir\$_.dll" }
New-Item "$PSScriptRoot\build" -ItemType Directory -Force | Out-Null
& "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /noconfig /nostdlib /target:library /optimize+ "/out:$PSScriptRoot\build/REPOrt.dll" @refs "$PSScriptRoot\REPOrt.cs"
exit $LASTEXITCODE
