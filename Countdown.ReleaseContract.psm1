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

$script:offlineHolidayDataFiles = @(
    'holiday-data\2026.json',
    'holiday-data\README.md',
    'holiday-data\LICENSE.txt'
)

$script:sourceBuildNonAssetFiles = @(
    'global.json',
    'src\MinminCountdown.Core\CalendarOccurrenceCalculator.cs',
    'src\MinminCountdown.Core\MinminCountdown.Core.csproj',
    'src\MinminCountdown.Core\SettingsContract.cs',
    'src\MinminCountdown.Core\SettingsFileOperations.cs',
    'src\MinminCountdown.Core\SettingsModels.cs',
    'src\MinminCountdown.Core\DateDisplay.cs',
    'src\MinminCountdown.Core\NoteOrdering.cs',
    'src\MinminCountdown.Core\SettingsRecovery.cs',
    'src\MinminCountdown.Core\SettingsStore.cs',
    'src\MinminCountdown.Core\SettingsSynchronization.cs',
    'src\MinminCountdown\App.xaml',
    'src\MinminCountdown\App.xaml.cs',
    'src\MinminCountdown\ApplicationSession.cs',
    'src\MinminCountdown\ApplicationTheme.cs',
    'src\MinminCountdown\ApplicationValidation.cs',
    'src\MinminCountdown\DesktopWorkspace.cs',
    'src\MinminCountdown\DeleteUndoOffer.cs',
    'src\MinminCountdown\DiagnosticLogger.cs',
    'src\MinminCountdown\DwmWindowChrome.cs',
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
    'src\MinminCountdown\app.manifest',
    'src\MinminCountdown\MonitorGeometryValidation.cs',
    'src\MinminCountdown\MonitorWorkAreaResolver.cs',
    'src\MinminCountdown\MotionBehavior.cs',
    'src\MinminCountdown\NoteCompletionExit.cs',
    'src\MinminCountdown\ReminderCenterViewModel.cs',
    'src\MinminCountdown\ReminderEditing.cs',
    'src\MinminCountdown\ReminderRulesEditorViewModel.cs',
    'src\MinminCountdown\ReminderRulesEditorWindow.cs',
    'src\MinminCountdown\ReminderWindow.cs',
    'src\MinminCountdown\RuntimeProtocol.cs',
    'src\MinminCountdown\SegmentIndicator.cs',
    'src\MinminCountdown\SettingsRecoveryWindow.cs',
    'src\MinminCountdown\TrayIconHost.cs',
    'src\MinminCountdown\UiControls.cs',
    'src\MinminCountdown\ValidationExercises.cs',
    'src\MinminCountdown\ValidationReport.cs',
    'src\MinminCountdown\ValidationReportContract.cs',
    'src\MinminCountdown\VisualReferenceBaseline.cs',
    'src\MinminCountdown\WindowValidationScenarios.cs',
    'src\MinminCountdown\WindowWorkAreaGeometry.cs'
)

$script:sourceBuildFiles = @($script:sourceBuildNonAssetFiles) + @('countdown-icon.ico') + @(
    $script:buildAssetDefinitions | ForEach-Object { [string] $_.SourcePath }
)

