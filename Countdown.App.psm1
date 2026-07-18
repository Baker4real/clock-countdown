#requires -Version 7.0

Set-StrictMode -Version Latest
# 应用宿主收拢 WPF 生命周期与 ValidateOnly 兼容入口；CLI 由薄脚本转发。
$ErrorActionPreference = 'Stop'

function Start-CountdownApplication {
    [CmdletBinding()]
    param(
        [switch] $ValidateOnly,
        [switch] $ExerciseNoteAdd,
        [switch] $ExercisePersistenceFailures,
        [switch] $ExerciseTagInteractions,
        [switch] $ExitAfterReadyForTest,
        [string] $SettingsFile,
        [string] $ApplicationPath,
        [scriptblock] $HolidayCalendarTaskFactory
    )

$settingsPath = if ([string]::IsNullOrWhiteSpace($SettingsFile)) {
    [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'settings.json'))
}
else {
    [System.IO.Path]::GetFullPath($SettingsFile)
}
$runtimeStatusPath = Join-Path $PSScriptRoot 'runtime-status.json'
$startupRequestPath = Join-Path $PSScriptRoot 'startup-request.json'
$script:applicationPath = if ([string]::IsNullOrWhiteSpace($ApplicationPath)) { Join-Path $PSScriptRoot '敏敏子倒计时.ps1' } else { [System.IO.Path]::GetFullPath($ApplicationPath) }
$script:singleInstanceMutex = $null
if (-not $ValidateOnly) {
    # 以应用根和设置文件确定实例域；测试副本可独立运行，但公开命令行不能绕过同一应用/设置组合的单实例保护。
    $instanceScope = '{0}|{1}' -f [System.IO.Path]::GetFullPath($PSScriptRoot).ToUpperInvariant(), $settingsPath.ToUpperInvariant()
    $scopeHasher = [System.Security.Cryptography.SHA256]::Create()
    try {
        $scopeHash = [System.BitConverter]::ToString($scopeHasher.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($instanceScope))).Replace('-', '').Substring(0, 24)
    }
    finally {
        $scopeHasher.Dispose()
    }
    $createdNew = $false
    $userSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $script:singleInstanceMutex = New-Object System.Threading.Mutex($true, "Local\MinminCountdown-$userSid-$scopeHash", [ref] $createdNew)
    if (-not $createdNew) {
        $script:singleInstanceMutex.Dispose()
        $script:singleInstanceMutex = $null
        return
    }
}

Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
Import-Module (Join-Path $PSScriptRoot 'Countdown.Core.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.Notes.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.Tags.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.Settings.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Countdown.Runtime.psm1') -Force

$script:countdowns = [System.Collections.ArrayList]::new()
$script:notes = [System.Collections.ArrayList]::new()
$script:tags = [System.Collections.ArrayList]::new()
$script:cardViews = @{}
$script:floatingViews = @{}
$script:editingId = $null
$script:settingsWereMigrated = $false
$script:lastSortSignature = ''
$script:showCompletedNotes = $false
$script:activeWorkspace = 'Countdowns'
$script:tagFilterMode = 'All'
$script:selectedTagIds = [System.Collections.ArrayList]::new()
$script:tagMatchMode = 'Any'
$script:countdownStatusFilter = 'All'
$script:noteStatusFilter = 'All'
$script:noteSearchText = ''
$script:countdownEditorTagIds = [System.Collections.ArrayList]::new()
$script:noteEditorTagIds = [System.Collections.ArrayList]::new()
$script:floatingNoteTagIds = [System.Collections.ArrayList]::new()
$script:editingNoteId = $null
$script:tagManagerVisible = $false
$script:displayPrecision = 'Second'
$script:salary = [decimal]0
$script:workStart = [TimeSpan]::FromHours(9)
$script:workEnd = [TimeSpan]::FromHours(18)
$script:workdayMap = $null
$script:holidayCalendarStatus = '节假日日历未就绪'
$script:workdayEndSignature = ''
$script:launchToken = if ($ValidateOnly) { $null } else { Get-CountdownStartupLaunchToken -Path $startupRequestPath }
$script:runtimeReceiptWritten = $false
$script:holidayCalendarClient = $null
$script:holidayCalendarTask = $null
$script:holidayCalendarPoller = $null
$script:holidayCalendarDeadline = $null
$script:holidayCalendarYear = $null
$script:holidayCalendarTaskFactory = $HolidayCalendarTaskFactory
$script:testCloseTimer = $null

[xml] $xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="敏敏子倒计时" Width="720" Height="860" MinWidth="660" MinHeight="700"
        WindowStartupLocation="CenterScreen" ResizeMode="CanResizeWithGrip"
        Background="{DynamicResource BackgroundBrush}" FontFamily="Microsoft YaHei UI" Topmost="True">
  <Window.Resources>
    <SolidColorBrush x:Key="CanvasBrush" Color="#FFFDFC"/>
    <SolidColorBrush x:Key="BackgroundBrush" Color="#FFF6EF"/>
    <SolidColorBrush x:Key="CardBrush" Color="#FFF0E4"/>
    <SolidColorBrush x:Key="NoteSurfaceBrush" Color="#FFF6D9"/>
    <SolidColorBrush x:Key="NoteLineBrush" Color="#E7C781"/>
    <SolidColorBrush x:Key="NoteInkBrush" Color="#725326"/>
    <SolidColorBrush x:Key="CompletedNoteBrush" Color="#A58B68"/>
    <SolidColorBrush x:Key="InkBrush" Color="#4A2D24"/>
    <SolidColorBrush x:Key="BodyBrush" Color="#765247"/>
    <SolidColorBrush x:Key="MutedBrush" Color="#A57B6C"/>
    <SolidColorBrush x:Key="HairlineBrush" Color="#F1D5C5"/>
    <SolidColorBrush x:Key="ActiveBrush" Color="#D86F52"/>
    <SolidColorBrush x:Key="FocusBrush" Color="#E99A76"/>
    <SolidColorBrush x:Key="ErrorBrush" Color="#C75748"/>
    <SolidColorBrush x:Key="SuccessBrush" Color="#E89A5D"/>
    <SolidColorBrush x:Key="SuccessTextBrush" Color="#92522E"/>
    <SolidColorBrush x:Key="SuccessSurfaceBrush" Color="#FFE3C7"/>
    <SolidColorBrush x:Key="ExpiredTextBrush" Color="#9D5E60"/>
    <SolidColorBrush x:Key="ExpiredSurfaceBrush" Color="#F9DFDB"/>
    <SolidColorBrush x:Key="DangerTextBrush" Color="#B95A4D"/>
    <Style TargetType="TextBox">
      <Setter Property="Height" Value="40"/><Setter Property="Padding" Value="12,0"/>
      <Setter Property="VerticalContentAlignment" Value="Center"/><Setter Property="FontSize" Value="14"/>
      <Setter Property="Foreground" Value="{StaticResource InkBrush}"/><Setter Property="Background" Value="{StaticResource CanvasBrush}"/>
      <Setter Property="BorderBrush" Value="{StaticResource HairlineBrush}"/><Setter Property="BorderThickness" Value="1"/>
    </Style>
    <Style TargetType="ComboBox">
      <Setter Property="Height" Value="40"/><Setter Property="Padding" Value="10,0"/>
      <Setter Property="VerticalContentAlignment" Value="Center"/><Setter Property="FontSize" Value="14"/>
      <Setter Property="Foreground" Value="{StaticResource InkBrush}"/><Setter Property="Background" Value="{StaticResource CanvasBrush}"/>
      <Setter Property="BorderBrush" Value="{StaticResource HairlineBrush}"/><Setter Property="BorderThickness" Value="1"/>
      <Setter Property="IsEditable" Value="False"/>
      <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox">
        <Grid>
          <Border x:Name="Surface" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="12">
            <Grid><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="38"/></Grid.ColumnDefinitions>
              <ContentPresenter Margin="12,0,0,0" VerticalAlignment="Center" Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}"/>
              <ToggleButton x:Name="ToggleButton" Grid.Column="1" IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}" ClickMode="Press" Background="Transparent" BorderThickness="0" Cursor="Hand">
                <ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border x:Name="ArrowSurface" Background="Transparent" CornerRadius="0,12,12,0"><Path Width="10" Height="6" Fill="{StaticResource BodyBrush}" Stretch="Fill" Data="M 0,0 L 10,0 L 5,6 Z" HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="ArrowSurface" Property="Background" Value="{StaticResource CardBrush}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></ToggleButton.Template>
              </ToggleButton>
            </Grid>
          </Border>
          <Popup x:Name="PART_Popup" Placement="Bottom" AllowsTransparency="True" Focusable="False" PopupAnimation="Fade" IsOpen="{TemplateBinding IsDropDownOpen}">
            <Border x:Name="DropDownBorder" Margin="0,6,0,0" MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}" MaxHeight="260" Padding="6" Background="{StaticResource CanvasBrush}" BorderBrush="{StaticResource HairlineBrush}" BorderThickness="1" CornerRadius="12">
              <Border.Effect><DropShadowEffect Color="#8A5240" BlurRadius="16" ShadowDepth="3" Opacity="0.18"/></Border.Effect>
              <ScrollViewer CanContentScroll="True" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled"><ItemsPresenter/></ScrollViewer>
            </Border>
          </Popup>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Surface" Property="Background" Value="{StaticResource CardBrush}"/></Trigger>
          <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Surface" Property="BorderBrush" Value="{StaticResource FocusBrush}"/><Setter TargetName="Surface" Property="BorderThickness" Value="2"/></Trigger>
          <Trigger Property="IsEnabled" Value="False"><Setter TargetName="Surface" Property="Opacity" Value="0.55"/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>
    <Style TargetType="ComboBoxItem">
      <Setter Property="Padding" Value="10,8"/><Setter Property="Foreground" Value="{StaticResource InkBrush}"/><Setter Property="Background" Value="Transparent"/>
      <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem"><Border x:Name="ItemSurface" Background="{TemplateBinding Background}" CornerRadius="8" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter TargetName="ItemSurface" Property="Background" Value="{StaticResource CardBrush}"/></Trigger><Trigger Property="IsSelected" Value="True"><Setter TargetName="ItemSurface" Property="Background" Value="{StaticResource SuccessSurfaceBrush}"/><Setter Property="Foreground" Value="{StaticResource SuccessTextBrush}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
    </Style>
    <Style TargetType="DatePicker">
      <Setter Property="Height" Value="40"/><Setter Property="FontSize" Value="14"/>
      <Setter Property="Foreground" Value="{StaticResource InkBrush}"/><Setter Property="Background" Value="{StaticResource CanvasBrush}"/>
      <Setter Property="BorderBrush" Value="{StaticResource HairlineBrush}"/><Setter Property="BorderThickness" Value="1"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="DatePicker">
            <Grid>
              <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="40"/></Grid.ColumnDefinitions>
              <DatePickerTextBox x:Name="PART_TextBox" Grid.Column="0" Padding="12,0" IsReadOnly="True" Focusable="False"
                 VerticalContentAlignment="Center" Background="{StaticResource CanvasBrush}" Foreground="{StaticResource InkBrush}" BorderBrush="{StaticResource HairlineBrush}"
                 BorderThickness="1,1,0,1" Text="{Binding SelectedDate, RelativeSource={RelativeSource TemplatedParent}, StringFormat={}{0:yyyy年M月d日}}"/>
              <Button x:Name="PART_Button" Grid.Column="1" Background="{StaticResource CanvasBrush}" BorderBrush="{StaticResource HairlineBrush}" BorderThickness="1" Cursor="Hand" ToolTip="打开日历">
                <Path Width="10" Height="6" Fill="{StaticResource BodyBrush}" Stretch="Fill" Data="M 0,0 L 10,0 L 5,6 Z"/>
              </Button>
              <Popup x:Name="PART_Popup" Grid.ColumnSpan="2" Placement="Bottom" StaysOpen="False" AllowsTransparency="True" Focusable="False">
                <Border Margin="0,6,0,0" Padding="8" Background="{StaticResource CanvasBrush}" BorderBrush="{StaticResource HairlineBrush}" BorderThickness="1" CornerRadius="12">
                  <Border.Effect><DropShadowEffect Color="#111111" BlurRadius="16" ShadowDepth="3" Opacity="0.14"/></Border.Effect>
                  <Calendar x:Name="PART_Calendar"/>
                </Border>
              </Popup>
            </Grid>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key="PrimaryButton" TargetType="Button">
      <Setter Property="Height" Value="40"/><Setter Property="Padding" Value="18,0"/>
      <Setter Property="Foreground" Value="{StaticResource CanvasBrush}"/><Setter Property="Background" Value="{StaticResource InkBrush}"/>
      <Setter Property="BorderThickness" Value="0"/><Setter Property="FontSize" Value="14"/>
      <Setter Property="FontWeight" Value="SemiBold"/><Setter Property="Cursor" Value="Hand"/>
      <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
        <Border x:Name="Surface" Background="{TemplateBinding Background}" CornerRadius="12" Padding="{TemplateBinding Padding}">
          <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Surface" Property="Background" Value="{StaticResource ActiveBrush}"/></Trigger>
          <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Surface" Property="BorderBrush" Value="{StaticResource FocusBrush}"/><Setter TargetName="Surface" Property="BorderThickness" Value="2"/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>
    <Style x:Key="SecondaryButton" TargetType="Button">
      <Setter Property="Height" Value="40"/><Setter Property="Padding" Value="14,0"/>
      <Setter Property="Foreground" Value="{StaticResource InkBrush}"/><Setter Property="Background" Value="{StaticResource CanvasBrush}"/>
      <Setter Property="BorderBrush" Value="{StaticResource HairlineBrush}"/><Setter Property="BorderThickness" Value="1"/>
      <Setter Property="FontSize" Value="13"/><Setter Property="FontWeight" Value="SemiBold"/><Setter Property="Cursor" Value="Hand"/>
      <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
        <Border x:Name="Surface" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="12" Padding="{TemplateBinding Padding}">
          <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Surface" Property="Background" Value="{StaticResource CardBrush}"/></Trigger>
          <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Surface" Property="BorderBrush" Value="{StaticResource FocusBrush}"/><Setter TargetName="Surface" Property="BorderThickness" Value="2"/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>
  </Window.Resources>

  <Grid Margin="24,20,24,18">
    <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="*"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
    <Grid Grid.Row="0">
      <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
      <StackPanel>
        <TextBlock Text="敏敏子的时光小屋" FontSize="26" FontWeight="SemiBold" Foreground="{StaticResource InkBrush}"/>
        <TextBlock Text="重要时刻和今日努力，都在这里慢慢长大" Margin="0,5,0,0" FontSize="13" Foreground="{StaticResource MutedBrush}"/>
      </StackPanel>
      <CheckBox x:Name="TopmostCheck" Grid.Column="1" Content="窗口置顶" IsChecked="True" VerticalAlignment="Center" FontSize="13" Foreground="{StaticResource BodyBrush}"/>
    </Grid>

    <Grid x:Name="WorkspaceSwitcher" Grid.Row="1" Margin="0,16,0,10">
      <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/><ColumnDefinition Width="Auto"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
      <TextBlock x:Name="WorkspaceTitle" Text="我的倒计时" FontSize="16" FontWeight="SemiBold" Foreground="{StaticResource InkBrush}" VerticalAlignment="Center"/>
      <Button x:Name="ShowCountdownsButton" Grid.Column="1" Width="74" Height="32" Margin="8,0,0,0" Style="{StaticResource SecondaryButton}" Content="倒计时" ToolTip="切换到倒计时管理"/>
      <Button x:Name="ShowNotesButton" Grid.Column="2" Width="62" Height="32" Margin="6,0,0,0" Style="{StaticResource SecondaryButton}" Content="便签" ToolTip="切换到便签管理"/>
      <TextBlock x:Name="CountdownCount" Grid.Column="3" Margin="10,0,0,0" FontSize="12" Foreground="{StaticResource MutedBrush}" VerticalAlignment="Center"/>
    </Grid>

    <Border x:Name="CountdownEditorWorkspace" Grid.Row="2" Margin="0,8,0,0" Padding="20" Background="{StaticResource CardBrush}" CornerRadius="12" BorderBrush="{StaticResource HairlineBrush}" BorderThickness="1">
      <Grid>
        <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
        <TextBlock x:Name="EditorTitle" Text="新增倒计时" FontSize="18" FontWeight="SemiBold" Foreground="{StaticResource InkBrush}"/>
        <Grid Grid.Row="1" Margin="0,14,0,0">
          <Grid.ColumnDefinitions><ColumnDefinition Width="220"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
          <StackPanel>
            <TextBlock Text="名称" Margin="1,0,0,6" FontSize="12" FontWeight="SemiBold" Foreground="{StaticResource BodyBrush}"/>
            <TextBox x:Name="CountdownName" MaxLength="30" ToolTip="输入唯一名称，最多 30 个字符"/>
          </StackPanel>
          <StackPanel Grid.Column="1" Margin="12,0,0,0">
            <TextBlock Text="截止日期" Margin="1,0,0,6" FontSize="12" FontWeight="SemiBold" Foreground="{StaticResource BodyBrush}"/>
            <DatePicker x:Name="TargetDate" SelectedDateFormat="Long" ToolTip="点击选择北京时间日期"/>
          </StackPanel>
        </Grid>
        <Grid Grid.Row="2" Margin="0,12,0,0">
          <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="*"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
          <StackPanel><TextBlock Text="时" Margin="1,0,0,6" FontSize="12" FontWeight="SemiBold" Foreground="{StaticResource BodyBrush}"/><ComboBox x:Name="TargetHour"/></StackPanel>
          <StackPanel Grid.Column="1" Margin="8,0,0,0"><TextBlock Text="分" Margin="1,0,0,6" FontSize="12" FontWeight="SemiBold" Foreground="{StaticResource BodyBrush}"/><ComboBox x:Name="TargetMinute"/></StackPanel>
          <StackPanel Grid.Column="2" Margin="8,0,0,0"><TextBlock Text="秒" Margin="1,0,0,6" FontSize="12" FontWeight="SemiBold" Foreground="{StaticResource BodyBrush}"/><ComboBox x:Name="TargetSecond"/></StackPanel>
          <Button x:Name="SetNowButton" Grid.Column="3" Width="94" Margin="12,18,0,0" Style="{StaticResource SecondaryButton}" Content="设为现在" ToolTip="使用当前北京时间"/>
        </Grid>
        <Grid Grid.Row="3" Margin="0,14,0,0">
          <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
          <TextBlock x:Name="MessageText" VerticalAlignment="Center" FontSize="12" Foreground="{StaticResource ErrorBrush}" TextWrapping="Wrap"/>
          <Button x:Name="CancelButton" Grid.Column="1" Width="76" Margin="8,0,0,0" Style="{StaticResource SecondaryButton}" Content="取消" Visibility="Collapsed"/>
          <Button x:Name="SaveButton" Grid.Column="2" Width="112" Margin="8,0,0,0" Style="{StaticResource PrimaryButton}" Content="添加倒计时" IsDefault="True"/>
        </Grid>
      </Grid>
    </Border>

    <Border x:Name="PreferencesWorkspace" Grid.Row="3" Margin="0,16,0,0" Padding="18" Background="{StaticResource CardBrush}" CornerRadius="16" BorderBrush="{StaticResource HairlineBrush}" BorderThickness="1">
      <Grid>
        <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
        <Grid>
          <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
          <StackPanel><TextBlock Text="悬浮窗显示精度" Margin="1,0,0,6" FontSize="12" FontWeight="SemiBold" Foreground="{StaticResource BodyBrush}"/><ComboBox x:Name="DisplayPrecision" ToolTip="选择悬浮窗倒计时显示到哪一位"/></StackPanel>
          <StackPanel Grid.Column="1" Margin="12,0,0,0"><TextBlock Text="月薪（元）" Margin="1,0,0,6" FontSize="12" FontWeight="SemiBold" Foreground="{StaticResource BodyBrush}"/><TextBox x:Name="MonthlySalary" ToolTip="用于计算本工作日截至此刻已赚收入"/></StackPanel>
        </Grid>
        <Grid Grid.Row="1" Margin="0,12,0,0">
          <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
          <StackPanel><TextBlock Text="上班时间" Margin="1,0,0,6" FontSize="12" FontWeight="SemiBold" Foreground="{StaticResource BodyBrush}"/><TextBox x:Name="WorkStart" ToolTip="输入 09:00 这样的 24 小时制时间"/></StackPanel>
          <StackPanel Grid.Column="1" Margin="12,0,0,0"><TextBlock Text="下班时间" Margin="1,0,0,6" FontSize="12" FontWeight="SemiBold" Foreground="{StaticResource BodyBrush}"/><TextBox x:Name="WorkEnd" ToolTip="输入 18:00 这样的 24 小时制时间"/></StackPanel>
          <Button x:Name="SavePreferencesButton" Grid.Column="2" Width="104" Margin="12,18,0,0" Style="{StaticResource PrimaryButton}" Content="保存设置" ToolTip="保存显示和收入计算设置"/>
        </Grid>
      </Grid>
    </Border>

    <Grid Grid.Row="4">
      <Grid x:Name="CountdownWorkspace">
        <TextBlock x:Name="EmptyState" Text="还没有倒计时。先在上方添加一个重要时刻。" Foreground="{StaticResource MutedBrush}" FontSize="14" HorizontalAlignment="Center" VerticalAlignment="Center"/>
        <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
          <ItemsControl x:Name="CountdownList"/>
        </ScrollViewer>
      </Grid>
      <Grid x:Name="NotesWorkspace" Visibility="Collapsed">
        <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="*"/></Grid.RowDefinitions>
        <Grid>
          <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
          <TextBox x:Name="NoteInput" MaxLength="140" ToolTip="写下一件要做的事，最多 140 个字符"/>
          <Button x:Name="AddNoteButton" Grid.Column="1" Width="76" Margin="8,0,0,0" Style="{StaticResource PrimaryButton}" Content="添加" ToolTip="添加便签"/>
          <Button x:Name="CancelNoteEditButton" Grid.Column="2" Width="64" Margin="8,0,0,0" Style="{StaticResource SecondaryButton}" Content="取消" ToolTip="取消编辑便签" Visibility="Collapsed"/>
        </Grid>
        <TextBlock x:Name="NoteMessageText" Grid.Row="1" Margin="2,8,2,0" MinHeight="18" FontSize="12" Foreground="{StaticResource ErrorBrush}" TextWrapping="Wrap"/>
        <Grid Grid.Row="2" Margin="0,4,0,0">
          <TextBlock x:Name="NoteEmptyState" Text="还没有便签。写下一件现在最想完成的事吧。" Foreground="{StaticResource MutedBrush}" FontSize="14" HorizontalAlignment="Center" VerticalAlignment="Center"/>
          <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
            <StackPanel>
              <TextBlock x:Name="ActiveNotesHeading" Margin="2,0,0,6" FontSize="12" FontWeight="SemiBold" Foreground="{StaticResource NoteInkBrush}" Visibility="Collapsed"/>
              <ItemsControl x:Name="NoteList"/>
              <Button x:Name="ToggleCompletedNotesButton" Margin="0,8,0,0" Style="{StaticResource SecondaryButton}" Content="已完成" Visibility="Collapsed"/>
              <ItemsControl x:Name="CompletedNoteList" Visibility="Collapsed"/>
            </StackPanel>
          </ScrollViewer>
        </Grid>
      </Grid>
    </Grid>

    <Grid Grid.Row="5" Margin="0,12,0,0">
      <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
      <Ellipse Width="7" Height="7" Fill="{StaticResource SuccessBrush}" VerticalAlignment="Center"/>
      <TextBlock x:Name="NowText" Grid.Column="1" Margin="8,0,0,0" FontSize="12" Foreground="{StaticResource MutedBrush}"/>
    </Grid>
  </Grid>
