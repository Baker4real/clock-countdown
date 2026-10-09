#requires -Version 7.0

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:packageTrustRoots = @(
    'Deploy-OfficeCountdown.ps1',
    'Install-Countdown.ps1',
    'Countdown.ReleaseContract.psm1'
)

$script:buildAssetDefinitions = @(
    [pscustomobject]@{
        SourcePath = 'sanrio-official-mymelody.png'
        PublishedPath = 'app\assets\sanrio-official-mymelody.png'
        SHA256 = '6C53BD64E8F601C0709C69DAEE863F282EAA763AFFA2F4F96CE77B5AA2B0B41C'
        Category = 'OfficialCharacter'
    },
    [pscustomobject]@{
        SourcePath = 'sanrio-official-cinnamoroll.png'
        PublishedPath = 'app\assets\sanrio-official-cinnamoroll.png'
        SHA256 = '9344D335E7591C8599E7CC32730B315401037D58E15C14DCF23853DE74867520'
        Category = 'OfficialCharacter'
    },
    [pscustomobject]@{
        SourcePath = 'sanrio-official-kuromi.png'
        PublishedPath = 'app\assets\sanrio-official-kuromi.png'
        SHA256 = '3DC52AAC57897374436C3394C434DE26090482A8C11E6BDF4BD7F3DCE581D4D0'
        Category = 'OfficialCharacter'
    },
    [pscustomobject]@{
        SourcePath = 'assets\floating-plush\floating-plush-mymelody-reference.png'
        PublishedPath = 'app\assets\floating-plush\floating-plush-mymelody-reference.png'
        SHA256 = '54F7C2EB62D48783D5977CF68AA795757EC891BE96298351D88784866ED98B07'
        Category = 'CurrentFloating'
    },
    [pscustomobject]@{
        SourcePath = 'assets\floating-plush\floating-plush-cinnamoroll-reference.png'
        PublishedPath = 'app\assets\floating-plush\floating-plush-cinnamoroll-reference.png'
        SHA256 = '9B4E007395F1AF3B20E2EC492C22C8A4FB1F205473A2AD16AFD71F457B44E6B9'
        Category = 'CurrentFloating'
    },
    [pscustomobject]@{
        SourcePath = 'assets\floating-plush\floating-plush-kuromi-reference.png'
        PublishedPath = 'app\assets\floating-plush\floating-plush-kuromi-reference.png'
        SHA256 = '52D0814F2A764EE1262F8919FE70BD535CC14BAFC399295532AAEEFA352384DC'
        Category = 'CurrentFloating'
    },
    [pscustomobject]@{
        SourcePath = 'assets\floating-plush\component-day-outer-shell.png'
        PublishedPath = 'app\assets\floating-plush\component-day-outer-shell.png'
        SHA256 = 'A4F2F78163F8AA5217C4161F0EE2BFB924686E8BED5768C527F3B5C7D6CC4924'
        Category = 'CurrentFloating'
    },
    [pscustomobject]@{
        SourcePath = 'assets\floating-plush\component-day-header-blank.png'
        PublishedPath = 'app\assets\floating-plush\component-day-header-blank.png'
        SHA256 = '857B570F1CD47C29F1C2744251DE1B1C7B4D9D921944BB79E974B0B60B26C1B1'
        Category = 'CurrentFloating'
    },
    [pscustomobject]@{
        SourcePath = 'assets\floating-plush\component-day-card-blank.png'
        PublishedPath = 'app\assets\floating-plush\component-day-card-blank.png'
        SHA256 = '253EF68502B1F798E37DDF02BF17F9BEE08D818B8AEE76EA287A4403226FB1C8'
        Category = 'CurrentFloating'
    },
    [pscustomobject]@{
        SourcePath = 'assets\floating-plush\component-day-mascots.png'
        PublishedPath = 'app\assets\floating-plush\component-day-mascots.png'
        SHA256 = 'D000285BFFC2CBCF3411C11407FF691E531A8602E9D3E8F3FD68EBFC4ADC0726'
        Category = 'CurrentFloating'
    },
    [pscustomobject]@{
        SourcePath = 'assets\main-decorations\main-countdown-scene-day.png'
        PublishedPath = 'app\assets\main-decorations\main-countdown-scene-day.png'
        SHA256 = '8165DA49869387FC60351F5961EA4B332EE8C961D17A1D1556B4E935BB5DD9CE'
        Category = 'MainDecoration'
    },
    [pscustomobject]@{
        SourcePath = 'assets\main-decorations\main-notes-scene-day.png'
        PublishedPath = 'app\assets\main-decorations\main-notes-scene-day.png'
        SHA256 = '69634D271CC16E585342BCD66231D3DCDBC50F129DEE658C9EDBA54C0AC6FC64'
        Category = 'MainDecoration'
    }
)