$script:nonAssetHashes = [ordered]@{
    'Build-CountdownApplication.ps1' = 'F636915B4B4330EBD8A16816978D067ABD61EA08276ED86CD40728B179FA7710'
    'tools\Countdown.BuildPreflight.psm1' = '8B6609C8299F58C65CD80FE6A1F35F66D2DEEB1C50FCD488A9F766954407F1B5'
    'tools\Test-CountdownBuildPreflight.ps1' = '06C387D084227C8F5B56DEBD258F9FCB62248CD973B3D24AA5AA9F5C0E1A3D06'
    'DESIGN.md' = 'BB5D378764DCBD7962CBB339EE45CD2F0EFD54731C90276836581027A890BE3D'
    'Countdown.App.psm1' = '604681FC3CF7D32B4D6DC45FC98A4B3FBF1BFBD4FFB958A5CE616F3FB716705B'
    'Countdown.Core.psm1' = 'E1A639B06D404F7F4638CAB4DE6392FB27E5C324D77A2AC44D1B226C8375410B'
    'Countdown.InstallTransaction.psm1' = '087F9B4C8F3116D16FC0379F177BB094A3CC5F3A176DEDDC4076B1223576D8B8'
    'Countdown.RemoteDeployment.psm1' = '0A5B9E4CC18EE573371EE991EC11FF85B9CC19E3FE91503421D1F7E23B0D96D3'
    'Countdown.TransactionAcceptance.psm1' = 'EC8D18BDBF438D9050A842A3DE7BCFA79EB58F4E9433D856F2062F83882FFE93'
    'Countdown.TransferLedger.psm1' = 'B3051D0F5AF1F7E0351A9637B076E0402DCC9500A21CC41C02594EC9012D88B8'
    'Countdown.Validation.psm1' = 'AB474DC88C8E2A804A0A56278F20B6A9E00046D8B423D86AD39A1BCACE4B7E06'
    'Countdown.Notes.psm1' = 'CF41E5A29B5FCFB0807E1EA1E6F52AC7FADE4478A18990698F2BA6F725E08CA0'
    'Countdown.Runtime.psm1' = '6EAD9634FAB49CED2C588D74DC62541FEC6852F0DE694EBC53147FD967CD3A88'
    'Countdown.Settings.psm1' = '507BB437A209CC6F340B00D7089171C6D78EFF6C6EF597A87F49E45BF40E95DC'
    'Countdown.Tags.psm1' = '57AE8BBE689F3838E3B9EE8B56F96D661C8A46EAD825103307482B00C472FAAC'
    'Countdown.Core.Tests.ps1' = 'C03F8BA1249BFFB862178925A6DB5EAF0F9F8EC6CCE080DDC5022CF9D1DA20EB'
    'Countdown.Ui.Tests.ps1' = 'ACE9EFDEDB266A33E812052E4AC9276B2BD0706B39A805CDA5F55B9B643434CF'
    'Countdown.CSharp.Tests.ps1' = 'D081640ABFC3C1BDF4D33142C84C6798693CC924F5230C1741518186E79A90F1'
    'tools\Build-MainDayOrnamentScene.ps1' = '1C9BFC52DBDE1F6EE076F3EB84A7484FA9A48EFCE7297B57E1D4DF52CA199036'
    'tools\MainDecorationSceneBands.psd1' = '9DAEDF905F2FD6A0E8396E8718A06B115566EA4A8799354347E3A30D2E41C6B7'
    'tools\Test-MainDayOrnamentScene.ps1' = '2291AA01AB03161F8500B98E68335F55BD2946795EBE0341687480179563BF81'
    'tools\Start-IsolatedSuiteProcess.ps1' = '1BEAE70A347DC28109F4035D77A346726E6D607659FC6E6723AA56EA95AA986A'
    'tools\Countdown.SuiteIsolation.psm1' = '61E87B0912ADD0C0995B9E26DFC66EEF59E1134FF15BAC1AB1B2590BAC73ACCC'
    '敏敏子倒计时.ps1' = '3A4C018DEE2C1453D01BA4C837EBB4E207C1422D0301CE2973757CD4670B3106'
    'CountdownLauncher.exe' = '4B4C66274278049FF185C7824685D1B5A85F5DA10557F70A1D7E458778066440'
    'CountdownLauncher.cs' = 'FF4602D8C63A42C1237855B7CF787F47A8F09CEA4F49D9006F777A367BBFDC42'
    'countdown-icon.ico' = 'C5B7CB0A09524AEFE4488291078F61B0AF54958582DA98B99B25D0F4DC6976C5'
    'assets\floating-plush\SOURCES.md' = '0F17E6B2C45AA160A5B6D69135C7FB9BCCBE774B4E5C656908D01D9B6643B272'
    'holiday-data\2026.json' = '5B9887FB139FA190150BF0FC3E8EDB32A06647E884F1447633B305783ADACEBB'
    'holiday-data\README.md' = 'E177C43E1EA189D8C191A058DFC46E0E524ED4DA78881DD1E95CD600E508D53F'
    'holiday-data\LICENSE.txt' = 'C1614A521EFA0BD3FC0D271D01E38E9450C8BA3A13825044C325E433B4870142'
    'docs\third-party\H.NotifyIcon\2.4.1\LICENSE.md' = 'B41F5ADB8391601E3C693462925317E4CBE446270BCD646BDB8D02572821FE80'
    'assets\floating-plush\floating-plush-mymelody-front.png' = '9D078C528CB6581222D292EA7E13AF42A77DC60C793C9A8B90AE163B30D27A4D'
    'assets\floating-plush\floating-plush-mymelody-pose.png' = 'FC8EE48368C6BAEBF45CE3250BD93EB0A43760A03089BFBECCCE2295AF9D5333'
    'assets\floating-plush\floating-plush-cinnamoroll-front.png' = 'DA9BD4C55AD11CAAADD68A8B153D104A4F170B453175BB67DB7AE99C5CFDAD9B'
    'assets\floating-plush\floating-plush-cinnamoroll-pose.png' = '5FABCFB1420B6D3162DED0BC0C0FFC2A97ACCE24C3B214A1708E652CDB62D107'
    'assets\floating-plush\floating-plush-kuromi-front.png' = '65A0ACB6B90BCFB2E4409840B1AD5A0844951951A1BBE9050330644592DFD8BD'
    'assets\floating-plush\floating-plush-kuromi-pose.png' = '92A4493D227A3EA60E5BC23D12CF6AC7F8E5CEBED4F3FDA0328DE6C8B5158F93'
    # #231：三张派生姿态参考图只作悬浮窗视觉基线 sourceBinding 输入，随发布（验证）包分发但不装进应用。
    'assets\floating-plush\floating-plush-mymelody-reference.png' = '54F7C2EB62D48783D5977CF68AA795757EC891BE96298351D88784866ED98B07'
    'assets\floating-plush\floating-plush-cinnamoroll-reference.png' = '9B4E007395F1AF3B20E2EC492C22C8A4FB1F205473A2AD16AFD71F457B44E6B9'
    'assets\floating-plush\floating-plush-kuromi-reference.png' = '52D0814F2A764EE1262F8919FE70BD535CC14BAFC399295532AAEEFA352384DC'
    'global.json' = 'B132D105648C7DBE49F2C391FE0B9898027E470DDC7CFD4D7239AF5DF63401FA'
    'src\MinminCountdown.Core\CalendarOccurrenceCalculator.cs' = 'DCDC8D652A32A01914EBCBA9E9FCA55762AC9CCC8DF6BC5968D9F8168845FF66'
    'src\MinminCountdown.Core\MinminCountdown.Core.csproj' = 'D8CB86FE56DC5E8AB7C61BCBB920969667AA80EBD65E88C33B1590B61338390B'
    'src\MinminCountdown.Core\SettingsContract.cs' = '2CB0870BD0A9D8CF55621CE181C94B64A8267926DA2A49090D326BD41DAF5779'
    'src\MinminCountdown.Core\SettingsFileOperations.cs' = '3EE55429417892D006B1E1AA81578686D996B9393C5B4603B38DC6A2838E872F'
    'src\MinminCountdown.Core\SettingsModels.cs' = '8DAEEF60AEE1E67E744452043155F110DDE1E6947A2EED7F004F5F2AA63B9B28'
    'src\MinminCountdown.Core\DateDisplay.cs' = '90A6F503407D27140E4DA96A724D165171273EA18CD95F7AA3F9815B2E7B2DC3'
    'src\MinminCountdown.Core\NoteOrdering.cs' = '5495B8AB9E6B1295026EBDA06194ECDB63753D828BBD10C2A02BC792AD7F43A3'
    'src\MinminCountdown.Core\SettingsRecovery.cs' = 'C955F13308523F1B49E017341373EE7E7687FF1E192C8148C4AE541E0FB6D5CB'
    'src\MinminCountdown.Core\SettingsStore.cs' = '2CD878573BBBEA25FB19958BA00AFAE012F063C7D45DD342E66C10769D1F6B15'
    'src\MinminCountdown.Core\SettingsSynchronization.cs' = '0B313C7A4236ACCE0DB1E3EAF464DC10C22C15E4A5EC5357389B9BDA6510B003'
    'src\MinminCountdown\App.xaml' = 'BE2804CAD1D0C04502E8A7498624F8FE055FE17A452DEA3CF651B3D709555351'
    'src\MinminCountdown\App.xaml.cs' = '1907D4A29EF30982F89F0D095EB91AA1C004AC06B3ABF4E23E0AAB8577AEE200'
    'src\MinminCountdown\ApplicationSession.cs' = 'AF16D1D44EBCA942D5A0F951339977DAB3C04688CEEF7093CDA54D8091D99413'
    'src\MinminCountdown\ApplicationTheme.cs' = '5420B0287A6F443F0867E7771D7B2E988F9D426ADF87983D05B87A2727E201CF'
    'src\MinminCountdown\ApplicationValidation.cs' = '2577227750A5C435714AED9424BAC560BA29E1526269208F0FC09450418C5525'
    'src\MinminCountdown\DesktopWorkspace.cs' = 'C8D02368AA038C6D433AFDB779F03190E911BB1DB420897504DC0C544AB4DF21'
    'src\MinminCountdown\DeleteUndoOffer.cs' = 'C42400CD3092697F531F687B53F37C4E793D071A4D68D3FBC4ECB0B34054B282'
    'src\MinminCountdown\DiagnosticLogger.cs' = '234EB941A3F13FF23DB6D58B96A1783FD7C9D7A23BA2B87EAAC0D0CE4762A241'
    'src\MinminCountdown\DwmWindowChrome.cs' = '8D535ABC40E5903F454C5806BA6EBD2EE844E59843778235FC6FDB057C0D2BBA'
    'src\MinminCountdown\DomainLogic.cs' = 'FBFB34378DF7468B8FF911038268BCEEAE8F64D975A5D229A72282BC95DE8248'
    'src\MinminCountdown\FloatingBottomStatus.cs' = 'FAB9AE910D5F815158D8DC3487BC5D37DE9CEFE0E8F90F43DA65F2B06C3D8C4B'
    'src\MinminCountdown\FloatingTrayController.cs' = '748B194AD533EF14571C92F57D1EAC3A82AF485153920E1B9360D6BE9472B20C'
    'src\MinminCountdown\FloatingWindow.Countdowns.cs' = '4BF481E0374E0C0848821DD13A774F403A483A4935903CBE885D6E635A660BE2'
    'src\MinminCountdown\FloatingWindow.Notes.cs' = 'FC184D524DA0A2257BA581CD84AAD552F760C1B26CF5DF94F6114C4EFAE00B79'
    'src\MinminCountdown\FloatingWindow.xaml' = 'DCFD71C597630734D664CAF303BCBEEC057B7C385A5A94A74BFE7076008FC41B'
    'src\MinminCountdown\FloatingWindow.xaml.cs' = 'A049E0568AED7FEECC5E0E0B7134A93E648F1A5F659449F3DBB5E3080EC1B209'
    'src\MinminCountdown\GlobalUsings.cs' = '9EB56B713F4BD450C169F140E74FF7B6D5E456F3742A30188B947223D01628E4'
    'src\MinminCountdown\HolidayCalendar.cs' = 'D93311DB5E74931A6B9378D7FC0360560FF08EA6E45AEFE29CB6E92C5A4322DC'
    'src\MinminCountdown\HostOptions.cs' = '4C3BB5ED222319195A459C0BA4323A49EAB03D119250E926AFE988F25339A4F3'
    'src\MinminCountdown\InstanceActivation.cs' = '26A31C9A31EC7FF860E3102DBE23EFD840810CD31FDE0F232B91421B2F16D7CE'
    'src\MinminCountdown\MainWindow.xaml' = '48E8EC43B417D6A6413F4675A58E70B439B936C389590B7B1E7BF83AC6A2ED57'
    'src\MinminCountdown\MainWindow.xaml.cs' = '67F51D7C8AA70E8C6EFF150933C125460CDDCB616FB6D9A48416983805264CB0'
    'src\MinminCountdown\MinminCountdown.csproj' = '34DDC5260BA973D4C5485F90D325DF634A0DD60C61754AEBD57D213DB09FAE88'
    'src\MinminCountdown\app.manifest' = '51D00E5A5BABD249EF22E33CFBED623809398AE9F3C6114729821A085B8A9DD8'
    'src\MinminCountdown\MonitorGeometryValidation.cs' = '197D1E995CCC16F07424C76D19E1736028393F1961050795C4E9444D089F257E'
    'src\MinminCountdown\WindowWorkAreaGeometry.cs' = '929150A0624B41CE3551F24F7512843ACD8B76D18CF9C21CE78756115052EB6C'
    'src\MinminCountdown\MonitorWorkAreaResolver.cs' = 'FAC5BE6DABFB65DFD5D9FC3506F3CE126D47C5A0B922F5D2B847B41F554E9624'
    'src\MinminCountdown\MotionBehavior.cs' = '52B526EFFBA105049A3EA94580BFE5154978592DD81A1141F87E898787CD41AB'
    'src\MinminCountdown\NoteCompletionExit.cs' = '360B7D4E2693284BF0F5B1A27BEDC36438285EE831DB347A40E8E5F355738156'
    'src\MinminCountdown\ReminderCenterViewModel.cs' = '4B270291A913C00085B272D8422DE87922277D3AF00A990EB6867335DCF10408'
    'src\MinminCountdown\ReminderEditing.cs' = '952F7935F4971A7BDFA145938525A7E7D14F8CE4305EA8ECEC52F4AA5E2FB138'
    'src\MinminCountdown\ReminderRulesEditorViewModel.cs' = 'DB0A1BB5809E4B791A655F49BE6267E224F7CC0A2940827C3A7A05A69180B688'
    'src\MinminCountdown\ReminderRulesEditorWindow.cs' = '92794D1F194582363F38DF29440A0E7382E3526BA5760D21B99E7ACBF6C2277B'
    'src\MinminCountdown\ReminderWindow.cs' = '570E792566A011F6CC019DFE4A8FE9ACD802CEA368FFFA28C694D3ADE4270AF1'
    'src\MinminCountdown\RuntimeProtocol.cs' = '37E5585174890F4B748DE4394440CE9D674B675EEAD0775472E2222F0AAC9EE2'
    'src\MinminCountdown\SegmentIndicator.cs' = 'E5E5C23D555AE212B70784AE4FC3E31BF282AF7B849406A41DF8F90E7840C220'
    'src\MinminCountdown\SettingsRecoveryWindow.cs' = '47694C9EA9C1D025565A6ED490DB8F49085A9535B966E2BEEA815C5EE67EB1AF'
    'src\MinminCountdown\TrayIconHost.cs' = 'A5223DC533DD48936BA9D8A9F099D4F9045EFECFA443B9FE612C1B3413803C7B'
    'src\MinminCountdown\UiControls.cs' = '1FCB3ABC43E3C8A6AEA6DF005C52B51E3F66DD8EDB3A5FF7E4192D8870B58F3E'
    'src\MinminCountdown\ValidationExercises.cs' = '5268BE5E258CA9BFB0CA0D1A96DC7DB04EC01008E064365E70AA34C3AAD2823F'
    'src\MinminCountdown\ValidationReport.cs' = '362DD6496E72A550B23B15B57CF70F1E85A45D82772F0E3884AC99B6CD7731DC'
    'src\MinminCountdown\ValidationReportContract.cs' = 'A9629DD95F8712807EA75DD9465A7BDF8BF0893BD424BBC726AABC20D1E05163'
    'src\MinminCountdown\VisualReferenceBaseline.cs' = 'DF0E749EA0D7234AFB6E850AF9F17B46BBB5EE0ACDE11083725FD2B08008FB61'
    'src\MinminCountdown\WindowValidationScenarios.cs' = 'EBD3C3298E92E906FAC3FDBD2C7687F4FB3919A9E247D027E6F7BB22316C1EE2'
}