</Window>
'@

$reader = New-Object System.Xml.XmlNodeReader $xaml
$window = [Windows.Markup.XamlReader]::Load($reader)
$iconPath = Join-Path $PSScriptRoot 'countdown-icon.ico'
if (Test-Path -LiteralPath $iconPath) { $window.Icon = [System.Windows.Media.Imaging.BitmapFrame]::Create((New-Object System.Uri($iconPath))) }

$countdownName = $window.FindName('CountdownName')
$targetDate = $window.FindName('TargetDate')
$targetHour = $window.FindName('TargetHour')
$targetMinute = $window.FindName('TargetMinute')
$targetSecond = $window.FindName('TargetSecond')
$setNowButton = $window.FindName('SetNowButton')
$saveButton = $window.FindName('SaveButton')
$cancelButton = $window.FindName('CancelButton')
$messageText = $window.FindName('MessageText')
$nowText = $window.FindName('NowText')
$topmostCheck = $window.FindName('TopmostCheck')
$countdownEditorWorkspace = $window.FindName('CountdownEditorWorkspace')
$preferencesWorkspace = $window.FindName('PreferencesWorkspace')
$editorTitle = $window.FindName('EditorTitle')
$workspaceTitle = $window.FindName('WorkspaceTitle')
$workspaceSwitcher = $window.FindName('WorkspaceSwitcher')
$showCountdownsButton = $window.FindName('ShowCountdownsButton')
$showNotesButton = $window.FindName('ShowNotesButton')
$countdownWorkspace = $window.FindName('CountdownWorkspace')
$notesWorkspace = $window.FindName('NotesWorkspace')
$countdownList = $window.FindName('CountdownList')
$countdownCount = $window.FindName('CountdownCount')
$emptyState = $window.FindName('EmptyState')
$noteInput = $window.FindName('NoteInput')
$addNoteButton = $window.FindName('AddNoteButton')
$cancelNoteEditButton = $window.FindName('CancelNoteEditButton')
$noteMessageText = $window.FindName('NoteMessageText')
$noteEmptyState = $window.FindName('NoteEmptyState')
$noteList = $window.FindName('NoteList')
$activeNotesHeading = $window.FindName('ActiveNotesHeading')
$toggleCompletedNotesButton = $window.FindName('ToggleCompletedNotesButton')
$completedNoteList = $window.FindName('CompletedNoteList')
$displayPrecisionInput = $window.FindName('DisplayPrecision')
$monthlySalary = $window.FindName('MonthlySalary')
$workStartInput = $window.FindName('WorkStart')
$workEndInput = $window.FindName('WorkEnd')
$savePreferencesButton = $window.FindName('SavePreferencesButton')
$canvasBrush = $window.FindResource('CanvasBrush')
$inkBrush = $window.FindResource('InkBrush')
$mutedBrush = $window.FindResource('MutedBrush')
$hairlineBrush = $window.FindResource('HairlineBrush')
$errorBrush = $window.FindResource('ErrorBrush')
$successTextBrush = $window.FindResource('SuccessTextBrush')
$successSurfaceBrush = $window.FindResource('SuccessSurfaceBrush')
$expiredTextBrush = $window.FindResource('ExpiredTextBrush')
$expiredSurfaceBrush = $window.FindResource('ExpiredSurfaceBrush')
$dangerTextBrush = $window.FindResource('DangerTextBrush')
$noteSurfaceBrush = $window.FindResource('NoteSurfaceBrush')
$noteLineBrush = $window.FindResource('NoteLineBrush')
$noteInkBrush = $window.FindResource('NoteInkBrush')
$completedNoteBrush = $window.FindResource('CompletedNoteBrush')

[xml] $floatingXaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="敏敏子倒计时悬浮窗" Width="368" SizeToContent="Height" ResizeMode="NoResize"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent" Topmost="True"
        ShowInTaskbar="False" FontFamily="Microsoft YaHei UI">
  <Border Background="#FFFDFC" BorderBrush="#F1D5C5" BorderThickness="1" CornerRadius="18" Padding="16">
    <Border.Effect><DropShadowEffect Color="#8A5240" BlurRadius="20" ShadowDepth="5" Opacity="0.20"/></Border.Effect>
    <Grid>
      <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
      <Grid>
        <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
        <Grid x:Name="FloatingDragArea" Cursor="SizeAll">
          <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
          <TextBlock Text="今天也在闪闪发光" FontSize="16" FontWeight="SemiBold" Foreground="#4A2D24" TextTrimming="CharacterEllipsis"/>
          <TextBlock x:Name="FloatingCount" Grid.Column="1" FontSize="11" Foreground="#A57B6C" VerticalAlignment="Center"/>
        </Grid>
        <Button x:Name="ShowFloatingCountdownsButton" Grid.Column="1" Width="52" Height="28" Margin="8,0,0,0" Content="倒计时" FontSize="11" FontWeight="SemiBold" Cursor="Hand" Background="#FFE3C7" Foreground="#92522E" BorderBrush="#F1D5C5" ToolTip="显示进行中的倒计时"/>
        <Button x:Name="ShowFloatingNotesButton" Grid.Column="2" Width="44" Height="28" Margin="6,0,0,0" Content="便签" FontSize="11" FontWeight="SemiBold" Cursor="Hand" Background="#FFF0E4" Foreground="#92522E" BorderBrush="#F1D5C5" ToolTip="显示桌面便签"/>
      </Grid>
      <Border x:Name="FloatingCountdownSummary" Grid.Row="1" Margin="0,10,0,10" Padding="12,8" Background="#FFF0E4" CornerRadius="12">
        <TextBlock x:Name="FloatingIncome" Text="今日已赚 ￥0.00" FontSize="16" FontWeight="SemiBold" Foreground="#92522E"/>
      </Border>
      <Grid x:Name="FloatingNotesEditor" Grid.Row="1" Margin="0,10,0,10" Visibility="Collapsed">
        <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
        <Grid>
          <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
          <TextBox x:Name="FloatingNoteInput" Height="36" Padding="10,0" VerticalContentAlignment="Center" FontSize="13" Foreground="#4A2D24" Background="#FFFFF8" BorderBrush="#E7C681" BorderThickness="1" MaxLength="140" ToolTip="写下待办，按回车或点击添加"/>
          <Button x:Name="FloatingAddNoteButton" Grid.Column="1" Width="58" Height="36" Margin="8,0,0,0" Content="添加" FontSize="12" FontWeight="SemiBold" Cursor="Hand" Background="#D86F52" Foreground="#FFFDFC" BorderThickness="0" ToolTip="添加便签"/>
        </Grid>
        <TextBlock x:Name="FloatingNoteMessage" Grid.Row="1" Margin="2,6,2,0" MinHeight="16" FontSize="11" Foreground="#B95A4D" TextWrapping="Wrap"/>
      </Grid>
      <ScrollViewer Grid.Row="2" MaxHeight="480" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
        <ItemsControl x:Name="FloatingList"/>
      </ScrollViewer>
      <Grid Grid.Row="3" Margin="0,10,0,0">
        <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
        <TextBlock Text="北京时间 UTC+8" FontSize="11" Foreground="#A57B6C" VerticalAlignment="Center"/>
        <Button x:Name="OpenManagerButton" Grid.Column="1" Width="62" Height="30" Margin="8,0,0,0" Content="管理" FontSize="12" Cursor="Hand" ToolTip="打开管理窗口" Background="#FFE3C7" Foreground="#92522E" BorderBrush="#F1D5C5"/>
        <Button x:Name="CloseFloatingButton" Grid.Column="2" Width="30" Height="30" Margin="6,0,0,0" Content="×" FontSize="16" Cursor="Hand" ToolTip="退出应用" Background="#FFF0E4" Foreground="#92522E" BorderBrush="#F1D5C5"/>
      </Grid>
    </Grid>
  </Border>
</Window>
'@
$floatingReader = New-Object System.Xml.XmlNodeReader $floatingXaml
$floatingWindow = [Windows.Markup.XamlReader]::Load($floatingReader)
$floatingDragArea = $floatingWindow.FindName('FloatingDragArea')
$floatingCount = $floatingWindow.FindName('FloatingCount')
$showFloatingCountdownsButton = $floatingWindow.FindName('ShowFloatingCountdownsButton')
$showFloatingNotesButton = $floatingWindow.FindName('ShowFloatingNotesButton')
$floatingCountdownSummary = $floatingWindow.FindName('FloatingCountdownSummary')
$floatingNotesEditor = $floatingWindow.FindName('FloatingNotesEditor')
$floatingIncome = $floatingWindow.FindName('FloatingIncome')
$floatingList = $floatingWindow.FindName('FloatingList')
$floatingNoteInput = $floatingWindow.FindName('FloatingNoteInput')
$floatingAddNoteButton = $floatingWindow.FindName('FloatingAddNoteButton')
$floatingNoteMessage = $floatingWindow.FindName('FloatingNoteMessage')
$openManagerButton = $floatingWindow.FindName('OpenManagerButton')
$closeFloatingButton = $floatingWindow.FindName('CloseFloatingButton')

foreach ($hour in 0..23) { [void] $targetHour.Items.Add(('{0:D2}' -f $hour)) }
foreach ($minute in 0..59) { [void] $targetMinute.Items.Add(('{0:D2}' -f $minute)); [void] $targetSecond.Items.Add(('{0:D2}' -f $minute)) }
$displayPrecisionInput.DisplayMemberPath = 'Label'
$displayPrecisionInput.SelectedValuePath = 'Value'
foreach ($item in @(
    [pscustomobject]@{ Value = 'Day'; Label = '精确到天' },
    [pscustomobject]@{ Value = 'Hour'; Label = '精确到小时' },
    [pscustomobject]@{ Value = 'Minute'; Label = '精确到分钟' },
    [pscustomobject]@{ Value = 'Second'; Label = '精确到秒' }
)) { [void] $displayPrecisionInput.Items.Add($item) }

function Set-AccessibleName {
    param(
        [Parameter(Mandatory)] [System.Windows.DependencyObject] $Control,
        [Parameter(Mandatory)] [string] $Name
    )

    [System.Windows.Automation.AutomationProperties]::SetName($Control, $Name)
}

function New-TagActionButton {
    param(
        [Parameter(Mandatory)] [string] $Content,
        [Parameter(Mandatory)] [string] $ToolTip,
        [int] $Width = 0
    )

    $button = New-Object System.Windows.Controls.Button
    $button.Content = $Content
    $button.ToolTip = $ToolTip
    $button.Height = 30
    if ($Width -gt 0) { $button.Width = $Width }
    else { $button.MinWidth = 58 }
    $button.Margin = '0,0,6,6'
    $button.Padding = '10,0'
    $button.Cursor = 'Hand'
    $button.Style = $window.FindResource('SecondaryButton')
    Set-AccessibleName -Control $button -Name $ToolTip
    $button
}

function New-TagEditorPanel {
    param(
        [Parameter(Mandatory)] [string] $Label,
        [Parameter(Mandatory)] [string] $ToolTip
    )

    $panel = New-Object System.Windows.Controls.StackPanel
    $panel.Margin = '0,14,0,0'
    $heading = New-Object System.Windows.Controls.TextBlock
    $heading.Text = $Label
    $heading.Margin = '1,0,0,6'
    $heading.FontSize = 12
    $heading.FontWeight = 'SemiBold'
    $heading.Foreground = $window.FindResource('BodyBrush')
    $heading.ToolTip = $ToolTip
    Set-AccessibleName -Control $heading -Name $ToolTip
    $panel.Children.Add($heading) | Out-Null
    $search = New-Object System.Windows.Controls.TextBox
    $search.MaxLength = 30
    $search.Height = 32
    $search.Margin = '0,0,0,6'
    $search.ToolTip = '按标签名称搜索可选标签'
    Set-AccessibleName -Control $search -Name ("搜索$Label")
    $panel.Children.Add($search) | Out-Null
    $selector = New-Object System.Windows.Controls.WrapPanel
    $selector.Margin = '0,0,0,6'
    Set-AccessibleName -Control $selector -Name ("$Label 选择器")
    $panel.Children.Add($selector) | Out-Null
    $newTagRow = New-Object System.Windows.Controls.Grid
    $newTagRow.Margin = '0,2,0,0'
    $newTagRow.ColumnDefinitions.Add((New-Object System.Windows.Controls.ColumnDefinition -Property @{ Width = '*' })) | Out-Null
    $newTagRow.ColumnDefinitions.Add((New-Object System.Windows.Controls.ColumnDefinition -Property @{ Width = 'Auto' })) | Out-Null
    $newTagRow.ColumnDefinitions.Add((New-Object System.Windows.Controls.ColumnDefinition -Property @{ Width = 'Auto' })) | Out-Null
    $tagNameInput = New-Object System.Windows.Controls.TextBox
    $tagNameInput.MaxLength = 30
    $tagNameInput.Height = 34
    $tagNameInput.ToolTip = "输入新标签名称后创建并选中"
    Set-AccessibleName -Control $tagNameInput -Name ("$Label 新标签名称")
    $create = New-TagActionButton -Content '创建并选中' -ToolTip "创建标签并用于$Label" -Width 94
    $create.Height = 34
    [System.Windows.Controls.Grid]::SetColumn($create, 1)
    $manage = New-TagActionButton -Content '管理标签' -ToolTip '创建、改名或删除标签' -Width 82
    $manage.Height = 34
    [System.Windows.Controls.Grid]::SetColumn($manage, 2)
    $newTagRow.Children.Add($tagNameInput) | Out-Null
    $newTagRow.Children.Add($create) | Out-Null
    $newTagRow.Children.Add($manage) | Out-Null
    $panel.Children.Add($newTagRow) | Out-Null
    [pscustomobject]@{ Panel = $panel; Search = $search; Selector = $selector; Input = $tagNameInput; Create = $create; Manage = $manage }
}

