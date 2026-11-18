$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$build = Join-Path $root ('.msi-build-' + [Guid]::NewGuid().ToString('N'))
$exe = Join-Path $root 'KeyDebounce.exe'
$icon = Join-Path $root 'KeyDebounce.ico'
$msi = Join-Path $build 'KeyDebounceSetup.msi'
$cab = Join-Path $build 'App.cab'
$productCode = '{0B823DBB-46A2-4693-95A8-F3E74FDEFD2B}'
$upgradeCode = '{63AF8D98-8BD0-4DEA-B557-34FB4B48E287}'
$version = '1.2.0'
$packageCode = [Guid]::NewGuid().ToString('B').ToUpperInvariant()

if (-not ('KeyDebounceMsi' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class KeyDebounceMsi
{
    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    public static extern uint MsiOpenDatabase(string path, IntPtr mode, out uint database);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    public static extern uint MsiDatabaseImport(uint database, string folder, string file);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    public static extern uint MsiDatabaseOpenView(uint database, string query, out uint view);
    [DllImport("msi.dll")]
    public static extern uint MsiCreateRecord(uint fields);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    public static extern uint MsiRecordSetString(uint record, uint field, string value);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    public static extern uint MsiRecordSetStream(uint record, uint field, string file);
    [DllImport("msi.dll")]
    public static extern uint MsiViewExecute(uint view, uint record);
    [DllImport("msi.dll")]
    public static extern uint MsiDatabaseCommit(uint database);
    [DllImport("msi.dll")]
    public static extern uint MsiCloseHandle(uint handle);
}
'@
}

function Confirm-MsiResult([uint32]$Code, [string]$Operation) {
    if ($Code -ne 0) { throw "Windows Installer failed to $Operation (code $Code)." }
}

function Import-Table([string]$Name, [string[]]$Lines) {
    $filename = "$Name.idt"
    $path = Join-Path $build $filename
    ($Lines -replace '\|', "`t") | Set-Content -LiteralPath $path -Encoding ascii
    Confirm-MsiResult ([KeyDebounceMsi]::MsiDatabaseImport($database, $build, $filename)) "import $Name"
}

function Add-MsiBinary([string]$Query, [string]$Name, [string]$File) {
    $view = [uint32]0
    $record = [uint32]0
    try {
        Confirm-MsiResult ([KeyDebounceMsi]::MsiDatabaseOpenView($database, $Query, [ref]$view)) "open $Name"
        $record = [KeyDebounceMsi]::MsiCreateRecord(2)
        if ($record -eq 0) { throw "Could not allocate an MSI record for $Name." }
        Confirm-MsiResult ([KeyDebounceMsi]::MsiRecordSetString($record, 1, $Name)) "name $Name"
        Confirm-MsiResult ([KeyDebounceMsi]::MsiRecordSetStream($record, 2, $File)) "read $Name"
        Confirm-MsiResult ([KeyDebounceMsi]::MsiViewExecute($view, $record)) "embed $Name"
    }
    finally {
        foreach ($handle in @($record, $view)) {
            if ($handle -ne 0) { [void][KeyDebounceMsi]::MsiCloseHandle($handle) }
        }
    }
}

New-Item -Path $build -ItemType Directory | Out-Null
$database = [uint32]0
try {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    & $compiler /nologo /target:winexe /optimize+ /codepage:65001 /warnaserror+ "/out:$exe" "/win32icon:$icon" "/resource:$icon,KeyDebounce.Icon" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:Microsoft.CSharp.dll (Join-Path $root 'KeyDebounce.cs') (Join-Path $root 'WindowsInstallation.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Could not compile KeyDebounce.' }

    $ddf = Join-Path $build 'App.ddf'
    @('.OPTION EXPLICIT', '.Set Cabinet=on', '.Set Compress=on', '.Set CabinetNameTemplate=App.cab',
      ".Set DiskDirectoryTemplate=`"$build`"", ".Set InfFileName=`"$build\App.inf`"",
      ".Set RptFileName=`"$build\App.rpt`"", "`"$exe`" AppExe") |
        Set-Content -LiteralPath $ddf -Encoding ascii
    & makecab.exe /F $ddf | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $cab)) { throw 'Could not create the application cabinet.' }

    Confirm-MsiResult ([KeyDebounceMsi]::MsiOpenDatabase($msi, [IntPtr]3, [ref]$database)) 'create the package database'
    $fileSize = (Get-Item -LiteralPath $exe).Length

    Import-Table Property @(
        'Property|Value', 's72|l0', 'Property|Property', "ProductCode|$productCode",
        'ProductName|KeyDebounce', "ProductVersion|$version", 'ProductLanguage|1033',
        'Manufacturer|KeyDebounce', "UpgradeCode|$upgradeCode", 'INSTALLLEVEL|1',
        'AUTOSTART|1', 'ARPPRODUCTICON|KeyDebounce.ico', 'SecureCustomProperties|AUTOSTART;OLDPRODUCTS;NEWERPRODUCTS')
    Import-Table Icon @('Name|Data', 's72|v0', 'Icon|Name')
    Import-Table Upgrade @(
        'UpgradeCode|VersionMin|VersionMax|Language|Attributes|Remove|ActionProperty',
        's38|S20|S20|S255|i4|S255|s72', 'Upgrade|UpgradeCode|VersionMin|VersionMax|Language|Attributes',
        "$upgradeCode||$version||0||OLDPRODUCTS", "$upgradeCode|$version|||2||NEWERPRODUCTS")
    Import-Table LaunchCondition @(
        'Condition|Description', 's255|l255', 'LaunchCondition|Condition',
        'Installed OR NOT NEWERPRODUCTS|A newer version of KeyDebounce is already installed.')
    Import-Table Directory @(
        'Directory|Directory_Parent|DefaultDir', 's72|S72|l255', 'Directory|Directory',
        'TARGETDIR||SourceDir', 'LocalAppDataFolder|TARGETDIR|.', 'LocalPrograms|LocalAppDataFolder|Programs',
        'INSTALLDIR|LocalPrograms|KeyDebounce', 'ProgramMenuFolder|TARGETDIR|.', 'StartupFolder|TARGETDIR|.')
    Import-Table Feature @(
        'Feature|Feature_Parent|Title|Description|Display|Level|Directory_|Attributes',
        's38|S38|L64|L255|I2|i2|S72|i2', 'Feature|Feature', 'Main||KeyDebounce||1|1|INSTALLDIR|0')
    Import-Table Component @(
        'Component|ComponentId|Directory_|Attributes|Condition|KeyPath', 's72|S38|s72|i2|S255|S72', 'Component|Component',
        'DesktopApp|{804C5E81-3F97-4F71-B69B-87D82F1702EB}|INSTALLDIR|4||AppMarker',
        'SignInLink|{B9904EC3-F880-4D2D-9E3F-92DCF70600AA}|StartupFolder|4|AUTOSTART = "1"|SignInMarker')
    Import-Table FeatureComponents @(
        'Feature_|Component_', 's38|s72', 'FeatureComponents|Feature_|Component_', 'Main|DesktopApp', 'Main|SignInLink')
    Import-Table Registry @(
        'Registry|Root|Key|Name|Value|Component_', 's72|i2|l255|L255|L0|s72', 'Registry|Registry',
        'AppMarker|1|Software\KeyDebounce\Installer|Installed|#1|DesktopApp',
        'SignInMarker|1|Software\KeyDebounce\Installer|SignInLink|#1|SignInLink')
    Import-Table File @(
        'File|Component_|FileName|FileSize|Version|Language|Attributes|Sequence',
        's72|s72|l255|i4|S72|S20|I2|i2', 'File|File', "AppExe|DesktopApp|KeyDebounce.exe|$fileSize|$version.0||512|1")
    Import-Table Media @(
        'DiskId|LastSequence|DiskPrompt|Cabinet|VolumeLabel|Source', 'i2|i4|L64|S255|S32|S72', 'Media|DiskId', '1|1||#App.cab||')
    Import-Table Shortcut @(
        'Shortcut|Directory_|Name|Component_|Target|Arguments|Description|Hotkey|Icon_|IconIndex|ShowCmd|WkDir',
        's72|s72|l128|s72|s72|S255|L255|I2|S72|I2|I2|S72', 'Shortcut|Shortcut',
        'StartMenu|ProgramMenuFolder|KeyDebounce|DesktopApp|[#AppExe]|--settings|Open KeyDebounce settings|||||INSTALLDIR',
        'SignIn|StartupFolder|KeyDebounce|SignInLink|[#AppExe]|--startup|Start keyboard filtering at sign-in|||||INSTALLDIR')
    # Also remove the link if it was created from the application after installation.
    Import-Table RemoveFile @(
        'FileKey|Component_|FileName|DirProperty|InstallMode', 's72|s72|L255|s72|i2', 'RemoveFile|FileKey',
        'AppSignInLink|DesktopApp|KeyDebounce.lnk|StartupFolder|2')
    Import-Table Dialog @(
        'Dialog|HCentering|VCentering|Width|Height|Attributes|Title|Control_First|Control_Default|Control_Cancel',
        's72|i2|i2|i2|i2|I4|L128|s72|S72|S72', 'Dialog|Dialog',
        'ConfirmInstall|50|50|330|135|3|KeyDebounce Setup|Startup|Install|Cancel',
        'ConfirmRemove|50|50|330|119|3|KeyDebounce Setup|Remove|Cancel|Cancel',
        'Completed|50|50|330|105|3|KeyDebounce Setup|Finish|Finish|Finish')
    Import-Table Control @(
        'Dialog_|Control|Type|X|Y|Width|Height|Attributes|Property|Text|Control_Next|Help',
        's72|s50|s20|i2|i2|i2|i2|I4|S50|L0|S50|L50', 'Control|Dialog_|Control',
        'ConfirmInstall|Heading|Text|16|15|294|14|3||Install or upgrade KeyDebounce||',
        'ConfirmInstall|Body|Text|16|35|294|28|3||Click Install to continue for this Windows account. Your saved filter settings are kept.||',
        'ConfirmInstall|Startup|CheckBox|16|73|292|15|3|AUTOSTART|Start KeyDebounce when I sign in to Windows|Install|',
        'ConfirmInstall|Install|PushButton|170|106|70|18|3||&Install|Cancel|',
        'ConfirmInstall|Cancel|PushButton|247|106|70|18|3||Cancel|Startup|',
        'ConfirmRemove|Heading|Text|16|15|294|14|3||KeyDebounce is already installed.||',
        'ConfirmRemove|Body|Text|16|35|294|28|3||Click Remove to uninstall KeyDebounce and remove its sign-in shortcut. Your filter settings are kept.||',
        'ConfirmRemove|Remove|PushButton|170|89|70|18|3||&Remove|Cancel|',
        'ConfirmRemove|Cancel|PushButton|247|89|70|18|3||Cancel|Remove|',
        'Completed|Body|Text|16|26|294|28|3||KeyDebounce Setup completed successfully.||',
        'Completed|Finish|PushButton|247|75|70|18|3||&Finish|Finish|')
    Import-Table CheckBox @('Property|Value', 's72|L64', 'CheckBox|Property', 'AUTOSTART|1')
    Import-Table ControlEvent @(
        'Dialog_|Control_|Event|Argument|Condition|Ordering',
        's72|s50|s50|l255|S255|I2', 'ControlEvent|Dialog_|Control_|Event|Argument|Condition',
        'ConfirmInstall|Install|[AUTOSTART]|0|NOT AUTOSTART|1',
        'ConfirmInstall|Install|EndDialog|Return|1|2', 'ConfirmInstall|Cancel|EndDialog|Exit|1|1',
        'ConfirmRemove|Remove|[REMOVE]|ALL|1|1', 'ConfirmRemove|Remove|EndDialog|Return|1|2',
        'ConfirmRemove|Cancel|EndDialog|Exit|1|1', 'Completed|Finish|EndDialog|Return|1|1')
    Import-Table InstallUISequence @(
        'Action|Condition|Sequence', 's72|S255|I2', 'InstallUISequence|Action',
        'FindRelatedProducts||25', 'LaunchConditions||100',
        'CostInitialize||800', 'FileCost||900', 'CostFinalize||1000',
        'ConfirmInstall|UILevel >= 5 AND NOT Installed|1230',
        'ConfirmRemove|UILevel >= 5 AND Installed AND NOT REMOVE|1235',
        'ExecuteAction||1300', 'Completed|UILevel >= 5|-1')
    Import-Table InstallExecuteSequence @(
        'Action|Condition|Sequence', 's72|S255|I2', 'InstallExecuteSequence|Action',
        'FindRelatedProducts||25', 'LaunchConditions||100', 'CostInitialize||800',
        'FileCost||900', 'CostFinalize||1000', 'InstallValidate||1400', 'InstallInitialize||1500',
        'RemoveExistingProducts||1510',
        'ProcessComponents||1600', 'UnpublishFeatures||1800', 'RemoveRegistryValues||2600',
        'RemoveShortcuts||3200', 'RemoveFiles||3500', 'RemoveFolders||3600', 'InstallFiles||4000',
        'CreateShortcuts||4500', 'WriteRegistryValues||5000', 'RegisterUser||6000', 'RegisterProduct||6100',
        'PublishFeatures||6300', 'PublishProduct||6400', 'InstallFinalize||6600')
    Import-Table _SummaryInformation @(
        'PropertyId|Value', 'i2|l255', '_SummaryInformation|PropertyId', '1|1252', '2|Installation Database',
        '3|KeyDebounce', '4|KeyDebounce', '5|Installer,MSI,Database', '6|Per-user keyboard debounce',
        '7|Intel;1033', "9|$packageCode", '14|500', '15|10', '18|Windows Installer', '19|2')

    Add-MsiBinary 'INSERT INTO `_Streams` (`Name`, `Data`) VALUES (?, ?)' 'App.cab' $cab
    Add-MsiBinary 'INSERT INTO `Icon` (`Name`, `Data`) VALUES (?, ?)' 'KeyDebounce.ico' $icon
    Confirm-MsiResult ([KeyDebounceMsi]::MsiDatabaseCommit($database)) 'save the package'
    [void][KeyDebounceMsi]::MsiCloseHandle($database)
    $database = 0
    $output = Join-Path $root 'KeyDebounceSetup.msi'
    [IO.File]::Copy($msi, $output, $true)
    Get-Item -LiteralPath $output
}
finally {
    if ($database -ne 0) { [void][KeyDebounceMsi]::MsiCloseHandle($database) }
    Get-ChildItem -LiteralPath $build -File | Remove-Item
    Remove-Item -LiteralPath $build
}