$script:nonAssetIntegrityPaths = @(
    'Build-CountdownApplication.ps1',
    'tools\Countdown.BuildPreflight.psm1',
    'tools\Test-CountdownBuildPreflight.ps1',
    'DESIGN.md',
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
    'docs\third-party\H.NotifyIcon\2.4.1\LICENSE.md'
) + @($script:sourceBuildNonAssetFiles) + @($script:offlineHolidayDataFiles)

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

# #218 节假日年度覆盖守卫：纯函数，注入北京时间与随包覆盖年份集合。北京时间进入 12 月后，
# 随包数据必须已覆盖下一年；否则验证明确失败并指明缺失年份与补数据流程。
function Test-CountdownHolidayYearCoverage {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [DateTimeOffset] $BeijingNow,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [int[]] $PackagedYears
    )

    $nextYear = $BeijingNow.Year + 1
    if ($BeijingNow.Month -lt 12 -or $PackagedYears.Contains($nextYear)) {
        return [pscustomobject]@{
            Covered = $true
            MissingYear = $null
            Message = ''
        }
    }

    [pscustomobject]@{
        Covered = $false
        MissingYear = $nextYear
        Message = "随包节假日数据尚未覆盖 $nextYear 年：当前北京时间已进入 $($BeijingNow.Year) 年 12 月。请按 holiday-data/README.md 的更新流程补齐 $nextYear 年随包数据（固定上游提交、重新生成全年 days 数组、更新来源提交、年份与 IntegrityHashes，工单 #234），再重新运行验证。"
    }
}