function Initialize-TagInterface {
    # 过滤条属于当前工作区，插入既有切换器下方，不新增第三个工作区。
    foreach ($unused in 0..2) {
        $workspaceSwitcher.RowDefinitions.Add((New-Object System.Windows.Controls.RowDefinition -Property @{ Height = 'Auto' })) | Out-Null
    }

    $script:tagFilterBar = New-Object System.Windows.Controls.StackPanel
    $script:tagFilterBar.Margin = '0,4,0,0'
    [System.Windows.Controls.Grid]::SetRow($script:tagFilterBar, 1)
    [System.Windows.Controls.Grid]::SetColumnSpan($script:tagFilterBar, 4)
    $filterTop = New-Object System.Windows.Controls.WrapPanel
    $filterTop.Margin = '0,0,0,2'
    $filterTitle = New-Object System.Windows.Controls.TextBlock
    $filterTitle.Text = '筛选'
    $filterTitle.FontSize = 12
    $filterTitle.FontWeight = 'SemiBold'
    $filterTitle.Foreground = $mutedBrush
    $filterTitle.VerticalAlignment = 'Center'
    $filterTitle.Margin = '0,0,10,6'
    $filterTop.Children.Add($filterTitle) | Out-Null
    $script:tagFilterAllButton = New-TagActionButton -Content '全部' -ToolTip '显示所有标签状态的记录' -Width 54
    $script:tagFilterUntaggedButton = New-TagActionButton -Content '无标签' -ToolTip '仅显示没有标签的记录' -Width 64
    $script:tagMatchAnyButton = New-TagActionButton -Content '任一' -ToolTip '多选标签时命中任意一个标签' -Width 54
    $script:tagMatchAllButton = New-TagActionButton -Content '全部匹配' -ToolTip '多选标签时必须同时命中全部标签' -Width 78
    $script:tagManagerToggleButton = New-TagActionButton -Content '管理标签' -ToolTip '创建、改名或删除共享标签' -Width 82
    foreach ($button in @($script:tagFilterAllButton, $script:tagFilterUntaggedButton, $script:tagMatchAnyButton, $script:tagMatchAllButton, $script:tagManagerToggleButton)) { $filterTop.Children.Add($button) | Out-Null }
    $script:tagFilterBar.Children.Add($filterTop) | Out-Null

    $statusRow = New-Object System.Windows.Controls.WrapPanel
    $statusRow.Margin = '0,0,0,2'
    $script:countdownStatusFilterPanel = New-Object System.Windows.Controls.WrapPanel
    $countdownStatusLabel = New-Object System.Windows.Controls.TextBlock
    $countdownStatusLabel.Text = '倒计时状态'
    $countdownStatusLabel.FontSize = 12
    $countdownStatusLabel.Foreground = $mutedBrush
    $countdownStatusLabel.VerticalAlignment = 'Center'
    $countdownStatusLabel.Margin = '0,0,8,6'
    $script:countdownStatusFilterPanel.Children.Add($countdownStatusLabel) | Out-Null
    $script:countdownStatusAllButton = New-TagActionButton -Content '全部状态' -ToolTip '显示进行中和已到期倒计时' -Width 72
    $script:countdownStatusActiveButton = New-TagActionButton -Content '进行中' -ToolTip '仅显示进行中的倒计时' -Width 64
    $script:countdownStatusExpiredButton = New-TagActionButton -Content '已到期' -ToolTip '仅显示已到期的倒计时' -Width 64
    foreach ($button in @($script:countdownStatusAllButton, $script:countdownStatusActiveButton, $script:countdownStatusExpiredButton)) { $script:countdownStatusFilterPanel.Children.Add($button) | Out-Null }
    $statusRow.Children.Add($script:countdownStatusFilterPanel) | Out-Null

    $script:noteStatusFilterPanel = New-Object System.Windows.Controls.WrapPanel
    $noteStatusLabel = New-Object System.Windows.Controls.TextBlock
    $noteStatusLabel.Text = '便签状态'
    $noteStatusLabel.FontSize = 12
    $noteStatusLabel.Foreground = $mutedBrush
    $noteStatusLabel.VerticalAlignment = 'Center'
    $noteStatusLabel.Margin = '0,0,8,6'
    $script:noteStatusFilterPanel.Children.Add($noteStatusLabel) | Out-Null
    $script:noteStatusAllButton = New-TagActionButton -Content '全部状态' -ToolTip '显示未完成和已完成便签' -Width 72
    $script:noteStatusActiveButton = New-TagActionButton -Content '未完成' -ToolTip '仅显示未完成便签' -Width 64
    $script:noteStatusCompletedButton = New-TagActionButton -Content '已完成' -ToolTip '仅显示已完成便签' -Width 64
    foreach ($button in @($script:noteStatusAllButton, $script:noteStatusActiveButton, $script:noteStatusCompletedButton)) { $script:noteStatusFilterPanel.Children.Add($button) | Out-Null }
    $script:noteSearchInput = New-Object System.Windows.Controls.TextBox
    $script:noteSearchInput.Width = 180
    $script:noteSearchInput.Height = 30
    $script:noteSearchInput.Margin = '8,0,0,6'
    $script:noteSearchInput.ToolTip = '按便签内容搜索当前筛选结果'
    Set-AccessibleName -Control $script:noteSearchInput -Name '搜索便签内容'
    $script:noteStatusFilterPanel.Children.Add($script:noteSearchInput) | Out-Null
    $statusRow.Children.Add($script:noteStatusFilterPanel) | Out-Null
    $script:tagFilterBar.Children.Add($statusRow) | Out-Null

    $script:tagFilterTagsPanel = New-Object System.Windows.Controls.WrapPanel
    $script:tagFilterTagsPanel.Margin = '0,0,0,2'
    Set-AccessibleName -Control $script:tagFilterTagsPanel -Name '按标签筛选'
    $script:tagFilterBar.Children.Add($script:tagFilterTagsPanel) | Out-Null
    $workspaceSwitcher.Children.Add($script:tagFilterBar) | Out-Null

    $script:tagManagerPanel = New-Object System.Windows.Controls.Border
    $script:tagManagerPanel.Margin = '0,4,0,2'
    $script:tagManagerPanel.Padding = 12
    $script:tagManagerPanel.Background = $canvasBrush
    $script:tagManagerPanel.BorderBrush = $hairlineBrush
    $script:tagManagerPanel.BorderThickness = 1
    $script:tagManagerPanel.CornerRadius = 8
    $script:tagManagerPanel.Visibility = 'Collapsed'
    [System.Windows.Controls.Grid]::SetRow($script:tagManagerPanel, 2)
    [System.Windows.Controls.Grid]::SetColumnSpan($script:tagManagerPanel, 4)
    $managerStack = New-Object System.Windows.Controls.StackPanel
    $managerTitle = New-Object System.Windows.Controls.TextBlock
    $managerTitle.Text = '管理标签'
    $managerTitle.FontSize = 13
    $managerTitle.FontWeight = 'SemiBold'
    $managerTitle.Foreground = $inkBrush
    $managerTitle.Margin = '0,0,0,8'
    $managerStack.Children.Add($managerTitle) | Out-Null
    $managerCreateRow = New-Object System.Windows.Controls.Grid
    $managerCreateRow.ColumnDefinitions.Add((New-Object System.Windows.Controls.ColumnDefinition -Property @{ Width = '*' })) | Out-Null
    $managerCreateRow.ColumnDefinitions.Add((New-Object System.Windows.Controls.ColumnDefinition -Property @{ Width = 'Auto' })) | Out-Null
    $script:tagManagerNewTagInput = New-Object System.Windows.Controls.TextBox
    $script:tagManagerNewTagInput.MaxLength = 30
    $script:tagManagerNewTagInput.Height = 34
    $script:tagManagerNewTagInput.ToolTip = '输入新标签名称'
    Set-AccessibleName -Control $script:tagManagerNewTagInput -Name '新标签名称'
    $script:tagManagerCreateButton = New-TagActionButton -Content '创建标签' -ToolTip '创建共享标签' -Width 78
    $script:tagManagerCreateButton.Height = 34
    [System.Windows.Controls.Grid]::SetColumn($script:tagManagerCreateButton, 1)
    $managerCreateRow.Children.Add($script:tagManagerNewTagInput) | Out-Null
    $managerCreateRow.Children.Add($script:tagManagerCreateButton) | Out-Null
    $managerStack.Children.Add($managerCreateRow) | Out-Null
    $script:tagManagerMessage = New-Object System.Windows.Controls.TextBlock
    $script:tagManagerMessage.MinHeight = 16
    $script:tagManagerMessage.Margin = '2,5,2,3'
    $script:tagManagerMessage.FontSize = 11
    $script:tagManagerMessage.Foreground = $mutedBrush
    $managerStack.Children.Add($script:tagManagerMessage) | Out-Null
    $script:tagManagerList = New-Object System.Windows.Controls.ItemsControl
    Set-AccessibleName -Control $script:tagManagerList -Name '标签列表'
    $managerStack.Children.Add($script:tagManagerList) | Out-Null
    $script:tagManagerPanel.Child = $managerStack
    $workspaceSwitcher.Children.Add($script:tagManagerPanel) | Out-Null

    $countdownEditorGrid = [System.Windows.Controls.Grid] $countdownEditorWorkspace.Child
    foreach ($child in @($countdownEditorGrid.Children | Where-Object { [System.Windows.Controls.Grid]::GetRow($_) -eq 3 })) { [System.Windows.Controls.Grid]::SetRow($child, 4) }
    $countdownEditorGrid.RowDefinitions.Add((New-Object System.Windows.Controls.RowDefinition -Property @{ Height = 'Auto' })) | Out-Null
    $countdownTagEditor = New-TagEditorPanel -Label '标签' -ToolTip '倒计时标签'
    [System.Windows.Controls.Grid]::SetRow($countdownTagEditor.Panel, 3)
    $countdownEditorGrid.Children.Add($countdownTagEditor.Panel) | Out-Null
    $script:countdownTagSelectorPanel = $countdownTagEditor.Selector
    $script:countdownTagSearchInput = $countdownTagEditor.Search
    $script:countdownNewTagInput = $countdownTagEditor.Input
    $script:countdownCreateTagButton = $countdownTagEditor.Create
    $script:countdownManageTagsButton = $countdownTagEditor.Manage

    $notesListArea = @($notesWorkspace.Children | Where-Object { [System.Windows.Controls.Grid]::GetRow($_) -eq 2 })[0]
    [System.Windows.Controls.Grid]::SetRow($noteMessageText, 2)
    [System.Windows.Controls.Grid]::SetRow($notesListArea, 3)
    $notesWorkspace.RowDefinitions[1].Height = 'Auto'
    $notesWorkspace.RowDefinitions[2].Height = 'Auto'
    $notesWorkspace.RowDefinitions.Add((New-Object System.Windows.Controls.RowDefinition -Property @{ Height = '*' })) | Out-Null
    $noteTagEditor = New-TagEditorPanel -Label '标签' -ToolTip '便签标签'
    $noteTagEditor.Panel.Margin = '0,8,0,2'
    [System.Windows.Controls.Grid]::SetRow($noteTagEditor.Panel, 1)
    $notesWorkspace.Children.Add($noteTagEditor.Panel) | Out-Null
    $script:noteTagSelectorPanel = $noteTagEditor.Selector
    $script:noteTagSearchInput = $noteTagEditor.Search
    $script:noteNewTagInput = $noteTagEditor.Input
    $script:noteCreateTagButton = $noteTagEditor.Create
    $script:noteManageTagsButton = $noteTagEditor.Manage

    $floatingNotesEditor.RowDefinitions.Add((New-Object System.Windows.Controls.RowDefinition -Property @{ Height = 'Auto' })) | Out-Null
    $floatingTagSummary = New-Object System.Windows.Controls.StackPanel
    $floatingTagSummary.Orientation = 'Horizontal'
    $floatingTagSummary.Margin = '2,2,2,0'
    $script:floatingTagSelectorButton = New-TagActionButton -Content '标签（0）' -ToolTip '选择本次新增便签的标签' -Width 64
    $script:floatingTagSelectorButton.Height = 24
    $script:floatingTagSelectorButton.FontSize = 10
    $script:floatingTagSelectorButton.Margin = '0,0,0,0'
    $floatingTagSummary.Children.Add($script:floatingTagSelectorButton) | Out-Null
    $script:floatingInheritedTagText = New-Object System.Windows.Controls.TextBlock
    $script:floatingInheritedTagText.Margin = '8,0,0,0'
    $script:floatingInheritedTagText.FontSize = 10
    $script:floatingInheritedTagText.Foreground = $mutedBrush
    $script:floatingInheritedTagText.TextWrapping = 'Wrap'
    Set-AccessibleName -Control $script:floatingInheritedTagText -Name '悬浮便签继承的标签'
    $floatingTagSummary.Children.Add($script:floatingInheritedTagText) | Out-Null
    [System.Windows.Controls.Grid]::SetRow($floatingTagSummary, 2)
    $floatingNotesEditor.Children.Add($floatingTagSummary) | Out-Null

    $script:floatingTagPopup = New-Object System.Windows.Controls.Primitives.Popup
    $script:floatingTagPopup.PlacementTarget = $script:floatingTagSelectorButton
    $script:floatingTagPopup.Placement = [System.Windows.Controls.Primitives.PlacementMode]::Bottom
    $script:floatingTagPopup.VerticalOffset = 4
    $script:floatingTagPopup.StaysOpen = $false
    $floatingTagPopupBorder = New-Object System.Windows.Controls.Border
    $floatingTagPopupBorder.Padding = 10
    $floatingTagPopupBorder.MaxWidth = 320
    $floatingTagPopupBorder.Background = $canvasBrush
    $floatingTagPopupBorder.BorderBrush = $hairlineBrush
    $floatingTagPopupBorder.BorderThickness = 1
    $floatingTagPopupBorder.CornerRadius = 8
    $floatingTagPopupStack = New-Object System.Windows.Controls.StackPanel
    $floatingTagPopupHeader = New-Object System.Windows.Controls.DockPanel
    $floatingTagPopupHeading = New-Object System.Windows.Controls.TextBlock
    $floatingTagPopupHeading.Text = '选择标签'
    $floatingTagPopupHeading.FontSize = 11
    $floatingTagPopupHeading.FontWeight = 'SemiBold'
    $floatingTagPopupHeading.Foreground = $inkBrush
    $floatingTagPopupHeading.VerticalAlignment = 'Center'
    $floatingTagPopupHeader.Children.Add($floatingTagPopupHeading) | Out-Null
    $script:floatingClearTagButton = New-TagActionButton -Content '清空' -ToolTip '清空悬浮便签的已选标签' -Width 48
    $script:floatingClearTagButton.Height = 24
    $script:floatingClearTagButton.FontSize = 10
    $script:floatingClearTagButton.Margin = '8,0,0,4'
    [System.Windows.Controls.DockPanel]::SetDock($script:floatingClearTagButton, [System.Windows.Controls.Dock]::Right)
    $floatingTagPopupHeader.Children.Add($script:floatingClearTagButton) | Out-Null
    $floatingTagPopupStack.Children.Add($floatingTagPopupHeader) | Out-Null
    $script:floatingTagSelectorPanel = New-Object System.Windows.Controls.WrapPanel
    $script:floatingTagSelectorPanel.Margin = '0,0,0,0'
    Set-AccessibleName -Control $script:floatingTagSelectorPanel -Name '悬浮便签标签选择器'
    $floatingTagPopupStack.Children.Add($script:floatingTagSelectorPanel) | Out-Null
    $floatingTagPopupBorder.Child = $floatingTagPopupStack
    $script:floatingTagPopup.Child = $floatingTagPopupBorder
}

Initialize-TagInterface

function New-CountdownRecord {
    param(
        [string] $Id,
        [string] $Name,
        [DateTimeOffset] $TargetUtc,
        [System.Collections.IEnumerable] $TagIds
    )

    $normalizedTagIds = [System.Collections.ArrayList]::new()
    foreach ($tagId in @($TagIds)) {
        if (-not [string]::IsNullOrWhiteSpace([string] $tagId)) { [void] $normalizedTagIds.Add(([string] $tagId).Trim()) }
    }
    [pscustomobject]@{ id = $Id; name = $Name; targetUtc = $TargetUtc.ToUniversalTime(); tagIds = $normalizedTagIds }
}

function Format-WorkTime {
    param([Parameter(Mandatory)] [TimeSpan] $Value)
    '{0:D2}:{1:D2}' -f $Value.Hours, $Value.Minutes
}

function Get-CurrentSettingsState {
    param(
        [System.Collections.IEnumerable] $Notes,
        [System.Collections.IEnumerable] $Tags
    )

    $effectiveNotes = $script:notes
    $effectiveTags = $script:tags
    if ($null -ne $Notes) { $effectiveNotes = $Notes }
    if ($null -ne $Tags) { $effectiveTags = $Tags }
    [pscustomobject]@{
        Records = $script:countdowns
        Notes = $effectiveNotes
        Tags = $effectiveTags
        Preferences = [pscustomobject]@{
            Precision = $script:displayPrecision
            MonthlySalary = $script:salary
            WorkStart = $script:workStart
            WorkEnd = $script:workEnd
        }
    }
}

function New-CurrentSettingsCandidate {
    Copy-CountdownSettingsState -State (Get-CurrentSettingsState)
}

function Commit-SettingsState {
    param([Parameter(Mandatory)] [object] $State)

    $script:countdowns = $State.Records
    $script:notes = $State.Notes
    $script:tags = $State.Tags
    $script:displayPrecision = $State.Preferences.Precision
    $script:salary = $State.Preferences.MonthlySalary
    $script:workStart = $State.Preferences.WorkStart
    $script:workEnd = $State.Preferences.WorkEnd
}

function Save-Settings {
    param(
        [System.Collections.IEnumerable] $Notes,
        [System.Collections.IEnumerable] $Tags,
        [object] $State
    )

    $stateToSave = if ($null -ne $State) { $State } else { Get-CurrentSettingsState -Notes $Notes -Tags $Tags }
    Save-CountdownSettings -Path $settingsPath -State $stateToSave
}