$script:legacyFloatingRegressionAssets = @(
    'assets\floating-plush\floating-plush-mymelody-front.png',
    'assets\floating-plush\floating-plush-mymelody-pose.png',
    'assets\floating-plush\floating-plush-cinnamoroll-front.png',
    'assets\floating-plush\floating-plush-cinnamoroll-pose.png',
    'assets\floating-plush\floating-plush-kuromi-front.png',
    'assets\floating-plush\floating-plush-kuromi-pose.png'
)

$script:floatingPlushAssets = @(
    'assets\floating-plush\floating-plush-mymelody-front.png',
    'assets\floating-plush\floating-plush-mymelody-pose.png',
    'assets\floating-plush\floating-plush-mymelody-reference.png',
    'assets\floating-plush\floating-plush-cinnamoroll-front.png',
    'assets\floating-plush\floating-plush-cinnamoroll-pose.png',
    'assets\floating-plush\floating-plush-cinnamoroll-reference.png',
    'assets\floating-plush\floating-plush-kuromi-front.png',
    'assets\floating-plush\floating-plush-kuromi-pose.png',
    'assets\floating-plush\floating-plush-kuromi-reference.png',
    'assets\floating-plush\component-day-outer-shell.png',
    'assets\floating-plush\component-day-header-blank.png',
    'assets\floating-plush\component-day-card-blank.png',
    'assets\floating-plush\component-day-mascots.png'
)

$script:sourceBuildNonAssetFiles = @(
    'global.json',
    'src\MinminCountdown.Core\CalendarOccurrenceCalculator.cs',
    'src\MinminCountdown.Core\MinminCountdown.Core.csproj',
    'src\MinminCountdown.Core\SettingsContract.cs',
    'src\MinminCountdown.Core\SettingsFileOperations.cs',
    'src\MinminCountdown.Core\SettingsModels.cs',
    'src\MinminCountdown.Core\SettingsRecovery.cs',
    'src\MinminCountdown.Core\SettingsStore.cs',
    'src\MinminCountdown.Core\SettingsSynchronization.cs',
    'src\MinminCountdown\App.xaml',
    'src\MinminCountdown\App.xaml.cs',
    'src\MinminCountdown\ApplicationSession.cs',
    'src\MinminCountdown\ApplicationTheme.cs',
    'src\MinminCountdown\ApplicationValidation.cs',
    'src\MinminCountdown\DesktopWorkspace.cs',
    'src\MinminCountdown\DiagnosticLogger.cs',
    'src\MinminCountdown\DomainLogic.cs',
    'src\MinminCountdown\FloatingBottomStatus.cs',
    'src\MinminCountdown\FloatingTrayController.cs',
    'src\MinminCountdown\FloatingWindow.Countdowns.cs',
    'src\MinminCountdown\FloatingWindow.Notes.cs',
    'src\MinminCountdown\FloatingWindow.xaml',
    'src\MinminCountdown\FloatingWindow.xaml.cs',
    'src\MinminCountdown\GlobalUsings.cs',
    'src\MinminCountdown\HolidayCalendar.cs',
    'src\MinminCountdown\HostOptions.cs',
    'src\MinminCountdown\InstanceActivation.cs',
    'src\MinminCountdown\MainWindow.xaml',
    'src\MinminCountdown\MainWindow.xaml.cs',
    'src\MinminCountdown\MinminCountdown.csproj',
    'src\MinminCountdown\ReminderEditing.cs',
    'src\MinminCountdown\ReminderRulesEditorWindow.cs',
    'src\MinminCountdown\ReminderWindow.cs',
    'src\MinminCountdown\RuntimeProtocol.cs',
    'src\MinminCountdown\SettingsRecoveryWindow.cs',
    'src\MinminCountdown\TrayIconHost.cs',
    'src\MinminCountdown\UiControls.cs',
    'src\MinminCountdown\ValidationExercises.cs',
    'src\MinminCountdown\ValidationReport.cs',
    'src\MinminCountdown\ValidationReportContract.cs',
    'src\MinminCountdown\VisualReferenceBaseline.cs',
    'src\MinminCountdown\WindowValidationScenarios.cs'
)

$script:sourceBuildFiles = @($script:sourceBuildNonAssetFiles) + @('countdown-icon.ico') + @(
    $script:buildAssetDefinitions | ForEach-Object { [string] $_.SourcePath }
)

