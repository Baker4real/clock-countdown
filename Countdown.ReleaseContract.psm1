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
    },
    [pscustomobject]@{
        SourcePath = 'assets\main-decorations\main-countdown-top-night.png'
        PublishedPath = 'app\assets\main-decorations\main-countdown-top-night.png'
        SHA256 = '607A7C45F3EC763C47D05B03866AA185825317670A8696450F8866B24FFC20A3'
        Category = 'MainDecoration'
    },
    [pscustomobject]@{
        SourcePath = 'assets\main-decorations\main-notes-top-night.png'
        PublishedPath = 'app\assets\main-decorations\main-notes-top-night.png'
        SHA256 = '84C23BA9CE005A0477AE28A8B23892156A238BE82B29DD42CF2BF3C7E60EA6EB'
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
    'Build-CountdownApplication.ps1' = 'F055EB0F34C5A809F0DE115964F200917A60AF7E3B864960149CB5F4B8E92AE9'
    'Countdown.App.psm1' = '6F40387744A2DF43A41A6A95B3DD02AF748CE7C3B00F532FC1588FB33B5994AF'
    'Countdown.Core.psm1' = 'E1A639B06D404F7F4638CAB4DE6392FB27E5C324D77A2AC44D1B226C8375410B'
    'Countdown.InstallTransaction.psm1' = '5A9FAAE019095134D53586886A83581F3794654B9790102A17751860B157B08E'
    'Countdown.RemoteDeployment.psm1' = '9E6E1BD211C88E79DF03EC6DEE5922CFB33CF23112E4D53A1B8B7FE6152F2EC6'
    'Countdown.TransactionAcceptance.psm1' = '085F59A68A8FF542E023D028FF8FBA3B014350A45F537CDB227A9F18E08468C1'
    'Countdown.TransferLedger.psm1' = 'EE5A55AE5CEBAAFC68789D616AB72E455759778DC5367CD5D4C614BD68670557'
    'Countdown.Validation.psm1' = 'C92528DE417FA5205F12AF66C72FC4F0D89E124F5634B3A6101FD19598F48401'
    'Countdown.Notes.psm1' = 'CF41E5A29B5FCFB0807E1EA1E6F52AC7FADE4478A18990698F2BA6F725E08CA0'
    'Countdown.Runtime.psm1' = 'FF9F9FFE54FA267F83379526B2D488412EF25B800F5FAB3734F59290676B8511'
    'Countdown.Settings.psm1' = 'E336CC4C001F1201151B0894889EB0BDF84EDEB01252D7EF71CF20AA9E5EAA84'
    'Countdown.Tags.psm1' = '57AE8BBE689F3838E3B9EE8B56F96D661C8A46EAD825103307482B00C472FAAC'
    'Countdown.Core.Tests.ps1' = '742E422A8926810656C4685149C6634D5EA61A5D380BA434B8521C47A46D9D54'
    'Countdown.Ui.Tests.ps1' = '780CB3E2F8AD89CAD5968F6A36F7D9AFCF642F88A5F0DAC5BE18083CDA92C6AC'
    'Countdown.CSharp.Tests.ps1' = 'C0038D9BB21C3819704C31289CD618EE640DEF5275556D9D6888EF8CD0654209'
    'tools\Build-MainDayOrnamentScene.ps1' = '1C9BFC52DBDE1F6EE076F3EB84A7484FA9A48EFCE7297B57E1D4DF52CA199036'
    'tools\MainDecorationSceneBands.psd1' = '9DAEDF905F2FD6A0E8396E8718A06B115566EA4A8799354347E3A30D2E41C6B7'
    'tools\Test-MainDayOrnamentScene.ps1' = '2291AA01AB03161F8500B98E68335F55BD2946795EBE0341687480179563BF81'
    '敏敏子倒计时.ps1' = 'A4BFD4ECE350D74F2B10F65E5D627755951B053F00B99BAC8BF29D371E10E26D'
    'CountdownLauncher.exe' = '195A228F647E4541AE4EEF7358BF2BDD478F705FA23D1981C0D96E8DD835CC00'
    'countdown-icon.ico' = 'C5B7CB0A09524AEFE4488291078F61B0AF54958582DA98B99B25D0F4DC6976C5'
    'verification\MinminCountdown.Verification.exe' = 'E6FFEA7881638D3D81A444D3137401E5E11E66229FF18239FD105086EC69D720'
    'app\MinminCountdown.exe' = 'A336BF0FCA32E106F20E28FF43B5059AA3662498CFB0290669969DD6C0710ACD'
    'assets\floating-plush\floating-plush-mymelody-front.png' = '9D078C528CB6581222D292EA7E13AF42A77DC60C793C9A8B90AE163B30D27A4D'
    'assets\floating-plush\floating-plush-mymelody-pose.png' = 'FC8EE48368C6BAEBF45CE3250BD93EB0A43760A03089BFBECCCE2295AF9D5333'
    'assets\floating-plush\floating-plush-cinnamoroll-front.png' = 'DA9BD4C55AD11CAAADD68A8B153D104A4F170B453175BB67DB7AE99C5CFDAD9B'
    'assets\floating-plush\floating-plush-cinnamoroll-pose.png' = '5FABCFB1420B6D3162DED0BC0C0FFC2A97ACCE24C3B214A1708E652CDB62D107'
    'assets\floating-plush\floating-plush-kuromi-front.png' = '65A0ACB6B90BCFB2E4409840B1AD5A0844951951A1BBE9050330644592DFD8BD'
    'assets\floating-plush\floating-plush-kuromi-pose.png' = '92A4493D227A3EA60E5BC23D12CF6AC7F8E5CEBED4F3FDA0328DE6C8B5158F93'
    'global.json' = 'B132D105648C7DBE49F2C391FE0B9898027E470DDC7CFD4D7239AF5DF63401FA'
    'src\MinminCountdown.Core\CalendarOccurrenceCalculator.cs' = 'DCDC8D652A32A01914EBCBA9E9FCA55762AC9CCC8DF6BC5968D9F8168845FF66'
    'src\MinminCountdown.Core\MinminCountdown.Core.csproj' = 'D8CB86FE56DC5E8AB7C61BCBB920969667AA80EBD65E88C33B1590B61338390B'
    'src\MinminCountdown.Core\SettingsContract.cs' = '3F3B090FB539C7AEF8A5C0F114991BB292CC40502C8AACD66B058A2F7933A174'
    'src\MinminCountdown.Core\SettingsFileOperations.cs' = 'CC2A01D5122D4230120CD491C598394ACD6AD0C56148580C572C9E4BFED41A68'
    'src\MinminCountdown.Core\SettingsModels.cs' = '917450E26A07DE590189D6B38E159868FDCEB55B492DD87ACA3BCACC771221E0'
    'src\MinminCountdown.Core\SettingsRecovery.cs' = 'C955F13308523F1B49E017341373EE7E7687FF1E192C8148C4AE541E0FB6D5CB'
    'src\MinminCountdown.Core\SettingsStore.cs' = 'F7EA0C2323F1EF20AFF03E15E6AC34286BA0746517AD8D151AC96FA72AB0AD32'
    'src\MinminCountdown.Core\SettingsSynchronization.cs' = '0B313C7A4236ACCE0DB1E3EAF464DC10C22C15E4A5EC5357389B9BDA6510B003'
    'src\MinminCountdown\App.xaml' = 'F0D3777A7415FFDB0D44008BFD317E44408FD639400B1BCD9B08F5C7F93BC920'
    'src\MinminCountdown\App.xaml.cs' = '8B048AD3C5D1AE21224896DA897B256DCA4B73FA69F83F32E5F69FC1E2A1419C'
    'src\MinminCountdown\ApplicationSession.cs' = '03419A2795D8A7E2D03B03E77EF32A0891C41E392D3576B6BF53762AC38236C7'
    'src\MinminCountdown\ApplicationTheme.cs' = 'F45DCBD945D98B0E14F17A21DED73369CE8563FDA49A51990B723B24908E83E3'
    'src\MinminCountdown\ApplicationValidation.cs' = '85414435D44ABFB7A186CFFFC907A5E65DB527F149C9C3DD29A45C75F3A868A1'
    'src\MinminCountdown\DesktopWorkspace.cs' = '59AB6FF62DCE3DFE30F41EA15E15871063F2ADC0FDB17CD057A63EA1D609FA13'
    'src\MinminCountdown\DiagnosticLogger.cs' = '234EB941A3F13FF23DB6D58B96A1783FD7C9D7A23BA2B87EAAC0D0CE4762A241'
    'src\MinminCountdown\DomainLogic.cs' = '0672AAE0BDF61DE7265899A114CEF77746BDFCE97F627C9F741ACFDD19246C68'
    'src\MinminCountdown\FloatingTrayController.cs' = '6B1AA8B9BF43023EB8DEAD7E460CB88616CEB880F0767DFE7D95ED2215F06D5E'
    'src\MinminCountdown\FloatingWindow.Countdowns.cs' = '37A7790B9EB756B35640213C0C2612A0F7B19ACDE4368A6755C2FB817B70FD3E'
    'src\MinminCountdown\FloatingWindow.Notes.cs' = '0CEE735F51707B81214A93305B137C566AC1452EA342909A5C223B1CBDFC40C2'
    'src\MinminCountdown\FloatingWindow.xaml' = '5A35CE689DEA03472AAA41A1A43A8E41C2D015FC574769ECFC2CB3B668A2D2F9'
    'src\MinminCountdown\FloatingWindow.xaml.cs' = 'D9CFFABFD571C57B6AFB559D407FE02012261DD8F7723526DCB16FAAFCC538FA'
    'src\MinminCountdown\GlobalUsings.cs' = '9EB56B713F4BD450C169F140E74FF7B6D5E456F3742A30188B947223D01628E4'
    'src\MinminCountdown\HolidayCalendar.cs' = 'F0E6E2F4E269A4E21979C1C75E107E865EDCD105F9B5D0455A3EDC0CB10FF147'
    'src\MinminCountdown\HostOptions.cs' = '6C37F0C367FC10A7A22B21631880F121463CCA6C425BF2E080CFC14B6DB1767B'
    'src\MinminCountdown\InstanceActivation.cs' = '26A31C9A31EC7FF860E3102DBE23EFD840810CD31FDE0F232B91421B2F16D7CE'
    'src\MinminCountdown\MainWindow.xaml' = 'F06A97C01B739FA8490A3386D89179C58CE930024AEC6C9E7DA77BA919F30F2C'
    'src\MinminCountdown\MainWindow.xaml.cs' = '73B4B3C4A3108DD381CD73ED72916DE5222206EF9143AA373B3EF4DDA24AF4F4'
    'src\MinminCountdown\MinminCountdown.csproj' = '8FB22B6846D254F2F33D4D73598FC9BEF3146C434ADC4C791B1E0FB067D7CA4C'
    'src\MinminCountdown\ReminderEditing.cs' = '952F7935F4971A7BDFA145938525A7E7D14F8CE4305EA8ECEC52F4AA5E2FB138'
    'src\MinminCountdown\ReminderRulesEditorWindow.cs' = '329BC0AE2A50E06E6F3C41F164C6B4AD69567034396F83913A12029DADE3BD8A'
    'src\MinminCountdown\ReminderWindow.cs' = '5359BE0C04E0FE06E7C1D74744A7D1203A7C4AA71F82FDB452030D3B44CB1BB7'
    'src\MinminCountdown\RuntimeProtocol.cs' = '37E5585174890F4B748DE4394440CE9D674B675EEAD0775472E2222F0AAC9EE2'
    'src\MinminCountdown\SettingsRecoveryWindow.cs' = '9A6B0C7464C9970F9E4AC6305FD75A719AD5350C559A94A155CD07F83DF6BD48'
    'src\MinminCountdown\TrayIconHost.cs' = 'A40BEA846B2E396122BDB4461865B04617EE4495951349FB4AB033DD14D09E4A'
    'src\MinminCountdown\UiControls.cs' = '0C391BA6B62E91EF40E59AE0F08EBA6CC0E9F27FDBCBC39C9B4FC5AA7F020250'
    'src\MinminCountdown\ValidationExercises.cs' = 'F8B482F2C1D8AC40438432C0D23F075BBF670C558C7A22D5C90AC30E15406195'
    'src\MinminCountdown\ValidationReport.cs' = '4780CD499D49A1F9C1F16D621405684C3F7050E499D0B125E7F6261318D74FD2'
    'src\MinminCountdown\ValidationReportContract.cs' = '890BA433D24D7BAC1A8468BD5D4AACD1B067EACD3B153C12FF7357C7BD112F49'
    'src\MinminCountdown\VisualReferenceBaseline.cs' = 'DF0E749EA0D7234AFB6E850AF9F17B46BBB5EE0ACDE11083725FD2B08008FB61'
    'src\MinminCountdown\WindowValidationScenarios.cs' = '8489CF96A30EC1A440F106375F7D28652D253B3E0067DF62B5D00EBE8DE1DED3'
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
    '敏敏子倒计时.ps1',
    'CountdownLauncher.exe',
    'countdown-icon.ico',
    'verification\MinminCountdown.Verification.exe',
    'app\MinminCountdown.exe'
) + @($script:sourceBuildNonAssetFiles)

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
        '敏敏子倒计时.ps1',
        'app\MinminCountdown.exe'
    ) + $applicationAssets + @(
        'CountdownLauncher.exe',
        'countdown-icon.ico'
    ) + $officialCharacterAssets
    $retiredRuntimeFiles = @('Countdown.App.psm1') + $script:floatingPlushAssets + @(
        'app\assets\floating-plush\floating-plush-mymelody-front.png',
        'app\assets\floating-plush\floating-plush-mymelody-pose.png',
        'app\assets\floating-plush\floating-plush-cinnamoroll-front.png',
        'app\assets\floating-plush\floating-plush-cinnamoroll-pose.png',
        'app\assets\floating-plush\floating-plush-kuromi-front.png',
        'app\assets\floating-plush\floating-plush-kuromi-pose.png'
    )
    $compatibilityFixtures = @('Countdown.App.psm1') + $officialCharacterAssets + $script:legacyFloatingRegressionAssets
    $packageFiles = @($script:packageTrustRoots) + @($integrityHashes.Keys)

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