function Get-RecordTagIds {
    param([Parameter(Mandatory)] [object] $Record)

    if ($null -eq $Record.PSObject.Properties['tagIds']) { return @() }
    @($Record.tagIds | ForEach-Object { ([string] $_).Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Get-TagRecordsForIds {
    param([System.Collections.IEnumerable] $TagIds)

    $results = [System.Collections.ArrayList]::new()
    foreach ($tagId in @(Get-RecordTagIds -Record ([pscustomobject]@{ tagIds = $TagIds }))) {
        $tag = @($script:tags | Where-Object { $_.id -eq $tagId })[0]
        if ($null -ne $tag) { [void] $results.Add($tag) }
    }
    foreach ($result in $results) { $result }
}

function Get-TagLabelText {
    param([System.Collections.IEnumerable] $TagIds)

    @((Get-TagRecordsForIds -TagIds $TagIds) | ForEach-Object { $_.name }) -join '、'
}

function Get-CurrentTagFilterHasConstraints {
    $script:tagFilterMode -ne 'All' -or $script:selectedTagIds.Count -gt 0
}

function Test-CurrentTagFilter {
    param([Parameter(Mandatory)] [object] $Record)

    $tagIds = @(Get-RecordTagIds -Record $Record)
    if ($script:tagFilterMode -eq 'All') { return $true }
    if ($script:tagFilterMode -eq 'Untagged') {
        return Test-CountdownTagFilter -TagIds $tagIds -UntaggedOnly
    }
    Test-CountdownTagFilter -TagIds $tagIds -SelectedTagIds @($script:selectedTagIds) -MatchMode $script:tagMatchMode
}

function Get-FilteredCountdowns {
    param([DateTimeOffset] $NowUtc = ([DateTimeOffset]::UtcNow))

    @((Get-SortedCountdowns) | Where-Object {
            if (-not (Test-CurrentTagFilter -Record $_)) { return $false }
            if ($script:countdownStatusFilter -eq 'Active') { return $_.targetUtc -gt $NowUtc }
            if ($script:countdownStatusFilter -eq 'Expired') { return $_.targetUtc -le $NowUtc }
            $true
        })
}

function Get-FilteredNotes {
    $query = $script:noteSearchText.Trim()
    @($script:notes | Where-Object {
            if (-not (Test-CurrentTagFilter -Record $_)) { return $false }
            if ($script:noteStatusFilter -eq 'Active' -and [bool] $_.isCompleted) { return $false }
            if ($script:noteStatusFilter -eq 'Completed' -and -not [bool] $_.isCompleted) { return $false }
            [string]::IsNullOrWhiteSpace($query) -or ([string] $_.text).IndexOf($query, [System.StringComparison]::OrdinalIgnoreCase) -ge 0
        })
}

function Get-FilteredActiveNotes {
    @(Get-FilteredNotes | Where-Object { -not [bool] $_.isCompleted })
}

function Get-FilteredCompletedNotes {
    @(Get-FilteredNotes | Where-Object { [bool] $_.isCompleted })
}

function Set-TagActionState {
    param(
        [Parameter(Mandatory)] [System.Windows.Controls.Control] $Control,
        [Parameter(Mandatory)] [bool] $Selected
    )

    if ($Selected) {
        $Control.Background = $successSurfaceBrush
        $Control.Foreground = $successTextBrush
        $Control.BorderBrush = $successTextBrush
    }
    else {
        $Control.Background = $canvasBrush
        $Control.Foreground = $inkBrush
        $Control.BorderBrush = $hairlineBrush
    }
}

function Get-TagSelectionForContext {
    param([Parameter(Mandatory)] [ValidateSet('Filter', 'CountdownEditor', 'NoteEditor', 'FloatingNoteEditor')] [string] $Context)

    switch ($Context) {
        'Filter' { Write-Output -NoEnumerate $script:selectedTagIds }
        'CountdownEditor' { Write-Output -NoEnumerate $script:countdownEditorTagIds }
        'NoteEditor' { Write-Output -NoEnumerate $script:noteEditorTagIds }
        'FloatingNoteEditor' { Write-Output -NoEnumerate $script:floatingNoteTagIds }
    }
}

function Set-TagSelectionValues {
    param(
        [Parameter(Mandatory)] [ValidateSet('Filter', 'CountdownEditor', 'NoteEditor', 'FloatingNoteEditor')] [string] $Context,
        [System.Collections.IEnumerable] $TagIds
    )

    $selection = Get-TagSelectionForContext -Context $Context
    $selection.Clear()
    foreach ($tagId in @($TagIds)) {
        $normalizedTagId = ([string] $tagId).Trim()
        if (-not [string]::IsNullOrWhiteSpace($normalizedTagId) -and $normalizedTagId -notin @($selection)) { [void] $selection.Add($normalizedTagId) }
    }
}

function Get-CurrentNoteTagInheritance {
    if ($script:tagFilterMode -eq 'Selected') { return @($script:selectedTagIds) }
    @()
}

function New-TagToggleButton {
    param(
        [Parameter(Mandatory)] [object] $TagRecord,
        [Parameter(Mandatory)] [bool] $Selected,
        [Parameter(Mandatory)] [ValidateSet('Filter', 'CountdownEditor', 'NoteEditor', 'FloatingNoteEditor')] [string] $Context
    )

    $button = New-Object System.Windows.Controls.Primitives.ToggleButton
    $button.Content = $TagRecord.name
    $button.Uid = [string] $TagRecord.id
    $button.DataContext = $Context
    $button.IsChecked = $Selected
    $button.Height = 28
    $button.MinWidth = 48
    $button.Margin = '0,0,6,6'
    $button.Padding = '9,0'
    $button.Cursor = 'Hand'
    $button.ToolTip = if ($Context -eq 'Filter') { "按标签 [$($TagRecord.name)] 筛选" } else { "选择标签 [$($TagRecord.name)]" }
    Set-AccessibleName -Control $button -Name $button.ToolTip
    Set-TagActionState -Control $button -Selected $Selected
    $button.Add_Checked({
        param($sender, $eventArgs)
        Set-TagActionState -Control $sender -Selected $true
        Set-TagSelection -Context ([string] $sender.DataContext) -Id ([string] $sender.Uid) -Selected $true
    })
    $button.Add_Unchecked({
        param($sender, $eventArgs)
        Set-TagActionState -Control $sender -Selected $false
        Set-TagSelection -Context ([string] $sender.DataContext) -Id ([string] $sender.Uid) -Selected $false
    })
    $button
}

function Set-TagSelection {
    param(
        [Parameter(Mandatory)] [ValidateSet('Filter', 'CountdownEditor', 'NoteEditor', 'FloatingNoteEditor')] [string] $Context,
        [Parameter(Mandatory)] [string] $Id,
        [Parameter(Mandatory)] [bool] $Selected
    )

    $selection = Get-TagSelectionForContext -Context $Context
    $existing = @($selection | Where-Object { $_ -eq $Id })
    if ($Selected -and $existing.Count -eq 0) { [void] $selection.Add($Id) }
    elseif (-not $Selected -and $existing.Count -gt 0) {
        for ($index = $selection.Count - 1; $index -ge 0; $index--) {
            if ($selection[$index] -eq $Id) { $selection.RemoveAt($index) }
        }
    }
    if ($Context -eq 'Filter') {
        $script:tagFilterMode = if ($script:selectedTagIds.Count -gt 0) { 'Selected' } else { 'All' }
        Sync-NoteEditorTagsWithTagFilter
        Refresh-TagFilteredViews
    }
    elseif ($Context -eq 'FloatingNoteEditor') {
        Update-FloatingInheritedTagText
    }
}

function Set-TagFilter {
    param([ValidateSet('All', 'Untagged', 'Selected')] [string] $Mode)

    if ($Mode -eq 'All' -or $Mode -eq 'Untagged') { $script:selectedTagIds.Clear() }
    $script:tagFilterMode = if ($Mode -eq 'Selected' -and $script:selectedTagIds.Count -eq 0) { 'All' } else { $Mode }
    Sync-NoteEditorTagsWithTagFilter
    Refresh-TagFilteredViews
}

function Sync-NoteEditorTagsWithTagFilter {
    $inheritedTagIds = Get-CurrentNoteTagInheritance
    if ([string]::IsNullOrWhiteSpace($script:editingNoteId)) { Set-TagSelectionValues -Context 'NoteEditor' -TagIds $inheritedTagIds }
    Set-TagSelectionValues -Context 'FloatingNoteEditor' -TagIds $inheritedTagIds
}

function Set-TagMatchMode {
    param([ValidateSet('Any', 'All')] [string] $Mode)

    $script:tagMatchMode = $Mode
    Refresh-TagFilteredViews
}

function Set-CountdownStatusFilter {
    param([ValidateSet('All', 'Active', 'Expired')] [string] $Status)

    $script:countdownStatusFilter = $Status
    Refresh-TagFilteredViews
}

function Set-NoteStatusFilter {
    param([ValidateSet('All', 'Active', 'Completed')] [string] $Status)

    $script:noteStatusFilter = $Status
    Refresh-TagFilteredViews
}

function New-TagChipPanel {
    param(
        [System.Collections.IEnumerable] $TagIds,
        [switch] $Compact
    )

    $panel = New-Object System.Windows.Controls.WrapPanel
    $panel.Margin = if ($Compact) { '0,5,0,0' } else { '0,8,0,0' }
    $records = @(Get-TagRecordsForIds -TagIds $TagIds)
    $visible = @($records | Select-Object -First 2)
    foreach ($tag in $visible) {
        $chip = New-TagActionButton -Content $tag.name -ToolTip "按标签 [$($tag.name)] 筛选" -Width 0
        $chip.Uid = [string] $tag.id
        $chip.Height = if ($Compact) { 22 } else { 24 }
        $chip.MinWidth = 0
        $chip.Margin = '0,0,5,0'
        $chip.Padding = if ($Compact) { '7,0' } else { '8,0' }
        $chip.FontSize = if ($Compact) { 10 } else { 11 }
        $chip.Background = $noteSurfaceBrush
        $chip.BorderBrush = $noteLineBrush
        $chip.Foreground = $noteInkBrush
        $chip.Add_Click({ param($sender, $eventArgs) Select-TagForCurrentWorkspace -Id ([string] $sender.Uid) })
        $panel.Children.Add($chip) | Out-Null
    }
    if ($records.Count -gt 2) {
        $more = New-Object System.Windows.Controls.TextBlock
        $more.Text = "+$($records.Count - 2)"
        $more.FontSize = if ($Compact) { 10 } else { 11 }
        $more.Foreground = $mutedBrush
        $more.VerticalAlignment = 'Center'
        $more.Margin = '1,0,0,0'
        $more.ToolTip = "还有 $($records.Count - 2) 个标签"
        Set-AccessibleName -Control $more -Name $more.ToolTip
        $panel.Children.Add($more) | Out-Null
    }
    $panel
}

function Select-TagForCurrentWorkspace {
    param([Parameter(Mandatory)] [string] $Id)

    $script:selectedTagIds.Clear()
    [void] $script:selectedTagIds.Add($Id)
    $script:tagFilterMode = 'Selected'
    $script:tagMatchMode = 'Any'
    Sync-NoteEditorTagsWithTagFilter
    Refresh-TagFilteredViews
}

function Render-TagFilterControls {
    $script:tagFilterTagsPanel.Children.Clear()
    foreach ($tag in $script:tags) {
        $script:tagFilterTagsPanel.Children.Add((New-TagToggleButton -TagRecord $tag -Selected ($tag.id -in @($script:selectedTagIds)) -Context 'Filter')) | Out-Null
    }
    if ($script:tags.Count -eq 0) {
        $empty = New-Object System.Windows.Controls.TextBlock
        $empty.Text = '还没有标签，可在“管理标签”中创建。'
        $empty.FontSize = 12
        $empty.Foreground = $mutedBrush
        $empty.Margin = '0,0,0,6'
        $script:tagFilterTagsPanel.Children.Add($empty) | Out-Null
    }
    Set-TagActionState -Control $script:tagFilterAllButton -Selected ($script:tagFilterMode -eq 'All')
    Set-TagActionState -Control $script:tagFilterUntaggedButton -Selected ($script:tagFilterMode -eq 'Untagged')
    Set-TagActionState -Control $script:tagMatchAnyButton -Selected ($script:tagMatchMode -eq 'Any')
    Set-TagActionState -Control $script:tagMatchAllButton -Selected ($script:tagMatchMode -eq 'All')
    $script:tagMatchAnyButton.IsEnabled = $script:selectedTagIds.Count -gt 1
    $script:tagMatchAllButton.IsEnabled = $script:selectedTagIds.Count -gt 1
    Set-TagActionState -Control $script:countdownStatusAllButton -Selected ($script:countdownStatusFilter -eq 'All')
    Set-TagActionState -Control $script:countdownStatusActiveButton -Selected ($script:countdownStatusFilter -eq 'Active')
    Set-TagActionState -Control $script:countdownStatusExpiredButton -Selected ($script:countdownStatusFilter -eq 'Expired')
    Set-TagActionState -Control $script:noteStatusAllButton -Selected ($script:noteStatusFilter -eq 'All')
    Set-TagActionState -Control $script:noteStatusActiveButton -Selected ($script:noteStatusFilter -eq 'Active')
    Set-TagActionState -Control $script:noteStatusCompletedButton -Selected ($script:noteStatusFilter -eq 'Completed')
    $showNotes = $script:activeWorkspace -eq 'Notes'
    $script:countdownStatusFilterPanel.Visibility = if ($showNotes) { 'Collapsed' } else { 'Visible' }
    $script:noteStatusFilterPanel.Visibility = if ($showNotes) { 'Visible' } else { 'Collapsed' }
}

function Render-TagEditorSelectors {
    foreach ($target in @(
        [pscustomobject]@{ Panel = $script:countdownTagSelectorPanel; Search = $script:countdownTagSearchInput; Context = 'CountdownEditor' },
        [pscustomobject]@{ Panel = $script:noteTagSelectorPanel; Search = $script:noteTagSearchInput; Context = 'NoteEditor' },
        [pscustomobject]@{ Panel = $script:floatingTagSelectorPanel; Search = $null; Context = 'FloatingNoteEditor' }
    )) {
        $target.Panel.Children.Clear()
        $query = if ($null -eq $target.Search) { '' } else { $target.Search.Text.Trim() }
        $selection = Get-TagSelectionForContext -Context $target.Context
        foreach ($tag in @($script:tags | Where-Object { [string]::IsNullOrWhiteSpace($query) -or $_.name.IndexOf($query, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 })) {
            $target.Panel.Children.Add((New-TagToggleButton -TagRecord $tag -Selected ($tag.id -in @($selection)) -Context $target.Context)) | Out-Null
        }
        if ($script:tags.Count -eq 0 -or $target.Panel.Children.Count -eq 0) {
            $empty = New-Object System.Windows.Controls.TextBlock
            $empty.Text = if ($script:tags.Count -eq 0) { '未选择标签' } else { '没有匹配的标签' }
            $empty.FontSize = 12
            $empty.Foreground = $mutedBrush
            $empty.Margin = '0,0,0,6'
            $target.Panel.Children.Add($empty) | Out-Null
        }
    }
    Update-FloatingInheritedTagText
}

function Update-FloatingInheritedTagText {
    $selectedCount = @($script:floatingNoteTagIds).Count
    $script:floatingTagSelectorButton.Content = "标签（$selectedCount）"
    $script:floatingClearTagButton.IsEnabled = $selectedCount -gt 0
    $script:floatingInheritedTagText.Text = if ($selectedCount -eq 0) { '本次新增不带标签' } else { "已选 $selectedCount 个标签" }
}

function Toggle-FloatingTagPopup {
    $script:floatingTagPopup.IsOpen = -not [bool] $script:floatingTagPopup.IsOpen
}

function Clear-FloatingNoteTags {
    Set-TagSelectionValues -Context 'FloatingNoteEditor' -TagIds @()
    Render-TagEditorSelectors
}

function Get-TagAssociationCount {
    param([Parameter(Mandatory)] [string] $Id)

    @(@($script:countdowns) + @($script:notes) | Where-Object { $Id -in @(Get-RecordTagIds -Record $_) }).Count
}

function Set-TagManagerMessage {
    param([string] $Text, [switch] $IsError)

    $script:tagManagerMessage.Text = $Text
    $script:tagManagerMessage.Foreground = if ($IsError) { $errorBrush } else { $noteInkBrush }
}

function New-TagManagerRow {
    param([Parameter(Mandatory)] [object] $TagRecord)

    $row = New-Object System.Windows.Controls.Grid
    $row.Margin = '0,3,0,0'
    $row.ColumnDefinitions.Add((New-Object System.Windows.Controls.ColumnDefinition -Property @{ Width = '*' })) | Out-Null
    $row.ColumnDefinitions.Add((New-Object System.Windows.Controls.ColumnDefinition -Property @{ Width = 'Auto' })) | Out-Null
    $row.ColumnDefinitions.Add((New-Object System.Windows.Controls.ColumnDefinition -Property @{ Width = 'Auto' })) | Out-Null
    $tagNameInput = New-Object System.Windows.Controls.TextBox
    $tagNameInput.Text = $TagRecord.name
    $tagNameInput.Uid = [string] $TagRecord.id
    $tagNameInput.MaxLength = 30
    $tagNameInput.Height = 32
    $tagNameInput.ToolTip = "修改标签 [$($TagRecord.name)] 的名称"
    Set-AccessibleName -Control $tagNameInput -Name $tagNameInput.ToolTip
    $save = New-TagActionButton -Content '改名' -ToolTip "保存标签 [$($TagRecord.name)] 的新名称" -Width 54
    $save.Height = 32
    $save.Uid = [string] $TagRecord.id
    [System.Windows.Controls.Grid]::SetColumn($save, 1)
    $impact = Get-TagAssociationCount -Id ([string] $TagRecord.id)
    $delete = New-TagActionButton -Content ("删除（解除 $impact 条关联）") -ToolTip "删除标签 [$($TagRecord.name)]，只解除 $impact 条记录关联" -Width 126
    $delete.Height = 32
    $delete.Uid = [string] $TagRecord.id
    $delete.Foreground = $dangerTextBrush
    [System.Windows.Controls.Grid]::SetColumn($delete, 2)
    $save.Add_Click({
        param($sender, $eventArgs)
        $editor = @($script:tagManagerList.Items | ForEach-Object { $_.Children[0] } | Where-Object { $_.Uid -eq $sender.Uid })[0]
        Rename-TagFromInput -Id ([string] $sender.Uid) -Text $editor.Text
    })
    $delete.Add_Click({ param($sender, $eventArgs) Remove-TagFromLibrary -Id ([string] $sender.Uid) })
    $row.Children.Add($tagNameInput) | Out-Null
    $row.Children.Add($save) | Out-Null
    $row.Children.Add($delete) | Out-Null
    $row
}

function Render-TagManager {
    $script:tagManagerPanel.Visibility = if ($script:tagManagerVisible) { 'Visible' } else { 'Collapsed' }
    $script:tagManagerToggleButton.Content = if ($script:tagManagerVisible) { '收起标签管理' } else { '管理标签' }
    $script:tagManagerList.Items.Clear()
    foreach ($tag in $script:tags) { $script:tagManagerList.Items.Add((New-TagManagerRow -TagRecord $tag)) | Out-Null }
    if ($script:tags.Count -eq 0) {
        $empty = New-Object System.Windows.Controls.TextBlock
        $empty.Text = '还没有标签。'
        $empty.FontSize = 12
        $empty.Foreground = $mutedBrush
        $script:tagManagerList.Items.Add($empty) | Out-Null
    }
}

function Toggle-TagManager {
    $script:tagManagerVisible = -not $script:tagManagerVisible
    Render-TagManager
}

function Add-TagIdToSelection {
    param(
        [Parameter(Mandatory)] [ValidateSet('CountdownEditor', 'NoteEditor', 'FloatingNoteEditor', 'None')] [string] $Context,
        [Parameter(Mandatory)] [string] $Id
    )

    $selection = if ($Context -eq 'None') { $null } else { Get-TagSelectionForContext -Context $Context }
    if ($null -ne $selection -and $Id -notin @($selection)) { [void] $selection.Add($Id) }
}

function Create-TagFromInput {
    param(
        [Parameter(Mandatory)] [System.Windows.Controls.TextBox] $TextBox,
        [Parameter(Mandatory)] [ValidateSet('CountdownEditor', 'NoteEditor', 'FloatingNoteEditor', 'None')] [string] $Context
    )

    try {
        $name = $TextBox.Text.Trim()
        $candidate = New-CurrentSettingsCandidate
        if (@($candidate.Tags | Where-Object { [string]::Equals($_.name, $name, [System.StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) { throw '标签名称不能重复。' }
        $tag = New-CountdownTagRecord -Id ([guid]::NewGuid().ToString('N')) -Name $name
        [void] $candidate.Tags.Add($tag)
        Save-Settings -State $candidate
        Commit-SettingsState -State $candidate
        Add-TagIdToSelection -Context $Context -Id $tag.id
        $TextBox.Text = ''
        Set-TagManagerMessage -Text ("已创建标签 [$($tag.name)]。")
        Refresh-TagFilteredViews
    }
    catch {
        Set-TagManagerMessage -Text $_.Exception.Message -IsError
    }
}

function Rename-TagFromInput {
    param(
        [Parameter(Mandatory)] [string] $Id,
        [Parameter(Mandatory)] [string] $Text
    )

    try {
        $candidate = New-CurrentSettingsCandidate
        $tag = @($candidate.Tags | Where-Object { $_.id -eq $Id })[0]
        if ($null -eq $tag) { throw '未找到要改名的标签。' }
        $name = $Text.Trim()
        if (@($candidate.Tags | Where-Object { $_.id -ne $Id -and [string]::Equals($_.name, $name, [System.StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) { throw '标签名称不能重复。' }
        $tag.name = (New-CountdownTagRecord -Id $tag.id -Name $name -Color ([string] $tag.color)).name
        Save-Settings -State $candidate
        Commit-SettingsState -State $candidate
        Set-TagManagerMessage -Text '标签名称已更新。'
        Refresh-TagFilteredViews
    }
    catch {
        Set-TagManagerMessage -Text $_.Exception.Message -IsError
    }
}

function Remove-TagIdFromSelections {
    param([Parameter(Mandatory)] [string] $Id)

    foreach ($selection in @($script:selectedTagIds, $script:countdownEditorTagIds, $script:noteEditorTagIds, $script:floatingNoteTagIds)) {
        for ($index = $selection.Count - 1; $index -ge 0; $index--) {
            if ($selection[$index] -eq $Id) { $selection.RemoveAt($index) }
        }
    }
    if ($script:tagFilterMode -eq 'Selected' -and $script:selectedTagIds.Count -eq 0) { $script:tagFilterMode = 'All' }
}

function Remove-TagFromLibrary {
    param(
        [Parameter(Mandatory)] [string] $Id,
        [switch] $SkipConfirmation
    )

    try {
        $currentTag = @($script:tags | Where-Object { $_.id -eq $Id })[0]
        if ($null -eq $currentTag) { throw '未找到要删除的标签。' }
        $currentImpact = Get-TagAssociationCount -Id $Id
        if (-not $SkipConfirmation) {
            $answer = [System.Windows.MessageBox]::Show(
                ('删除“{0}”会解除 {1} 条倒计时或便签关联，记录本身不会删除。是否继续？' -f $currentTag.name, $currentImpact),
                '删除标签',
                'YesNo',
                'Warning'
            )
            if ($answer -ne 'Yes') { return }
        }
        $candidate = New-CurrentSettingsCandidate
        $tag = @($candidate.Tags | Where-Object { $_.id -eq $Id })[0]
        if ($null -eq $tag) { throw '未找到要删除的标签。' }
        $impact = @(@($candidate.Records) + @($candidate.Notes) | Where-Object { $Id -in @(Get-RecordTagIds -Record $_) }).Count
        # 先从候选记录解除关联，再移除标签；保存时不会把待删除 ID 当作悬空引用校验。
        $knownTagIds = @($candidate.Tags | ForEach-Object { $_.id })
        foreach ($record in (@($candidate.Records) + @($candidate.Notes))) {
            $remainingTagIds = @(Get-RecordTagIds -Record $record | Where-Object { $_ -ne $Id })
            $record.tagIds = ConvertTo-CountdownTagIds -TagIds $remainingTagIds -KnownTagIds $knownTagIds
        }
        [void] $candidate.Tags.Remove($tag)
        Save-Settings -State $candidate
        Commit-SettingsState -State $candidate
        Remove-TagIdFromSelections -Id $Id
        Set-TagManagerMessage -Text ("已删除标签 [$($tag.name)]，已解除 $impact 条记录关联。")
        Refresh-TagFilteredViews
    }
    catch {
        Set-TagManagerMessage -Text $_.Exception.Message -IsError
    }
}

function Refresh-TagFilteredViews {
    Render-TagFilterControls
    Render-TagEditorSelectors
    Render-TagManager
    Render-Cards
    Render-Notes
    Render-FloatingContent
}

function Set-TimeControls {
    param([Parameter(Mandatory)] [DateTimeOffset] $BeijingTime)
    $targetDate.SelectedDate = $BeijingTime.Date
    $targetHour.SelectedItem = $BeijingTime.ToString('HH')
    $targetMinute.SelectedItem = $BeijingTime.ToString('mm')
    $targetSecond.SelectedItem = $BeijingTime.ToString('ss')
}

function Reset-Editor {
    $script:editingId = $null
    $script:countdownEditorTagIds.Clear()
    $editorTitle.Text = '新增倒计时'
    $countdownName.Text = ''
    Set-TimeControls -BeijingTime ((Get-BeijingNow).Date.AddDays(1).AddHours(18))
    $saveButton.Content = '添加倒计时'
    $cancelButton.Visibility = 'Collapsed'
    $messageText.Text = ''
    Render-TagEditorSelectors
}

function Get-SortedCountdowns {
    $now = [DateTimeOffset]::UtcNow
    @($script:countdowns | Sort-Object @{ Expression = { if ($_.targetUtc -gt $now) { 0 } else { 1 } } }, @{ Expression = { $_.targetUtc } })
}

function Get-ChinaHolidayCalendarCachePath {
    param([Parameter(Mandatory)] [int] $Year)

    Join-Path (Join-Path $PSScriptRoot 'holiday-cache') "$Year.json"
}

function Read-ChinaHolidayCalendarPayload {
    param([Parameter(Mandatory)] [int] $Year)

    $cachePath = Get-ChinaHolidayCalendarCachePath -Year $Year
    if (-not (Test-Path -LiteralPath $cachePath -PathType Leaf)) { return $null }
    try {
        [System.IO.File]::ReadAllText($cachePath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    }
    catch {
        $null
    }
}

function Save-ChinaHolidayCalendarPayload {
    param(
        [Parameter(Mandatory)] [int] $Year,
        [Parameter(Mandatory)] [object] $Payload
    )

    $cachePath = Get-ChinaHolidayCalendarCachePath -Year $Year
    New-Item -ItemType Directory -Path (Split-Path -Parent $cachePath) -Force | Out-Null
    [System.IO.File]::WriteAllText($cachePath, ($Payload | ConvertTo-Json -Depth 8), (New-Object System.Text.UTF8Encoding($false)))
}

function ConvertTo-ChinaHolidayCalendarState {
    param(
        [AllowNull()] [object] $Payload,
        [Parameter(Mandatory)] [int] $Year
    )

    # 空缓存经 PowerShell 管道赋值会变成 $null；先初始化数组，避免异步首次启动在严格模式下访问不存在的 Count。
    $days = @()
    if ($null -ne $Payload) { $days = @($Payload.days) }
    if ($days.Count -lt 365) { return $null }
    $workdayMap = @{}
    foreach ($day in $days) {
        $dateText = [string] $day.date
        if ([string]::IsNullOrWhiteSpace($dateText)) { return $null }
        $workdayMap[$dateText] = [bool] $day.is_workday
    }
    [pscustomobject]@{
        WorkdayMap = $workdayMap
        Status = "已同步 $Year 年法定节假日与调休"
    }
}

function Set-ChinaHolidayCalendarPayload {
    param(
        [AllowNull()] [object] $Payload,
        [Parameter(Mandatory)] [int] $Year
    )

    $calendar = ConvertTo-ChinaHolidayCalendarState -Payload $Payload -Year $Year
    if ($null -eq $calendar) {
        $script:workdayMap = $null
        $script:holidayCalendarStatus = '节假日日历未就绪'
        return $false
    }
    $script:workdayMap = $calendar.WorkdayMap
    $script:holidayCalendarStatus = $calendar.Status
    $true
}

function Set-ChinaHolidayCalendarUnavailable {
    $script:workdayMap = $null
    $script:holidayCalendarStatus = '节假日日历未就绪'
}

function Stop-ChinaHolidayCalendarRequest {
    if ($null -ne $script:holidayCalendarPoller) {
        $script:holidayCalendarPoller.Stop()
        $script:holidayCalendarPoller = $null
    }
    if ($null -ne $script:holidayCalendarClient) {
        try { $script:holidayCalendarClient.CancelPendingRequests() } catch { }
        $script:holidayCalendarClient.Dispose()
        $script:holidayCalendarClient = $null
    }
    $script:holidayCalendarTask = $null
    $script:holidayCalendarDeadline = $null
    $script:holidayCalendarYear = $null
}

function Write-CountdownCalendarStateReceipt {
    param([ValidateSet('Ready', 'Unavailable')] [string] $CalendarState)

    if (-not $script:runtimeReceiptWritten) { return }
    try {
        [void] (Write-CountdownWindowReadyReceipt -Path $runtimeStatusPath -LaunchToken $script:launchToken -ProcessId $PID -SessionId ([System.Diagnostics.Process]::GetCurrentProcess().SessionId) -ApplicationPath $script:applicationPath -CalendarState $CalendarState)
    }
    catch {
        # 首帧就绪回执已经足以证明应用可见；日历状态的后续写入不得使窗口退出。
    }
}

function Refresh-CountdownAfterCalendarState {
    Render-Cards
    Render-FloatingContent
    Update-CardValues
    Update-FloatingCountdowns
}

function Complete-ChinaHolidayCalendarBootstrap {
    param([ValidateSet('Ready', 'Unavailable')] [string] $CalendarState)

    Stop-ChinaHolidayCalendarRequest
    Write-CountdownCalendarStateReceipt -CalendarState $CalendarState
    Refresh-CountdownAfterCalendarState
}

function Start-ChinaHolidayCalendarBootstrap {
    param([Parameter(Mandatory)] [int] $Year)

    $cachedPayload = Read-ChinaHolidayCalendarPayload -Year $Year
    if (Set-ChinaHolidayCalendarPayload -Payload $cachedPayload -Year $Year) {
        Complete-ChinaHolidayCalendarBootstrap -CalendarState Ready
        return
    }

    # 网络请求只创建 Task；Dispatcher 线程只轮询完成态并应用已经返回的数据。
    $script:holidayCalendarStatus = '节假日日历正在加载'
    try {
        $uri = [uri] "https://uapis.cn/api/v1/misc/holiday-calendar?year=$Year&holiday_type=legal&timezone=Asia%2FShanghai"
        if ($null -ne $script:holidayCalendarTaskFactory) {
            $task = & $script:holidayCalendarTaskFactory $uri
            if ($null -eq $task -or -not ($task -is [System.Threading.Tasks.Task])) {
                throw '节假日日历测试任务工厂必须返回 Task。'
            }
        }
        else {
            $client = [System.Net.Http.HttpClient]::new()
            $client.Timeout = [TimeSpan]::FromSeconds(10)
            $task = $client.GetStringAsync($uri)
            $script:holidayCalendarClient = $client
        }
        $script:holidayCalendarTask = $task
        $script:holidayCalendarDeadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
        $script:holidayCalendarYear = $Year
    }
    catch {
        Set-ChinaHolidayCalendarUnavailable
        Complete-ChinaHolidayCalendarBootstrap -CalendarState Unavailable
        return
    }

    $poller = New-Object Windows.Threading.DispatcherTimer
    $poller.Interval = [TimeSpan]::FromMilliseconds(250)
    $poller.Add_Tick({
        $task = $script:holidayCalendarTask
        if ($null -eq $task) { return }
        if ($task.IsCompleted) {
            $calendarState = 'Unavailable'
            try {
                $payload = $task.GetAwaiter().GetResult() | ConvertFrom-Json
                if (Set-ChinaHolidayCalendarPayload -Payload $payload -Year $script:holidayCalendarYear) {
                    Save-ChinaHolidayCalendarPayload -Year $script:holidayCalendarYear -Payload $payload
                    $calendarState = 'Ready'
                }
            }
            catch {
                Set-ChinaHolidayCalendarUnavailable
            }
            Complete-ChinaHolidayCalendarBootstrap -CalendarState $calendarState
            return
        }
        if ([DateTimeOffset]::UtcNow -ge $script:holidayCalendarDeadline) {
            Set-ChinaHolidayCalendarUnavailable
            Complete-ChinaHolidayCalendarBootstrap -CalendarState Unavailable
        }
    })
    $script:holidayCalendarPoller = $poller
    $poller.Start()
}

function Get-WorkdayEndCountdown {
    param([DateTimeOffset] $NowUtc = ([DateTimeOffset]::UtcNow))
    if ($null -eq $script:workdayMap) { return $null }
    $beijingNow = [System.TimeZoneInfo]::ConvertTimeBySystemTimeZoneId($NowUtc, 'China Standard Time')
    $dateText = $beijingNow.ToString('yyyy-MM-dd', [System.Globalization.CultureInfo]::InvariantCulture)
    if (-not $script:workdayMap.ContainsKey($dateText) -or -not [bool] $script:workdayMap[$dateText] -or $beijingNow.TimeOfDay -ge $script:workEnd) { return $null }
    [pscustomobject]@{
        id = '__workday_end__'
        name = '距离下班'
        targetUtc = ConvertTo-CountdownTargetUtc -DateText $dateText -TimeText ((Format-WorkTime -Value $script:workEnd) + ':00')
    }
}

function New-FloatingCountdownView {
    param([Parameter(Mandatory)] [object] $Record)
    $border = New-Object System.Windows.Controls.Border
    $border.Background = $canvasBrush; $border.BorderBrush = $hairlineBrush; $border.BorderThickness = 1
    $border.CornerRadius = 12; $border.Padding = 10; $border.Margin = '0,0,0,8'
    $stack = New-Object System.Windows.Controls.StackPanel
    $name = New-Object System.Windows.Controls.TextBlock; $name.Text = $Record.name; $name.ToolTip = $Record.name; $name.FontSize = 13; $name.FontWeight = 'SemiBold'; $name.Foreground = $inkBrush; $name.TextTrimming = 'CharacterEllipsis'
    $remaining = New-Object System.Windows.Controls.TextBlock; $remaining.Margin = '0,4,0,0'; $remaining.FontFamily = 'Consolas'; $remaining.FontSize = 16; $remaining.FontWeight = 'SemiBold'
    [void] $stack.Children.Add($name); [void] $stack.Children.Add($remaining)
    if (@(Get-RecordTagIds -Record $Record).Count -gt 0) { [void] $stack.Children.Add((New-TagChipPanel -TagIds $Record.tagIds -Compact)) }
    $border.Child = $stack
    [pscustomobject]@{ Border = $border; Remaining = $remaining }
}

function Update-FloatingCountdowns {
    param([DateTimeOffset] $NowUtc = ([DateTimeOffset]::UtcNow))
    if ($script:activeWorkspace -ne 'Countdowns') { return }
    $activeCount = @(Get-FilteredCountdowns -NowUtc $NowUtc | Where-Object { $_.targetUtc -gt $NowUtc }).Count
    $floatingCount.Text = if ($activeCount -gt 0) { "$activeCount 个倒计时" } else { '' }
    $beijingNow = [System.TimeZoneInfo]::ConvertTimeBySystemTimeZoneId($NowUtc, 'China Standard Time')
    if ($null -eq $script:workdayMap) { $floatingIncome.Text = $script:holidayCalendarStatus }
    else {
        $earnings = Get-WorkdayEarnings -MonthlySalary $script:salary -WorkStart $script:workStart -WorkEnd $script:workEnd -BeijingNow $beijingNow -WorkdayMap $script:workdayMap
        $floatingIncome.Text = ('今日已赚 ￥{0:N2}' -f $earnings)
    }
    foreach ($record in $script:countdowns) {
        $view = $script:floatingViews[$record.id]
        if ($null -eq $view) { continue }
        $parts = Get-CountdownParts -TargetUtc $record.targetUtc -NowUtc $NowUtc
        $view.Remaining.Text = Format-CountdownParts -Parts $parts -Precision $script:displayPrecision
        $view.Remaining.Foreground = if ($parts.IsElapsed) { $expiredTextBrush } else { $inkBrush }
    }
    $workdayEnd = Get-WorkdayEndCountdown -NowUtc $NowUtc
    if ($null -ne $workdayEnd -and $null -ne $script:floatingViews[$workdayEnd.id]) {
        $parts = Get-CountdownParts -TargetUtc $workdayEnd.targetUtc -NowUtc $NowUtc
        $script:floatingViews[$workdayEnd.id].Remaining.Text = Format-CountdownParts -Parts $parts -Precision $script:displayPrecision
    }
}

function Render-FloatingCountdowns {
    $floatingList.Items.Clear()
    $script:floatingViews = @{}
    $nowUtc = [DateTimeOffset]::UtcNow
    $workdayEnd = if (Get-CurrentTagFilterHasConstraints) { $null } else { Get-WorkdayEndCountdown -NowUtc $nowUtc }
    $script:workdayEndSignature = if ($null -eq $workdayEnd) { '' } else { $workdayEnd.targetUtc.ToString('o') }
    $sorted = @(Get-FilteredCountdowns -NowUtc $nowUtc | Where-Object { $_.targetUtc -gt $nowUtc })
    $floatingRecords = @()
    if ($null -ne $workdayEnd) { $floatingRecords += $workdayEnd }
    $floatingRecords += $sorted
    if ($floatingRecords.Count -eq 0) {
        $empty = New-Object System.Windows.Controls.TextBlock; $empty.Text = '点击管理添加一个进行中的倒计时吧'; $empty.Foreground = $mutedBrush; $empty.FontSize = 13; $empty.Margin = '2,8,0,8'
        [void] $floatingList.Items.Add($empty)
    }
    foreach ($record in $floatingRecords) {
        $view = New-FloatingCountdownView -Record $record
        [void] $floatingList.Items.Add($view.Border)
        $script:floatingViews[$record.id] = $view
    }
    Update-FloatingCountdowns -NowUtc $nowUtc
}

function Get-ActiveNotes {
    @($script:notes | Where-Object { -not ([bool] $_.isCompleted) })
}

function Get-CompletedNotes {
    @($script:notes | Where-Object { [bool] $_.isCompleted })
}

function Set-NoteMessage {
    param([string] $Text, [switch] $IsError)
    $brush = if ($IsError) { $errorBrush } else { $noteInkBrush }
    foreach ($target in @($noteMessageText, $floatingNoteMessage)) {
        $target.Foreground = $brush
        $target.Text = $Text
    }
}

function New-NoteRow {
    param([Parameter(Mandatory)] [object] $Record, [switch] $Compact)
    $horizontalPadding = if ($Compact) { 10 } else { 14 }
    $maxWidth = if ($Compact) { 220 } else { 420 }
    $isCompleted = [bool] $Record.isCompleted
    $border = New-Object System.Windows.Controls.Border
    $border.Background = $noteSurfaceBrush; $border.BorderBrush = $noteLineBrush; $border.BorderThickness = 1
    $border.CornerRadius = 12; $border.Padding = "$horizontalPadding,10"; $border.Margin = '0,0,0,8'
    $grid = New-Object System.Windows.Controls.Grid
    [void] $grid.ColumnDefinitions.Add((New-Object System.Windows.Controls.ColumnDefinition -Property @{ Width = 'Auto' }))
    [void] $grid.ColumnDefinitions.Add((New-Object System.Windows.Controls.ColumnDefinition -Property @{ Width = '*' }))
    [void] $grid.ColumnDefinitions.Add((New-Object System.Windows.Controls.ColumnDefinition -Property @{ Width = 'Auto' }))
    [void] $grid.ColumnDefinitions.Add((New-Object System.Windows.Controls.ColumnDefinition -Property @{ Width = 'Auto' }))
    [void] $grid.ColumnDefinitions.Add((New-Object System.Windows.Controls.ColumnDefinition -Property @{ Width = 'Auto' }))
    $check = New-Object System.Windows.Controls.CheckBox
    $check.Width = 22; $check.Height = 22; $check.Margin = '0,1,9,0'; $check.VerticalAlignment = 'Top'; $check.Cursor = 'Hand'
    $check.ToolTip = if ($isCompleted) { '标记为未完成' } else { '标记为已完成' }
    $check.Tag = $Record.id; $check.IsChecked = $isCompleted
    $content = New-Object System.Windows.Controls.StackPanel
    $content.VerticalAlignment = 'Center'
    $text = New-Object System.Windows.Controls.TextBlock
    $text.Text = $Record.text; $text.FontSize = if ($Compact) { 13 } else { 14 }; $text.MaxWidth = $maxWidth
    $text.TextWrapping = 'Wrap'; $text.VerticalAlignment = 'Center'; $text.Foreground = if ($isCompleted) { $completedNoteBrush } else { $noteInkBrush }
    if ($isCompleted) { $text.TextDecorations = [System.Windows.TextDecorations]::Strikethrough }
    [void] $content.Children.Add($text)
    if (@(Get-RecordTagIds -Record $Record).Count -gt 0) { [void] $content.Children.Add((New-TagChipPanel -TagIds $Record.tagIds -Compact:$Compact)) }
    [System.Windows.Controls.Grid]::SetColumn($content, 1)
    $status = New-Object System.Windows.Controls.TextBlock
    $status.Text = if ($isCompleted) { '已完成' } else { '未完成' }
    $status.FontSize = if ($Compact) { 10 } else { 11 }; $status.FontWeight = 'SemiBold'; $status.VerticalAlignment = 'Center'; $status.Margin = '8,0,0,0'
    $status.Foreground = if ($isCompleted) { $completedNoteBrush } else { $noteInkBrush }
    [System.Windows.Controls.Grid]::SetColumn($status, 2)
    $edit = $null
    if (-not $Compact) {
        $edit = New-Object System.Windows.Controls.Button
        $edit.Content = '编辑'; $edit.Width = 44; $edit.Height = 28; $edit.Margin = '8,0,0,0'; $edit.Padding = 0
        $edit.Cursor = 'Hand'; $edit.ToolTip = '编辑便签'; $edit.Background = $canvasBrush; $edit.Foreground = $noteInkBrush; $edit.BorderBrush = $noteLineBrush; $edit.BorderThickness = 1
        $edit.Tag = $Record.id
        [System.Windows.Controls.Grid]::SetColumn($edit, 3)
    }
    $delete = New-Object System.Windows.Controls.Button
    $delete.Content = '×'; $delete.Width = 28; $delete.Height = 28; $delete.Margin = '8,0,0,0'; $delete.Padding = 0
    $delete.Cursor = 'Hand'; $delete.ToolTip = '删除便签'; $delete.Background = $canvasBrush; $delete.Foreground = $dangerTextBrush; $delete.BorderBrush = $noteLineBrush; $delete.BorderThickness = 1
    $delete.Tag = $Record.id
    [System.Windows.Controls.Grid]::SetColumn($delete, $(if ($Compact) { 3 } else { 4 }))
    $check.Add_Checked({ param($sender, $eventArgs) Set-NoteCompletion -Id ([string] $sender.Tag) -IsCompleted $true })
    $check.Add_Unchecked({ param($sender, $eventArgs) Set-NoteCompletion -Id ([string] $sender.Tag) -IsCompleted $false })
    if ($null -ne $edit) { $edit.Add_Click({ param($sender, $eventArgs) Start-NoteEditing -Id ([string] $sender.Tag) }) }
    $delete.Add_Click({ param($sender, $eventArgs) Remove-Note -Id ([string] $sender.Tag) })
    [void] $grid.Children.Add($check); [void] $grid.Children.Add($content); [void] $grid.Children.Add($status)
    if ($null -ne $edit) { [void] $grid.Children.Add($edit) }
    [void] $grid.Children.Add($delete); $border.Child = $grid
    $border
}

function New-CompletedNotesToggle {
    param([Parameter(Mandatory)] [int] $Count, [switch] $Compact)
    $button = New-Object System.Windows.Controls.Button
    $button.Content = if ($script:showCompletedNotes) { "已完成（$Count）⌃" } else { "已完成（$Count）⌄" }
    $button.Height = if ($Compact) { 32 } else { 40 }; $button.Margin = '0,8,0,8'; $button.Padding = '12,0'; $button.Cursor = 'Hand'
    $button.Background = $canvasBrush; $button.Foreground = $completedNoteBrush; $button.BorderBrush = $noteLineBrush; $button.BorderThickness = 1
    $button.Add_Click({ Toggle-CompletedNotes })
    $button
}

function Render-Notes {
    $noteList.Items.Clear(); $completedNoteList.Items.Clear()
    $filtered = @(Get-FilteredNotes)
    $active = @($filtered | Where-Object { -not ([bool] $_.isCompleted) })
    $completed = @($filtered | Where-Object { [bool] $_.isCompleted })
    $noteEmptyState.Text = if ($script:notes.Count -eq 0) { '还没有便签。写下一件现在最想完成的事吧。' } else { '当前筛选没有匹配的便签。' }
    $noteEmptyState.Visibility = if ($filtered.Count -eq 0) { 'Visible' } else { 'Collapsed' }
    $activeNotesHeading.Visibility = if ($active.Count -gt 0) { 'Visible' } else { 'Collapsed' }
    $activeNotesHeading.Text = "待完成（$($active.Count)）"
    foreach ($record in $active) { [void] $noteList.Items.Add((New-NoteRow -Record $record)) }
    $toggleCompletedNotesButton.Visibility = if ($completed.Count -eq 0) { 'Collapsed' } else { 'Visible' }
    $toggleCompletedNotesButton.Content = if ($script:showCompletedNotes) { "已完成（$($completed.Count)）⌃" } else { "已完成（$($completed.Count)）⌄" }
    $completedNoteList.Visibility = if ($script:showCompletedNotes -and $completed.Count -gt 0) { 'Visible' } else { 'Collapsed' }
    if ($script:showCompletedNotes) {
        foreach ($record in $completed) { [void] $completedNoteList.Items.Add((New-NoteRow -Record $record)) }
    }
    if ($script:activeWorkspace -eq 'Notes') { $countdownCount.Text = "$($filtered.Count) 条" }
}

function Render-FloatingNotes {
    $floatingList.Items.Clear(); $script:floatingViews = @{}
    $active = @(Get-FilteredActiveNotes)
    $floatingCount.Text = if ($active.Count -gt 0) { "$($active.Count) 项待办" } else { '今天先轻松一点' }
    if ($active.Count -eq 0) {
        $empty = New-Object System.Windows.Controls.TextBlock; $empty.Text = if ($script:notes.Count -eq 0) { '写下第一件想完成的事吧' } else { '当前筛选没有未完成便签' }; $empty.Foreground = $mutedBrush; $empty.FontSize = 13; $empty.Margin = '2,8,0,8'
        [void] $floatingList.Items.Add($empty)
    }
    foreach ($record in $active) { [void] $floatingList.Items.Add((New-NoteRow -Record $record -Compact)) }
}

function Render-FloatingContent {
    if ($script:activeWorkspace -eq 'Notes') { Render-FloatingNotes } else { Render-FloatingCountdowns }
}

function Set-Workspace {
    param([ValidateSet('Countdowns', 'Notes')] [string] $Workspace)
    $script:activeWorkspace = $Workspace
    $showNotes = $Workspace -eq 'Notes'
    if (-not $showNotes) { $script:floatingTagPopup.IsOpen = $false }
    $countdownEditorWorkspace.Visibility = if ($showNotes) { 'Collapsed' } else { 'Visible' }
    $preferencesWorkspace.Visibility = if ($showNotes) { 'Collapsed' } else { 'Visible' }
    $countdownWorkspace.Visibility = if ($showNotes) { 'Collapsed' } else { 'Visible' }
    $notesWorkspace.Visibility = if ($showNotes) { 'Visible' } else { 'Collapsed' }
    $workspaceTitle.Text = if ($showNotes) { '我的便签' } else { '我的倒计时' }
    $showCountdownsButton.Background = if ($showNotes) { $canvasBrush } else { $successSurfaceBrush }
    $showCountdownsButton.Foreground = if ($showNotes) { $inkBrush } else { $successTextBrush }
    $showNotesButton.Background = if ($showNotes) { $noteSurfaceBrush } else { $canvasBrush }
    $showNotesButton.BorderBrush = if ($showNotes) { $noteLineBrush } else { $hairlineBrush }
    $showNotesButton.Foreground = if ($showNotes) { $noteInkBrush } else { $inkBrush }
    $floatingCountdownSummary.Visibility = if ($showNotes) { 'Collapsed' } else { 'Visible' }
    $floatingNotesEditor.Visibility = if ($showNotes) { 'Visible' } else { 'Collapsed' }
    $showFloatingCountdownsButton.Background = if ($showNotes) { $canvasBrush } else { $successSurfaceBrush }
    $showFloatingCountdownsButton.Foreground = if ($showNotes) { $noteInkBrush } else { $successTextBrush }
    $showFloatingNotesButton.Background = if ($showNotes) { $noteSurfaceBrush } else { $canvasBrush }
    $showFloatingNotesButton.BorderBrush = if ($showNotes) { $noteLineBrush } else { $hairlineBrush }
    $showFloatingNotesButton.Foreground = if ($showNotes) { $noteInkBrush } else { $noteInkBrush }
    Render-TagFilterControls; Render-TagEditorSelectors; Render-TagManager
    Render-Cards; Render-Notes; Render-FloatingContent
    if ($showNotes) {
        if ($window.IsVisible) { [void] $noteInput.Focus() }
        else { [void] $floatingNoteInput.Focus() }
    }
}

function Invoke-NoteMutationFromUi {
    param(
        [Parameter(Mandatory)] [ValidateSet('Add', 'Update', 'SetCompletion', 'Remove')] [string] $Action,
        [string] $Text,
        [string] $Id,
        [bool] $IsCompleted,
        [System.Collections.IEnumerable] $TagIds
    )

    $arguments = @{
        Notes = $script:notes
        Action = $Action
        PersistCandidate = {
            param($candidateNotes)
            Save-Settings -Notes $candidateNotes
        }
    }
    if ($Action -in @('Add', 'Update')) {
        $arguments.Text = $Text
        $arguments.TagIds = $TagIds
    }
    if ($Action -eq 'Update') { $arguments.Id = $Id }
    if ($Action -eq 'SetCompletion') {
        $arguments.Id = $Id
        $arguments.IsCompleted = $IsCompleted
    }
    if ($Action -eq 'Remove') { $arguments.Id = $Id }

    try {
        $result = Invoke-NoteMutation @arguments
    }
    catch {
        try { Render-Notes; Render-FloatingContent } catch {}
        Set-NoteMessage -Text $_.Exception.Message -IsError
        return $null
    }

    if (-not $result.Changed) { return $result }
    $script:notes = $result.Notes
    try {
        Set-NoteMessage -Text $result.Message
        Render-Notes; Render-FloatingContent
    }
    catch {
        Set-NoteMessage -Text ('便签已保存，但显示更新失败：{0}' -f $_.Exception.Message) -IsError
    }
    $result
}

function Add-NoteFromInput {
    param(
        [Parameter(Mandatory)] [System.Windows.Controls.TextBox] $TextBox,
        [Parameter(Mandatory)] [ValidateSet('NoteEditor', 'FloatingNoteEditor')] [string] $Context
    )

    $knownTagIds = @($script:tags | ForEach-Object { $_.id })
    $tagIds = ConvertTo-CountdownTagIds -TagIds (Get-TagSelectionForContext -Context $Context) -KnownTagIds $knownTagIds
    $isNoteEdit = $Context -eq 'NoteEditor' -and -not [string]::IsNullOrWhiteSpace($script:editingNoteId)
    $result = if ($isNoteEdit) {
        Invoke-NoteMutationFromUi -Action Update -Id $script:editingNoteId -Text $TextBox.Text -TagIds $tagIds
    }
    else {
        Invoke-NoteMutationFromUi -Action Add -Text $TextBox.Text -TagIds $tagIds
    }
    if ($null -ne $result -and $result.Changed) {
        if ($isNoteEdit) { Reset-NoteEditor }
        else { $TextBox.Text = '' }
        [void] $TextBox.Focus()
    }
}

function Reset-NoteEditor {
    $script:editingNoteId = $null
    $noteInput.Text = ''
    $addNoteButton.Content = '添加'
    $addNoteButton.ToolTip = '添加便签'
    $cancelNoteEditButton.Visibility = 'Collapsed'
    Set-TagSelectionValues -Context 'NoteEditor' -TagIds (Get-CurrentNoteTagInheritance)
    Render-TagEditorSelectors
}

function Start-NoteEditing {
    param([Parameter(Mandatory)] [string] $Id)

    $note = @($script:notes | Where-Object { $_.id -eq $Id })[0]
    if ($null -eq $note) { return }
    $script:editingNoteId = $note.id
    $noteInput.Text = $note.text
    $addNoteButton.Content = '保存'
    $addNoteButton.ToolTip = '保存便签修改'
    $cancelNoteEditButton.Visibility = 'Visible'
    Set-TagSelectionValues -Context 'NoteEditor' -TagIds (Get-RecordTagIds -Record $note)
    Set-NoteMessage -Text ''
    Render-TagEditorSelectors
    [void] $noteInput.Focus()
}

function Set-NoteCompletion {
    param([Parameter(Mandatory)] [string] $Id, [Parameter(Mandatory)] [bool] $IsCompleted)

    [void] (Invoke-NoteMutationFromUi -Action SetCompletion -Id $Id -IsCompleted $IsCompleted)
}

function Remove-Note {
    param([Parameter(Mandatory)] [string] $Id)

    $note = @($script:notes | Where-Object { $_.id -eq $Id })[0]
    if ($null -eq $note) { return }
    $answer = [System.Windows.MessageBox]::Show(('确定永久删除“{0}”吗？' -f $note.text), '删除便签', 'YesNo', 'Warning')
    if ($answer -ne 'Yes') { return }
    $result = Invoke-NoteMutationFromUi -Action Remove -Id $Id
    if ($null -ne $result -and $result.Changed -and $script:editingNoteId -eq $Id) { Reset-NoteEditor }
}

function Toggle-CompletedNotes {
    $script:showCompletedNotes = -not $script:showCompletedNotes
    Render-Notes; Render-FloatingContent
}

function New-CardButton {
    param([string] $Content, [string] $ToolTip)
    $button = New-Object System.Windows.Controls.Button
    $button.Content = $Content; $button.ToolTip = $ToolTip; $button.Height = 40; $button.MinWidth = 58
    $button.Margin = '8,0,0,0'; $button.Padding = '12,0'; $button.Cursor = 'Hand'
    $button.Background = $canvasBrush; $button.Foreground = $inkBrush; $button.BorderBrush = $hairlineBrush; $button.BorderThickness = 1
    $button.Style = $window.FindResource('SecondaryButton')
    $button
}

function Update-CardValues {
    $nowUtc = [DateTimeOffset]::UtcNow
    foreach ($record in $script:countdowns) {
        $view = $script:cardViews[$record.id]
        if ($null -eq $view) { continue }
        $parts = Get-CountdownParts -TargetUtc $record.targetUtc -NowUtc $nowUtc
        if ($parts.IsElapsed) {
            $view.Remaining.Text = '已到期'
            $view.Remaining.Foreground = $errorBrush
            $view.Status.Text = '已到期'
            $view.Status.Background = $expiredSurfaceBrush
            $view.Status.Foreground = $expiredTextBrush
        }
        else {
            $view.Remaining.Text = Format-CountdownParts -Parts $parts -Precision Second
            $view.Remaining.Foreground = $inkBrush
            $view.Status.Text = '进行中'
            $view.Status.Background = $successSurfaceBrush
            $view.Status.Foreground = $successTextBrush
        }
    }
}

function Render-Cards {
    $countdownList.Items.Clear()
    $script:cardViews = @{}
    $sorted = @(Get-FilteredCountdowns)
    $script:lastSortSignature = @($sorted | ForEach-Object { $_.id }) -join '|'
    $emptyState.Text = if ($script:countdowns.Count -eq 0) { '还没有倒计时。先在上方添加一个重要时刻。' } else { '当前筛选没有匹配的倒计时。' }
    $emptyState.Visibility = if ($sorted.Count -eq 0) { 'Visible' } else { 'Collapsed' }
    if ($script:activeWorkspace -eq 'Countdowns') { $countdownCount.Text = "$($sorted.Count) 个" }
    foreach ($record in $sorted) {
        $border = New-Object System.Windows.Controls.Border
        $border.Background = $canvasBrush; $border.BorderBrush = $hairlineBrush; $border.BorderThickness = 1
        $border.CornerRadius = 12; $border.Padding = 18; $border.Margin = '0,0,0,10'
        $grid = New-Object System.Windows.Controls.Grid
        [void] $grid.ColumnDefinitions.Add((New-Object System.Windows.Controls.ColumnDefinition -Property @{ Width = '*' }))
        [void] $grid.ColumnDefinitions.Add((New-Object System.Windows.Controls.ColumnDefinition -Property @{ Width = 'Auto' }))
        $stack = New-Object System.Windows.Controls.StackPanel
        $header = New-Object System.Windows.Controls.StackPanel; $header.Orientation = 'Horizontal'
        $name = New-Object System.Windows.Controls.TextBlock; $name.Text = $record.name; $name.FontSize = 16; $name.FontWeight = 'SemiBold'; $name.Foreground = $inkBrush; $name.VerticalAlignment = 'Center'; $name.TextWrapping = 'Wrap'; $name.MaxWidth = 360
        $status = New-Object System.Windows.Controls.TextBlock; $status.Margin = '10,0,0,0'; $status.Padding = '9,3'; $status.FontSize = 11; $status.FontWeight = 'SemiBold'
        [void] $header.Children.Add($name); [void] $header.Children.Add($status)
        $remaining = New-Object System.Windows.Controls.TextBlock; $remaining.Margin = '0,10,0,0'; $remaining.FontFamily = 'Consolas'; $remaining.FontSize = 22; $remaining.FontWeight = 'SemiBold'
        $beijing = [System.TimeZoneInfo]::ConvertTimeBySystemTimeZoneId($record.targetUtc, 'China Standard Time')
        $deadline = New-Object System.Windows.Controls.TextBlock; $deadline.Margin = '0,7,0,0'; $deadline.FontSize = 12; $deadline.Foreground = $mutedBrush; $deadline.Text = "截止：$($beijing.ToString('yyyy年MM月dd日 HH:mm:ss'))"
        [void] $stack.Children.Add($header); [void] $stack.Children.Add($remaining); [void] $stack.Children.Add($deadline)
        if (@(Get-RecordTagIds -Record $record).Count -gt 0) { [void] $stack.Children.Add((New-TagChipPanel -TagIds $record.tagIds)) }
        $actions = New-Object System.Windows.Controls.StackPanel; $actions.Orientation = 'Horizontal'; $actions.VerticalAlignment = 'Center'; [System.Windows.Controls.Grid]::SetColumn($actions, 1)
        $edit = New-CardButton -Content '编辑' -ToolTip "编辑 $($record.name)"
        $delete = New-CardButton -Content '删除' -ToolTip "删除 $($record.name)"; $delete.Foreground = $dangerTextBrush
        $edit.Tag = $record.id
        $delete.Tag = $record.id
        $edit.Add_Click({ param($sender, $eventArgs)
            $actionId = [string] $sender.Tag
            $item = @($script:countdowns | Where-Object { $_.id -eq $actionId })[0]
            $script:editingId = $item.id; $editorTitle.Text = "编辑：$($item.name)"; $countdownName.Text = $item.name
            Set-TimeControls -BeijingTime ([System.TimeZoneInfo]::ConvertTimeBySystemTimeZoneId($item.targetUtc, 'China Standard Time'))
            $script:countdownEditorTagIds.Clear()
            foreach ($tagId in @(Get-RecordTagIds -Record $item)) { [void] $script:countdownEditorTagIds.Add($tagId) }
            Render-TagEditorSelectors
            $saveButton.Content = '保存修改'; $cancelButton.Visibility = 'Visible'; $messageText.Text = ''; $countdownName.Focus()
        })
        $delete.Add_Click({ param($sender, $eventArgs)
            $actionId = [string] $sender.Tag
            $item = @($script:countdowns | Where-Object { $_.id -eq $actionId })[0]
            $answer = [System.Windows.MessageBox]::Show(('确定永久删除“{0}”吗？' -f $item.name), '删除倒计时', 'YesNo', 'Warning')
            if ($answer -eq 'Yes') {
                try {
                    $candidate = New-CurrentSettingsCandidate
                    $candidateItem = @($candidate.Records | Where-Object { $_.id -eq $actionId })[0]
                    if ($null -eq $candidateItem) { throw '未找到要删除的倒计时。' }
                    [void] $candidate.Records.Remove($candidateItem)
                    Save-Settings -State $candidate
                    Commit-SettingsState -State $candidate
                    if ($script:editingId -eq $actionId) { Reset-Editor }
                    Render-Cards; Render-FloatingContent; $messageText.Foreground = $successTextBrush; $messageText.Text = ('已删除“{0}”。' -f $item.name)
                }
                catch { $messageText.Foreground = $errorBrush; $messageText.Text = $_.Exception.Message }
            }
        })
        [void] $actions.Children.Add($edit); [void] $actions.Children.Add($delete)
        [void] $grid.Children.Add($stack); [void] $grid.Children.Add($actions); $border.Child = $grid
        [void] $countdownList.Items.Add($border)
        $script:cardViews[$record.id] = [pscustomobject]@{ Remaining = $remaining; Status = $status }
    }
    Update-CardValues
}

if (Test-Path -LiteralPath $settingsPath) {
    try {
        $settings = [System.IO.File]::ReadAllText($settingsPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
        $loaded = ConvertFrom-CountdownSettingsObject -Settings $settings
        foreach ($record in $loaded.Records) { [void] $script:countdowns.Add($record) }
        foreach ($note in $loaded.Notes) { [void] $script:notes.Add($note) }
        foreach ($tag in $loaded.Tags) { [void] $script:tags.Add($tag) }
        $script:displayPrecision = $loaded.Preferences.Precision
        $script:salary = $loaded.Preferences.MonthlySalary
        $script:workStart = $loaded.Preferences.WorkStart
        $script:workEnd = $loaded.Preferences.WorkEnd
        $script:settingsWereMigrated = $loaded.Migrated
        if ($loaded.Migrated -and -not $ValidateOnly) { Save-Settings }
    }
    catch { $messageText.Text = "已有设置无法读取：$($_.Exception.Message)" }
}

$setNowButton.Add_Click({ Set-TimeControls -BeijingTime (Get-BeijingNow); $messageText.Text = '' })
$cancelButton.Add_Click({ Reset-Editor })
$savePreferencesButton.Add_Click({
    try {
        [decimal] $salary = 0
        if (-not [decimal]::TryParse($monthlySalary.Text.Trim(), [System.Globalization.NumberStyles]::Number, [System.Globalization.CultureInfo]::InvariantCulture, [ref] $salary)) { throw '月薪请输入 0 或更大的数字。' }
        $preferences = New-CountdownPreferences -Precision ([string] $displayPrecisionInput.SelectedValue) -MonthlySalary $salary -WorkStartText $workStartInput.Text -WorkEndText $workEndInput.Text
        $candidate = New-CurrentSettingsCandidate
        $candidate.Preferences = $preferences
        Save-Settings -State $candidate
        Commit-SettingsState -State $candidate
        Update-FloatingCountdowns
        $messageText.Foreground = $successTextBrush; $messageText.Text = '悬浮窗精度和今日收入设置已保存。'
    }
    catch { $messageText.Foreground = $errorBrush; $messageText.Text = $_.Exception.Message }
})
$saveButton.Add_Click({
    try {
        $messageText.Text = ''
        $name = $countdownName.Text.Trim()
        if ([string]::IsNullOrWhiteSpace($name)) { throw '请输入倒计时名称。' }
        if ($name.Length -gt 30) { throw '倒计时名称不能超过 30 个字符。' }
        $duplicate = @($script:countdowns | Where-Object { $_.id -ne $script:editingId -and [string]::Equals($_.name, $name, [System.StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
        if ($duplicate) { throw '倒计时名称不能重复。' }
        if ($null -eq $targetDate.SelectedDate -or $null -eq $targetHour.SelectedItem -or $null -eq $targetMinute.SelectedItem -or $null -eq $targetSecond.SelectedItem) { throw '请选择完整的截止日期和时间。' }
        $dateText = ConvertTo-CountdownDateText -SelectedDate $targetDate.SelectedDate
        $timeText = "$($targetHour.SelectedItem):$($targetMinute.SelectedItem):$($targetSecond.SelectedItem)"
        $targetUtc = ConvertTo-CountdownTargetUtc -DateText $dateText -TimeText $timeText
        if ($targetUtc -le [DateTimeOffset]::UtcNow) { throw '截止时间必须晚于当前北京时间，请向后调整。' }
        $tagIds = ConvertTo-CountdownTagIds -TagIds $script:countdownEditorTagIds -KnownTagIds @($script:tags | ForEach-Object { $_.id })
        $candidate = New-CurrentSettingsCandidate
        if ($null -eq $script:editingId) {
            [void] $candidate.Records.Add((New-CountdownRecord -Id ([guid]::NewGuid().ToString('N')) -Name $name -TargetUtc $targetUtc -TagIds $tagIds))
            $success = '已添加“{0}”。' -f $name
        }
        else {
            $item = @($candidate.Records | Where-Object { $_.id -eq $script:editingId })[0]
            if ($null -eq $item) { throw '未找到要编辑的倒计时。' }
            $item.name = $name; $item.targetUtc = $targetUtc; $item.tagIds = $tagIds
            $success = '已保存“{0}”。' -f $name
        }
        Save-Settings -State $candidate
        Commit-SettingsState -State $candidate
        Reset-Editor; Render-Cards
        Render-FloatingContent
        $messageText.Foreground = $successTextBrush; $messageText.Text = $success
    }
    catch { $messageText.Foreground = $errorBrush; $messageText.Text = $_.Exception.Message }
})

$topmostCheck.Add_Checked({ $window.Topmost = $true })
$topmostCheck.Add_Unchecked({ $window.Topmost = $false })
$showCountdownsButton.Add_Click({ Set-Workspace -Workspace 'Countdowns' })
$showNotesButton.Add_Click({ Set-Workspace -Workspace 'Notes' })
$showFloatingCountdownsButton.Add_Click({ Set-Workspace -Workspace 'Countdowns' })
$showFloatingNotesButton.Add_Click({ Set-Workspace -Workspace 'Notes' })
$script:tagFilterAllButton.Add_Click({ Set-TagFilter -Mode 'All' })
$script:tagFilterUntaggedButton.Add_Click({ Set-TagFilter -Mode 'Untagged' })
$script:tagMatchAnyButton.Add_Click({ Set-TagMatchMode -Mode 'Any' })
$script:tagMatchAllButton.Add_Click({ Set-TagMatchMode -Mode 'All' })
$script:tagManagerToggleButton.Add_Click({ Toggle-TagManager })
$script:countdownStatusAllButton.Add_Click({ Set-CountdownStatusFilter -Status 'All' })
$script:countdownStatusActiveButton.Add_Click({ Set-CountdownStatusFilter -Status 'Active' })
$script:countdownStatusExpiredButton.Add_Click({ Set-CountdownStatusFilter -Status 'Expired' })
$script:noteStatusAllButton.Add_Click({ Set-NoteStatusFilter -Status 'All' })
$script:noteStatusActiveButton.Add_Click({ Set-NoteStatusFilter -Status 'Active' })
$script:noteStatusCompletedButton.Add_Click({ Set-NoteStatusFilter -Status 'Completed' })
$script:noteSearchInput.Add_TextChanged({
    param($sender, $eventArgs)
    $script:noteSearchText = $sender.Text
    Refresh-TagFilteredViews
})
$script:countdownTagSearchInput.Add_TextChanged({ Render-TagEditorSelectors })
$script:noteTagSearchInput.Add_TextChanged({ Render-TagEditorSelectors })
$script:tagManagerCreateButton.Add_Click({ Create-TagFromInput -TextBox $script:tagManagerNewTagInput -Context 'None' })
$script:countdownCreateTagButton.Add_Click({ Create-TagFromInput -TextBox $script:countdownNewTagInput -Context 'CountdownEditor' })
$script:noteCreateTagButton.Add_Click({ Create-TagFromInput -TextBox $script:noteNewTagInput -Context 'NoteEditor' })
$script:countdownManageTagsButton.Add_Click({ if (-not $script:tagManagerVisible) { Toggle-TagManager } })
$script:noteManageTagsButton.Add_Click({ if (-not $script:tagManagerVisible) { Toggle-TagManager } })
$script:floatingTagSelectorButton.Add_Click({ Toggle-FloatingTagPopup })
$script:floatingClearTagButton.Add_Click({ Clear-FloatingNoteTags })
$addNoteButton.Add_Click({ Add-NoteFromInput -TextBox $noteInput -Context 'NoteEditor' })
$cancelNoteEditButton.Add_Click({ Reset-NoteEditor })
$floatingAddNoteButton.Add_Click({ Add-NoteFromInput -TextBox $floatingNoteInput -Context 'FloatingNoteEditor' })
$noteInput.Add_KeyDown({
    param($sender, $eventArgs)
    if ($eventArgs.Key -eq [System.Windows.Input.Key]::Enter) { Add-NoteFromInput -TextBox $noteInput -Context 'NoteEditor'; $eventArgs.Handled = $true }
})
$floatingNoteInput.Add_KeyDown({
    param($sender, $eventArgs)
    if ($eventArgs.Key -eq [System.Windows.Input.Key]::Enter) { Add-NoteFromInput -TextBox $floatingNoteInput -Context 'FloatingNoteEditor'; $eventArgs.Handled = $true }
})
$toggleCompletedNotesButton.Add_Click({ Toggle-CompletedNotes })
$floatingDragArea.Add_MouseLeftButtonDown({
    param($sender, $eventArgs)
    if ($eventArgs.LeftButton -eq [System.Windows.Input.MouseButtonState]::Pressed) { $floatingWindow.DragMove() }
})
$openManagerButton.Add_Click({
    Set-Workspace -Workspace $script:activeWorkspace
    $window.Show()
    $window.WindowState = 'Normal'
    $window.Activate()
})
$closeFloatingButton.Add_Click({ $floatingWindow.Close() })
$floatingWindow.Add_Loaded({
    $workArea = [System.Windows.SystemParameters]::WorkArea
    $floatingWindow.Left = $workArea.Right - $floatingWindow.ActualWidth - 24
    $floatingWindow.Top = $workArea.Bottom - $floatingWindow.ActualHeight - 48
})
$floatingWindow.Add_ContentRendered({
    if ($script:runtimeReceiptWritten) { return }
    [void] (Write-CountdownWindowReadyReceipt -Path $runtimeStatusPath -LaunchToken $script:launchToken -ProcessId $PID -SessionId ([System.Diagnostics.Process]::GetCurrentProcess().SessionId) -ApplicationPath $script:applicationPath -CalendarState Pending)
    $script:runtimeReceiptWritten = $true
    if ($ExitAfterReadyForTest) {
        $script:testCloseTimer = New-Object Windows.Threading.DispatcherTimer
        $script:testCloseTimer.Interval = [TimeSpan]::FromSeconds(2)
        $script:testCloseTimer.Add_Tick({
            $script:testCloseTimer.Stop()
            $floatingWindow.Close()
        })
        $script:testCloseTimer.Start()
    }
})
$window.Add_Closing({ param($sender, $eventArgs) if (-not $ValidateOnly) { $eventArgs.Cancel = $true; $window.Hide() } })
$floatingWindow.Add_Closed({
    if ($null -ne $script:testCloseTimer) { $script:testCloseTimer.Stop(); $script:testCloseTimer = $null }
    Stop-ChinaHolidayCalendarRequest
    if ($null -ne $script:singleInstanceMutex) { $script:singleInstanceMutex.ReleaseMutex(); $script:singleInstanceMutex.Dispose() }
    [Windows.Threading.Dispatcher]::CurrentDispatcher.InvokeShutdown()
})

function Invoke-ValidationButtonClick {
    param([Parameter(Mandatory)] [System.Windows.Controls.Primitives.ButtonBase] $Button)

    $Button.RaiseEvent((New-Object System.Windows.RoutedEventArgs([System.Windows.Controls.Primitives.ButtonBase]::ClickEvent)))
}

function Get-ValidationTagToggle {
    param(
        [Parameter(Mandatory)] [System.Windows.Controls.Panel] $Panel,
        [Parameter(Mandatory)] [string] $Id
    )

    $toggle = @($Panel.Children | Where-Object {
            $_ -is [System.Windows.Controls.Primitives.ToggleButton] -and [string] $_.Uid -eq $Id
        })[0]
    if ($null -eq $toggle) { throw "未找到标签选择控件：$Id" }
    $toggle
}

function Set-ValidationCountdownTarget {
    $targetDate.SelectedDate = [DateTime]'2099-08-30'
    $targetHour.SelectedItem = '12'
    $targetMinute.SelectedItem = '34'
    $targetSecond.SelectedItem = '56'
}

function Invoke-TagInteractionValidation {
    if ([string]::IsNullOrWhiteSpace($SettingsFile)) { throw '-ExerciseTagInteractions requires -SettingsFile.' }

    $token = [guid]::NewGuid().ToString('N').Substring(0, 8)
    $tagAName = "验证标签甲$token"
    $tagBName = "验证标签乙$token"
    $renamedTagAName = "验证标签甲改$token"
    $countdownAName = "标签任一甲$token"
    $countdownBothName = "标签全部甲乙$token"
    $countdownUntaggedName = "标签无标$token"
    $noteBothText = "验证标签搜索$token"
    $floatingNoteText = "验证悬浮继承$token"
    $floatingOverrideText = "验证悬浮改选$token"
    $floatingClearText = "验证悬浮清空$token"
    $editedFloatingNoteText = "验证便签编辑$token"

    Set-Workspace -Workspace 'Countdowns'
    Reset-Editor
    $countdownName.Text = $countdownAName
    Set-ValidationCountdownTarget
    $script:countdownNewTagInput.Text = $tagAName
    Invoke-ValidationButtonClick -Button $script:countdownCreateTagButton
    $tagA = @($script:tags | Where-Object { $_.name -eq $tagAName })[0]
    if ($null -eq $tagA -or $tagA.id -notin @($script:countdownEditorTagIds)) { throw "创建并选中第一个共享标签失败：$($script:tagManagerMessage.Text)；已选=$(@($script:countdownEditorTagIds) -join ',')。" }
    Invoke-ValidationButtonClick -Button $saveButton
    $countdownA = @($script:countdowns | Where-Object { $_.name -eq $countdownAName })[0]
    if ($null -eq $countdownA -or @($countdownA.tagIds).Count -ne 1 -or $countdownA.tagIds[0] -ne $tagA.id) { throw '倒计时没有保存第一个标签关联。' }

    Reset-Editor
    $countdownName.Text = $countdownBothName
    Set-ValidationCountdownTarget
    (Get-ValidationTagToggle -Panel $script:countdownTagSelectorPanel -Id $tagA.id).IsChecked = $true
    $script:countdownNewTagInput.Text = $tagBName
    Invoke-ValidationButtonClick -Button $script:countdownCreateTagButton
    $tagB = @($script:tags | Where-Object { $_.name -eq $tagBName })[0]
    if ($null -eq $tagB -or @($script:countdownEditorTagIds).Count -ne 2) { throw '创建并选中第二个共享标签失败。' }
    Invoke-ValidationButtonClick -Button $saveButton
    $countdownBoth = @($script:countdowns | Where-Object { $_.name -eq $countdownBothName })[0]
    if ($null -eq $countdownBoth -or @($countdownBoth.tagIds).Count -ne 2) { throw '倒计时没有保存多标签关联。' }

    Reset-Editor
    $countdownName.Text = $countdownUntaggedName
    Set-ValidationCountdownTarget
    Invoke-ValidationButtonClick -Button $saveButton
    $countdownUntagged = @($script:countdowns | Where-Object { $_.name -eq $countdownUntaggedName })[0]
    if ($null -eq $countdownUntagged -or @(Get-RecordTagIds -Record $countdownUntagged).Count -ne 0) { throw '无标签倒计时没有创建成功。' }

    Set-TagFilter -Mode 'All'
    (Get-ValidationTagToggle -Panel $script:tagFilterTagsPanel -Id $tagA.id).IsChecked = $true
    (Get-ValidationTagToggle -Panel $script:tagFilterTagsPanel -Id $tagB.id).IsChecked = $true
    Set-TagMatchMode -Mode 'Any'
    $anyCountdownIds = @(Get-FilteredCountdowns | ForEach-Object { $_.id })
    $anyMatches = $countdownA.id -in $anyCountdownIds -and $countdownBoth.id -in $anyCountdownIds -and $countdownUntagged.id -notin $anyCountdownIds
    Set-TagMatchMode -Mode 'All'
    $allCountdownIds = @(Get-FilteredCountdowns | ForEach-Object { $_.id })
    $allMatches = $countdownBoth.id -in $allCountdownIds -and $countdownA.id -notin $allCountdownIds -and $countdownUntagged.id -notin $allCountdownIds
    Set-CountdownStatusFilter -Status 'Active'
    $activeStatusMatches = $countdownBoth.id -in @(Get-FilteredCountdowns | ForEach-Object { $_.id })
    Set-CountdownStatusFilter -Status 'All'
    Set-TagFilter -Mode 'Untagged'
    $untaggedMatches = $countdownUntagged.id -in @(Get-FilteredCountdowns | ForEach-Object { $_.id }) -and $countdownA.id -notin @(Get-FilteredCountdowns | ForEach-Object { $_.id })

    Set-TagFilter -Mode 'All'
    Set-Workspace -Workspace 'Notes'
    (Get-ValidationTagToggle -Panel $script:noteTagSelectorPanel -Id $tagA.id).IsChecked = $true
    (Get-ValidationTagToggle -Panel $script:noteTagSelectorPanel -Id $tagB.id).IsChecked = $true
    $noteInput.Text = $noteBothText
    Invoke-ValidationButtonClick -Button $addNoteButton
    $noteBoth = @($script:notes | Where-Object { $_.text -eq $noteBothText })[0]
    if ($null -eq $noteBoth -or @($noteBoth.tagIds).Count -ne 2) { throw '便签没有保存共享标签关联。' }

    Set-TagFilter -Mode 'All'
    (Get-ValidationTagToggle -Panel $script:tagFilterTagsPanel -Id $tagA.id).IsChecked = $true
    Set-NoteStatusFilter -Status 'Active'
    $script:noteSearchInput.Text = $noteBothText
    $searchMatches = $noteBoth.id -in @(Get-FilteredNotes | ForEach-Object { $_.id })
    $noteRow = @($noteList.Items | Where-Object {
            $_.Child.Children[1] -is [System.Windows.Controls.StackPanel] -and $_.Child.Children[1].Children[0].Text -eq $noteBothText
        })[0]
    if ($null -eq $noteRow) { throw '未找到待完成便签的可操作行。' }
    $completionToggle = @($noteRow.Child.Children | Where-Object { $_ -is [System.Windows.Controls.CheckBox] })[0]
    $completionToggle.IsChecked = $true
    $completedNote = @($script:notes | Where-Object { $_.id -eq $noteBoth.id })[0]
    Set-NoteStatusFilter -Status 'Completed'
    $completionRetainsTags = $null -ne $completedNote -and [bool] $completedNote.isCompleted -and @($completedNote.tagIds).Count -eq 2 -and $completedNote.id -in @(Get-FilteredNotes | ForEach-Object { $_.id })
    Set-NoteStatusFilter -Status 'All'
    $script:noteSearchInput.Text = ''

    Set-TagFilter -Mode 'All'
    (Get-ValidationTagToggle -Panel $script:tagFilterTagsPanel -Id $tagA.id).IsChecked = $true
    Set-Workspace -Workspace 'Notes'
    $floatingNoteInput.Text = $floatingNoteText
    Invoke-ValidationButtonClick -Button $floatingAddNoteButton
    $floatingNote = @($script:notes | Where-Object { $_.text -eq $floatingNoteText })[0]
    $floatingInheritsTags = $null -ne $floatingNote -and @($floatingNote.tagIds).Count -eq 1 -and $floatingNote.tagIds[0] -eq $tagA.id -and [string] $script:floatingTagSelectorButton.Content -eq '标签（1）' -and $script:floatingInheritedTagText.Text -eq '已选 1 个标签'

    $floatingPopupStartsClosed = -not $script:floatingTagPopup.IsOpen
    Invoke-ValidationButtonClick -Button $script:floatingTagSelectorButton
    $floatingPopupOpened = $script:floatingTagPopup.IsOpen
    (Get-ValidationTagToggle -Panel $script:floatingTagSelectorPanel -Id $tagB.id).IsChecked = $true
    $floatingNoteInput.Text = $floatingOverrideText
    Invoke-ValidationButtonClick -Button $floatingAddNoteButton
    $floatingOverrideNote = @($script:notes | Where-Object { $_.text -eq $floatingOverrideText })[0]
    $floatingCanSelectAdditionalTag = $null -ne $floatingOverrideNote -and @($floatingOverrideNote.tagIds).Count -eq 2 -and $tagA.id -in @($floatingOverrideNote.tagIds) -and $tagB.id -in @($floatingOverrideNote.tagIds)
    if (-not $script:floatingTagPopup.IsOpen) { Invoke-ValidationButtonClick -Button $script:floatingTagSelectorButton }
    $floatingPopupReopenedForClear = $script:floatingTagPopup.IsOpen
    Invoke-ValidationButtonClick -Button $script:floatingClearTagButton
    $floatingNoteInput.Text = $floatingClearText
    Invoke-ValidationButtonClick -Button $floatingAddNoteButton
    $floatingClearNote = @($script:notes | Where-Object { $_.text -eq $floatingClearText })[0]
    $floatingCanClearTags = @($script:floatingNoteTagIds).Count -eq 0 -and $null -ne $floatingClearNote -and @(Get-RecordTagIds -Record $floatingClearNote).Count -eq 0 -and $script:floatingInheritedTagText.Text -eq '本次新增不带标签'
    if ($script:floatingTagPopup.IsOpen) { Invoke-ValidationButtonClick -Button $script:floatingTagSelectorButton }
    $floatingPopupCloses = -not $script:floatingTagPopup.IsOpen
    $floatingPopupOnDemand = $floatingPopupStartsClosed -and $floatingPopupOpened -and $floatingPopupReopenedForClear -and $floatingPopupCloses

    $noteEditRow = @($noteList.Items | Where-Object {
            $_.Child.Children[1] -is [System.Windows.Controls.StackPanel] -and $_.Child.Children[1].Children[0].Text -eq $floatingOverrideText
        })[0]
    if ($null -eq $noteEditRow) { throw '未找到可编辑便签行。' }
    $noteEditButton = @($noteEditRow.Child.Children | Where-Object {
            $_ -is [System.Windows.Controls.Button] -and [string] $_.Content -eq '编辑' -and [string] $_.Tag -eq $floatingOverrideNote.id
        })[0]
    if ($null -eq $noteEditButton) { throw '未找到便签编辑操作。' }
    Invoke-ValidationButtonClick -Button $noteEditButton
    (Get-ValidationTagToggle -Panel $script:noteTagSelectorPanel -Id $tagB.id).IsChecked = $false
    $noteInput.Text = $editedFloatingNoteText
    Invoke-ValidationButtonClick -Button $addNoteButton
    $editedFloatingNote = @($script:notes | Where-Object { $_.id -eq $floatingOverrideNote.id })[0]
    $persistedEditedFloatingNote = @((([System.IO.File]::ReadAllText($settingsPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json).notes) | Where-Object { $_.id -eq $floatingOverrideNote.id })[0]
    $noteEditingPersistsTags =
        $null -ne $editedFloatingNote -and
        $editedFloatingNote.text -eq $editedFloatingNoteText -and
        @($editedFloatingNote.tagIds).Count -eq 1 -and
        $editedFloatingNote.tagIds[0] -eq $tagA.id -and
        $null -ne $persistedEditedFloatingNote -and
        $persistedEditedFloatingNote.text -eq $editedFloatingNoteText -and
        @($persistedEditedFloatingNote.tagIds).Count -eq 1 -and
        $persistedEditedFloatingNote.tagIds[0] -eq $tagA.id -and
        [string]::IsNullOrWhiteSpace($script:editingNoteId) -and
        [string] $addNoteButton.Content -eq '添加' -and
        $cancelNoteEditButton.Visibility -eq 'Collapsed'

    if (-not $script:tagManagerVisible) { Toggle-TagManager }
    $tagManagerRow = @($script:tagManagerList.Items | Where-Object {
            $_ -is [System.Windows.Controls.Grid] -and $_.Children.Count -gt 1 -and [string] $_.Children[0].Uid -eq $tagA.id
        })[0]
    if ($null -eq $tagManagerRow) { throw '未找到标签改名控件。' }
    $tagManagerRow.Children[0].Text = $renamedTagAName
    Invoke-ValidationButtonClick -Button $tagManagerRow.Children[1]
    $renamedTagA = @($script:tags | Where-Object { $_.id -eq $tagA.id })[0]
    $renamePreservesAssociations = $null -ne $renamedTagA -and $renamedTagA.name -eq $renamedTagAName -and $tagA.id -in @(Get-RecordTagIds -Record (@($script:countdowns | Where-Object { $_.id -eq $countdownA.id })[0])) -and $tagA.id -in @(Get-RecordTagIds -Record (@($script:notes | Where-Object { $_.id -eq $floatingNote.id })[0]))

    $recordCountBeforeDeletion = $script:countdowns.Count
    $noteCountBeforeDeletion = $script:notes.Count
    Remove-TagFromLibrary -Id $tagB.id -SkipConfirmation
    $allRecordsAfterDeletion = @($script:countdowns) + @($script:notes)
    $deleteDetachesOnly = @($script:tags | Where-Object { $_.id -eq $tagB.id }).Count -eq 0 -and $script:countdowns.Count -eq $recordCountBeforeDeletion -and $script:notes.Count -eq $noteCountBeforeDeletion -and @($allRecordsAfterDeletion | Where-Object { $tagB.id -in @(Get-RecordTagIds -Record $_) }).Count -eq 0 -and $tagA.id -in @(Get-RecordTagIds -Record (@($script:countdowns | Where-Object { $_.id -eq $countdownBoth.id })[0]))

    $result = [pscustomobject]@{
        Executed = $true
        CreationAndAssociation = $null -ne $tagA -and $null -ne $tagB -and $null -ne $countdownA -and $null -ne $countdownBoth -and $null -ne $noteBoth
        AnyAllFilter = $anyMatches -and $allMatches
        UntaggedAndCountdownStatus = $untaggedMatches -and $activeStatusMatches
        NoteStatusSearchAndCompletion = $searchMatches -and $completionRetainsTags
        FloatingNoteInheritsTags = $floatingInheritsTags
        FloatingNotePopupOnDemand = $floatingPopupOnDemand
        FloatingNoteCanOverrideTags = $floatingCanSelectAdditionalTag -and $floatingCanClearTags
        NoteEditingPersistsTags = $noteEditingPersistsTags
        RenameAndDeleteDetachOnly = $renamePreservesAssociations -and $deleteDetachesOnly
    }
    Set-TagFilter -Mode 'All'
    Set-CountdownStatusFilter -Status 'All'
    Set-NoteStatusFilter -Status 'All'
    $script:noteSearchInput.Text = ''
    $script:tagManagerVisible = $false
    Set-Workspace -Workspace 'Countdowns'
    $result
}

Reset-Editor
$displayPrecisionInput.SelectedValue = $script:displayPrecision
$monthlySalary.Text = $script:salary.ToString('0.##', [System.Globalization.CultureInfo]::InvariantCulture)
$workStartInput.Text = Format-WorkTime -Value $script:workStart
$workEndInput.Text = Format-WorkTime -Value $script:workEnd
Set-Workspace -Workspace 'Countdowns'

if ($ValidateOnly) {
    $targetDate.SelectedDate = [DateTime]'2026-08-30'
    [void] $targetDate.ApplyTemplate()
    $datePickerParts = @($targetDate.Template.FindName('PART_TextBox', $targetDate), $targetDate.Template.FindName('PART_Button', $targetDate), $targetDate.Template.FindName('PART_Popup', $targetDate), $targetDate.Template.FindName('PART_Calendar', $targetDate))
    if (@($datePickerParts | Where-Object { $null -eq $_ }).Count -gt 0) { throw 'DatePicker template is missing one or more required PART_* controls.' }
    if ($datePickerParts[0].Text -ne '2026年8月30日') { throw "DatePicker did not render the complete selected date. Actual: $($datePickerParts[0].Text)" }
    $targetDate.IsDropDownOpen = $false
    $previewClick = New-Object System.Windows.Input.MouseButtonEventArgs([System.Windows.Input.Mouse]::PrimaryDevice, 0, [System.Windows.Input.MouseButton]::Left)
    $previewClick.RoutedEvent = [System.Windows.UIElement]::PreviewMouseLeftButtonDownEvent
    $targetDate.RaiseEvent($previewClick)
    $datePickerParts[1].RaiseEvent((New-Object System.Windows.RoutedEventArgs([System.Windows.Controls.Primitives.ButtonBase]::ClickEvent)))
    $datePickerButtonOpensCalendar = $targetDate.IsDropDownOpen
    $targetDate.IsDropDownOpen = $false
    if ($null -eq $window.Icon) { throw 'Window icon was not loaded.' }
    $legacy = [pscustomobject]@{ schemaVersion = 1; timeZoneId = 'China Standard Time'; targetUtc = '2026-08-30T10:00:00Z' }
    $migration = ConvertFrom-CountdownSettingsObject -Settings $legacy
    $persistenceRoundTrip = $false
    $notePersistenceRoundTrip = $false
    $tagPersistenceRoundTrip = $false
    if (-not [string]::IsNullOrWhiteSpace($SettingsFile)) {
        $roundTrip = ConvertTo-CountdownSettingsJson -State (Get-CurrentSettingsState) | ConvertFrom-Json
        $persistenceRoundTrip = $roundTrip.schemaVersion -eq 5 -and $roundTrip.displayPrecision -eq $script:displayPrecision -and @($roundTrip.countdowns).Count -eq $script:countdowns.Count
        $notePersistenceRoundTrip = $roundTrip.schemaVersion -eq 5 -and @($roundTrip.notes).Count -eq $script:notes.Count
        $tagPersistenceRoundTrip = $roundTrip.schemaVersion -eq 5 -and @($roundTrip.tags).Count -eq $script:tags.Count
    }
    $noteAddViaClick = $false
    $noteStatusViaCheckbox = $false
    if ($ExerciseNoteAdd) {
        if ([string]::IsNullOrWhiteSpace($SettingsFile)) { throw '-ExerciseNoteAdd requires -SettingsFile.' }
        Set-Workspace -Workspace 'Notes'
        $notesBeforeAdd = $script:notes.Count
        $addedNoteText = '验证点击添加便签'
        $noteInput.Text = $addedNoteText
        $addNoteButton.RaiseEvent((New-Object System.Windows.RoutedEventArgs([System.Windows.Controls.Primitives.ButtonBase]::ClickEvent)))
        $noteAddViaClick = $script:notes.Count -eq ($notesBeforeAdd + 1) -and $noteInput.Text -eq '' -and @($script:notes | Where-Object { $_.text -eq $addedNoteText -and -not $_.isCompleted }).Count -eq 1
        if ($noteAddViaClick) {
            $addedNote = @($script:notes | Where-Object { $_.text -eq $addedNoteText })[0]
            $activeRow = New-NoteRow -Record $addedNote
            $activeCheckBox = @($activeRow.Child.Children | Where-Object { $_ -is [System.Windows.Controls.CheckBox] -and [string] $_.Tag -eq $addedNote.id })[0]
            if ($null -ne $activeCheckBox) {
                $activeCheckBox.RaiseEvent((New-Object System.Windows.RoutedEventArgs([System.Windows.Controls.Primitives.ToggleButton]::CheckedEvent)))
                $completedNote = @($script:notes | Where-Object { $_.id -eq $addedNote.id })[0]
                $wasMarkedComplete = [bool] $completedNote.isCompleted -and @((Get-CompletedNotes) | Where-Object { $_.id -eq $addedNote.id }).Count -eq 1
                $completedRow = New-NoteRow -Record $completedNote
                $completedCheckBox = @($completedRow.Child.Children | Where-Object { $_ -is [System.Windows.Controls.CheckBox] -and [string] $_.Tag -eq $addedNote.id })[0]
                if ($null -ne $completedCheckBox) {
                    $completedCheckBox.RaiseEvent((New-Object System.Windows.RoutedEventArgs([System.Windows.Controls.Primitives.ToggleButton]::UncheckedEvent)))
                }
                $restoredNote = @($script:notes | Where-Object { $_.id -eq $addedNote.id })[0]
                $noteStatusViaCheckbox = $wasMarkedComplete -and -not [bool] $restoredNote.isCompleted -and @((Get-ActiveNotes) | Where-Object { $_.id -eq $addedNote.id }).Count -eq 1
            }
        }
    }
    $tagInteractions = [pscustomobject]@{
        Executed = $false
        CreationAndAssociation = $false
        AnyAllFilter = $false
        UntaggedAndCountdownStatus = $false
        NoteStatusSearchAndCompletion = $false
        FloatingNoteInheritsTags = $false
        FloatingNotePopupOnDemand = $false
        FloatingNoteCanOverrideTags = $false
        NoteEditingPersistsTags = $false
        RenameAndDeleteDetachOnly = $false
    }
    if ($ExerciseTagInteractions) {
        $tagInteractions = Invoke-TagInteractionValidation
    }
    $countdownSaveFailurePreservesState = $false
    $countdownSaveFailureRetainsDraft = $false
    $preferenceSaveFailurePreservesState = $false
    $preferenceSaveFailureRetainsDraft = $false
    $persistenceFailureTempPathRemainsDirectory = $false
    $tagSaveFailurePreservesState = $false
    $tagSaveFailureRetainsDraft = $false
    $tagSaveFailureRetainsFilter = $false
    if ($ExercisePersistenceFailures) {
        if ([string]::IsNullOrWhiteSpace($SettingsFile)) { throw '-ExercisePersistenceFailures requires -SettingsFile.' }
        if (-not (Test-Path -LiteralPath "$settingsPath.tmp" -PathType Container)) { throw '-ExercisePersistenceFailures requires a pre-created <SettingsFile>.tmp directory.' }

        Set-Workspace -Workspace 'Countdowns'
        $countdownStateBeforeFailure = ConvertTo-CountdownSettingsJson -State (Get-CurrentSettingsState)
        $countdownDraftName = '验证保存失败倒计时'
        Reset-Editor
        $countdownName.Text = $countdownDraftName
        $targetDate.SelectedDate = [DateTime]'2099-08-30'
        $targetHour.SelectedItem = '12'
        $targetMinute.SelectedItem = '34'
        $targetSecond.SelectedItem = '56'
        if ($null -eq $targetDate.SelectedDate -or [string] $targetHour.SelectedItem -ne '12' -or [string] $targetMinute.SelectedItem -ne '34' -or [string] $targetSecond.SelectedItem -ne '56') { throw 'Persistence-failure countdown test controls were not initialized.' }
        $countdownFailureSentinel = 'countdown persistence failure was not exercised'
        $messageText.Text = $countdownFailureSentinel
        $messageText.Foreground = $mutedBrush
        $saveButton.RaiseEvent((New-Object System.Windows.RoutedEventArgs([System.Windows.Controls.Primitives.ButtonBase]::ClickEvent)))
        $countdownSaveFailurePreservesState =
            (ConvertTo-CountdownSettingsJson -State (Get-CurrentSettingsState)) -eq $countdownStateBeforeFailure -and
            $messageText.Text -ne $countdownFailureSentinel -and
            $messageText.Foreground -eq $errorBrush
        $countdownSaveFailureRetainsDraft =
            $countdownName.Text -eq $countdownDraftName -and
            $targetDate.SelectedDate -eq [DateTime]'2099-08-30' -and
            [string] $targetHour.SelectedItem -eq '12' -and
            [string] $targetMinute.SelectedItem -eq '34' -and
            [string] $targetSecond.SelectedItem -eq '56'

        $preferenceStateBeforeFailure = ConvertTo-CountdownSettingsJson -State (Get-CurrentSettingsState)
        $displayPrecisionInput.SelectedValue = 'Day'
        $monthlySalary.Text = '23001'
        $workStartInput.Text = '08:30'
        $workEndInput.Text = '17:30'
        if ([string] $displayPrecisionInput.SelectedValue -ne 'Day' -or $monthlySalary.Text -ne '23001' -or $workStartInput.Text -ne '08:30' -or $workEndInput.Text -ne '17:30') { throw 'Persistence-failure preference test controls were not initialized.' }
        $preferenceFailureSentinel = 'preference persistence failure was not exercised'
        $messageText.Text = $preferenceFailureSentinel
        $messageText.Foreground = $mutedBrush
        $savePreferencesButton.RaiseEvent((New-Object System.Windows.RoutedEventArgs([System.Windows.Controls.Primitives.ButtonBase]::ClickEvent)))
        $preferenceSaveFailurePreservesState =
            (ConvertTo-CountdownSettingsJson -State (Get-CurrentSettingsState)) -eq $preferenceStateBeforeFailure -and
            $messageText.Text -ne $preferenceFailureSentinel -and
            $messageText.Foreground -eq $errorBrush
        $preferenceSaveFailureRetainsDraft =
            [string] $displayPrecisionInput.SelectedValue -eq 'Day' -and
            $monthlySalary.Text -eq '23001' -and
            $workStartInput.Text -eq '08:30' -and
            $workEndInput.Text -eq '17:30'

        if ($script:tags.Count -gt 0) {
            Set-TagFilter -Mode 'All'
            $failureTag = $script:tags[0]
            (Get-ValidationTagToggle -Panel $script:tagFilterTagsPanel -Id $failureTag.id).IsChecked = $true
            if (-not $script:tagManagerVisible) { Toggle-TagManager }
            $failureTagRow = @($script:tagManagerList.Items | Where-Object {
                    $_ -is [System.Windows.Controls.Grid] -and $_.Children.Count -gt 1 -and [string] $_.Children[0].Uid -eq $failureTag.id
                })[0]
            if ($null -eq $failureTagRow) { throw 'Persistence-failure tag test could not find the tag editor row.' }
            $tagStateBeforeFailure = ConvertTo-CountdownSettingsJson -State (Get-CurrentSettingsState)
            $tagFilterModeBeforeFailure = $script:tagFilterMode
            $selectedTagIdsBeforeFailure = @($script:selectedTagIds)
            $tagFailureToken = [guid]::NewGuid().ToString('N').Substring(0, 8)
            $renameDraft = "保存失败改名$tagFailureToken"
            $createDraft = "保存失败创建$tagFailureToken"
            $failureTagRow.Children[0].Text = $renameDraft
            Invoke-ValidationButtonClick -Button $failureTagRow.Children[1]
            $script:tagManagerNewTagInput.Text = $createDraft
            Invoke-ValidationButtonClick -Button $script:tagManagerCreateButton
            Remove-TagFromLibrary -Id $failureTag.id -SkipConfirmation
            $tagSaveFailurePreservesState =
                (ConvertTo-CountdownSettingsJson -State (Get-CurrentSettingsState)) -eq $tagStateBeforeFailure -and
                @($script:tags | Where-Object { $_.id -eq $failureTag.id -and $_.name -eq $failureTag.name }).Count -eq 1
            $tagSaveFailureRetainsDraft = $failureTagRow.Children[0].Text -eq $renameDraft -and $script:tagManagerNewTagInput.Text -eq $createDraft
            $tagSaveFailureRetainsFilter = $script:tagFilterMode -eq $tagFilterModeBeforeFailure -and (@($script:selectedTagIds) -join '|') -eq ($selectedTagIdsBeforeFailure -join '|')
        }
        $persistenceFailureTempPathRemainsDirectory = Test-Path -LiteralPath "$settingsPath.tmp" -PathType Container
        Set-TagFilter -Mode 'All'
        Set-CountdownStatusFilter -Status 'All'
        Set-NoteStatusFilter -Status 'All'
        $script:noteSearchInput.Text = ''
        $script:tagManagerVisible = $false
        Set-Workspace -Workspace 'Countdowns'
    }
    $invalidNotesRejected = $false
    try {
        ConvertFrom-CountdownSettingsObject -Settings ([pscustomobject]@{ schemaVersion = 5; timeZoneId = 'China Standard Time'; displayPrecision = 'Second'; monthlySalary = 0; workStart = '09:00'; workEnd = '18:00'; tags = @(); countdowns = @(); notes = @([pscustomobject]@{ id = 'note'; text = ''; isCompleted = $false; tagIds = @() }) }) | Out-Null
    }
    catch { $invalidNotesRejected = $true }
    $duplicateSettingsRejected = $false
    try {
        ConvertFrom-CountdownSettingsObject -Settings ([pscustomobject]@{ schemaVersion = 2; timeZoneId = 'China Standard Time'; countdowns = @([pscustomobject]@{ id = 'a'; name = '计划'; targetUtc = '2099-01-01T00:00:00Z' }, [pscustomobject]@{ id = 'b'; name = '计划'; targetUtc = '2099-02-01T00:00:00Z' }) }) | Out-Null
    }
    catch { $duplicateSettingsRejected = $true }
    $invalidTagReferencesRejected = $false
    try {
        ConvertFrom-CountdownSettingsObject -Settings ([pscustomobject]@{
                schemaVersion = 5
                timeZoneId = 'China Standard Time'
                displayPrecision = 'Second'
                monthlySalary = 0
                workStart = '09:00'
                workEnd = '18:00'
                tags = @()
                countdowns = @([pscustomobject]@{ id = 'tag-reference'; name = '悬空标签'; targetUtc = '2099-01-01T00:00:00Z'; tagIds = @('missing') })
                notes = @()
            }) | Out-Null
    }
    catch { $invalidTagReferencesRejected = $true }
    $hasTagControls = $null -ne $script:tagFilterBar -and $null -ne $script:tagFilterTagsPanel -and $null -ne $script:tagManagerPanel -and $null -ne $script:countdownTagSelectorPanel -and $null -ne $script:noteTagSelectorPanel -and $null -ne $script:floatingTagSelectorButton -and $null -ne $script:floatingTagPopup -and $null -ne $script:floatingTagSelectorPanel -and $null -ne $script:floatingClearTagButton -and $null -ne $script:floatingInheritedTagText
    $tagFilterSupportsAnyAll =
        $null -ne $script:tagMatchAnyButton -and
        $null -ne $script:tagMatchAllButton -and
        [string] $script:tagMatchAnyButton.Content -eq '任一' -and
        [string] $script:tagMatchAllButton.Content -eq '全部匹配' -and
        (Test-CountdownTagFilter -TagIds @('a') -SelectedTagIds @('a', 'b') -MatchMode Any) -and
        -not (Test-CountdownTagFilter -TagIds @('a') -SelectedTagIds @('a', 'b') -MatchMode All)
    $tagsBeforeChipValidation = $script:tags
    $chipValidationTags = [System.Collections.ArrayList]::new()
    $chipValidationTag = New-CountdownTagRecord -Id 'tag-chip-validation' -Name '芯片验证'
    [void] $chipValidationTags.Add($chipValidationTag)
    $script:tags = $chipValidationTags
    try {
        $chipValidationPanel = New-TagChipPanel -TagIds @($chipValidationTag.id)
        $chipValidationControl = @($chipValidationPanel.Children | Where-Object { $_ -is [System.Windows.Controls.Button] })[0]
        $tagChipsUseUid = $null -ne $chipValidationControl -and [string] $chipValidationControl.Uid -eq $chipValidationTag.id -and $null -eq $chipValidationControl.Tag
    }
    finally {
        $script:tags = $tagsBeforeChipValidation
    }
    $cardActionsBoundByTag = $true
    $sortedForValidation = @(Get-SortedCountdowns)
    for ($index = 0; $index -lt $sortedForValidation.Count; $index++) {
        $card = $countdownList.Items[$index]
        $actions = @($card.Child.Children | Where-Object { [System.Windows.Controls.Grid]::GetColumn($_) -eq 1 })[0]
        $tags = @($actions.Children | ForEach-Object { [string] $_.Tag })
        if ($tags.Count -ne 2 -or @($tags | Where-Object { $_ -ne $sortedForValidation[$index].id }).Count -gt 0) {
            $cardActionsBoundByTag = $false
            break
        }
    }
    $completedNotesCollapsed = $completedNoteList.Visibility -eq 'Collapsed' -and -not $script:showCompletedNotes
    $noteActionsBoundByTag = $true
    $script:showCompletedNotes = $true
    Render-Notes
    $renderedNoteCards = @($noteList.Items) + @($completedNoteList.Items)
    $notesForValidation = @()
    $notesForValidation += @(Get-ActiveNotes)
    $notesForValidation += @(Get-CompletedNotes)
    if ($renderedNoteCards.Count -ne $notesForValidation.Count) { $noteActionsBoundByTag = $false }
    else {
        for ($index = 0; $index -lt $notesForValidation.Count; $index++) {
            $noteCard = $renderedNoteCards[$index]
            $tags = @($noteCard.Child.Children | Where-Object { $_ -is [System.Windows.Controls.CheckBox] -or $_ -is [System.Windows.Controls.Button] } | ForEach-Object { [string] $_.Tag })
            if ($tags.Count -ne 3 -or @($tags | Where-Object { $_ -ne $notesForValidation[$index].id }).Count -gt 0) {
                $noteActionsBoundByTag = $false
                break
            }
        }
    }
    $renderedNoteTexts = @($renderedNoteCards | ForEach-Object {
        @($_.Child.Children | Where-Object { $_ -is [System.Windows.Controls.TextBlock] } | ForEach-Object { [string] $_.Text })
    })
    $noteStatusLabelsPresent = $renderedNoteTexts -contains '未完成' -and $renderedNoteTexts -contains '已完成'
    $script:showCompletedNotes = $false
    Render-Notes
    Set-Workspace -Workspace 'Notes'
    [void] ($unifiedWorkspaceSwitches = $notesWorkspace.Visibility -eq 'Visible' -and $countdownWorkspace.Visibility -eq 'Collapsed' -and $floatingNotesEditor.Visibility -eq 'Visible' -and $floatingCountdownSummary.Visibility -eq 'Collapsed')
    Set-Workspace -Workspace 'Countdowns'
    $workspaceSwitcherBeforeEditors = [System.Windows.Controls.Grid]::GetRow($workspaceSwitcher) -lt [System.Windows.Controls.Grid]::GetRow($countdownEditorWorkspace)
    [pscustomobject]@{
        Valid = $true; Title = $window.Title; Width = $window.Width; Height = $window.Height
        RequiredControls = @($countdownName, $targetDate, $targetHour, $targetMinute, $targetSecond, $setNowButton, $saveButton, $cancelButton, $countdownList, $messageText, $nowText, $topmostCheck).Count
        DatePickerTemplateParts = $datePickerParts.Count; DatePickerText = $datePickerParts[0].Text
        DateInputReadOnly = $datePickerParts[0].IsReadOnly; DatePickerButtonOpensCalendar = $datePickerButtonOpensCalendar
        TimeDropDowns = @($targetHour, $targetMinute, $targetSecond).Count
        TimeOptionCounts = @($targetHour.Items.Count, $targetMinute.Items.Count, $targetSecond.Items.Count)
        HasSetNowButton = $null -ne $setNowButton; HasCountdownList = $null -ne $countdownList; HasNameEditor = $null -ne $countdownName
        LegacyMigrationSupported = $migration.Migrated -and $migration.Records.Count -eq 1 -and $migration.Records[0].name -eq '我的倒计时' -and $migration.Notes.Count -eq 0
        DuplicateSettingsRejected = $duplicateSettingsRejected
        InvalidNotesRejected = $invalidNotesRejected
        InvalidTagReferencesRejected = $invalidTagReferencesRejected
        HasTagControls = $hasTagControls
        TagFilterSupportsAnyAll = $tagFilterSupportsAnyAll
        TagChipsUseUid = $tagChipsUseUid
        PersistenceRoundTrip = $persistenceRoundTrip
        NotePersistenceRoundTrip = $notePersistenceRoundTrip
        TagPersistenceRoundTrip = $tagPersistenceRoundTrip
        NoteAddViaClick = $noteAddViaClick
        NoteStatusViaCheckbox = $noteStatusViaCheckbox
        TagInteractionsExecuted = $tagInteractions.Executed
        TagCreationAndAssociation = $tagInteractions.CreationAndAssociation
        TagAnyAllFilter = $tagInteractions.AnyAllFilter
        TagUntaggedAndCountdownStatus = $tagInteractions.UntaggedAndCountdownStatus
        TagNoteStatusSearchAndCompletion = $tagInteractions.NoteStatusSearchAndCompletion
        TagFloatingNoteInheritsTags = $tagInteractions.FloatingNoteInheritsTags
        TagFloatingNotePopupOnDemand = $tagInteractions.FloatingNotePopupOnDemand
        TagFloatingNoteCanOverrideTags = $tagInteractions.FloatingNoteCanOverrideTags
        TagNoteEditingPersistsTags = $tagInteractions.NoteEditingPersistsTags
        TagRenameAndDeleteDetachOnly = $tagInteractions.RenameAndDeleteDetachOnly
        CountdownSaveFailurePreservesState = $countdownSaveFailurePreservesState
        PreferenceSaveFailurePreservesState = $preferenceSaveFailurePreservesState
        CountdownSaveFailureRetainsDraft = $countdownSaveFailureRetainsDraft
        PreferenceSaveFailureRetainsDraft = $preferenceSaveFailureRetainsDraft
        TagSaveFailurePreservesState = $tagSaveFailurePreservesState
        TagSaveFailureRetainsDraft = $tagSaveFailureRetainsDraft
        TagSaveFailureRetainsFilter = $tagSaveFailureRetainsFilter
        PersistenceFailureTempPathRemainsDirectory = $persistenceFailureTempPathRemainsDirectory
        CardActionsBoundByTag = $cardActionsBoundByTag
        NoteActionsBoundByTag = $noteActionsBoundByTag
        NoteStatusLabelsPresent = $noteStatusLabelsPresent
        CompletedNotesCollapsed = $completedNotesCollapsed
        FloatingWindowPresent = $null -ne $floatingWindow
        FloatingCountdownCount = @($script:floatingViews.Keys | Where-Object { $_ -ne '__workday_end__' }).Count
        HasWorkdayEndCountdown = $null -ne (Get-WorkdayEndCountdown)
        FloatingCountdownTexts = @((Get-SortedCountdowns | Where-Object { $_.targetUtc -gt [DateTimeOffset]::UtcNow }) | ForEach-Object { $script:floatingViews[$_.id].Remaining.Text })
        FloatingIncomeText = $floatingIncome.Text
        FloatingManagerButtonPresent = $null -ne $openManagerButton
        HasUnifiedNoteEntry = $null -ne $workspaceSwitcher -and $null -ne $showNotesButton -and $null -ne $showFloatingNotesButton -and $null -ne $notesWorkspace
        HasNoteControls = $null -ne $noteInput -and $null -ne $addNoteButton -and $null -ne $cancelNoteEditButton -and $null -ne $floatingNoteInput -and $null -ne $floatingAddNoteButton
        HasOpenNotesActions = $null -ne $showNotesButton -and $null -ne $showFloatingNotesButton
        UnifiedWorkspaceSwitches = $unifiedWorkspaceSwitches
        WorkspaceSwitcherBeforeEditors = $workspaceSwitcherBeforeEditors
        HasPreferenceControls = $null -ne $displayPrecisionInput -and $null -ne $monthlySalary -and $null -ne $workStartInput -and $null -ne $workEndInput -and $null -ne $savePreferencesButton
        LoadedCountdowns = $script:countdowns.Count; LoadedNotes = $script:notes.Count; LoadedTags = $script:tags.Count; SettingsWereMigrated = $script:settingsWereMigrated
        LoadedDisplayPrecision = $script:displayPrecision; LoadedMonthlySalary = $script:salary
        LoadedWorkStart = (Format-WorkTime -Value $script:workStart); LoadedWorkEnd = (Format-WorkTime -Value $script:workEnd)
        ActiveNoteCount = @(Get-ActiveNotes).Count; CompletedNoteCount = @(Get-CompletedNotes).Count
        SortedNames = @((Get-SortedCountdowns) | ForEach-Object { $_.name })
        WindowIconLoaded = $null -ne $window.Icon
    } | ConvertTo-Json
    return
}

$timer = New-Object Windows.Threading.DispatcherTimer
$timer.Interval = [TimeSpan]::FromMilliseconds(250)
$timer.Add_Tick({
    $now = Get-BeijingNow
    $nowText.Text = "当前北京时间：$($now.ToString('yyyy年MM月dd日 HH:mm:ss'))"
    $sortSignature = @((Get-SortedCountdowns) | ForEach-Object { $_.id }) -join '|'
    $workdayEnd = Get-WorkdayEndCountdown
    $workdayEndSignature = if ($null -eq $workdayEnd) { '' } else { $workdayEnd.targetUtc.ToString('o') }
    if ($sortSignature -ne $script:lastSortSignature) { Render-Cards; Render-FloatingContent }
    elseif ($workdayEndSignature -ne $script:workdayEndSignature) { Render-FloatingContent }
    else { Update-CardValues; Update-FloatingCountdowns }
})
$timer.Start()
$now = Get-BeijingNow; $nowText.Text = "当前北京时间：$($now.ToString('yyyy年MM月dd日 HH:mm:ss'))"
$floatingWindow.WindowStartupLocation = 'Manual'
$floatingWindow.Show()
$calendarBootstrapTimer = New-Object Windows.Threading.DispatcherTimer
$calendarBootstrapTimer.Interval = [TimeSpan]::FromMilliseconds(1)
$calendarBootstrapTimer.Add_Tick({
    if (-not $script:runtimeReceiptWritten) { return }
    $calendarBootstrapTimer.Stop()
    try {
        Start-ChinaHolidayCalendarBootstrap -Year (Get-BeijingNow).Year
    }
    catch {
        throw "节假日日历异步启动失败：$($_.Exception.Message)；$($_.ScriptStackTrace)"
    }
})
$calendarBootstrapTimer.Start()
[Windows.Threading.Dispatcher]::Run()
}

Export-ModuleMember -Function Start-CountdownApplication