$script:nonAssetHashes = [ordered]@{
    'Build-CountdownApplication.ps1' = '946146E9EED417780D345FDB494A3D45CF2BB9EEC25750A462ACA252F421EE2F'
    'Countdown.App.psm1' = '604681FC3CF7D32B4D6DC45FC98A4B3FBF1BFBD4FFB958A5CE616F3FB716705B'
    'Countdown.Core.psm1' = 'E1A639B06D404F7F4638CAB4DE6392FB27E5C324D77A2AC44D1B226C8375410B'
    'Countdown.InstallTransaction.psm1' = '5A9FAAE019095134D53586886A83581F3794654B9790102A17751860B157B08E'
    'Countdown.RemoteDeployment.psm1' = '0340A4F41F64BFB1A3BCB7A554FFC042330E0EC8040FF9C47A7ECADE34BE34FC'
    'Countdown.TransactionAcceptance.psm1' = 'EC8D18BDBF438D9050A842A3DE7BCFA79EB58F4E9433D856F2062F83882FFE93'
    'Countdown.TransferLedger.psm1' = 'B3051D0F5AF1F7E0351A9637B076E0402DCC9500A21CC41C02594EC9012D88B8'
    'Countdown.Validation.psm1' = 'EDF5F93CC66485C94EC8F3E5E99A4CDF527F2D86C9A280B7B9FDF16A8E1DF03D'
    'Countdown.Notes.psm1' = 'CF41E5A29B5FCFB0807E1EA1E6F52AC7FADE4478A18990698F2BA6F725E08CA0'
    'Countdown.Runtime.psm1' = '6EAD9634FAB49CED2C588D74DC62541FEC6852F0DE694EBC53147FD967CD3A88'
    'Countdown.Settings.psm1' = '507BB437A209CC6F340B00D7089171C6D78EFF6C6EF597A87F49E45BF40E95DC'
    'Countdown.Tags.psm1' = '57AE8BBE689F3838E3B9EE8B56F96D661C8A46EAD825103307482B00C472FAAC'
    'Countdown.Core.Tests.ps1' = '67633D6AEC9589A4E474B1FC00B30FE4EEA1F6F9B6A9D774C0DE5F21683D747E'
    'Countdown.Ui.Tests.ps1' = '661E0397B3C9CD69F5FF23E42D9238BCC09D662554272D8611A1CB5E1C6CB49C'
    'Countdown.CSharp.Tests.ps1' = '03B12A017E8286E5AB8C8C9F61F415E59A5C7B9F658E18C5B745B1D8152B189E'
    'tools\Build-MainDayOrnamentScene.ps1' = '1C9BFC52DBDE1F6EE076F3EB84A7484FA9A48EFCE7297B57E1D4DF52CA199036'
    'tools\MainDecorationSceneBands.psd1' = '9DAEDF905F2FD6A0E8396E8718A06B115566EA4A8799354347E3A30D2E41C6B7'
    'tools\Test-MainDayOrnamentScene.ps1' = '2291AA01AB03161F8500B98E68335F55BD2946795EBE0341687480179563BF81'
    'tools\Start-IsolatedSuiteProcess.ps1' = '1B71B2E87B2D65365F647441A793779A4005D76C020E39033C55E0AF1CB0B805'
    'tools\Countdown.SuiteIsolation.psm1' = '79294F3DF69A252E7050F984608E863370A0978C3EB5FA3811A93AA65CED50CE'
    '敏敏子倒计时.ps1' = '3A4C018DEE2C1453D01BA4C837EBB4E207C1422D0301CE2973757CD4670B3106'
    'CountdownLauncher.exe' = '4B4C66274278049FF185C7824685D1B5A85F5DA10557F70A1D7E458778066440'
    'CountdownLauncher.cs' = 'FF4602D8C63A42C1237855B7CF787F47A8F09CEA4F49D9006F777A367BBFDC42'
    'countdown-icon.ico' = 'C5B7CB0A09524AEFE4488291078F61B0AF54958582DA98B99B25D0F4DC6976C5'
    'assets\floating-plush\SOURCES.md' = '7B6A95A1EADEA0ED9A9FD4C44128BBE2F05F9D2C2675AF37998133C6228F5846'
    'assets\floating-plush\floating-plush-mymelody-front.png' = '9D078C528CB6581222D292EA7E13AF42A77DC60C793C9A8B90AE163B30D27A4D'
    'assets\floating-plush\floating-plush-mymelody-pose.png' = 'FC8EE48368C6BAEBF45CE3250BD93EB0A43760A03089BFBECCCE2295AF9D5333'
    'assets\floating-plush\floating-plush-cinnamoroll-front.png' = 'DA9BD4C55AD11CAAADD68A8B153D104A4F170B453175BB67DB7AE99C5CFDAD9B'
    'assets\floating-plush\floating-plush-cinnamoroll-pose.png' = '5FABCFB1420B6D3162DED0BC0C0FFC2A97ACCE24C3B214A1708E652CDB62D107'
    'assets\floating-plush\floating-plush-kuromi-front.png' = '65A0ACB6B90BCFB2E4409840B1AD5A0844951951A1BBE9050330644592DFD8BD'
    'assets\floating-plush\floating-plush-kuromi-pose.png' = '92A4493D227A3EA60E5BC23D12CF6AC7F8E5CEBED4F3FDA0328DE6C8B5158F93'
    'global.json' = 'B132D105648C7DBE49F2C391FE0B9898027E470DDC7CFD4D7239AF5DF63401FA'
    'src\MinminCountdown.Core\CalendarOccurrenceCalculator.cs' = 'DCDC8D652A32A01914EBCBA9E9FCA55762AC9CCC8DF6BC5968D9F8168845FF66'
    'src\MinminCountdown.Core\MinminCountdown.Core.csproj' = 'D8CB86FE56DC5E8AB7C61BCBB920969667AA80EBD65E88C33B1590B61338390B'
    'src\MinminCountdown.Core\SettingsContract.cs' = '2CB0870BD0A9D8CF55621CE181C94B64A8267926DA2A49090D326BD41DAF5779'
    'src\MinminCountdown.Core\SettingsFileOperations.cs' = '72163E13D99CF7FB09C98A0F2FE373C6B76A21F6A00FBF2FF67CB1E40A185A22'
    'src\MinminCountdown.Core\SettingsModels.cs' = '29E4FD8BAF07F56105213BD50B050F66F21062682466E0218CCB33079233FCEF'
    'src\MinminCountdown.Core\SettingsRecovery.cs' = 'C955F13308523F1B49E017341373EE7E7687FF1E192C8148C4AE541E0FB6D5CB'
    'src\MinminCountdown.Core\SettingsStore.cs' = 'F023505ECCB9AC54305A74837B303CA2E6DFA0A45F1017B27F014D4E7FCC99B7'
    'src\MinminCountdown.Core\SettingsSynchronization.cs' = '0B313C7A4236ACCE0DB1E3EAF464DC10C22C15E4A5EC5357389B9BDA6510B003'
    'src\MinminCountdown\App.xaml' = '17C8553208FE1A13A741F7F7B587630BBF0284E4B7DB7522658712AE2C432067'
    'src\MinminCountdown\App.xaml.cs' = '3895FAFA593C235C72A6B9BBE7D5021F62C14A5607F2C5B754C14CEC519E9FD8'
    'src\MinminCountdown\ApplicationSession.cs' = 'D87A95A65905C7A05533AFB35F44DCAF7AC5F0A12B2B7260BBD299907933BDC4'
    'src\MinminCountdown\ApplicationTheme.cs' = 'FFEBF84183D5C2237AF02F8E09862A5E08012D6534E599ABD16A5072BCDB5E3A'
    'src\MinminCountdown\ApplicationValidation.cs' = '1C6B7DE097ADD47EC6385433D7DBFCBDDDD8B3AF24E2D2C6AE8FEE8E0E418B78'
    'src\MinminCountdown\DesktopWorkspace.cs' = 'BDF3410B3944CC5A59F3DC4E87C5F9B2B8D34F1896E22529DBEC3B2A4F5A18BC'
    'src\MinminCountdown\DiagnosticLogger.cs' = '234EB941A3F13FF23DB6D58B96A1783FD7C9D7A23BA2B87EAAC0D0CE4762A241'
    'src\MinminCountdown\DomainLogic.cs' = 'FBFB34378DF7468B8FF911038268BCEEAE8F64D975A5D229A72282BC95DE8248'
    'src\MinminCountdown\FloatingBottomStatus.cs' = 'BDDB919E3A0C2DAC13F45B113652938E2BBB72F2C6F31189A4B4DA169A8ECF05'
    'src\MinminCountdown\FloatingTrayController.cs' = 'C0905CDE21B666B9412EF822C15A6C567E5C2401CB7F0C48D454219BA8795326'
    'src\MinminCountdown\FloatingWindow.Countdowns.cs' = 'CAEEC60CD11B3B25D2E93CCD5519A52CE434E448B1CB8E907E6E57603DE8A85F'
    'src\MinminCountdown\FloatingWindow.Notes.cs' = '7133D7A578BB1B2FBC489900DAFC24624453DC33E838921991B9444465AAC93D'
    'src\MinminCountdown\FloatingWindow.xaml' = 'F94693033B6B59197D5DFEF7967E0684611E9855F2748B4F7ABF6F048A506479'
    'src\MinminCountdown\FloatingWindow.xaml.cs' = '0AFD015011A30DE471EEE1DEB31D5D43C30BF6D1800555F94FFDCF46BF762004'
    'src\MinminCountdown\GlobalUsings.cs' = '9EB56B713F4BD450C169F140E74FF7B6D5E456F3742A30188B947223D01628E4'
    'src\MinminCountdown\HolidayCalendar.cs' = 'C70C998ADCA948015E8CCAAFF2D36ECCF273D470444774BF0E67E09F4E70BFF5'
    'src\MinminCountdown\HostOptions.cs' = '4C3BB5ED222319195A459C0BA4323A49EAB03D119250E926AFE988F25339A4F3'
    'src\MinminCountdown\InstanceActivation.cs' = '26A31C9A31EC7FF860E3102DBE23EFD840810CD31FDE0F232B91421B2F16D7CE'
    'src\MinminCountdown\MainWindow.xaml' = 'B96E3771C3066D588DA39C5E1482C2EDE9FB23EE157D03D1B08CE5CA9A9CFFC7'
    'src\MinminCountdown\MainWindow.xaml.cs' = '8785BF96DE7542F7B6EBAF74B997B185D1CE6E27086D98A5181699425C86C201'
    'src\MinminCountdown\MinminCountdown.csproj' = '5F8A813454B10FD6EE7A6401F7BE2F566C9C13D630715B36BE00C74B33A06E70'
    'src\MinminCountdown\ReminderEditing.cs' = '952F7935F4971A7BDFA145938525A7E7D14F8CE4305EA8ECEC52F4AA5E2FB138'
    'src\MinminCountdown\ReminderRulesEditorWindow.cs' = '6465D072A3848AD674E3F3CB5733754BC0A41F8ACB50D87B9892ED71F232DEB9'
    'src\MinminCountdown\ReminderWindow.cs' = '51DD8F0B6B0D74A3DFB2450367A00C131D91DE2C613C39C071D31A45BA8F8091'
    'src\MinminCountdown\RuntimeProtocol.cs' = '37E5585174890F4B748DE4394440CE9D674B675EEAD0775472E2222F0AAC9EE2'
    'src\MinminCountdown\SettingsRecoveryWindow.cs' = 'CD632E4F65173D771D1AC3A36DF5749F2A408F928EBF67FCF8E1FEC664672363'
    'src\MinminCountdown\TrayIconHost.cs' = 'E966A64CA77DD99BDF7BB81A3952654F9AE706420C1AEAB31D0BC9FEAFABFA3F'
    'src\MinminCountdown\UiControls.cs' = '5DF12A618124F949C05C6129F8EA10DD10E60FD3B0AB26930EBB7BD1F31B32FC'
    'src\MinminCountdown\ValidationExercises.cs' = '096262F7548833E22BD77626E7E5B06739B0CB4B69DC1C24CF3A5BA3ED1BD1F9'
    'src\MinminCountdown\ValidationReport.cs' = 'C7774A171BFF52D17DF394D11AC89482E5918C05210CEDE742D747715660B3CB'
    'src\MinminCountdown\ValidationReportContract.cs' = '5B8397FE6C020D59E8AD86576980D5718801C721647FC61CA30117CBF2F9CDE5'
    'src\MinminCountdown\VisualReferenceBaseline.cs' = 'DF0E749EA0D7234AFB6E850AF9F17B46BBB5EE0ACDE11083725FD2B08008FB61'
    'src\MinminCountdown\WindowValidationScenarios.cs' = '497FC54E9640D23FECEC3EDE4647F8B4FF6BDE9648576867183F814613E5AB73'
}

