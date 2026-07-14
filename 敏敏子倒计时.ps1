[CmdletBinding()]
param(
    [switch] $ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:singleInstanceMutex = $null
if (-not $ValidateOnly) {
    $createdNew = $false
    $userSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $script:singleInstanceMutex = New-Object System.Threading.Mutex($true, "Local\MinminCountdown-$userSid", [ref] $createdNew)
    if (-not $createdNew) {
        exit 0
    }
}

Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
Import-Module (Join-Path $PSScriptRoot 'Countdown.Core.psm1') -Force

$settingsPath = Join-Path $PSScriptRoot 'settings.json'
$runtimeStatusPath = Join-Path $PSScriptRoot 'runtime-status.json'
$script:targetUtc = $null

[xml] $xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="敏敏子倒计时" Width="640" Height="520"
        WindowStartupLocation="CenterScreen" ResizeMode="NoResize"
        Background="#F0EEE9" FontFamily="Microsoft YaHei UI"
        Topmost="True">
  <Window.Resources>
    <Style TargetType="TextBox">
      <Setter Property="Height" Value="42"/>
      <Setter Property="Padding" Value="12,0"/>
      <Setter Property="VerticalContentAlignment" Value="Center"/>
      <Setter Property="FontSize" Value="15"/>
      <Setter Property="Foreground" Value="#202634"/>
      <Setter Property="Background" Value="#FFFFFF"/>
      <Setter Property="BorderBrush" Value="#D9DCE3"/>
      <Setter Property="BorderThickness" Value="1"/>
    </Style>
    <Style TargetType="DatePicker">
      <Setter Property="Height" Value="42"/>
      <Setter Property="FontSize" Value="15"/>
      <Setter Property="Foreground" Value="#202634"/>
      <Setter Property="Background" Value="#FFFFFF"/>
      <Setter Property="BorderBrush" Value="#D9DCE3"/>
      <Setter Property="BorderThickness" Value="1"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="DatePicker">
            <Grid>
              <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="42"/></Grid.ColumnDefinitions>
              <DatePickerTextBox x:Name="PART_TextBox" Grid.Column="0" Padding="12,0" VerticalContentAlignment="Center" Background="#FFFFFF" Foreground="#202634" BorderBrush="#D9DCE3" BorderThickness="1,1,0,1" Text="{Binding SelectedDate, RelativeSource={RelativeSource TemplatedParent}, StringFormat={}{0:yyyy年M月d日}}"/>
              <Button x:Name="PART_Button" Grid.Column="1" Background="#F1EFEA" BorderBrush="#D9DCE3" BorderThickness="0" Cursor="Hand" ToolTip="打开日历">
                <Button.Template>
                  <ControlTemplate TargetType="Button">
                    <Border x:Name="DropSurface" Background="{TemplateBinding Background}" BorderBrush="#D9DCE3" BorderThickness="1" CornerRadius="0,7,7,0">
                      <Path Width="10" Height="6" Fill="#3F4858" Stretch="Fill" Data="M 0,0 L 10,0 L 5,6 Z" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                      <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="DropSurface" Property="Background" Value="#E6E3DC"/></Trigger>
                      <Trigger Property="IsPressed" Value="True"><Setter TargetName="DropSurface" Property="Background" Value="#D8D4CC"/></Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate>
                </Button.Template>
              </Button>
              <Popup x:Name="PART_Popup" Grid.ColumnSpan="2" Placement="Bottom" StaysOpen="False" AllowsTransparency="True" Focusable="False">
                <Border Margin="0,6,0,0" Padding="10" Background="#FBFAF8" BorderBrush="#D9DCE3" BorderThickness="1" CornerRadius="12">
                  <Border.Effect><DropShadowEffect Color="#172033" BlurRadius="18" ShadowDepth="4" Opacity="0.2"/></Border.Effect>
                  <Calendar x:Name="PART_Calendar"/>
                </Border>
              </Popup>
            </Grid>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style TargetType="Calendar">
      <Setter Property="Background" Value="#FBFAF8"/>
      <Setter Property="Foreground" Value="#202634"/>
      <Setter Property="BorderBrush" Value="#D9DCE3"/>
      <Setter Property="BorderThickness" Value="1"/>
      <Setter Property="FontFamily" Value="Microsoft YaHei UI"/>
      <Setter Property="FontSize" Value="13"/>
    </Style>
    <Style TargetType="CalendarDayButton">
      <Setter Property="Width" Value="34"/>
      <Setter Property="Height" Value="30"/>
      <Setter Property="Margin" Value="2"/>
      <Setter Property="Padding" Value="0"/>
      <Setter Property="HorizontalContentAlignment" Value="Center"/>
      <Setter Property="VerticalContentAlignment" Value="Center"/>
      <Setter Property="Background" Value="Transparent"/>
      <Setter Property="Foreground" Value="#303746"/>
      <Setter Property="BorderBrush" Value="Transparent"/>
      <Setter Property="BorderThickness" Value="1"/>
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="CalendarDayButton">
            <Border x:Name="DaySurface" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="7">
              <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsInactive" Value="True"><Setter TargetName="DaySurface" Property="Opacity" Value="0.35"/></Trigger>
              <Trigger Property="IsBlackout" Value="True"><Setter TargetName="DaySurface" Property="Opacity" Value="0.25"/><Setter Property="Cursor" Value="Arrow"/></Trigger>
              <Trigger Property="IsToday" Value="True"><Setter TargetName="DaySurface" Property="BorderBrush" Value="#D85C4B"/><Setter Property="FontWeight" Value="SemiBold"/></Trigger>
              <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="DaySurface" Property="Background" Value="#F4E4E0"/></Trigger>
              <Trigger Property="IsSelected" Value="True"><Setter TargetName="DaySurface" Property="Background" Value="#D85C4B"/><Setter Property="Foreground" Value="White"/><Setter TargetName="DaySurface" Property="BorderBrush" Value="#D85C4B"/><Setter Property="FontWeight" Value="SemiBold"/></Trigger>
              <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="DaySurface" Property="BorderBrush" Value="#172033"/><Setter TargetName="DaySurface" Property="BorderThickness" Value="2"/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style TargetType="CalendarButton">
      <Setter Property="Margin" Value="3"/>
      <Setter Property="Padding" Value="8,6"/>
      <Setter Property="Background" Value="Transparent"/>
      <Setter Property="Foreground" Value="#303746"/>
      <Setter Property="BorderBrush" Value="Transparent"/>
      <Setter Property="Cursor" Value="Hand"/>
      <Style.Triggers>
        <Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="#F4E4E0"/></Trigger>
        <Trigger Property="IsInactive" Value="True"><Setter Property="Opacity" Value="0.4"/></Trigger>
      </Style.Triggers>
    </Style>
    <Style x:Key="PrimaryButton" TargetType="Button">
      <Setter Property="Height" Value="44"/>
      <Setter Property="Padding" Value="18,0"/>
      <Setter Property="Foreground" Value="White"/>
      <Setter Property="Background" Value="#D85C4B"/>
      <Setter Property="BorderThickness" Value="0"/>
      <Setter Property="FontSize" Value="15"/>
      <Setter Property="FontWeight" Value="SemiBold"/>
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Border x:Name="ButtonSurface" Background="{TemplateBinding Background}" CornerRadius="8" Padding="{TemplateBinding Padding}">
              <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="ButtonSurface" Property="Background" Value="#C94E3E"/></Trigger>
              <Trigger Property="IsPressed" Value="True"><Setter TargetName="ButtonSurface" Property="Background" Value="#B94435"/></Trigger>
              <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="ButtonSurface" Property="BorderBrush" Value="#172033"/><Setter TargetName="ButtonSurface" Property="BorderThickness" Value="2"/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key="SecondaryButton" TargetType="Button">
      <Setter Property="Height" Value="44"/>
      <Setter Property="Padding" Value="14,0"/>
      <Setter Property="Foreground" Value="#596171"/>
      <Setter Property="Background" Value="#FBFAF8"/>
      <Setter Property="BorderBrush" Value="#C9CDD6"/>
      <Setter Property="BorderThickness" Value="1"/>
      <Setter Property="FontSize" Value="14"/>
      <Setter Property="FontWeight" Value="SemiBold"/>
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Border x:Name="ButtonSurface" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="8" Padding="{TemplateBinding Padding}">
              <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="ButtonSurface" Property="Background" Value="#F0EEE9"/><Setter TargetName="ButtonSurface" Property="BorderBrush" Value="#AEB4C0"/></Trigger>
              <Trigger Property="IsPressed" Value="True"><Setter TargetName="ButtonSurface" Property="Background" Value="#E5E2DC"/></Trigger>
              <Trigger Property="IsEnabled" Value="False"><Setter TargetName="ButtonSurface" Property="Opacity" Value="0.42"/><Setter Property="Cursor" Value="Arrow"/></Trigger>
              <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="ButtonSurface" Property="BorderBrush" Value="#172033"/><Setter TargetName="ButtonSurface" Property="BorderThickness" Value="2"/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
  </Window.Resources>

  <Border Margin="16" Padding="28,24" Background="#FBFAF8" CornerRadius="18" BorderBrush="#E3E0DA" BorderThickness="1">
    <Border.Effect>
      <DropShadowEffect Color="#1C2535" BlurRadius="22" ShadowDepth="5" Opacity="0.16"/>
    </Border.Effect>
    <Grid>
      <Grid.RowDefinitions>
        <RowDefinition Height="Auto"/>
        <RowDefinition Height="162"/>
        <RowDefinition Height="164"/>
        <RowDefinition Height="Auto" MinHeight="32"/>
        <RowDefinition Height="*"/>
      </Grid.RowDefinitions>

      <Grid Grid.Row="0">
        <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
        <StackPanel>
          <TextBlock Text="敏敏子倒计时" FontSize="24" FontWeight="SemiBold" Foreground="#171B26"/>
          <StackPanel Orientation="Horizontal" Margin="0,7,0,0">
            <Border Background="#E7EBF2" CornerRadius="10" Padding="9,3">
              <TextBlock Text="北京时间  UTC+8" FontSize="11" FontWeight="SemiBold" Foreground="#344057"/>
            </Border>
            <TextBlock Text="依据系统校准时间" Margin="10,2,0,0" FontSize="12" Foreground="#727986"/>
          </StackPanel>
        </StackPanel>
        <CheckBox x:Name="TopmostCheck" Grid.Column="1" Content="窗口置顶" IsChecked="True" VerticalAlignment="Center" FontSize="13" Foreground="#4E5563"/>
      </Grid>

      <Border Grid.Row="1" Margin="0,20,0,0" Padding="24,18" Background="#172033" CornerRadius="14">
        <Grid>
          <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="*"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
          <TextBlock Text="距目标还有" FontSize="12" Foreground="#AEB8CA"/>
          <TextBlock x:Name="CountdownText" Grid.Row="1" Text="请设置截止日期" FontFamily="Segoe UI" FontSize="36" FontWeight="SemiBold" Foreground="#FFFFFF" HorizontalAlignment="Center" VerticalAlignment="Center"/>
          <TextBlock x:Name="TargetText" Grid.Row="2" FontSize="12" Foreground="#BFC7D5" HorizontalAlignment="Center"/>
        </Grid>
      </Border>

      <Border Grid.Row="2" Margin="0,18,0,0" Padding="16,13" Background="#F1EFEA" CornerRadius="12">
        <Grid>
          <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
          </Grid.RowDefinitions>
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="172"/>
          </Grid.ColumnDefinitions>
          <StackPanel Grid.Column="0">
            <TextBlock Text="截止日期" Margin="1,0,0,6" FontSize="12" FontWeight="SemiBold" Foreground="#555D6B"/>
            <DatePicker x:Name="TargetDate" SelectedDateFormat="Long" ToolTip="选择北京时间日期"/>
          </StackPanel>
          <StackPanel Grid.Column="1" Margin="12,0,0,0">
            <TextBlock Text="具体时间" Margin="1,0,0,6" FontSize="12" FontWeight="SemiBold" Foreground="#555D6B"/>
            <TextBox x:Name="TargetTime" Text="18:00:00" ToolTip="请输入时:分:秒，例如 18:00:00"/>
          </StackPanel>
          <StackPanel Grid.Row="1" Grid.ColumnSpan="2" Margin="0,12,0,0" Orientation="Horizontal" HorizontalAlignment="Right">
            <Button x:Name="SaveButton" Width="138" Style="{StaticResource PrimaryButton}" Content="保存日期" IsDefault="True"/>
            <Button x:Name="ClearButton" Width="118" Margin="10,0,0,0" Style="{StaticResource SecondaryButton}" Content="停止并清除" IsEnabled="False"/>
          </StackPanel>
        </Grid>
      </Border>

      <TextBlock x:Name="MessageText" Grid.Row="3" Margin="2,7,0,0" FontSize="12" Foreground="#B23A34" TextAlignment="Left" TextWrapping="Wrap"/>

      <Grid Grid.Row="4" Margin="0,10,0,0" VerticalAlignment="Bottom">
        <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
        <Ellipse Width="7" Height="7" Fill="#4D9B6A" VerticalAlignment="Center"/>
        <TextBlock x:Name="NowText" Grid.Column="1" Margin="8,0,0,0" FontSize="12" Foreground="#68707D"/>
      </Grid>
    </Grid>
  </Border>
</Window>
'@

$reader = New-Object System.Xml.XmlNodeReader $xaml
$window = [Windows.Markup.XamlReader]::Load($reader)

$iconPath = Join-Path $PSScriptRoot 'countdown-icon.ico'
if (Test-Path -LiteralPath $iconPath) {
    $window.Icon = [System.Windows.Media.Imaging.BitmapFrame]::Create((New-Object System.Uri($iconPath)))
}

$countdownText = $window.FindName('CountdownText')
$targetText = $window.FindName('TargetText')
$targetDate = $window.FindName('TargetDate')
$targetTime = $window.FindName('TargetTime')
$saveButton = $window.FindName('SaveButton')
$clearButton = $window.FindName('ClearButton')
$messageText = $window.FindName('MessageText')
$nowText = $window.FindName('NowText')
$topmostCheck = $window.FindName('TopmostCheck')

function Save-Settings {
    param([Parameter(Mandatory)] [DateTimeOffset] $TargetUtc)

    $payload = [ordered]@{
        schemaVersion = 1
        title = '敏敏子倒计时'
        targetUtc = $TargetUtc.ToUniversalTime().ToString('o')
        timeZoneId = 'China Standard Time'
    } | ConvertTo-Json
    $tempPath = "$settingsPath.tmp"
    [System.IO.File]::WriteAllText($tempPath, $payload, (New-Object System.Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $tempPath -Destination $settingsPath -Force
}

function Set-TargetControls {
    param([Parameter(Mandatory)] [DateTimeOffset] $TargetUtc)

    $beijing = [System.TimeZoneInfo]::ConvertTimeBySystemTimeZoneId($TargetUtc, 'China Standard Time')
    $targetDate.SelectedDate = $beijing.Date
    $targetTime.Text = $beijing.ToString('HH:mm:ss')
    $targetText.Text = "截止：$($beijing.ToString('yyyy年MM月dd日 HH:mm:ss'))（北京时间）"
}

function Update-Display {
    $beijingNow = Get-BeijingNow
    $nowText.Text = "当前北京时间：$($beijingNow.ToString('yyyy年MM月dd日 HH:mm:ss'))"

    if ($null -eq $script:targetUtc) {
        $countdownText.Text = '请设置截止日期'
        $targetText.Text = ''
        return
    }

    $parts = Get-CountdownParts -TargetUtc $script:targetUtc -NowUtc ([DateTimeOffset]::UtcNow)
    if ($parts.IsElapsed) {
        $countdownText.Text = '目标时间已到'
    }
    else {
        $countdownText.Text = ('{0} 天 {1:D2} 时 {2:D2} 分 {3:D2} 秒' -f $parts.Days, $parts.Hours, $parts.Minutes, $parts.Seconds)
    }
}

if (Test-Path -LiteralPath $settingsPath) {
    try {
        $settingsJson = [System.IO.File]::ReadAllText($settingsPath, [System.Text.Encoding]::UTF8)
        $settings = $settingsJson | ConvertFrom-Json
        if ($settings.schemaVersion -ne 1 -or $settings.timeZoneId -ne 'China Standard Time') {
            throw '设置文件版本或时区无效。'
        }
        $script:targetUtc = [DateTimeOffset]::Parse($settings.targetUtc, [System.Globalization.CultureInfo]::InvariantCulture)
        Set-TargetControls -TargetUtc $script:targetUtc
        $clearButton.IsEnabled = $true
    }
    catch {
        $messageText.Text = "已有设置无法读取：$($_.Exception.Message)"
        $clearButton.IsEnabled = $true
    }
}

if ($null -eq $targetDate.SelectedDate) {
    $targetDate.SelectedDate = (Get-BeijingNow).Date.AddDays(1)
}

$saveButton.Add_Click({
    try {
        $messageText.Text = ''
        if ($null -eq $targetDate.SelectedDate) {
            throw '请选择截止日期。'
        }
        $dateText = ConvertTo-CountdownDateText -SelectedDate $targetDate.SelectedDate
        $newTarget = ConvertTo-CountdownTargetUtc -DateText $dateText -TimeText $targetTime.Text.Trim()
        if ($newTarget -le [DateTimeOffset]::UtcNow) {
            throw '截止日期必须晚于当前北京时间。'
        }
        Save-Settings -TargetUtc $newTarget
        $script:targetUtc = $newTarget
        $clearButton.IsEnabled = $true
        Set-TargetControls -TargetUtc $newTarget
        $messageText.Foreground = '#477A54'
        $messageText.Text = '已保存，倒计时立即生效。'
        Update-Display
    }
    catch {
        $messageText.Foreground = '#B33A48'
        $messageText.Text = $_.Exception.Message
    }
})

$clearButton.Add_Click({
    try {
        $null = Remove-CountdownSettings -SettingsPath $settingsPath
        $script:targetUtc = $null
        $clearButton.IsEnabled = $false
        $targetDate.SelectedDate = (Get-BeijingNow).Date.AddDays(1)
        $targetTime.Text = '18:00:00'
        $messageText.Foreground = '#477A54'
        $messageText.Text = '倒计时已停止，保存的截止日期已清除。'
        Update-Display
    }
    catch {
        $messageText.Foreground = '#B23A34'
        $messageText.Text = "无法清除倒计时：$($_.Exception.Message)"
    }
})

$topmostCheck.Add_Checked({ $window.Topmost = $true })
$topmostCheck.Add_Unchecked({ $window.Topmost = $false })
$window.Add_Loaded({
    $status = [ordered]@{
        schemaVersion = 1
        title = $window.Title
        processId = $PID
        sessionId = [System.Diagnostics.Process]::GetCurrentProcess().SessionId
        loadedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        timeZoneId = 'China Standard Time'
    } | ConvertTo-Json
    $statusTemp = "$runtimeStatusPath.tmp"
    [System.IO.File]::WriteAllText($statusTemp, $status, (New-Object System.Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $statusTemp -Destination $runtimeStatusPath -Force
})
$window.Add_Closed({
    if ($null -ne $script:singleInstanceMutex) {
        $script:singleInstanceMutex.ReleaseMutex()
        $script:singleInstanceMutex.Dispose()
    }
})

if ($ValidateOnly) {
    $targetDate.SelectedDate = [DateTime]'2026-08-30'
    $null = $targetDate.ApplyTemplate()
    $datePickerParts = @(
        $targetDate.Template.FindName('PART_TextBox', $targetDate),
        $targetDate.Template.FindName('PART_Button', $targetDate),
        $targetDate.Template.FindName('PART_Popup', $targetDate),
        $targetDate.Template.FindName('PART_Calendar', $targetDate)
    )
    if (@($datePickerParts | Where-Object { $null -eq $_ }).Count -gt 0) {
        throw 'DatePicker template is missing one or more required PART_* controls.'
    }
    $datePickerText = $datePickerParts[0].Text
    if ($datePickerText -ne '2026年8月30日') {
        throw "DatePicker did not render the complete selected date. Actual: $datePickerText"
    }
    if ($null -eq $window.Icon) {
        throw 'Window icon was not loaded.'
    }
    [pscustomobject]@{
        Valid = $true
        Title = $window.Title
        Width = $window.Width
        Height = $window.Height
        RequiredControls = @($countdownText, $targetText, $targetDate, $targetTime, $saveButton, $clearButton, $messageText, $nowText, $topmostCheck).Count
        DatePickerTemplateParts = $datePickerParts.Count
        DatePickerText = $datePickerText
        WindowIconLoaded = $null -ne $window.Icon
    } | ConvertTo-Json
    if ($null -ne $script:singleInstanceMutex) {
        $script:singleInstanceMutex.ReleaseMutex()
        $script:singleInstanceMutex.Dispose()
    }
    exit 0
}

$timer = New-Object Windows.Threading.DispatcherTimer
$timer.Interval = [TimeSpan]::FromMilliseconds(250)
$timer.Add_Tick({ Update-Display })
$timer.Start()
Update-Display

$null = $window.ShowDialog()
