[CmdletBinding()]
param(
    [switch] $ValidateOnly,
    [switch] $ExerciseNoteAdd,
    [string] $SettingsFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:singleInstanceMutex = $null
if (-not $ValidateOnly) {
    $createdNew = $false
    $userSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $script:singleInstanceMutex = New-Object System.Threading.Mutex($true, "Local\MinminCountdown-$userSid", [ref] $createdNew)
    if (-not $createdNew) { exit 0 }
}

Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
Import-Module (Join-Path $PSScriptRoot 'Countdown.Core.psm1') -Force

$settingsPath = if ([string]::IsNullOrWhiteSpace($SettingsFile)) { Join-Path $PSScriptRoot 'settings.json' } else { $SettingsFile }
$runtimeStatusPath = Join-Path $PSScriptRoot 'runtime-status.json'
$script:countdowns = [System.Collections.ArrayList]::new()
$script:notes = [System.Collections.ArrayList]::new()
$script:cardViews = @{}
$script:floatingViews = @{}
$script:editingId = $null
$script:settingsWereMigrated = $false
$script:lastSortSignature = ''
$script:showCompletedNotes = $false
$script:activeWorkspace = 'Countdowns'
$script:displayPrecision = 'Second'
$script:salary = [decimal]0
$script:workStart = [TimeSpan]::FromHours(9)
$script:workEnd = [TimeSpan]::FromHours(18)
$script:workdayMap = $null
$script:holidayCalendarStatus = '节假日日历未就绪'
$script:workdayEndSignature = ''

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
          <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
          <TextBox x:Name="NoteInput" MaxLength="140" ToolTip="写下一件要做的事，最多 140 个字符"/>
          <Button x:Name="AddNoteButton" Grid.Column="1" Width="76" Margin="8,0,0,0" Style="{StaticResource PrimaryButton}" Content="添加" ToolTip="添加便签"/>
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

function New-CountdownRecord {
    param([string] $Id, [string] $Name, [DateTimeOffset] $TargetUtc)
    [pscustomobject]@{ id = $Id; name = $Name; targetUtc = $TargetUtc.ToUniversalTime() }
}

function New-NoteRecord {
    param([string] $Id, [string] $Text, [bool] $IsCompleted = $false)
    $normalizedId = ([string] $Id).Trim()
    $normalizedText = ([string] $Text).Trim()
    if ([string]::IsNullOrWhiteSpace($normalizedId)) { throw '便签 ID 不能为空。' }
    if ([string]::IsNullOrWhiteSpace($normalizedText)) { throw '便签内容不能为空。' }
    if ($normalizedText.Length -gt 140) { throw '便签内容不能超过 140 个字符。' }
    [pscustomobject]@{ id = $normalizedId; text = $normalizedText; isCompleted = [bool] $IsCompleted }
}

function New-Preferences {
    param(
        [ValidateSet('Day', 'Hour', 'Minute', 'Second')] [string] $Precision = 'Second',
        [decimal] $MonthlySalary = 0,
        [TimeSpan] $WorkStart = ([TimeSpan]'09:00'),
        [TimeSpan] $WorkEnd = ([TimeSpan]'18:00')
    )
    if ($MonthlySalary -lt 0) { throw '月薪不能小于 0。' }
    if ($WorkEnd -le $WorkStart) { throw '下班时间必须晚于上班时间。' }
    [pscustomobject]@{ Precision = $Precision; MonthlySalary = $MonthlySalary; WorkStart = $WorkStart; WorkEnd = $WorkEnd }
}

function ConvertTo-WorkTime {
    param([Parameter(Mandatory)] [string] $Text)
    [TimeSpan] $value = [TimeSpan]::Zero
    if (-not [TimeSpan]::TryParseExact($Text.Trim(), 'hh\:mm', [System.Globalization.CultureInfo]::InvariantCulture, [ref] $value)) {
        throw '上下班时间格式无效，请使用 09:00 这样的 24 小时制时间。'
    }
    $value
}

function Format-WorkTime {
    param([Parameter(Mandatory)] [TimeSpan] $Value)
    '{0:D2}:{1:D2}' -f $Value.Hours, $Value.Minutes
}

function ConvertFrom-SettingsObject {
    param([Parameter(Mandatory)] [object] $Settings)
    $records = [System.Collections.ArrayList]::new()
    $notes = [System.Collections.ArrayList]::new()
    $preferences = New-Preferences
    if ($Settings.timeZoneId -ne 'China Standard Time') { throw '设置文件时区无效。' }
    if ($Settings.schemaVersion -eq 1) {
        [void] $records.Add((New-CountdownRecord -Id ([guid]::NewGuid().ToString('N')) -Name '我的倒计时' -TargetUtc ([DateTimeOffset]::Parse($Settings.targetUtc, [System.Globalization.CultureInfo]::InvariantCulture))))
        return [pscustomobject]@{ Records = $records; Notes = $notes; Preferences = $preferences; Migrated = $true }
    }
    if ($Settings.schemaVersion -notin @(2, 3, 4)) { throw '设置文件版本无效。' }
    $names = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    $ids = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($item in @($Settings.countdowns)) {
        $id = ([string] $item.id).Trim()
        $name = ([string] $item.name).Trim()
        if ([string]::IsNullOrWhiteSpace($name) -or $name.Length -gt 30) { throw '设置中包含无效的倒计时名称。' }
        if ([string]::IsNullOrWhiteSpace($id)) { throw '设置中包含空的倒计时 ID。' }
        if (-not $names.Add($name)) { throw '设置中包含重复的倒计时名称。' }
        if (-not $ids.Add($id)) { throw '设置中包含重复的倒计时 ID。' }
        [void] $records.Add((New-CountdownRecord -Id $id -Name $name -TargetUtc ([DateTimeOffset]::Parse($item.targetUtc, [System.Globalization.CultureInfo]::InvariantCulture))))
    }
    if ($Settings.schemaVersion -ge 3) {
        $precision = [string] $Settings.displayPrecision
        if ($precision -notin @('Day', 'Hour', 'Minute', 'Second')) { throw '设置中包含无效的悬浮窗显示精度。' }
        [decimal] $salary = 0
        if (-not [decimal]::TryParse(([string] $Settings.monthlySalary), [System.Globalization.NumberStyles]::Number, [System.Globalization.CultureInfo]::InvariantCulture, [ref] $salary)) { throw '设置中包含无效的月薪。' }
        $preferences = New-Preferences -Precision $precision -MonthlySalary $salary -WorkStart (ConvertTo-WorkTime -Text ([string] $Settings.workStart)) -WorkEnd (ConvertTo-WorkTime -Text ([string] $Settings.workEnd))
    }
    if ($Settings.schemaVersion -eq 4) {
        if ($null -eq $Settings.PSObject.Properties['notes']) { throw '设置中缺少便签列表。' }
        $noteIds = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($item in @($Settings.notes)) {
            $id = ([string] $item.id).Trim()
            if ($null -eq $item.PSObject.Properties['isCompleted'] -or $item.isCompleted -isnot [bool]) { throw '设置中包含无效的便签完成状态。' }
            if (-not $noteIds.Add($id)) { throw '设置中包含重复的便签 ID。' }
            [void] $notes.Add((New-NoteRecord -Id $id -Text ([string] $item.text) -IsCompleted ([bool] $item.isCompleted)))
        }
    }
    [pscustomobject]@{ Records = $records; Notes = $notes; Preferences = $preferences; Migrated = $Settings.schemaVersion -ne 4 }
}

function ConvertTo-SettingsJson {
    $items = @($script:countdowns | ForEach-Object { [ordered]@{ id = $_.id; name = $_.name; targetUtc = $_.targetUtc.ToUniversalTime().ToString('o') } })
    $notes = @($script:notes | ForEach-Object { [ordered]@{ id = $_.id; text = $_.text; isCompleted = [bool] $_.isCompleted } })
    [ordered]@{ schemaVersion = 4; timeZoneId = 'China Standard Time'; displayPrecision = $script:displayPrecision; monthlySalary = $script:salary; workStart = (Format-WorkTime -Value $script:workStart); workEnd = (Format-WorkTime -Value $script:workEnd); countdowns = $items; notes = $notes } | ConvertTo-Json -Depth 4
}

function Save-Settings {
    $payload = ConvertTo-SettingsJson
    $tempPath = "$settingsPath.tmp"
    [System.IO.File]::WriteAllText($tempPath, $payload, (New-Object System.Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $tempPath -Destination $settingsPath -Force
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
    $editorTitle.Text = '新增倒计时'
    $countdownName.Text = ''
    Set-TimeControls -BeijingTime ((Get-BeijingNow).Date.AddDays(1).AddHours(18))
    $saveButton.Content = '添加倒计时'
    $cancelButton.Visibility = 'Collapsed'
    $messageText.Text = ''
}

function Get-SortedCountdowns {
    $now = [DateTimeOffset]::UtcNow
    @($script:countdowns | Sort-Object @{ Expression = { if ($_.targetUtc -gt $now) { 0 } else { 1 } } }, @{ Expression = { $_.targetUtc } })
}

function Update-ChinaHolidayCalendar {
    param([Parameter(Mandatory)] [int] $Year, [switch] $AllowNetwork)
    $cacheDirectory = Join-Path $PSScriptRoot 'holiday-cache'
    $cachePath = Join-Path $cacheDirectory "$Year.json"
    $payload = $null
    if (Test-Path -LiteralPath $cachePath) {
        try { $payload = [System.IO.File]::ReadAllText($cachePath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json } catch { $payload = $null }
    }
    if ($null -eq $payload -and $AllowNetwork) {
        try {
            $uri = "https://uapis.cn/api/v1/misc/holiday-calendar?year=$Year&holiday_type=legal&timezone=Asia%2FShanghai"
            $payload = (Invoke-WebRequest -UseBasicParsing -Uri $uri -TimeoutSec 10).Content | ConvertFrom-Json
            New-Item -ItemType Directory -Path $cacheDirectory -Force | Out-Null
            [System.IO.File]::WriteAllText($cachePath, ($payload | ConvertTo-Json -Depth 8), (New-Object System.Text.UTF8Encoding($false)))
        }
        catch { $script:holidayCalendarStatus = '节假日日历未就绪'; return $false }
    }
    if ($null -eq $payload -or @($payload.days).Count -lt 365) { $script:holidayCalendarStatus = '节假日日历未就绪'; return $false }
    $map = @{}
    foreach ($day in @($payload.days)) { $map[[string] $day.date] = [bool] $day.is_workday }
    $script:workdayMap = $map
    $script:holidayCalendarStatus = "已同步 $Year 年法定节假日与调休"
    $true
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
    [void] $stack.Children.Add($name); [void] $stack.Children.Add($remaining); $border.Child = $stack
    [pscustomobject]@{ Border = $border; Remaining = $remaining }
}

function Update-FloatingCountdowns {
    param([DateTimeOffset] $NowUtc = ([DateTimeOffset]::UtcNow))
    if ($script:activeWorkspace -ne 'Countdowns') { return }
    $activeCount = @($script:countdowns | Where-Object { $_.targetUtc -gt $NowUtc }).Count
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
    $workdayEnd = Get-WorkdayEndCountdown -NowUtc $nowUtc
    $script:workdayEndSignature = if ($null -eq $workdayEnd) { '' } else { $workdayEnd.targetUtc.ToString('o') }
    $sorted = @(Get-SortedCountdowns | Where-Object { $_.targetUtc -gt $nowUtc })
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
    $check = New-Object System.Windows.Controls.CheckBox
    $check.Width = 22; $check.Height = 22; $check.Margin = '0,1,9,0'; $check.VerticalAlignment = 'Top'; $check.Cursor = 'Hand'
    $check.ToolTip = if ($isCompleted) { '标记为未完成' } else { '标记为已完成' }
    $check.Tag = $Record.id; $check.IsChecked = $isCompleted
    $text = New-Object System.Windows.Controls.TextBlock
    $text.Text = $Record.text; $text.FontSize = if ($Compact) { 13 } else { 14 }; $text.MaxWidth = $maxWidth
    $text.TextWrapping = 'Wrap'; $text.VerticalAlignment = 'Center'; $text.Foreground = if ($isCompleted) { $completedNoteBrush } else { $noteInkBrush }
    if ($isCompleted) { $text.TextDecorations = [System.Windows.TextDecorations]::Strikethrough }
    [System.Windows.Controls.Grid]::SetColumn($text, 1)
    $status = New-Object System.Windows.Controls.TextBlock
    $status.Text = if ($isCompleted) { '已完成' } else { '未完成' }
    $status.FontSize = if ($Compact) { 10 } else { 11 }; $status.FontWeight = 'SemiBold'; $status.VerticalAlignment = 'Center'; $status.Margin = '8,0,0,0'
    $status.Foreground = if ($isCompleted) { $completedNoteBrush } else { $noteInkBrush }
    [System.Windows.Controls.Grid]::SetColumn($status, 2)
    $delete = New-Object System.Windows.Controls.Button
    $delete.Content = '×'; $delete.Width = 28; $delete.Height = 28; $delete.Margin = '8,0,0,0'; $delete.Padding = 0
    $delete.Cursor = 'Hand'; $delete.ToolTip = '删除便签'; $delete.Background = $canvasBrush; $delete.Foreground = $dangerTextBrush; $delete.BorderBrush = $noteLineBrush; $delete.BorderThickness = 1
    $delete.Tag = $Record.id
    [System.Windows.Controls.Grid]::SetColumn($delete, 3)
    $check.Add_Checked({ param($sender, $eventArgs) Set-NoteCompletion -Id ([string] $sender.Tag) -IsCompleted $true })
    $check.Add_Unchecked({ param($sender, $eventArgs) Set-NoteCompletion -Id ([string] $sender.Tag) -IsCompleted $false })
    $delete.Add_Click({ param($sender, $eventArgs) Remove-Note -Id ([string] $sender.Tag) })
    [void] $grid.Children.Add($check); [void] $grid.Children.Add($text); [void] $grid.Children.Add($status); [void] $grid.Children.Add($delete); $border.Child = $grid
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
    $active = @(Get-ActiveNotes); $completed = @(Get-CompletedNotes)
    $noteEmptyState.Visibility = if ($script:notes.Count -eq 0) { 'Visible' } else { 'Collapsed' }
    $activeNotesHeading.Visibility = if ($active.Count -gt 0) { 'Visible' } else { 'Collapsed' }
    $activeNotesHeading.Text = "待完成（$($active.Count)）"
    foreach ($record in $active) { [void] $noteList.Items.Add((New-NoteRow -Record $record)) }
    $toggleCompletedNotesButton.Visibility = if ($completed.Count -eq 0) { 'Collapsed' } else { 'Visible' }
    $toggleCompletedNotesButton.Content = if ($script:showCompletedNotes) { "已完成（$($completed.Count)）⌃" } else { "已完成（$($completed.Count)）⌄" }
    $completedNoteList.Visibility = if ($script:showCompletedNotes -and $completed.Count -gt 0) { 'Visible' } else { 'Collapsed' }
    if ($script:showCompletedNotes) {
        foreach ($record in $completed) { [void] $completedNoteList.Items.Add((New-NoteRow -Record $record)) }
    }
    if ($script:activeWorkspace -eq 'Notes') { $countdownCount.Text = "$($script:notes.Count) 条" }
}

function Render-FloatingNotes {
    $floatingList.Items.Clear(); $script:floatingViews = @{}
    $active = @(Get-ActiveNotes); $completed = @(Get-CompletedNotes)
    $floatingCount.Text = if ($active.Count -gt 0) { "$($active.Count) 项待办" } else { '今天先轻松一点' }
    if ($script:notes.Count -eq 0) {
        $empty = New-Object System.Windows.Controls.TextBlock; $empty.Text = '写下第一件想完成的事吧'; $empty.Foreground = $mutedBrush; $empty.FontSize = 13; $empty.Margin = '2,8,0,8'
        [void] $floatingList.Items.Add($empty)
    }
    foreach ($record in $active) { [void] $floatingList.Items.Add((New-NoteRow -Record $record -Compact)) }
    if ($completed.Count -gt 0) {
        [void] $floatingList.Items.Add((New-CompletedNotesToggle -Count $completed.Count -Compact))
        if ($script:showCompletedNotes) {
            foreach ($record in $completed) { [void] $floatingList.Items.Add((New-NoteRow -Record $record -Compact)) }
        }
    }
}

function Render-FloatingContent {
    if ($script:activeWorkspace -eq 'Notes') { Render-FloatingNotes } else { Render-FloatingCountdowns }
}

function Set-Workspace {
    param([ValidateSet('Countdowns', 'Notes')] [string] $Workspace)
    $script:activeWorkspace = $Workspace
    $showNotes = $Workspace -eq 'Notes'
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
    Render-Cards; Render-Notes; Render-FloatingContent
    if ($showNotes) {
        if ($window.IsVisible) { [void] $noteInput.Focus() }
        else { [void] $floatingNoteInput.Focus() }
    }
}

function Add-NoteFromInput {
    param([Parameter(Mandatory)] [System.Windows.Controls.TextBox] $TextBox)
    try {
        $text = $TextBox.Text.Trim()
        [void] $script:notes.Add((New-NoteRecord -Id ([guid]::NewGuid().ToString('N')) -Text $text))
        $noteInput.Text = ''; $floatingNoteInput.Text = ''
        Save-Settings; Set-NoteMessage -Text '已添加到便签。'; Render-Notes; Render-FloatingContent
        [void] $TextBox.Focus()
    }
    catch { Set-NoteMessage -Text $_.Exception.Message -IsError }
}

function Set-NoteCompletion {
    param([Parameter(Mandatory)] [string] $Id, [Parameter(Mandatory)] [bool] $IsCompleted)
    $note = @($script:notes | Where-Object { $_.id -eq $Id })[0]
    if ($null -eq $note) { return }
    $previousIsCompleted = [bool] $note.isCompleted
    $note.isCompleted = $IsCompleted
    try {
        Save-Settings
        $statusMessage = if ($IsCompleted) { '已标记为已完成。' } else { '已恢复为未完成。' }
        Set-NoteMessage -Text $statusMessage
        Render-Notes; Render-FloatingContent
    }
    catch {
        $note.isCompleted = $previousIsCompleted
        Render-Notes; Render-FloatingContent
        Set-NoteMessage -Text $_.Exception.Message -IsError
    }
}

function Remove-Note {
    param([Parameter(Mandatory)] [string] $Id)
    $note = @($script:notes | Where-Object { $_.id -eq $Id })[0]
    if ($null -eq $note) { return }
    $answer = [System.Windows.MessageBox]::Show(('确定永久删除“{0}”吗？' -f $note.text), '删除便签', 'YesNo', 'Warning')
    if ($answer -ne 'Yes') { return }
    [void] $script:notes.Remove($note)
    Save-Settings; Set-NoteMessage -Text ('已删除“{0}”。' -f $note.text); Render-Notes; Render-FloatingContent
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
    $sorted = @(Get-SortedCountdowns)
    $script:lastSortSignature = @($sorted | ForEach-Object { $_.id }) -join '|'
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
            $saveButton.Content = '保存修改'; $cancelButton.Visibility = 'Visible'; $messageText.Text = ''; $countdownName.Focus()
        })
        $delete.Add_Click({ param($sender, $eventArgs)
            $actionId = [string] $sender.Tag
            $item = @($script:countdowns | Where-Object { $_.id -eq $actionId })[0]
            $answer = [System.Windows.MessageBox]::Show(('确定永久删除“{0}”吗？' -f $item.name), '删除倒计时', 'YesNo', 'Warning')
            if ($answer -eq 'Yes') {
                [void] $script:countdowns.Remove($item); Save-Settings
                if ($script:editingId -eq $actionId) { Reset-Editor }
                Render-Cards; Render-FloatingContent; $messageText.Foreground = $successTextBrush; $messageText.Text = ('已删除“{0}”。' -f $item.name)
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
        $loaded = ConvertFrom-SettingsObject -Settings $settings
        foreach ($record in $loaded.Records) { [void] $script:countdowns.Add($record) }
        foreach ($note in $loaded.Notes) { [void] $script:notes.Add($note) }
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
        $preferences = New-Preferences -Precision ([string] $displayPrecisionInput.SelectedValue) -MonthlySalary $salary -WorkStart (ConvertTo-WorkTime -Text $workStartInput.Text) -WorkEnd (ConvertTo-WorkTime -Text $workEndInput.Text)
        $script:displayPrecision = $preferences.Precision; $script:salary = $preferences.MonthlySalary; $script:workStart = $preferences.WorkStart; $script:workEnd = $preferences.WorkEnd
        Save-Settings; Update-FloatingCountdowns
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
        if ($null -eq $script:editingId) {
            [void] $script:countdowns.Add((New-CountdownRecord -Id ([guid]::NewGuid().ToString('N')) -Name $name -TargetUtc $targetUtc))
            $success = '已添加“{0}”。' -f $name
        }
        else {
            $item = @($script:countdowns | Where-Object { $_.id -eq $script:editingId })[0]
            $item.name = $name; $item.targetUtc = $targetUtc
            $success = '已保存“{0}”。' -f $name
        }
        Save-Settings; Reset-Editor; Render-Cards
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
$addNoteButton.Add_Click({ Add-NoteFromInput -TextBox $noteInput })
$floatingAddNoteButton.Add_Click({ Add-NoteFromInput -TextBox $floatingNoteInput })
$noteInput.Add_KeyDown({
    param($sender, $eventArgs)
    if ($eventArgs.Key -eq [System.Windows.Input.Key]::Enter) { Add-NoteFromInput -TextBox $noteInput; $eventArgs.Handled = $true }
})
$floatingNoteInput.Add_KeyDown({
    param($sender, $eventArgs)
    if ($eventArgs.Key -eq [System.Windows.Input.Key]::Enter) { Add-NoteFromInput -TextBox $floatingNoteInput; $eventArgs.Handled = $true }
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
    $status = [ordered]@{ schemaVersion = 3; title = $window.Title; processId = $PID; sessionId = [System.Diagnostics.Process]::GetCurrentProcess().SessionId; loadedAtUtc = [DateTimeOffset]::UtcNow.ToString('o'); timeZoneId = 'China Standard Time' } | ConvertTo-Json
    [System.IO.File]::WriteAllText("$runtimeStatusPath.tmp", $status, (New-Object System.Text.UTF8Encoding($false)))
    Move-Item -LiteralPath "$runtimeStatusPath.tmp" -Destination $runtimeStatusPath -Force
    $workArea = [System.Windows.SystemParameters]::WorkArea
    $floatingWindow.Left = $workArea.Right - $floatingWindow.ActualWidth - 24
    $floatingWindow.Top = $workArea.Bottom - $floatingWindow.ActualHeight - 48
})
$window.Add_Closing({ param($sender, $eventArgs) if (-not $ValidateOnly) { $eventArgs.Cancel = $true; $window.Hide() } })
$floatingWindow.Add_Closed({
    if ($null -ne $script:singleInstanceMutex) { $script:singleInstanceMutex.ReleaseMutex(); $script:singleInstanceMutex.Dispose() }
    [Windows.Threading.Dispatcher]::CurrentDispatcher.InvokeShutdown()
})

Reset-Editor
$displayPrecisionInput.SelectedValue = $script:displayPrecision
$monthlySalary.Text = $script:salary.ToString('0.##', [System.Globalization.CultureInfo]::InvariantCulture)
$workStartInput.Text = Format-WorkTime -Value $script:workStart
$workEndInput.Text = Format-WorkTime -Value $script:workEnd
if (-not $ValidateOnly) { [void] (Update-ChinaHolidayCalendar -Year (Get-BeijingNow).Year -AllowNetwork) }
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
    $migration = ConvertFrom-SettingsObject -Settings $legacy
    $persistenceRoundTrip = $false
    $notePersistenceRoundTrip = $false
    if (-not [string]::IsNullOrWhiteSpace($SettingsFile)) {
        $roundTrip = ConvertTo-SettingsJson | ConvertFrom-Json
        $persistenceRoundTrip = $roundTrip.schemaVersion -eq 4 -and $roundTrip.displayPrecision -eq $script:displayPrecision -and @($roundTrip.countdowns).Count -eq $script:countdowns.Count
        $notePersistenceRoundTrip = $roundTrip.schemaVersion -eq 4 -and @($roundTrip.notes).Count -eq $script:notes.Count
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
                $activeCheckBox.IsChecked = $true
                $activeCheckBox.RaiseEvent((New-Object System.Windows.RoutedEventArgs([System.Windows.Controls.Primitives.ToggleButton]::CheckedEvent)))
                $wasMarkedComplete = [bool] $addedNote.isCompleted -and @((Get-CompletedNotes) | Where-Object { $_.id -eq $addedNote.id }).Count -eq 1
                $completedRow = New-NoteRow -Record $addedNote
                $completedCheckBox = @($completedRow.Child.Children | Where-Object { $_ -is [System.Windows.Controls.CheckBox] -and [string] $_.Tag -eq $addedNote.id })[0]
                if ($null -ne $completedCheckBox) {
                    $completedCheckBox.IsChecked = $false
                    $completedCheckBox.RaiseEvent((New-Object System.Windows.RoutedEventArgs([System.Windows.Controls.Primitives.ToggleButton]::UncheckedEvent)))
                }
                $noteStatusViaCheckbox = $wasMarkedComplete -and -not [bool] $addedNote.isCompleted -and @((Get-ActiveNotes) | Where-Object { $_.id -eq $addedNote.id }).Count -eq 1
            }
        }
    }
    $invalidNotesRejected = $false
    try {
        ConvertFrom-SettingsObject -Settings ([pscustomobject]@{ schemaVersion = 4; timeZoneId = 'China Standard Time'; displayPrecision = 'Second'; monthlySalary = 0; workStart = '09:00'; workEnd = '18:00'; countdowns = @(); notes = @([pscustomobject]@{ id = 'note'; text = ''; isCompleted = $false }) }) | Out-Null
    }
    catch { $invalidNotesRejected = $true }
    $duplicateSettingsRejected = $false
    try {
        ConvertFrom-SettingsObject -Settings ([pscustomobject]@{ schemaVersion = 2; timeZoneId = 'China Standard Time'; countdowns = @([pscustomobject]@{ id = 'a'; name = '计划'; targetUtc = '2099-01-01T00:00:00Z' }, [pscustomobject]@{ id = 'b'; name = '计划'; targetUtc = '2099-02-01T00:00:00Z' }) }) | Out-Null
    }
    catch { $duplicateSettingsRejected = $true }
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
            if ($tags.Count -ne 2 -or @($tags | Where-Object { $_ -ne $notesForValidation[$index].id }).Count -gt 0) {
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
        PersistenceRoundTrip = $persistenceRoundTrip
        NotePersistenceRoundTrip = $notePersistenceRoundTrip
        NoteAddViaClick = $noteAddViaClick
        NoteStatusViaCheckbox = $noteStatusViaCheckbox
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
        HasNoteControls = $null -ne $noteInput -and $null -ne $addNoteButton -and $null -ne $floatingNoteInput -and $null -ne $floatingAddNoteButton
        HasOpenNotesActions = $null -ne $showNotesButton -and $null -ne $showFloatingNotesButton
        UnifiedWorkspaceSwitches = $unifiedWorkspaceSwitches
        WorkspaceSwitcherBeforeEditors = $workspaceSwitcherBeforeEditors
        HasPreferenceControls = $null -ne $displayPrecisionInput -and $null -ne $monthlySalary -and $null -ne $workStartInput -and $null -ne $workEndInput -and $null -ne $savePreferencesButton
        LoadedCountdowns = $script:countdowns.Count; LoadedNotes = $script:notes.Count; SettingsWereMigrated = $script:settingsWereMigrated
        LoadedDisplayPrecision = $script:displayPrecision; LoadedMonthlySalary = $script:salary
        LoadedWorkStart = (Format-WorkTime -Value $script:workStart); LoadedWorkEnd = (Format-WorkTime -Value $script:workEnd)
        ActiveNoteCount = @(Get-ActiveNotes).Count; CompletedNoteCount = @(Get-CompletedNotes).Count
        SortedNames = @((Get-SortedCountdowns) | ForEach-Object { $_.name })
        WindowIconLoaded = $null -ne $window.Icon
    } | ConvertTo-Json
    exit 0
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
[Windows.Threading.Dispatcher]::Run()