$script:nonAssetIntegrityPaths = @(
    'Build-CountdownApplication.ps1',
    'Countdown.App.psm1',
    'Countdown.Core.psm1',
    'Countdown.InstallTransaction.psm1',
    'Countdown.RemoteDeployment.psm1',
    'Countdown.TransactionAcceptance.psm1',
    'Countdown.TransferLedger.psm1',
    'Countdown.Validation.psm1',
    'Countdown.Notes.psm1',
    'Countdown.Runtime.psm1',
    'Countdown.Settings.psm1',
    'Countdown.Tags.psm1',
    'Countdown.Core.Tests.ps1',
    'Countdown.Ui.Tests.ps1',
    'Countdown.CSharp.Tests.ps1',
    'tools\Build-MainDayOrnamentScene.ps1',
    'tools\MainDecorationSceneBands.psd1',
    'tools\Test-MainDayOrnamentScene.ps1',
    'tools\Start-IsolatedSuiteProcess.ps1',
    'tools\Countdown.SuiteIsolation.psm1',
    '敏敏子倒计时.ps1',
    'CountdownLauncher.exe',
    'CountdownLauncher.cs',
    'countdown-icon.ico',
    'assets\floating-plush\SOURCES.md'
) + @($script:sourceBuildNonAssetFiles)