function Assert-CountdownHolidayYearCoverage {
    $packagedYears = @(
        foreach ($file in $script:offlineHolidayDataFiles) {
            if ($file -match '^holiday-data\\(\d{4})\.json$') {
                [int] $Matches[1]
            }
        }
    )
    $beijingNow = [System.TimeZoneInfo]::ConvertTimeBySystemTimeZoneId(
        [DateTimeOffset]::UtcNow, 'China Standard Time')
    $coverage = Test-CountdownHolidayYearCoverage -BeijingNow $beijingNow -PackagedYears $packagedYears
    if (-not $coverage.Covered) {
        throw $coverage.Message
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
        $script:offlineHolidayDataFiles
    ) + @(
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
        'app\assets\floating-plush\floating-plush-kuromi-pose.png',
        # #231：参考图自本版起不再随应用安装，清理既有安装的残留副本。
        'app\assets\floating-plush\floating-plush-mymelody-reference.png',
        'app\assets\floating-plush\floating-plush-cinnamoroll-reference.png',
        'app\assets\floating-plush\floating-plush-kuromi-reference.png'
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
        OfflineHolidayDataFiles = [string[]] @($script:offlineHolidayDataFiles)
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

    # #218 年度覆盖守卫：北京时间进入 12 月后随包数据必须已覆盖下一年。
    Assert-CountdownHolidayYearCoverage
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
Export-ModuleMember -Function Get-CountdownReleaseContract, Assert-CountdownReleasePackage, Test-CountdownHolidayYearCoverage