# 构建产物（如验证程序 exe）跨环境字节不可复现，不进静态哈希钉死；仍必须随发布包传输。
$script:buildOutputPackageFiles = @(
    'verification\MinminCountdown.Verification.exe'
)

function Assert-CountdownReleaseRelativePath {
    param([Parameter(Mandatory)] [string] $Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or
        [System.IO.Path]::IsPathRooted($Path) -or
        [System.Management.Automation.WildcardPattern]::ContainsWildcardCharacters($Path) -or
        $Path.Contains('/') -or
        $Path.IndexOfAny([System.IO.Path]::GetInvalidPathChars()) -ge 0) {
        throw "发布契约包含无效相对路径：$Path"
    }

    $segments = $Path.Split('\')
    $invalidFileNameChars = [System.IO.Path]::GetInvalidFileNameChars()
    if ($segments.Count -eq 0 -or @(
            $segments | Where-Object {
                $_ -in @('', '.', '..') -or
                $_.IndexOfAny($invalidFileNameChars) -ge 0 -or
                $_.EndsWith(' ') -or
                $_.EndsWith('.')
            }
        ).Count -ne 0) {
        throw "发布契约包含不安全路径片段：$Path"
    }
}

function Assert-CountdownUniqueReleasePaths {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string[]] $Paths
    )

    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $Paths) {
        Assert-CountdownReleaseRelativePath -Path $path
        if (-not $seen.Add($path)) {
            throw "发布契约的 $Name 包含重复路径：$path"
        }
    }
}

function Get-CountdownPublishedPayload {
    $manifestPath = Join-Path $PSScriptRoot 'app\published-payload.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        return [pscustomobject]@{
            AggregateSha256 = ''
            Files = @()
        }
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $files = @(
        foreach ($file in @($manifest.files)) {
            [pscustomobject]@{
                Path = [string] $file.path
                SHA256 = [string] $file.sha256
            }
        }
    )
    if ($files.Count -lt 2) {
        throw "发布载荷清单文件数无效：$($files.Count)"
    }

    [pscustomobject]@{
        AggregateSha256 = [string] $manifest.aggregateSha256
        Files = $files
    }
}

function Get-CountdownReleaseHashByPath {
    param([Parameter(Mandatory)] [string] $Path)

    if ($script:nonAssetHashes.Contains($Path)) {
        return [string] $script:nonAssetHashes[$Path]
    }

    foreach ($asset in $script:buildAssetDefinitions) {
        if ([string]::Equals($Path, [string] $asset.SourcePath, [System.StringComparison]::OrdinalIgnoreCase) -or
            [string]::Equals($Path, [string] $asset.PublishedPath, [System.StringComparison]::OrdinalIgnoreCase)) {
            return [string] $asset.SHA256
        }
    }

    foreach ($payloadFile in @(Get-CountdownPublishedPayload).Files) {
        if ([string]::Equals($Path, [string] $payloadFile.Path, [System.StringComparison]::OrdinalIgnoreCase)) {
            return [string] $payloadFile.SHA256
        }
    }

    if ([string]::Equals($Path, 'app\published-payload.json', [System.StringComparison]::OrdinalIgnoreCase)) {
        $manifestPath = Join-Path $PSScriptRoot 'app\published-payload.json'
        if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
            return [string] (Get-FileHash -Algorithm SHA256 -LiteralPath $manifestPath).Hash
        }
    }

    throw "发布契约缺少静态 SHA-256：$Path"
}

function New-CountdownReleaseIntegrityMap {
    $integrityPaths = @($script:nonAssetIntegrityPaths)
    $integrityPaths += @(
        $script:buildAssetDefinitions |
            Where-Object { $_.Category -eq 'OfficialCharacter' } |
            ForEach-Object { $_.SourcePath }
    )
    $integrityPaths += $script:floatingPlushAssets
    $integrityPaths += @(
        $script:buildAssetDefinitions |
            Where-Object { $_.Category -eq 'MainDecoration' } |
            ForEach-Object { $_.SourcePath }
    )
    $integrityPaths += @($script:buildAssetDefinitions | ForEach-Object { $_.PublishedPath })
    $publishedPayloadFiles = @(Get-CountdownPublishedPayload | ForEach-Object { $_.Files })
    $integrityPaths += @($publishedPayloadFiles | ForEach-Object { $_.Path })
    if ($publishedPayloadFiles.Count -gt 0) {
        # 载荷清单文件本身必须随包传输并受哈希保护，远端解包后才能重建同一份含载荷的契约。
        $integrityPaths += 'app\published-payload.json'
    }
    $uniqueIntegrity = [System.Collections.Generic.List[string]]::new()
    $seenIntegrity = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $integrityPaths) {
        if ($seenIntegrity.Add($path)) {
            $uniqueIntegrity.Add($path)
        }
    }
    $integrityPaths = @($uniqueIntegrity)
    Assert-CountdownUniqueReleasePaths -Name 'IntegrityHashes' -Paths $integrityPaths

    $hashes = [ordered]@{}
    foreach ($path in $integrityPaths) {
        $hash = Get-CountdownReleaseHashByPath -Path $path
        if ($hash -notmatch '^[A-F0-9]{64}$') {
            throw "发布契约包含无效 SHA-256：$path"
        }
        $hashes[$path] = $hash
    }
    $hashes
}

function New-CountdownReleaseContractSnapshot {
    $integrityHashes = New-CountdownReleaseIntegrityMap
    $officialCharacterAssets = @(
        $script:buildAssetDefinitions |
            Where-Object { $_.Category -eq 'OfficialCharacter' } |
            ForEach-Object { [string] $_.SourcePath }
    )
    $currentFloatingRuntimeAssets = @(
        $script:buildAssetDefinitions |
            Where-Object { $_.Category -eq 'CurrentFloating' } |
            ForEach-Object { [string] $_.PublishedPath }
    )
    $applicationAssets = @($script:buildAssetDefinitions | ForEach-Object { [string] $_.PublishedPath })
    $installedFiles = @(
        'Countdown.Core.psm1',
        'Countdown.InstallTransaction.psm1',
        'Countdown.Notes.psm1',
        'Countdown.Runtime.psm1',
        'Countdown.Settings.psm1',
        'Countdown.Tags.psm1',
        '敏敏子倒计时.ps1'
    ) + @(
        'CountdownLauncher.exe',
        'countdown-icon.ico'
    ) + $officialCharacterAssets + $applicationAssets + @(
        Get-CountdownPublishedPayload | ForEach-Object { $_.Files } | ForEach-Object { $_.Path }
    )
    $uniqueInstalled = [System.Collections.Generic.List[string]]::new()
    $seenInstalled = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $installedFiles) {
        if ($seenInstalled.Add($path)) {
            $uniqueInstalled.Add($path)
        }
    }
    $installedFiles = @($uniqueInstalled)
    $retiredRuntimeFiles = @('Countdown.App.psm1') + $script:floatingPlushAssets + @(
        'app\assets\floating-plush\floating-plush-mymelody-front.png',
        'app\assets\floating-plush\floating-plush-mymelody-pose.png',
        'app\assets\floating-plush\floating-plush-cinnamoroll-front.png',
        'app\assets\floating-plush\floating-plush-cinnamoroll-pose.png',
        'app\assets\floating-plush\floating-plush-kuromi-front.png',
        'app\assets\floating-plush\floating-plush-kuromi-pose.png'
    )
    $compatibilityFixtures = @('Countdown.App.psm1') + $officialCharacterAssets + $script:legacyFloatingRegressionAssets
    $packageFiles = @($script:packageTrustRoots) + @($integrityHashes.Keys) + @($script:buildOutputPackageFiles)

    $hashCopy = [ordered]@{}
    foreach ($entry in $integrityHashes.GetEnumerator()) {
        $hashCopy[[string] $entry.Key] = [string] $entry.Value
    }
    $buildAssetCopies = @(
        foreach ($asset in $script:buildAssetDefinitions) {
            [pscustomobject]@{
                SourcePath = [string] $asset.SourcePath
                PublishedPath = [string] $asset.PublishedPath
                SHA256 = [string] $asset.SHA256
                Category = [string] $asset.Category
            }
        }
    )

    [pscustomobject]@{
        ContractVersion = 1
        PackageTrustRoots = [string[]] @($script:packageTrustRoots)
        PackageFiles = [string[]] $packageFiles
        IntegrityHashes = $hashCopy
        BuildAssets = [object[]] $buildAssetCopies
        InstalledFiles = [string[]] $installedFiles
        CompatibilityFixtureFiles = [string[]] $compatibilityFixtures
        RetiredRuntimeFiles = [string[]] $retiredRuntimeFiles
        OfficialCharacterAssets = [string[]] $officialCharacterAssets
        FloatingPlushAssets = [string[]] @($script:floatingPlushAssets)
        CurrentFloatingRuntimeAssets = [string[]] $currentFloatingRuntimeAssets
        LegacyFloatingRegressionAssets = [string[]] @($script:legacyFloatingRegressionAssets)
        SourceBuildFiles = [string[]] @($script:sourceBuildFiles)
    }
}

function Assert-CountdownReleaseContractDefinition {
    $contract = New-CountdownReleaseContractSnapshot
    Assert-CountdownUniqueReleasePaths -Name 'PackageTrustRoots' -Paths $contract.PackageTrustRoots
    Assert-CountdownUniqueReleasePaths -Name 'PackageFiles' -Paths $contract.PackageFiles
    Assert-CountdownUniqueReleasePaths -Name 'InstalledFiles' -Paths $contract.InstalledFiles
    Assert-CountdownUniqueReleasePaths -Name 'RetiredRuntimeFiles' -Paths $contract.RetiredRuntimeFiles
    Assert-CountdownUniqueReleasePaths -Name 'CompatibilityFixtureFiles' -Paths $contract.CompatibilityFixtureFiles
    Assert-CountdownUniqueReleasePaths -Name 'SourceBuildFiles' -Paths $contract.SourceBuildFiles
    Assert-CountdownUniqueReleasePaths -Name 'BuildAssets.SourcePath' -Paths @($contract.BuildAssets.SourcePath)
    Assert-CountdownUniqueReleasePaths -Name 'BuildAssets.PublishedPath' -Paths @($contract.BuildAssets.PublishedPath)

    $packageSet = [System.Collections.Generic.HashSet[string]]::new($contract.PackageFiles, [System.StringComparer]::OrdinalIgnoreCase)
    $integritySet = [System.Collections.Generic.HashSet[string]]::new([string[]] @($contract.IntegrityHashes.Keys), [System.StringComparer]::OrdinalIgnoreCase)
    $installedSet = [System.Collections.Generic.HashSet[string]]::new($contract.InstalledFiles, [System.StringComparer]::OrdinalIgnoreCase)
    $retiredSet = [System.Collections.Generic.HashSet[string]]::new($contract.RetiredRuntimeFiles, [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $integritySet) {
        if (-not $packageSet.Contains($path)) { throw "受哈希文件不在发布包内：$path" }
    }
    foreach ($path in $installedSet) {
        if (-not $integritySet.Contains($path)) { throw "安装运行时文件未受哈希保护：$path" }
        if ($retiredSet.Contains($path)) { throw "安装运行时文件同时被标记为已淘汰：$path" }
    }
    foreach ($path in $contract.SourceBuildFiles) {
        if (-not $packageSet.Contains($path) -or -not $integritySet.Contains($path)) {
            throw "源码构建输入不属于受保护发布包：$path"
        }
    }
    foreach ($asset in $contract.BuildAssets) {
        Assert-CountdownReleaseRelativePath -Path $asset.SourcePath
        Assert-CountdownReleaseRelativePath -Path $asset.PublishedPath
        if (-not $installedSet.Contains($asset.PublishedPath) -or -not $integritySet.Contains($asset.PublishedPath)) {
            throw "构建资产目标不属于受保护运行时：$($asset.PublishedPath)"
        }
    }
    foreach ($path in @($contract.PackageFiles) + @($contract.InstalledFiles) + @($contract.BuildAssets.PublishedPath)) {
        if ($path -match '(^|\\)(bin|obj)(\\|$)') {
            throw "发布契约不得包含构建输出目录：$path"
        }
        if ($path -like 'assets\ui-references\*' -or $path -like 'app\assets\ui-references\*') {
            throw "UI 参考图不得进入发布运行时：$path"
        }
    }
}

function Resolve-CountdownReleasePackagePath {
    param(
        [Parameter(Mandatory)] [string] $Root,
        [Parameter(Mandatory)] [string] $RelativePath
    )

    Assert-CountdownReleaseRelativePath -Path $RelativePath
    $rootPath = [System.IO.Path]::GetFullPath($Root)
    $resolved = [System.IO.Path]::GetFullPath((Join-Path $rootPath $RelativePath))
    $rootPrefix = if ($rootPath.EndsWith('\')) { $rootPath } else { $rootPath + '\' }
    if (-not $resolved.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "发布包路径逃出根目录：$RelativePath"
    }
    $resolved
}

function Get-CountdownReleaseContract {
    [CmdletBinding()]
    param()

    New-CountdownReleaseContractSnapshot
}

function Assert-CountdownReleasePackage {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Root)

    $rootPath = [System.IO.Path]::GetFullPath($Root)
    if (-not (Test-Path -LiteralPath $rootPath -PathType Container)) {
        throw "发布包根目录不存在：$rootPath"
    }

    $contract = Get-CountdownReleaseContract
    foreach ($relativePath in $contract.PackageFiles) {
        $path = Resolve-CountdownReleasePackagePath -Root $rootPath -RelativePath $relativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "缺少发布包文件：$relativePath"
        }
    }
    foreach ($entry in $contract.IntegrityHashes.GetEnumerator()) {
        $path = Resolve-CountdownReleasePackagePath -Root $rootPath -RelativePath ([string] $entry.Key)
        $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
        if (-not [string]::Equals($actual, [string] $entry.Value, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "发布包哈希不匹配：$($entry.Key)"
        }
    }

    [pscustomobject]@{
        Valid = $true
        VerifiedFiles = $contract.IntegrityHashes.Count
    }
}

Assert-CountdownReleaseContractDefinition
Export-ModuleMember -Function Get-CountdownReleaseContract, Assert-CountdownReleasePackage
